using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MreChatStyle
{
    public static class ChatFormatting
    {
        private static readonly Regex Tags = new Regex(@"<[^<>]{1,200}>", RegexOptions.CultureInvariant);
        private static string CapitalizeBody(string body)
        {
            if (body.Length == 0 || !char.IsLower(body, 0)) return body;
            int end = 0;
            while (end < body.Length && !char.IsWhiteSpace(body[end])) end++;
            string first = body.Substring(0, end).TrimEnd('.', ',', ':', ';', '!', '?');
            // Keep technical names and URLs intact, including IDs with digits or separators.
            if (first.IndexOfAny(new[] { '/', ':', '_', '@', '#', '=', '\\' }) >= 0 ||
                (first.IndexOf('.') >= 0 && !first.EndsWith(".", StringComparison.Ordinal))) return body;
            for (int i = 0; i < first.Length; i++)
                if (char.IsDigit(first[i]) || char.IsUpper(first[i])) return body;
            return char.ToUpperInvariant(body[0]) + body.Substring(1);
        }
        public static string Format(string message, bool wasRich)
        {
            if (message == null) message = String.Empty;
            // Текст из чужих плагинов и пользовательские имена не должны вставлять разметку.
            if (wasRich) message = Tags.Replace(message, String.Empty);
            message = message.Replace('<', '‹').Replace('>', '›').Trim();
            string label = "MRE:";
            string body = message;
            if (message.StartsWith("Наборы:", StringComparison.OrdinalIgnoreCase)) {
                label = "Наборы:"; body = message.Substring("Наборы:".Length).TrimStart();
            } else if (message.StartsWith("[MRE · НАБОРЫ]", StringComparison.OrdinalIgnoreCase)) {
                label = "Наборы:"; body = message.Substring("[MRE · НАБОРЫ]".Length).TrimStart();
            } else if (message.StartsWith("MRE:", StringComparison.OrdinalIgnoreCase)) {
                body = message.Substring(4).TrimStart();
            } else if (message.StartsWith("MRE · TPA", StringComparison.OrdinalIgnoreCase)) {
                label = "TPA:"; body = message.Substring("MRE · TPA".Length).TrimStart();
            } else if (message.StartsWith("MRE · HOME", StringComparison.OrdinalIgnoreCase)) {
                label = "HOME:"; body = message.Substring("MRE · HOME".Length).TrimStart();
            }
            if (label == "MRE:") body = CapitalizeBody(body);
            return "<color=#B8D66B>" + label + "</color>" + (body.Length == 0 ? "" : " " + body);
        }
    }
}
