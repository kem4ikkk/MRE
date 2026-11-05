using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MreDecay.Engine
{
    // Stores opaque, versioned decay/raid state supplied by the caller. It cannot
    // make a Unity world save atomic with this journal; pending always needs review.
    public sealed class DurableJournal : IDisposable
    {
        private const int Schema = 1, Limit = 16 * 1024 * 1024;
        private readonly string path;
        private FileStream writerLock;
        private bool faulted;
        private readonly object sync = new object();
        private long? ownedIntent;
        public DurableJournal(string path) {
            this.path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!this.path.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) { faulted = true; return; }
            try { writerLock = new FileStream(this.path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch { faulted = true; }
        }
        public void Dispose() { lock (sync) { faulted = true; ownedIntent = null; if (writerLock != null) { writerLock.Dispose(); writerLock = null; } } }

        // Explicit bootstrap is allowed only in an existing empty directory. Never call
        // automatically on missing state: loss of an existing file must stop operation.
        public bool Bootstrap(byte[] initialState) { lock (sync) return BootstrapCore(initialState); }
        private bool BootstrapCore(byte[] initialState)
        {
            if (faulted || !Valid(initialState)) return Fail();
            try {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) return Fail();
                foreach (string entry in Directory.GetFileSystemEntries(dir))
                    if (!String.Equals(entry, path + ".lock", StringComparison.OrdinalIgnoreCase)) return Fail();
                return Write(0, false, initialState, false);
            } catch { return Fail(); }
        }

        // A fresh process must stop on a pending record and reconcile world and state offline.
        public bool Load(out long sequence, out byte[] state) { lock (sync) return LoadCore(out sequence, out state); }
        private bool LoadCore(out long sequence, out byte[] state)
        {
            sequence = 0; state = null;
            if (faulted) return false;
            try {
                long n; bool pending; byte[] data;
                if (!Read(out n, out pending, out data) || pending || StateCodec.Decode(data) == null) return Fail();
                sequence = n; state = data; return true;
            } catch { return Fail(); }
        }

        // Persist intent and proposed state before any possible world operation.
        public bool Begin(long expectedSequence, byte[] proposedState) { lock (sync) return BeginCore(expectedSequence, proposedState); }
        private bool BeginCore(long expectedSequence, byte[] proposedState)
        {
            if (faulted || !Valid(proposedState)) return Fail();
            try {
                long n; bool pending; byte[] old;
                if (!Read(out n, out pending, out old) || pending || StateCodec.Decode(old) == null || n != expectedSequence || n == long.MaxValue) return Fail();
                if (old.Length + (long)proposedState.Length + 8 > Limit) return Fail();
                using (var buffer = new MemoryStream()) using (var writer = new BinaryWriter(buffer)) {
                    writer.Write(old.Length); writer.Write(old); writer.Write(proposedState.Length); writer.Write(proposedState); writer.Flush();
                    if (!Write(n + 1, true, buffer.ToArray(), true)) return false;
                    ownedIntent = n + 1; return true;
                }
            } catch { return Fail(); }
        }

        // Caller may commit only after proving the corresponding world outcome.
        public bool Commit(long sequence) { lock (sync) return CommitCore(sequence); }
        private bool CommitCore(long sequence)
        {
            if (faulted || ownedIntent != sequence) return Fail();
            try {
                long n; bool pending; byte[] data;
                if (!Read(out n, out pending, out data) || !pending || n != sequence) return Fail();
                using (var buffer = new MemoryStream(data)) using (var reader = new BinaryReader(buffer)) {
                    int oldLength = reader.ReadInt32();
                    if (oldLength < 0 || oldLength > buffer.Length - buffer.Position - 4) return Fail();
                    reader.ReadBytes(oldLength); int nextLength = reader.ReadInt32();
                    if (nextLength < 0 || nextLength != buffer.Length - buffer.Position) return Fail();
                    byte[] next = reader.ReadBytes(nextLength);
                    if (!Valid(next)) return Fail();
                    if (!Write(n, false, next, true)) return false;
                    ownedIntent = null; return true;
                }
            } catch { return Fail(); }
        }

        private bool Write(long sequence, bool pending, byte[] state, bool replace)
        {
            string temp = path + ".tmp";
            try {
                byte[] body;
                using (var buffer = new MemoryStream()) using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true)) {
                    writer.Write(Schema); writer.Write(sequence); writer.Write(pending); writer.Write(state.Length); writer.Write(state); writer.Flush(); body = buffer.ToArray();
                }
                byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(body);
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    file.Write(body, 0, body.Length); file.Write(hash, 0, hash.Length); file.Flush(true);
                }
                if (replace) File.Replace(temp, path, null); else File.Move(temp, path);
                return true;
            } catch { return Fail(); }
        }

        private bool Read(out long sequence, out bool pending, out byte[] state)
        {
            sequence = 0; pending = false; state = null;
            // A stale temporary file indicates interrupted publication, even if the
            // previous primary file appears valid. Never delete it automatically.
            if (File.Exists(path + ".tmp") || !File.Exists(path)) return false;
            var info = new FileInfo(path);
            if (info.Length < 49 || info.Length > Limit + 49L) return false;
            byte[] bytes = File.ReadAllBytes(path); int bodyLength = bytes.Length - 32;
            byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(bytes, 0, bodyLength);
            int difference = 0; for (int i = 0; i < 32; i++) difference |= hash[i] ^ bytes[bodyLength + i];
            if (difference != 0) return false;
            using (var input = new MemoryStream(bytes, 0, bodyLength, false)) using (var reader = new BinaryReader(input)) {
                if (reader.ReadInt32() != Schema) return false;
                sequence = reader.ReadInt64(); pending = reader.ReadBoolean(); int length = reader.ReadInt32();
                if (sequence < 0 || length < 0 || length > Limit || input.Length - input.Position != length) return false;
                state = reader.ReadBytes(length); return state.Length == length;
            }
        }
        private static bool Valid(byte[] state) { return state != null && state.Length <= Limit && StateCodec.Decode(state) != null; }
        private bool Fail() { faulted = true; return false; }
    }
}
