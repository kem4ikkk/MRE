using System;
using System.Collections.Generic;

namespace MreSpawnProtection
{
    public sealed class ProtectionTimer
    {
        private readonly Dictionary<ulong, double> expiry = new Dictionary<ulong, double>();
        public void Start(ulong id, double now, double duration) { if (id != 0) expiry[id] = now + duration; }
        public double Remaining(ulong id, double now) { double end; return expiry.TryGetValue(id, out end) ? Math.Max(0, end - now) : 0; }
        public bool Remove(ulong id) { return expiry.Remove(id); }
        public void Clear() { expiry.Clear(); }
    }
}
