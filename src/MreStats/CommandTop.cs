using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using UnityEngine;

namespace MreStats
{
    public sealed class CommandTop : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "top"; } }
        public string Help { get { return "Топ сезона по убийствам игроков"; } }
        public string Syntax { get { return "[kills|deaths|kd|zombies|time]"; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.top" }; } }
        public void Execute(IRocketPlayer caller, string[] args)
        {
            var player = caller as UnturnedPlayer; if (player == null) return;
            var plugin = MreStatsPlugin.Instance;
            if (plugin == null) { UnturnedChat.Say(player, "Статистика недоступна.", Color.yellow); return; }
            TopCategory category;
            if (!TopModel.ParseCategory(args, out category)) { UnturnedChat.Say(player, "Использование: /top [kills|deaths|kd|zombies|time]", Color.yellow); return; }
            var rows = plugin.GetTop(category, 10);
            if (rows.Length == 0) { UnturnedChat.Say(player, "В этом сезоне пока нет данных.", Color.yellow); return; }
            UnturnedChat.Say(player, "Рейтинг сезона: " + TopModel.CategoryName(category) + ". Данные с момента включения статистики.", Color.yellow);
            for (int i = 0; i < rows.Length; i++)
                UnturnedChat.Say(player, (i + 1) + ". " + TopModel.SafeName(rows[i].Name, rows[i].SteamId) + " — " + TopModel.Format(rows[i], category), Color.white);
        }
    }
}
