using System;
using MreChatStyle;

internal static class Program
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Chat formatting check failed: " + name);
    }

    private static void Main()
    {
        string category;
        string normal = "<b><color=#B8D66B>[MRE]</color></b>";
        string admin = "<b><color=#FF756A>[MRE-ADMIN]</color></b>";

        string trade = ChatFormatting.Format("\u001eMRECHAT:TRADE\u001eTrade is unavailable during combat.", false, out category);
        Check(category == "TRADE" && trade == normal + " <b><color=#D6C78E>\u041E\u0411\u041C\u0415\u041D</color></b> Trade is unavailable during combat.", "trade category");
        Check(ChatFormatting.Format("\u001eMRECHAT:ADMIN\u001e[MRE-ADMIN] /admin help", false, out category) == admin + " <b><color=#D6C78E>\u0410\u0414\u041C\u0418\u041D</color></b> /admin help", "admin category");
        Check(ChatFormatting.Format("\u001eMRECHAT:STAFF\u001e[MRE-ADMIN] staff profile opened", false, out category).StartsWith(admin + " <b><color=#D6C78E>\u041F\u0415\u0420\u0421\u041E\u041D\u0410\u041B</color></b>", StringComparison.Ordinal), "staff category");
        Check(category == "STAFF", "staff metadata");
        Check(ChatFormatting.Format("<color=green><b>MRE \u00B7 HOME</b></color> Teleport completed.", true, out category) == normal + " <b><color=#D6C78E>\u0414\u041E\u041C</color></b> Teleport completed.", "rich HOME header");
        Check(category == "HOME", "home metadata");
        Check(ChatFormatting.Format("[MRE] \u00B7 \u0422\u0415\u041B\u0415\u041F\u041E\u0420\u0422\u0410\u0426\u0418\u042F /tpa player", false, out category) == normal + " <b><color=#D6C78E>TPA</color></b> /tpa player", "legacy TPA header");
        Check(category == "TPA", "tpa metadata");
        Check(ChatFormatting.Format("MRE \u00B7 \u0422\u0415\u041B\u0415\u041F\u041E\u0420\u0422\u0410\u0426\u0418\u042F /tpa player", false, out category) == normal + " <b><color=#D6C78E>TPA</color></b> /tpa player", "direct legacy TPA header");
        Check(category == "TPA", "direct tpa metadata");
        Check(ChatFormatting.Format("MRE: old broadcast", false) == normal + " old broadcast", "legacy MRE prefix");
        Check(ChatFormatting.Format("[MRE \u00B7 \u041D\u0410\u0411\u041E\u0420\u042B] old kit notice", false) == normal + " old kit notice", "legacy kits prefix");
        Check(ChatFormatting.Format("<b>MRE:</b> <color=red>bad</color>", true).IndexOf("<color=red>", StringComparison.Ordinal) < 0, "incoming rich tags");
        Check(ChatFormatting.Format("<script>text</script>", false).Contains("\u2039script\u203a"), "unsafe angle brackets");
        Check(ChatFormatting.Format("\u001eMRECHAT:UNKNOWN\u001eplain", false, out category) == normal + " plain" && category == "SYSTEM", "unknown category");
        Check(ChatFormatting.Format(null, false) == normal, "null message");
        Console.WriteLine("Chat formatting checks passed.");
    }
}
