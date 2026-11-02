using System;
using System.Collections.Generic;
using System.Linq;
using MreAirdrop;
internal static class Program
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Filler test failed"); }
    private static void Main()
    {
        foreach (var category in new[] { "Оружейный", "Строительный", "Рейдовый" })
        {
            var f = new LootFiller(category, new ushort[0], 0);
            Check(Enumerable.Range(0, 30).Select(f.At).Distinct().Count() >= 9);
            Check(Enumerable.Range(0, 196).All(i => f.At(i) != 0));
            Check(new LootFiller(category, new ushort[0], 1).At(0) != f.At(0));
            Check(new LootFiller(category, new ushort[0], int.MinValue).At(0) != 0);
        }
        var f1 = new LootFiller("Оружейный", new ushort[] { 297, 1000 }, 0);
        Check(f1.At(0) == 298 && f1.At(1) == 1003);
        Check(Enumerable.Range(2, 100).All(i => f1.At(i) != 298 && f1.At(i) != 1003));
        var f2 = new LootFiller("Оружейный", new ushort[] { 18, 1382 }, 0);
        Check(f2.At(0) == 20 && f2.At(1) == 1384);
        Check(!new LootFiller("Оружейный", new ushort[] { 132 }, 0).CandidateIds().Any(i => i == 298 || i == 20 || i == 1003 || i == 1384));
        Check(!new LootFiller("Рейдовый", new ushort[0], 0).CandidateIds().Any(i => i == 1100 || i == 1241 || i == 1393));
        try { new LootFiller("unknown", new ushort[0], 0); throw new Exception("Expected rejection"); } catch (ArgumentException) { }
        Console.WriteLine("Filler tests passed");
    }
}
