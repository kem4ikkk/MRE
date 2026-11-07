using System.Collections.Generic;

namespace Rocket.API
{
    public enum AllowedCaller { Player, Console, Both }
    public interface IRocketPlayer { }
    public interface IRocketCommand
    {
        AllowedCaller AllowedCaller { get; }
        string Name { get; }
        string Help { get; }
        string Syntax { get; }
        List<string> Aliases { get; }
        List<string> Permissions { get; }
        void Execute(IRocketPlayer caller, string[] args);
    }
}
namespace MreShared
{
    internal static class MreChat
    {
        internal static int Count;
        internal static void Say(Rocket.API.IRocketPlayer player, string text) { Count++; }
        internal static void SayAdmin(Rocket.API.IRocketPlayer player, string text) { Say(player, text); }
    }
}
namespace MreAirdrop
{
    public sealed class MreAirdropPlugin
    {
        public static MreAirdropPlugin Instance;
        public int Calls;
        public Rocket.API.IRocketPlayer Caller;
        public void LaunchManual(Rocket.API.IRocketPlayer caller) { Calls++; Caller = caller; }
    }
}
