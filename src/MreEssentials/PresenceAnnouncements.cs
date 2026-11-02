using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MreShared;
using Rocket.Unturned;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreEssentials
{
    internal sealed class PresenceAnnouncements
    {
        private readonly Dictionary<ulong, string> online = new Dictionary<ulong, string>();

        internal void Start()
        {
            // При перезагрузке плагина не сообщаем о повторном входе уже подключённых игроков.
            foreach (var client in Provider.clients)
            {
                if (client == null || client.player == null) continue;
                var player = UnturnedPlayer.FromCSteamID(client.playerID.steamID);
                if (player != null) online[client.playerID.steamID.m_SteamID] = CleanName(player.DisplayName);
            }
            U.Events.OnPlayerConnected += Connected;
            Provider.onServerDisconnected += Disconnected;
        }

        internal void Stop()
        {
            U.Events.OnPlayerConnected -= Connected;
            Provider.onServerDisconnected -= Disconnected;
            online.Clear();
        }

        private void Connected(UnturnedPlayer player)
        {
            if (player == null) return;
            ulong id = player.CSteamID.m_SteamID;
            if (id == 0 || online.ContainsKey(id)) return;
            string name = CleanName(player.DisplayName);
            online[id] = name;
            Announce(name + " зашёл на сервер.");
        }

        private void Disconnected(CSteamID steamId)
        {
            string name;
            if (!online.TryGetValue(steamId.m_SteamID, out name)) return;
            online.Remove(steamId.m_SteamID);
            Announce(name + " вышел с сервера.");
        }

        private static void Announce(string message)
        {
            try { MreChat.Say(message); }
            catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка сообщения о подключении игрока."); }
        }

        internal static string CleanName(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return "Игрок";
            var result = new StringBuilder();
            for (int i = 0; i < raw.Length && result.Length < 32; i++)
            {
                char c = raw[i];
                var category = Char.GetUnicodeCategory(c);
                if (Char.IsControl(c) || category == UnicodeCategory.Format || category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator) continue;
                if (Char.IsHighSurrogate(c))
                {
                    if (i + 1 < raw.Length && Char.IsLowSurrogate(raw[i + 1]) && result.Length < 31) { result.Append(c); result.Append(raw[++i]); }
                    continue;
                }
                if (Char.IsLowSurrogate(c)) continue;
                result.Append(c);
            }
            string value = result.ToString().Trim();
            return value.Length == 0 ? "Игрок" : value;
        }
    }
}
