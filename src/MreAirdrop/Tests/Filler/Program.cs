using System;
using System.Collections.Generic;
using System.Linq;
using MreAirdrop;
internal static class Program
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Main()
    {
        var universal = new ushort[] { 151, 152, 1007, 1008 };
        var byGun = new Dictionary<ushort, ushort[]> {
            { 297, new ushort[] { 298, 299 } }, { 1000, new ushort[] { 1003, 1001 } },
            { 132, new ushort[] { 134, 150 } }, { 18, new ushort[] { 20, 19 } },
            { 126, new ushort[] { 128, 1190 } }, { 1382, new ushort[] { 1384, 1383 } },
            { 1484, new ushort[] { 1486 } }, { 1364, new ushort[0] }
        };
        var bundles = LootPlan.Defaults().Single(c => c.Name == "Оружейный").Bundles;
        Check(bundles.Count == 5, "Expected all five default weapon bundles");
        foreach (var bundle in bundles)
        {
            var items = bundle.Items.Select(x => x.Id).ToArray();
            var expected = new HashSet<ushort>(universal);
            foreach (var pair in byGun) if (items.Contains(pair.Key)) expected.UnionWith(pair.Value);
            for (int rotation = -1; rotation <= 1; rotation++)
            {
                var filler = new LootFiller("Оружейный", items, rotation);
                Check(expected.SetEquals(filler.CandidateIds()), bundle.Name + ": candidate compatibility");
                var output = Enumerable.Range(0, 196).Select(filler.At).ToArray();
                Check(output.All(expected.Contains), bundle.Name + ": unexpected filler");
                Check(expected.All(output.Contains), bundle.Name + ": missing candidate");
                Check(output.Distinct().Count() >= 6, bundle.Name + ": variety");
                foreach (var id in expected.Except(universal)) Check(output.Count(x => x == id) == 1, bundle.Name + ": conditional item repeated");
            }
        }
        foreach (var category in new[] { "Строительный", "Рейдовый" })
        {
            var filler = new LootFiller(category, new ushort[0], 0);
            Check(Enumerable.Range(0, 196).Select(filler.At).Distinct().Count() >= 9, category + ": variety");
            Check(new LootFiller(category, new ushort[0], 1).At(0) != filler.At(0), category + ": rotation");
        }
        var unknown = new LootFiller("Оружейный", new ushort[] { 42 }, int.MinValue);
        Check(new HashSet<ushort>(universal).SetEquals(unknown.CandidateIds()), "Unknown gun conditional items");
        Check(unknown.At(0) != 0, "Minimum rotation");
        Check(!new LootFiller("Рейдовый", new ushort[0], 0).CandidateIds().Any(i => i == 1100 || i == 1241 || i == 1393), "Raid explosives");
        try { new LootFiller("unknown", new ushort[0], 0); throw new Exception("Expected rejection"); } catch (ArgumentException) { }
        Console.WriteLine("Filler tests passed");
    }
}
