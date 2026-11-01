using System;
using System.Collections.Generic;

namespace MreImmunity
{
    internal sealed class SyncGate<T>
    {
        private readonly Dictionary<T, double> last = new Dictionary<T, double>();
        internal bool ShouldSend(T key, double now, double interval)
        {
            double previous;
            if (last.TryGetValue(key, out previous) && now >= previous && now - previous < interval) return false;
            last[key] = now;
            return true;
        }
        internal void Remove(T key) { last.Remove(key); }
        internal void Clear() { last.Clear(); }
    }
}
