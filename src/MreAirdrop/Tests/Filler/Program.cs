using System;
using System.Collections.Generic;
using System.Linq;
using MreAirdrop;
internal static class Program
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Main()
    {
        var weaponPool = new ushort[] { 151, 152, 1007, 1008, 20, 98, 100, 103, 108, 111, 113, 130, 298, 381, 478, 485, 489, 1003, 1020, 1022, 1040, 1361, 1368, 1384, 1487 };
        var allSightIds = new ushort[] { 5, 19, 102, 110, 114, 118, 124, 128, 131, 134, 299, 349, 358, 364, 475, 486, 521, 1001, 1019, 1025, 1028, 1038, 1043, 1367, 1370, 1376, 1378, 1380, 1383, 1442, 1448, 1478, 1482, 1486, 1489 };
        var byGun = new Dictionary<ushort, ushort[]> {
            { 297, new ushort[] { 298 } }, { 1000, new ushort[] { 1003 } },
            { 132, new ushort[] { 150 } }, { 18, new ushort[] { 20 } },
            { 126, new ushort[] { 127, 1190 } }, { 1382, new ushort[] { 1384 } },
            { 1484, new ushort[] { 1485 } }, { 1364, new ushort[] { 1365 } }
        };
        var bundles = LootPlan.Defaults().Single(c => c.Name == "Оружейный").Bundles;
        Check(bundles.Count == 5, "Expected all five default weapon bundles");
        foreach (var bundle in bundles)
        {
            var items = bundle.Items.Select(x => x.Id).ToArray();
            var expected = new HashSet<ushort>(weaponPool);
            foreach (var pair in byGun) if (items.Contains(pair.Key)) expected.UnionWith(pair.Value);
            expected.ExceptWith(items);
            for (int rotation = -1; rotation <= 1; rotation++)
            {
                var filler = new LootFiller("Оружейный", items, rotation);
                Check(expected.SetEquals(filler.CandidateIds()), bundle.Name + ": candidate compatibility");
                var output = Enumerable.Range(0, 196).Select(filler.At).ToArray();
                Check(output.All(expected.Contains), bundle.Name + ": unexpected filler");
                Check(expected.All(output.Contains), bundle.Name + ": missing candidate");
                Check(output.Take(expected.Count).Distinct().Count() == expected.Count, bundle.Name + ": repeats before pool exhausted");
                Check(output.Skip(expected.Count).Take(expected.Count).Distinct().Count() == expected.Count, bundle.Name + ": repeat cycle variety");
                Check(!expected.Overlaps(allSightIds), bundle.Name + ": ordinary sights included");
            }
        }
        foreach (var category in new[] { "Строительный", "Рейдовый" })
        {
            var filler = new LootFiller(category, new ushort[0], 0);
            var expected = category == "Рейдовый"
                ? new HashSet<ushort> { 65, 67, 69, 70, 71, 72, 1242, 254, 1100, 1520, 1101, 1102, 1241, 1393 }
                : new HashSet<ushort> { 38, 40, 42, 64, 65, 66, 67, 69, 71, 72, 74, 285, 516, 1926, 365, 382, 892, 893, 894, 1059, 1061, 1062, 1065, 1067, 1068, 1071, 1073, 1074, 1092, 1093, 1101, 1102, 1227, 1329, 1330, 1331, 1332 };
            Check(expected.SetEquals(filler.CandidateIds()), category + ": allowed filler IDs");
            var output = Enumerable.Range(0, expected.Count * 2).Select(filler.At).ToArray();
            Check(output.Take(expected.Count).Distinct().Count() == expected.Count, category + ": repeats before pool exhausted");
            Check(output.Skip(expected.Count).Take(expected.Count).Distinct().Count() == expected.Count, category + ": repeat cycle variety");
            Check(!filler.CandidateIds().SequenceEqual(new LootFiller(category, new ushort[0], 1).CandidateIds()), category + ": shuffle seed");
        }
        var unknown = new LootFiller("Оружейный", new ushort[] { 42 }, int.MinValue);
        Check(new HashSet<ushort>(weaponPool).SetEquals(unknown.CandidateIds()), "Unknown gun conditional items");
        Check(unknown.At(0) != 0, "Minimum rotation");
        var raidFiller = new HashSet<ushort>(new LootFiller("Рейдовый", new ushort[0], 0).CandidateIds());
        Check(!raidFiller.Any(i => new ushort[] { 394, 95, 390, 389, 391, 392, 1346, 267, 14, 86 }.Contains(i)), "Unrelated raid filler");
        var raidBundles = LootPlan.Defaults().Single(c => c.Name == "Рейдовый").Bundles;
        Check(raidBundles.Count == 7, "Raid drop should offer seven distinct bundles");
        var shadowstalker = raidBundles.Single(b => b.Name == "Shadowstalker");
        var shadowIds = shadowstalker.Items.Select(x => x.Id).ToArray();
        Check(shadowIds.Contains((ushort)300) && shadowIds.Contains((ushort)301), "Structure-damaging Shadowstalker kit");
        Check(new LootFiller("Рейдовый", shadowIds, 0).CandidateIds().Contains((ushort)301) == false, "Don't repeat the base Rail magazine");
        Check(new LootFiller("Рейдовый", new ushort[] { 300 }, 0).CandidateIds().Contains((ushort)301), "Add Rail ammo only for Shadowstalker");
        Check(new LootFiller("Рейдовый", new ushort[] { 1441 }, 0).CandidateIds().Contains((ushort)1443), "Add Mk. II ammo only for matching weapon");
        try { new LootFiller("unknown", new ushort[0], 0); throw new Exception("Expected rejection"); } catch (ArgumentException) { }
        Console.WriteLine("Filler tests passed");
    }
}
