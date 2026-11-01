using MreShared;
using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;

namespace MreKits
{
    public sealed class CommandKit : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "kit"; } }
        public string Help { get { return "Наборы MRE: /kit start — получить стартовый набор; /kits — состав и доступность."; } }
        public string Syntax { get { return "<имя>"; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.kit" }; } }
        public void Execute(IRocketPlayer caller, string[] command)
        {
            var player = caller as UnturnedPlayer;
            var plugin = MreKitsPlugin.Instance;
            if (player == null || plugin == null) return;
            if (command.Length != 1) { plugin.List(player); return; }
            MreChat.Say(player, plugin.Claim(player, command[0]));
        }
    }
    public sealed class CommandKits : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "kits"; } }
        public string Help { get { return "Список доступных наборов MRE."; } }
        public string Syntax { get { return ""; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.kit" }; } }
        public void Execute(IRocketPlayer caller, string[] command)
        {
            var player = caller as UnturnedPlayer;
            if (player != null && MreKitsPlugin.Instance != null) MreKitsPlugin.Instance.List(player);
        }
    }
}
