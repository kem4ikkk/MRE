using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MreChatStyle
{
    public static class ChatFormatting
    {
        private static readonly Regex Tags = new Regex(@"<[^<>]{1,200}>", RegexOptions.CultureInvariant);
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
            return "<color=#B8D66B>" + label + "</color>" + (body.Length == 0 ? "" : " " + body);
        }
    }
}
