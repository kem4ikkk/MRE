using System;
using MreChatStyle;

internal static class Program
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Ошибка форматирования чата."); }
    private static void Main()
    {
        Check(ChatFormatting.Format("Наборы: start, vip", false) == "<color=#B8D66B>Наборы:</color> start, vip");
        Check(ChatFormatting.Format("MRE: привет", false) == "<color=#B8D66B>MRE:</color> Привет");
        Check(ChatFormatting.Format("<color=red>MRE · TPA</color> привет", true) == "<color=#B8D66B>TPA:</color> привет");
        Check(ChatFormatting.Format("[MRE · НАБОРЫ] готово", false) == "<color=#B8D66B>Наборы:</color> готово");
        Check(ChatFormatting.Format("Наборы: <color=red>bad</color>", false).Contains("‹color=red›"));
        Check(!ChatFormatting.Format("<b>MRE:</b> <color=red>bad</color>", true).Contains("<color=red>"));
        Check(ChatFormatting.Format(null, false) == "<color=#B8D66B>MRE:</color>");
        Check(ChatFormatting.Format("MRE: \u0432 \u043f\u0443\u0442\u0438", false) == "<color=#B8D66B>MRE:</color> \u0412 \u043f\u0443\u0442\u0438");
        Check(ChatFormatting.Format("<b>MRE:</b> <color=red>\u043d\u0435\u0442 \u043f\u0440\u0430\u0432</color>", true) == "<color=#B8D66B>MRE:</color> \u041d\u0435\u0442 \u043f\u0440\u0430\u0432");
        Check(ChatFormatting.Format("MRE: /airdrop", false).EndsWith(" /airdrop"));
        Check(ChatFormatting.Format("MRE: mre.airdrop.admin", false).EndsWith(" mre.airdrop.admin"));
        Check(ChatFormatting.Format("MRE: https://example.com", false).EndsWith(" https://example.com"));
        Check(ChatFormatting.Format("MRE: player_123", false).EndsWith(" player_123"));
        Check(ChatFormatting.Format("MRE: ошибка: нет прав", false).EndsWith(" Ошибка: нет прав"));
        Check(ChatFormatting.Format("ошибка: нет прав", false).EndsWith(" Ошибка: нет прав"));
        Check(ChatFormatting.Format("MRE: mre.airdrop.admin.", false).EndsWith(" mre.airdrop.admin."));
        Check(ChatFormatting.Format("MRE: \u00e9clair", false).EndsWith(" \u00c9clair"));
        Console.WriteLine("Проверки форматирования чата пройдены.");
    }
}
