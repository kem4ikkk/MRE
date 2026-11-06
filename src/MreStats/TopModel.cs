using System;
using System.Globalization;
using System.Text;

namespace MreStats
{
    public enum TopCategory { Kills, Deaths, Kd, Zombies, Time }
    public static class TopModel
    {
        public static bool ParseCategory(string[] args, out TopCategory category)
        {
            category = TopCategory.Kills;
            if (args == null || args.Length == 0) return true;
            if (args.Length != 1) return false;
            switch ((args[0] ?? "").ToLowerInvariant()) {
                case "kills": category = TopCategory.Kills; return true;
                case "deaths": category = TopCategory.Deaths; return true;
                case "kd": category = TopCategory.Kd; return true;
                case "zombies": category = TopCategory.Zombies; return true;
                case "time": category = TopCategory.Time; return true;
                default: return false;
            }
        }
        public static string CategoryName(TopCategory category)
        {
            switch (category) {
                case TopCategory.Kills: return "убийства игроков";
                case TopCategory.Deaths: return "смерти";
                case TopCategory.Kd: return "K/D";
                case TopCategory.Zombies: return "убитые зомби";
                case TopCategory.Time: return "время на сервере";
                default: throw new ArgumentOutOfRangeException("category");
            }
        }
        public static double Value(StatsSnapshot row, TopCategory category)
        {
            switch (category) {
                case TopCategory.Kills: return row.Kills;
                case TopCategory.Deaths: return row.Deaths;
                case TopCategory.Kd: return row.Kd;
                case TopCategory.Zombies: return row.Zombies ?? 0;
                case TopCategory.Time: return row.SecondsPlayed;
                default: throw new ArgumentOutOfRangeException("category");
            }
        }
        public static string Format(StatsSnapshot row, TopCategory category)
        {
            if (category == TopCategory.Kd) return row.Kd.ToString("0.00", CultureInfo.InvariantCulture);
            if (category == TopCategory.Time) return (row.SecondsPlayed / 3600).ToString(CultureInfo.InvariantCulture) + " ч " + (row.SecondsPlayed % 3600 / 60).ToString(CultureInfo.InvariantCulture) + " мин";
            if (category == TopCategory.Zombies && !row.Zombies.HasValue) return "—";
            return Value(row, category).ToString("0", CultureInfo.InvariantCulture);
        }
        public static string SafeName(string value, ulong id)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "") {
                var kind = Char.GetUnicodeCategory(c);
                if (Char.IsControl(c) || Char.IsSurrogate(c) || kind == UnicodeCategory.Format || kind == UnicodeCategory.LineSeparator || kind == UnicodeCategory.ParagraphSeparator || c == '<' || c == '>') continue;
                result.Append(c); if (result.Length == 32) break;
            }
            string cleaned = result.ToString().Trim(); return cleaned.Length == 0 ? "Игрок" : cleaned;
        }
    }
}
