using System;
using MreAirdrop;

internal static class Program
{
    private static void Check(bool value) { if (!value) throw new Exception("Проверка аирдропа не пройдена."); }
    private static bool Reject(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
    private sealed class FakePlayer : Rocket.API.IRocketPlayer { }
    private static void CheckCommand()
    {
        Rocket.API.IRocketCommand command = new CommandAirdrop();
        Check(command.Name == "airdrop");
        Check(command.Aliases.Count == 1 && command.Aliases[0] == "mreairdrop");
        Check(command.AllowedCaller == Rocket.API.AllowedCaller.Both);
        Check(command.Permissions.Count == 1 && command.Permissions[0] == "mre.airdrop.admin");
        var player = new FakePlayer();
        var plugin = new MreAirdropPlugin(); MreAirdropPlugin.Instance = plugin;
        command.Execute(player, new string[0]);
        Check(plugin.Calls == 1 && Object.ReferenceEquals(plugin.Caller, player));
        command.Execute(player, new[] { "unexpected" });
        Check(plugin.Calls == 1 && MreShared.MreChat.Count == 1);
        MreAirdropPlugin.Instance = null;
        command.Execute(player, new string[0]);
        Check(plugin.Calls == 1 && MreShared.MreChat.Count == 2);
    }
    private static void Main()
    {
        CheckCommand();
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
        Check(rare.Item1.Name == "Рейдовый" && rare.Item2.Name == "Shadowstalker Mk. II" && rare.Item2.Items[0].Id == 1441);
        defaults[0].Bundles[0].Items[0].Count = 0;
        Check(Reject(() => LootPlan.Validate(defaults)));
        Console.WriteLine("Границы вероятностей и контракт команды аирдропа проверены.");
    }
}
