using System;

namespace MreKits
{
    // Совместимость с журналом предыдущей локальной версии, где набор назывался starter.
    public static class KitNames
    {
        public static string LedgerName(string name)
        {
            return String.Equals(name, "start", StringComparison.OrdinalIgnoreCase) ? "starter" : name;
        }
    }
}
