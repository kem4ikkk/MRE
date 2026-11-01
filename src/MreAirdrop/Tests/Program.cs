using System;
using MreAirdrop;

internal static class Program
{
    private static void Check(bool value) { if (!value) throw new Exception("Ошибка вероятности выпадения."); }
    private static bool Reject(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
    private static void Main()
    {
        Check(!LootRoll.Percent(0, 0)); Check(LootRoll.Percent(100, 0.999999));
        Check(LootRoll.Percent(25, 0.249999)); Check(!LootRoll.Percent(25, 0.25));
        Check(LootRoll.WeightedIndex(new[] { 0.0, 1.0, 3.0, 0.0 }, 0) == 1);
        Check(LootRoll.WeightedIndex(new[] { 0.0, 1.0, 3.0, 0.0 }, 0.25) == 2);
        Check(LootRoll.WeightedIndex(new[] { 0.0, 1.0, 3.0, 0.0 }, 0.999999) == 2);
        Check(Reject(() => LootRoll.WeightedIndex(new[] { 0.0, 0.0 }, 0)));
        Check(Reject(() => LootRoll.Percent(Double.NaN, 0)));
        Check(Reject(() => LootRoll.Percent(50, 1)));
        var defaults = LootPlan.Defaults(); LootPlan.Validate(defaults);
        var common = LootPlan.Select(defaults, 0.0, 0.0);
        Check(common.Item1.Name == "Оружейный" && common.Item2.Name == "Grizzly");
        var rare = LootPlan.Select(defaults, 0.99, 0.999999);
        Check(rare.Item1.Name == "Рейдовый" && rare.Item2.Name == "Штурмовой комплект" && rare.Item2.Items[0].Id == 363);
        defaults[0].Bundles[0].Items[0].Count = 0;
        Check(Reject(() => LootPlan.Validate(defaults)));
        Console.WriteLine("Границы вероятностей проверены.");
    }
}
