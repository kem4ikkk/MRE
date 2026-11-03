using System.Collections.Generic;
using MreShared;
using Rocket.API;
using Rocket.Unturned.Player;

namespace MreTrade
{
    public sealed class CommandTrade : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "trade"; } }
        public string Help { get { return "Обмен предметами с другим игроком."; } }
        public string Syntax { get { return "<игрок|accept|list|offer|remove|confirm|cancel>"; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.trade" }; } }
        public void Execute(IRocketPlayer caller, string[] args)
        {
            var player = caller as UnturnedPlayer;
            if (player == null) return;
            if (MreTradePlugin.Instance == null) { MreChat.Say(player, "MRE: обмен временно недоступен."); return; }
            MreTradePlugin.Instance.Handle(player, args);
        }
    }
}
