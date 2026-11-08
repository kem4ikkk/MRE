using System.Collections.Generic;
using MreShared;
using Rocket.API;
using Rocket.Unturned.Player;

namespace MreHelp
{
    public sealed class CommandMenu : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "menu"; } }
        public string Help { get { return "Открыть меню сервера MRE."; } }
        public string Syntax { get { return ""; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string>(); } }

        public void Execute(IRocketPlayer caller, string[] args)
        {
            var player = caller as UnturnedPlayer;
            if (player == null) return;
            if (HelpUi.Open(player, MenuTab.Home)) return;
            MreChat.SayServer(player, "МЕНЮ MRE · ИНФОРМАЦИЯ");
            MreChat.SayServer(player, "Команды: /help. Дом и перемещение: /homes, /home, /tpa.");
            MreChat.SayServer(player, "Наборы: /kits, /kit <имя>. Обмен: /trade <игрок>. Хранилище: /s.");
        }
    }
}
