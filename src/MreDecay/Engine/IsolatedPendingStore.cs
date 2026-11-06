using System;
using System.Threading;

namespace MreDecay.Engine
{
    // Offline TEMP-only adapter for the existing durable intent journal. It does
    // not call Unity save, publish an anchor, or authorize mutation by itself.
    public sealed class IsolatedPendingStore : IDisposable
    {
        private readonly DurableJournal journal;
        private bool loaded, pending, faulted;
        private long generation;
        private readonly object sync = new object();
        public IsolatedPendingStore(string path) { journal = new DurableJournal(path); }
        public bool Open(long expectedGeneration, byte[] expectedState) { lock (sync) return OpenCore(expectedGeneration, expectedState); }
        private bool OpenCore(long expectedGeneration, byte[] expectedState)
        {
            if (loaded || pending || faulted || expectedState == null) return Fail();
            long actual; byte[] bytes;
            if (!journal.Load(out actual, out bytes) || actual != expectedGeneration || !Equal(bytes, expectedState)) return Fail();
            generation = actual; loaded = true; return true;
        }
        public bool Begin(byte[] proposedState) { lock (sync) return BeginCore(proposedState); }
        private bool BeginCore(byte[] proposedState)
        {
            if (!loaded || pending || faulted || generation == long.MaxValue || proposedState == null) return Fail();
            if (!journal.Begin(generation, proposedState)) return Fail();
            pending = true; return true;
        }
        // Deliberately no commit operation. A real commit must be designed together
        // with verified Unity save and an independently durable anchor.
        public void Dispose() { lock (sync) { faulted = true; journal.Dispose(); } }
        private bool Fail() { faulted = true; return false; }
        private static bool Equal(byte[] a, byte[] b) {
            if (a == null || b == null || a.Length != b.Length) return false;
            int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i]; return d == 0;
        }
    }
}
