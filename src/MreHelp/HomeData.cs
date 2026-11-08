using System;
using System.Globalization;
using System.Linq;
using System.Text;
using SDG.Unturned;

namespace MreHelp
{
    // Читается на игровом потоке; данные отправляются только владельцу этой сессии UI.
    internal sealed class HomeData
    {
        internal string PlayerName, SteamId, Group, GroupCount, ServerName, Online, Map;

        internal static HomeData Read(Player player)
        {
            var id = player.channel.owner.playerID;
            ulong groupId = player.quests == null ? 0 : player.quests.groupID.m_SteamID;
            GroupInfo group = groupId == 0 ? null : GroupManager.getGroupInfo(player.quests.groupID);
            int groupOnline = groupId == 0 || Provider.clients == null ? 0 : Provider.clients.Count(
                client => client != null && client.player != null && client.player.quests != null && client.player.quests.groupID.m_SteamID == groupId);
            int online = Provider.clients == null ? 0 : Provider.clients.Count(client => client != null && client.player != null);
            string server = (Provider.serverName ?? "").Split('|')[0].Trim();
            return new HomeData
            {
                PlayerName = Safe(id.characterName, 32, "Игрок"),
                SteamId = id.steamID.m_SteamID.ToString(CultureInfo.InvariantCulture),
                Group = groupId == 0 ? "Без группы" : Safe(group == null ? null : group.name, 36, "Группа #" + groupId),
                GroupCount = groupId == 0 ? "Играете самостоятельно" : "В сети: " + groupOnline.ToString(CultureInfo.InvariantCulture),
                ServerName = Safe(server, 48, "MRE"),
                Online = online.ToString(CultureInfo.InvariantCulture) + " / " + Provider.maxPlayers.ToString(CultureInfo.InvariantCulture),
                Map = Safe(Level.info == null ? null : Level.info.name, 32, "—")
            };
        }

        internal static string Safe(string value, int limit, string fallback)
        {
            var text = new StringBuilder();
            foreach (char c in value ?? String.Empty)
            {
                if (Char.IsControl(c) || Char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
                text.Append(c == '<' ? '‹' : c == '>' ? '›' : c);
            }
            string result = text.ToString().Trim();
            if (result.Length == 0) return fallback;
            if (result.Length <= limit) return result;
            int end = limit;
            if (end > 0 && Char.IsHighSurrogate(result[end - 1])) end--;
            return result.Substring(0, end).TrimEnd() + "…";
        }
    }
}
