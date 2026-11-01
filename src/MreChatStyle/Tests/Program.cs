using System;
using MreChatStyle;

internal static class Program
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Ошибка форматирования чата."); }
    private static void Main()
    {
        Check(ChatFormatting.Format("Наборы: start, vip", false) == "<color=#B8D66B>Наборы:</color> start, vip");
        Check(ChatFormatting.Format("MRE: привет", false) == "<color=#B8D66B>MRE:</color> привет");
        Check(ChatFormatting.Format("<color=red>MRE · TPA</color> привет", true) == "<color=#B8D66B>TPA:</color> привет");
        Check(ChatFormatting.Format("[MRE · НАБОРЫ] готово", false) == "<color=#B8D66B>Наборы:</color> готово");
        Check(ChatFormatting.Format("Наборы: <color=red>bad</color>", false).Contains("‹color=red›"));
        Check(!ChatFormatting.Format("<b>MRE:</b> <color=red>bad</color>", true).Contains("<color=red>"));
        Check(ChatFormatting.Format(null, false) == "<color=#B8D66B>MRE:</color>");
        Console.WriteLine("Проверки форматирования чата пройдены.");
    }
}
