using System;
using System.Collections.Generic;

namespace MreInterface
{
    internal sealed class ConfirmationStore<T> where T : class
    {
        private sealed class Entry { public string Token; public DateTime Expires; public T Value; }
        private readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
        public string Issue(ulong actor, T value, DateTime now)
        {
            string token = Guid.NewGuid().ToString("N");
            entries[actor] = new Entry { Token = token, Expires = now.AddSeconds(15), Value = value };
            return token;
        }
        public bool TryConsume(ulong actor, string token, DateTime now, out T value)
        {
            Entry entry; value = null;
            if (!entries.TryGetValue(actor, out entry)) return false;
            entries.Remove(actor); // Любая попытка подтверждения одноразовая.
            if (now >= entry.Expires || !string.Equals(token, entry.Token, StringComparison.Ordinal)) return false;
            value = entry.Value; return true;
        }
        public void Cancel(ulong actor) { entries.Remove(actor); }
        public void Clear() { entries.Clear(); }
    }
}
