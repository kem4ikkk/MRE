using System;
using System.Collections.Generic;
using System.Linq;

namespace MreAirdrop
{
    // Сначала выдаются все разные предметы пула, затем начинается повтор.
    internal sealed class LootFiller
    {
        private readonly ushort[] candidates;

        internal LootFiller(string category, IList<ushort> baseItems, int seed)
        {
            if (baseItems == null) throw new ArgumentNullException("baseItems");

            var common = new List<ushort>();
            var compatible = new List<ushort>();
            switch (category)
            {
                case "Оружейный":
                    common.AddRange(new ushort[] {
                        151, 152, 1007, 1008, 20, 98, 100, 103, 108, 111, 113,
                        130, 298, 381, 478, 485, 489, 1003, 1020, 1022, 1040,
                        1361, 1368, 1384, 1487
                    });
                    AddCompatible(baseItems, compatible, 297, 298); // Grizzly magazine
                    AddCompatible(baseItems, compatible, 18, 20); // Timberwolf magazine
                    AddCompatible(baseItems, compatible, 1000, 1003); // Matamorez magazine
                    AddCompatible(baseItems, compatible, 1382, 1384); // Ekho magazine
                    AddCompatible(baseItems, compatible, 126, 127); // Nykorev box magazine
                    AddCompatible(baseItems, compatible, 132, 133); // Dragonfang box magazine
                    AddCompatible(baseItems, compatible, 1364, 1365); // Hell's Fury drum
                    AddCompatible(baseItems, compatible, 1484, 1485); // Devil's Bane magazine
                    AddCompatible(baseItems, compatible, 132, 150); // Military Muzzle
                    AddCompatible(baseItems, compatible, 126, 1190); // Ranger Muzzle
                    break;
                case "Строительный":
                    common.AddRange(new ushort[] {
                        38, 40, 42, 64, 65, 66, 67, 69, 71, 72, 74, 285, 516, 1926,
                        365, 382, 892, 893, 894, 1059, 1061, 1062, 1065, 1067, 1068,
                        1071, 1073, 1074, 1092, 1093, 1101, 1102, 1227,
                        1329, 1330, 1331, 1332
                    });
                    break;
                case "Рейдовый":
                    common.AddRange(new ushort[] { 65, 67, 69, 70, 71, 72, 1242, 254, 1100, 1520, 1101, 1102, 1241, 1393 });
                    AddCompatible(baseItems, compatible, 300, 301); // Shadowstalker rail magazine
                    AddCompatible(baseItems, compatible, 1441, 1443); // Shadowstalker Mk. II magazine
                    break;
                default:
                    throw new ArgumentException("Unknown airdrop category", "category");
            }

            candidates = compatible.Concat(common).Where(id => !baseItems.Contains(id)).Distinct().ToArray();
            Shuffle(candidates, new Random(seed));
        }

        private static void AddCompatible(IList<ushort> baseItems, IList<ushort> output, ushort gun, ushort item)
        {
            if (baseItems.Contains(gun)) output.Add(item);
        }

        private static void Shuffle(ushort[] items, Random random)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                ushort value = items[i];
                items[i] = items[j];
                items[j] = value;
            }
        }

        internal IEnumerable<ushort> CandidateIds() { return candidates; }

        internal ushort At(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException("index");
            if (candidates.Length == 0) throw new InvalidOperationException("The filler pool is empty.");
            return candidates[index % candidates.Length];
        }
    }
}
