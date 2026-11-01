using System.Collections.Generic;

namespace RestoreMonarchy.Teleportation.Utils
{
    // Сервер получает индексы штатных Plugin Key, а не произвольные клавиши клиента.
    public sealed class PluginKeyEdges
    {
        private readonly Dictionary<ulong, byte> held = new Dictionary<ulong, byte>();

        public bool IsRising(ulong steamId, byte key, bool state)
        {
            if (key >= 5) return false;
            byte previous;
            held.TryGetValue(steamId, out previous);
            byte bit = (byte)(1 << key);
            held[steamId] = state ? (byte)(previous | bit) : (byte)(previous & ~bit);
            return state && (previous & bit) == 0;
        }

        public void Remove(ulong steamId) { held.Remove(steamId); }
        public void Clear() { held.Clear(); }
    }
}
