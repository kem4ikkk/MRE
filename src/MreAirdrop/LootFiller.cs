using System;
using System.Collections.Generic;

namespace MreAirdrop
{
    // Совместимые предметы выдаются по одному, общий добор повторяется циклически.
    internal sealed class LootFiller
    {
        private readonly ushort[] common;
        private readonly List<ushort> compatibleItems = new List<ushort>();
        private readonly int offset;
        internal LootFiller(string category, IList<ushort> baseItems, int rotation)
        {
            if (baseItems == null) throw new ArgumentNullException("baseItems");
            switch (category)
            {
                case "Оружейный": common = new ushort[] { 151, 152, 1007, 1008 }; break;
                case "Строительный": common = new ushort[] { 285, 74, 67, 71, 65, 69, 64, 66, 70, 1926 }; break;
                case "Рейдовый": common = new ushort[] { 394, 95, 390, 389, 391, 392, 1346, 267, 14, 86 }; break;
                default: throw new ArgumentException("Unknown airdrop category", "category");
            }
            if (category == "Оружейный")
            {
                AddCompatible(baseItems, 297, 298); // Grizzly
                AddCompatible(baseItems, 18, 20); // Timberwolf
                AddCompatible(baseItems, 1000, 1003); // Matamorez
                AddCompatible(baseItems, 1382, 1384); // Ekho
                // Прицелы и дульные устройства добавляются к совместимому оружию.
                AddCompatible(baseItems, 297, 299);
                AddCompatible(baseItems, 1000, 1001);
                AddCompatible(baseItems, 132, 134);
                AddCompatible(baseItems, 18, 19);
                AddCompatible(baseItems, 126, 128);
                AddCompatible(baseItems, 1382, 1383);
                AddCompatible(baseItems, 1484, 1486);
                AddCompatible(baseItems, 132, 150);
                AddCompatible(baseItems, 126, 1190);
            }
            offset = (int)((uint)rotation % (uint)common.Length);
        }
        private void AddCompatible(IList<ushort> items, ushort gun, ushort item)
        { if (items.Contains(gun)) compatibleItems.Add(item); }
        internal IEnumerable<ushort> CandidateIds()
        {
            foreach (ushort id in compatibleItems) yield return id;
            foreach (ushort id in common) yield return id;
        }
        internal ushort At(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException("index");
            if (index < compatibleItems.Count) return compatibleItems[index];
            return common[(int)(((long)index - compatibleItems.Count + offset) % common.Length)];
        }
    }
}
