using System.Collections.Generic;
using MreShared;
using Rocket.API;
using Rocket.Core;
using Rocket.Unturned.Player;

namespace MreHelp
{
    /// <summary>Public command guide for MRE. Staff commands are intentionally omitted.</summary>
    public sealed class CommandHelp : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "help"; } }
        public string Help { get { return "Справка по командам MRE."; } }
        public string Syntax { get { return ""; } }
        public List<string> Aliases { get { return new List<string> { "mrehelp" }; } }
        public List<string> Permissions { get { return new List<string>(); } }

        public void Execute(IRocketPlayer caller, string[] args)
        {
            var player = caller as UnturnedPlayer;
            if (player == null) return;

            if (HelpUi.Open(player, MenuPrimary.Main, MenuSection.Commands)) return;
            SendChatHelp(player);
        }

        private static void SendChatHelp(UnturnedPlayer player)
        {
            MreChat.SayServer(player, "КОМАНДЫ MRE · ПЕРЕМЕЩЕНИЕ");
            Add(player, "home", "перемещение к своей кровати; /home cancel отменяет ожидание", "home");
            Add(player, "homes", "список сохранённых кроватей", "homes");
            Add(player, "tpa <игрок>", "запросить телепортацию; /tpa a принять, /tpa d отклонить", "tpa");

            MreChat.SayServer(player, "КОМАНДЫ MRE · ИГРОВОЕ");
            Add(player, "kits", "посмотреть доступные наборы", "mre.kit");
            Add(player, "kit start", "получить стартовый набор", "mre.kit");
            Add(player, "vote day", "начать голосование за день ночью; голос: /vote yes или /vote no", "mre.vote");
            Add(player, "whenairdrop", "узнать время до следующего аирдропа", "mre.whenairdrop");
            Add(player, "trade <игрок>", "начать обмен; /trade list показывает предложение", "mre.trade");
            Add(player, "s", "открыть личное хранилище", "mre.vault");
            Add(player, "trustscore", "посмотреть свой trustscore", "mre.trustscore");
            MreChat.SayServer(player, "Подробнее о командах: /<команда>. Администраторские команды здесь не отображаются.");
        }

        private static void Add(UnturnedPlayer player, string command, string description, string permission)
        {
            if (R.Commands.GetCommand(command.Split(' ')[0]) == null) return;
            if (!string.IsNullOrEmpty(permission) && !R.Permissions.HasPermission(player, new List<string> { permission })) return;
            MreChat.SayServer(player, "/" + command + " — " + description + ".");
        }
    }
}
