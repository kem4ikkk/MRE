using System;
using System.Text.RegularExpressions;

namespace MreChatStyle
{
    public static class ChatFormatting
    {
        private const string MarkerStart = "\u001eMRECHAT:";
        private static readonly Regex Tags = new Regex(@"<[^<>]{1,200}>", RegexOptions.CultureInvariant);

        public static string Format(string message, bool wasRich)
        {
            string category;
            return Format(message, wasRich, out category);
        }

        public static string Format(string message, bool wasRich, out string category)
        {
            if (message == null) message = String.Empty;
            category = "SYSTEM";
            if (message.StartsWith(MarkerStart, StringComparison.Ordinal))
            {
                int end = message.IndexOf('\u001e', MarkerStart.Length);
                if (end > MarkerStart.Length)
                {
                    category = NormalizeCategory(message.Substring(MarkerStart.Length, end - MarkerStart.Length));
                    message = message.Substring(end + 1).TrimStart();
                }
            }

            if (wasRich) message = Tags.Replace(message, String.Empty);
            message = message.Replace('<', '\u2039').Replace('>', '\u203A').Trim();
            if (TryStripSectionHeader(ref message, "MRE \u00B7 HOME", "HOME")) category = "HOME";
            else if (TryStripSectionHeader(ref message, "MRE \u00B7 TPA", "TPA")) category = "TPA";
            else if (TryStripSectionHeader(ref message, "MRE \u00B7 \u0422\u0415\u041B\u0415\u041F\u041E\u0420\u0422\u0410\u0426\u0418\u042F", "TPA")) category = "TPA";
            else if (TryStripSectionHeader(ref message, "[MRE] \u00B7 HOME", "HOME")) category = "HOME";
            else if (TryStripSectionHeader(ref message, "[MRE] \u00B7 TPA", "TPA")) category = "TPA";
            else if (TryStripSectionHeader(ref message, "[MRE] \u00B7 \u0422\u0415\u041B\u0415\u041F\u041E\u0420\u0422\u0410\u0426\u0418\u042F", "TPA")) category = "TPA";

            string legacyCategory;
            if (TryStripMrePrefix(ref message, out legacyCategory) && category == "SYSTEM") category = legacyCategory;
            message = Regex.Replace(message, @"^MRE\s*:\s*", String.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            message = Regex.Replace(message, @"^\[MRE[^\]]*\]\s*", String.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (category == "SYSTEM" && IsUnknownCommand(message)) category = "SERVER";

            bool admin = category == "ADMIN" || category == "STAFF";
            string labelColor = admin ? "#FF756A" : "#B8D66B";
            string prefix = "<b><color=" + labelColor + ">" + CategoryPrefix(category) + "</color></b>";
            message = message.Trim();
            return prefix + (message.Length == 0 ? String.Empty : " " + message);
        }

        private static bool TryStripMrePrefix(ref string message, out string category)
        {
            string[] names = { "ADMIN", "STAFF", "SERVER", "HOME", "TPA", "KITS", "TRADE", "VAULT", "VOTE", "AIRDROP" };
            foreach (string name in names)
            {
                string prefix = "[MRE-" + name + "]";
                if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                message = message.Substring(prefix.Length).TrimStart();
                category = name;
                return true;
            }
            if (message.StartsWith("[MRE]", StringComparison.OrdinalIgnoreCase))
            {
                message = message.Substring("[MRE]".Length).TrimStart();
                category = "SYSTEM";
                return true;
            }
            category = "SYSTEM";
            return false;
        }

        private static bool TryStripSectionHeader(ref string message, string header, string category)
        {
            if (!message.StartsWith(header, StringComparison.OrdinalIgnoreCase)) return false;
            message = message.Substring(header.Length).TrimStart(' ', '\u00B7', '-', ':');
            return true;
        }

        private static bool IsUnknownCommand(string message)
        {
            return Regex.IsMatch(message ?? String.Empty, @"^command\s+not\s+found[.!]?$",
                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(message ?? String.Empty,
                    "^\\u041A\\u043E\\u043C\\u0430\\u043D\\u0434\\u0430\\s+\\u043D\\u0435\\s+\\u043D\\u0430\\u0439\\u0434\\u0435\\u043D\\u0430[.!]?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string NormalizeCategory(string category)
        {
            switch ((category ?? String.Empty).ToUpperInvariant())
            {
                case "HOME": case "TPA": case "KITS": case "TRADE": case "VAULT": case "SERVER":
                case "VOTE": case "AIRDROP": case "STAFF": case "ADMIN": return category.ToUpperInvariant();
                default: return "SYSTEM";
            }
        }

        private static string CategoryPrefix(string category)
        {
            switch (category)
            {
                case "HOME": case "TPA": case "KITS": case "TRADE": case "VAULT":
                case "VOTE": case "AIRDROP": case "STAFF": case "ADMIN": case "SERVER":
                    return "[MRE-" + category + "]";
                default: return "[MRE]";
            }
        }
    }
}
