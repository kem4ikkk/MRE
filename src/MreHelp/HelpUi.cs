using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreHelp
{
    internal static class HelpUi
    {
        internal const ushort EffectId = 48625;
        internal const short EffectKey = 14825;
        internal const string EffectGuid = "6e4a31b4-9c87-4f42-8a62-2e1a68e4d203";
        private sealed class Session
        {
            internal Player Player;
            internal HelpCategory Category;
        }
        private static readonly Dictionary<ulong, Session> viewers = new Dictionary<ulong, Session>();
        private static bool started;

        internal static void Start()
        {
            if (started) return;
            EffectManager.onEffectButtonClicked += Button;
            Provider.onServerDisconnected += Disconnected;
            PlayerLife.onPlayerDied += Died;
            started = true;
        }

        internal static void Stop()
        {
            if (started)
            {
                EffectManager.onEffectButtonClicked -= Button;
                Provider.onServerDisconnected -= Disconnected;
                PlayerLife.onPlayerDied -= Died;
                started = false;
            }
            foreach (ulong id in viewers.Keys.ToArray()) Close(id);
        }

        internal static bool Open(UnturnedPlayer player)
        {
            if (!started || player == null || player.Player == null || player.Player.channel == null) return false;
            Player p = player.Player;
            ulong id = player.CSteamID.m_SteamID;
            if (viewers.ContainsKey(id) || p.isPluginWidgetFlagActive(EPluginWidgetFlags.Modal)) return false;
            EffectAsset asset = Assets.find(Guid.Parse(EffectGuid)) as EffectAsset;
            if (asset == null || asset.id != EffectId) return false;
            try
            {
                viewers.Add(id, new Session { Player = p, Category = HelpCategory.Basics });
                p.enablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                EffectManager.SendUIEffect(asset, EffectKey, p.channel.GetOwnerTransportConnection(), true);
                Render(p, HelpCategory.Basics);
                return true;
            }
            catch (Exception error)
            {
                Logger.LogException(error, "[MRE Help] Не удалось открыть окно справки.");
                Close(id);
                return false;
            }
        }

        private static void Button(Player player, string name)
        {
            if (player == null || player.channel == null || player.channel.owner == null || String.IsNullOrEmpty(name)) return;
            ulong id = player.channel.owner.playerID.steamID.m_SteamID;
            Session session;
            if (!viewers.TryGetValue(id, out session) || !ReferenceEquals(session.Player, player)) return;
            if (name == "HelpClose") { Close(id); return; }
            string suffix = name.StartsWith("HelpTab", StringComparison.Ordinal) ? name.Substring("HelpTab".Length) : String.Empty;
            HelpCategory category;
            if (!Enum.TryParse(suffix, false, out category) || !Enum.IsDefined(typeof(HelpCategory), category) || category.ToString() != suffix) return;
            try { session.Category = category; Render(player, category); }
            catch (Exception error) { Logger.LogException(error, "[MRE Help] Не удалось переключить раздел справки."); Close(id); }
        }

        private static void Render(Player player, HelpCategory category)
        {
            var connection = player.channel.GetOwnerTransportConnection();
            var rows = HelpCatalog.For(category, UnturnedPlayer.FromPlayer(player));
            EffectManager.sendUIEffectText(EffectKey, connection, true, "HelpTitle", "MRE / СПРАВКА");
            EffectManager.sendUIEffectText(EffectKey, connection, true, "HelpSubtitle", Subtitle(category));
            foreach (HelpCategory item in Enum.GetValues(typeof(HelpCategory)))
                EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "HelpSelected" + item, item == category);
            for (int i = 0; i < 10; i++)
            {
                string number = (i + 1).ToString("00");
                bool visible = i < rows.Length;
                EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "HelpContent" + number, visible);
                if (!visible) continue;
                EffectManager.sendUIEffectText(EffectKey, connection, true, "HelpCommand" + number, rows[i].Command);
                EffectManager.sendUIEffectText(EffectKey, connection, true, "HelpDescription" + number, rows[i].Description);
            }
            EffectManager.sendUIEffectText(EffectKey, connection, true, "HelpFooterText", "/help · MRE");
        }

        private static string Subtitle(HelpCategory category)
        {
            switch (category)
            {
                case HelpCategory.Travel: return "ПЕРЕМЕЩЕНИЕ";
                case HelpCategory.Groups: return "ГРУППА И ОБМЕН";
                case HelpCategory.Combat: return "ВЫЖИВАНИЕ И PVP";
                default: return "КОМАНДЫ СЕРВЕРА";
            }
        }

        private static void Disconnected(CSteamID id) { Close(id.m_SteamID); }

        private static void Close(ulong id)
        {
            Session session;
            if (!viewers.TryGetValue(id, out session)) return;
            viewers.Remove(id);
            Player player = session.Player;
            try { if (player != null && player.channel != null) EffectManager.ClearEffectByGuid(Guid.Parse(EffectGuid), player.channel.GetOwnerTransportConnection()); }
            catch (Exception error) { Logger.LogException(error, "[MRE Help] Не удалось убрать окно справки."); }
            try { player.disablePluginWidgetFlag(EPluginWidgetFlags.Modal); }
            catch (Exception error) { Logger.LogException(error, "[MRE Help] Не удалось закрыть режим окна."); }
        }

        private static void Died(PlayerLife life, EDeathCause cause, ELimb limb, CSteamID instigator)
        {
            if (life == null || life.channel == null || life.channel.owner == null) return;
            Close(life.channel.owner.playerID.steamID.m_SteamID);
        }
    }
}
