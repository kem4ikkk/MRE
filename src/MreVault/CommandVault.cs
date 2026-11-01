using System.Collections.Generic;
using MreShared;
using Rocket.API;
using Rocket.Unturned.Player;

namespace MreVault
{
    public sealed class CommandVault : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "s"; } }
        public string Help { get { return "Личное хранилище."; } }
        public string Syntax { get { return ""; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.vault" }; } }
        public void Execute(IRocketPlayer caller, string[] args)
        {
            var p = caller as UnturnedPlayer;
            if (p == null) return;
            if (args.Length != 0) { MreChat.Say(p, "MRE: использование: /s."); return; }
            if (MreVaultPlugin.Instance == null) { MreChat.Say(p, "MRE: хранилище недоступно."); return; }
            MreVaultPlugin.Instance.Open(p);
        }
    }
}
