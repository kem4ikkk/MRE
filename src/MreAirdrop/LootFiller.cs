using System;
using System.Collections.Generic;

namespace MreAirdrop
{
    // A magazine is supplied at most once per compatible weapon; core fillers cycle.
    internal sealed class LootFiller
    {
        private readonly ushort[] common;
        private readonly List<ushort> magazines = new List<ushort>();
        private readonly int offset;
        internal LootFiller(string category, IList<ushort> baseItems, int rotation)
        {
            if (baseItems == null) throw new ArgumentNullException("baseItems");
            switch (category)
            {
                case "Оружейный": common = new ushort[] { 394, 95, 390, 391, 392, 86, 14, 1346, 267 }; break;
                case "Строительный": common = new ushort[] { 285, 74, 67, 71, 65, 69, 64, 66, 70, 1926 }; break;
                case "Рейдовый": common = new ushort[] { 394, 95, 390, 389, 391, 392, 1346, 267, 14, 86 }; break;
                default: throw new ArgumentException("Unknown airdrop category", "category");
            }
            if (category == "Оружейный")
            {
                AddMagazine(baseItems, 297, 298); // Grizzly
                AddMagazine(baseItems, 18, 20); // Timberwolf
                AddMagazine(baseItems, 1000, 1003); // Matamorez
                AddMagazine(baseItems, 1382, 1384); // Ekho
            }
            offset = (int)((uint)rotation % (uint)common.Length);
        }
        private void AddMagazine(IList<ushort> items, ushort gun, ushort magazine)
        { if (items.Contains(gun)) magazines.Add(magazine); }
        internal IEnumerable<ushort> CandidateIds()
        {
            foreach (ushort id in magazines) yield return id;
            foreach (ushort id in common) yield return id;
        }
        internal ushort At(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException("index");
            if (index < magazines.Count) return magazines[index];
            return common[(int)(((long)index - magazines.Count + offset) % common.Length)];
        }
    }
}
