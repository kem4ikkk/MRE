using System;
using System.Collections.Generic;
using Rocket.Core;
using Rocket.Unturned.Player;

namespace MreHelp
{
    internal enum HelpCategory { Basics, Travel, Groups, Combat }

    internal sealed class HelpRow
    {
        internal readonly string Command;
        internal readonly string Description;
        internal HelpRow(string command, string description) { Command = command; Description = description; }
    }

    internal static class HelpCatalog
    {
        internal static HelpRow[] For(HelpCategory category, UnturnedPlayer player)
        {
            var rows = new List<HelpRow>();
            switch (category)
            {
                case HelpCategory.Travel:
                    Add(rows, player, "home", "Переместиться к своей кровати; /home cancel отменяет ожидание.", "home");
                    Add(rows, player, "homes", "Показать сохранённые кровати.", "homes");
                    Add(rows, player, "tpa", "Запросить перенос: /tpa <игрок>; принять — /tpa a, отказать — /tpa d.", "tpa");
                    Add(rows, player, "destroyhome", "Удалить сохранённую кровать.", "destroyhome");
                    Add(rows, player, "renamehome", "Переименовать сохранённую кровать.", "renamehome");
                    break;
                case HelpCategory.Groups:
                    Add(rows, player, "trade", "Начать обмен: /trade <игрок>; проверить предметы: /trade list.", "mre.trade");
                    Add(rows, player, "s", "Открыть личное хранилище.", "mre.vault");
                    break;
                case HelpCategory.Combat:
                    rows.Add(new HelpRow("MRE · MAX4", "В одной группе сервера может быть до четырёх игроков."));
                    rows.Add(new HelpRow("Безопасное перемещение", "Перед /home и /tpa проверьте место назначения и обстановку вокруг."));
                    break;
                default:
                    rows.Add(new HelpRow("/help", "Открыть эту справку. Если UI недоступен, команды покажутся в чате."));
                    Add(rows, player, "kits", "Посмотреть доступные наборы.", "mre.kit");
                    Add(rows, player, "kit", "Получить набор: /kit start.", "mre.kit");
                    Add(rows, player, "vote", "Голосование за день: /vote day, /vote yes, /vote no.", "mre.vote");
                    Add(rows, player, "whenairdrop", "Узнать время до следующего аирдропа.", "mre.whenairdrop");
                    Add(rows, player, "mytrustscore", "/trustscore", "Посмотреть свой trustscore.", "mre.trustscore");
                    break;
            }
            return rows.ToArray();
        }

        private static void Add(List<HelpRow> rows, UnturnedPlayer player, string command, string description, string permission)
        {
            Add(rows, player, command, command, description, permission);
        }

        private static void Add(List<HelpRow> rows, UnturnedPlayer player, string lookup, string display, string description, string permission)
        {
            if (player == null || R.Commands.GetCommand(lookup) == null || !Allowed(player, permission)) return;
            string visibleCommand = display.StartsWith("/", StringComparison.Ordinal) ? display : "/" + display;
            rows.Add(new HelpRow(visibleCommand, description));
        }

        internal static bool Allowed(UnturnedPlayer player, string permission)
        {
            try { return R.Permissions.HasPermission(player, new List<string> { permission }); }
            catch (Exception) { return false; }
        }
    }
}
