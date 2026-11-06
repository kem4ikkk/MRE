using System;
using System.Threading;

namespace MreDecay.Engine
{
    // Filesystem-only rehearsal, not a Unity backend. A caller must supply a
    // verified independent anchor, exclusive maintenance, and TEMP world files.
    public sealed class IsolatedCheckpointBackend : ICheckpointBackend, IDisposable
    {
        private readonly string worldDirectory;
        private readonly IsolatedPendingStore pending;
        private readonly Func<bool> apply, save;
        private readonly byte[] expectedWorld;
        private readonly long expectedGeneration;
        private bool verified, intentWritten, applied, saved, faulted, disposed, busy;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        public IsolatedCheckpointBackend(string worldDirectory, string journalPath, long generation,
            byte[] expectedState, byte[] independentlyTrustedWorldHash, Func<bool> applyWorld, Func<bool> saveWorld)
        {
            if (generation < 0 || expectedState == null || independentlyTrustedWorldHash == null || independentlyTrustedWorldHash.Length != 32 || applyWorld == null || saveWorld == null)
                throw new ArgumentException("Incomplete isolated checkpoint input");
            this.worldDirectory = worldDirectory; expectedGeneration = generation;
            expectedWorld = (byte[])independentlyTrustedWorldHash.Clone();
            apply = applyWorld; save = saveWorld;
            pending = new IsolatedPendingStore(journalPath);
            if (!pending.Open(generation, expectedState)) { pending.Dispose(); throw new InvalidOperationException("Journal not ready"); }
        }
        public bool VerifyCurrent(long generation)
        {
            if (!Enter() || intentWritten || generation != expectedGeneration) return Fail();
            try { bool ok = Equal(IsolatedWorldFingerprint.Compute(worldDirectory), expectedWorld); if (!ok || faulted) return Fail(); verified = true; return true; }
            finally { busy = false; }
        }
        public bool WriteIntent(long generation, byte[] state)
        {
            if (!Enter() || !verified || intentWritten || expectedGeneration == long.MaxValue || generation != expectedGeneration + 1) return Fail();
            try { if (!pending.Begin(state) || faulted) return Fail(); intentWritten = true; return true; }
            finally { busy = false; }
        }
        public bool ApplyWorld() {
            if (!Enter() || !intentWritten || applied) return Fail();
            try { bool ok = apply(); if (!ok || faulted) return Fail(); applied = true; return true; }
            catch { faulted = true; throw; }
            finally { busy = false; }
        }
        public bool SaveAndVerifyWorld() {
            if (!Enter() || !applied || saved) return Fail();
            try { bool ok = save(); if (!ok || faulted || IsolatedWorldFingerprint.Compute(worldDirectory) == null) return Fail(); saved = true; return true; }
            catch { faulted = true; throw; }
            finally { busy = false; }
        }
        // Publication is unavailable and must fence the instance.
        public bool PublishStateAndAnchor(long generation, byte[] state) { return Fail(); }
        private bool Enter() {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread || disposed || faulted || busy) return false;
            busy = true; return true;
        }
        private bool Fail() { faulted = true; return false; }
        public void Dispose() { disposed = true; faulted = true; pending.Dispose(); }
        private static bool Equal(byte[] a, byte[] b) {
            if (a == null || b == null || a.Length != b.Length) return false;
            int x=0; for (int i=0;i<a.Length;i++) x |= a[i]^b[i]; return x==0;
        }
    }
}
