using System;

namespace MreKits
{
    internal static class WeaponSlotPlan
    {
        internal static int Choose(ushort id, Func<byte, bool> available)
        {
            if (id != 353 && id != 16) return -1;
            byte first = id == 353 ? (byte)0 : (byte)1;
            byte second = (byte)(1 - first);
            if (available(first)) return first;
            return available(second) ? second : -1;
        }
    }
}
