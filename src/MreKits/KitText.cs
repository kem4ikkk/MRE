using System;
using System.Text;

namespace MreKits
{
    // Обычный текст: не зависит от поддержки rich text игровым чатом.
    public static class KitText
    {
        public const string Prefix = "[MRE · НАБОРЫ] ";

        public static string Duration(double totalSeconds)
        {
            int seconds = (int)Math.Ceiling(Math.Max(0, totalSeconds));
            int days = seconds / 86400;
            int hours = seconds % 86400 / 3600;
            int minutes = seconds % 3600 / 60;
            if (days > 0) return days + " д." + (hours > 0 ? " " + hours + " ч." : "");
            if (hours > 0) return hours + " ч." + (minutes > 0 ? " " + minutes + " мин." : "");
            if (minutes > 0) return minutes + " мин." + (seconds % 60 > 0 ? " " + seconds % 60 + " с." : "");
            return seconds + " с.";
        }

        public static string Description(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return "Набор снаряжения";
            var result = new StringBuilder();
            foreach (char c in raw)
            {
                if (Char.IsControl(c) || Char.IsWhiteSpace(c))
                {
                    if (result.Length > 0 && result[result.Length - 1] != ' ') result.Append(' ');
                }
                else result.Append(c == '<' ? '‹' : c == '>' ? '›' : c);
                if (result.Length >= 90) break;
            }
            return result.ToString().Trim();
        }

        public static string Status(ClaimRecord record, DateTime nowUtc)
        {
            if (record != null && record.Status == "pending") return "Выдача проверяется администратором";
            if (record != null && record.NextClaimUtc > nowUtc)
                return "Через " + Duration((record.NextClaimUtc - nowUtc).TotalSeconds);
            return "Доступен сейчас";
        }
    }
}
