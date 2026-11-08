using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreHelp
{
    // Порядок совпадает с согласованной навигацией макета.
    internal enum MenuTab { Home, Help, Team, Events, Stats, Links, Settings, BattlePass, Rewards, Reports }

    internal static class HelpUi
    {
        internal const ushort EffectId = 48625;
        internal const short EffectKey = 14825;
        internal const string EffectGuid = "6e4a31b4-9c87-4f42-8a62-2e1a68e4d203";
        private static readonly MenuTab[] tabs = (MenuTab[])Enum.GetValues(typeof(MenuTab));
        private sealed class Session
        {
            internal Player Player;
            internal MenuTab Tab;
            internal bool Notifications;
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

        internal static bool Open(UnturnedPlayer player, MenuTab tab)
        {
            if (!started || player == null || player.Player == null || player.Player.channel == null || player.Player.channel.owner == null || !Enum.IsDefined(typeof(MenuTab), tab)) return false;
            Player p = player.Player;
            ulong id = player.CSteamID.m_SteamID;
            Session existing;
            if (viewers.TryGetValue(id, out existing))
            {
                if (!ReferenceEquals(existing.Player, p)) { Close(id); return false; }
                existing.Tab = tab;
                existing.Notifications = false;
                try { Render(existing); return true; }
                catch (Exception error)
                {
                    Logger.LogException(error, "[MRE Menu] Не удалось переключить вкладку.");
                    Close(id);
                    return false;
                }
            }
            // Чужой модальный UI не закрываем и не перехватываем.
            if (p.isPluginWidgetFlagActive(EPluginWidgetFlags.Modal)) return false;
            EffectAsset asset = Assets.find(Guid.Parse(EffectGuid)) as EffectAsset;
            if (asset == null || asset.id != EffectId) return false;
            try
            {
                var session = new Session { Player = p, Tab = tab };
                viewers.Add(id, session);
                p.enablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                EffectManager.SendUIEffect(asset, EffectKey, p.channel.GetOwnerTransportConnection(), true);
                Render(session);
                return true;
            }
            catch (Exception error)
            {
                Logger.LogException(error, "[MRE Menu] Не удалось открыть меню.");
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
            if (name == "MenuClose") { Close(id); return; }
            if (name == "MenuNotifications") session.Notifications = !session.Notifications;
            else if (name == "MenuNotificationsClose") session.Notifications = false;
            else
            {
                int index = Array.IndexOf(tabs, session.Tab);
                if (name == "MenuHomeHelp" && session.Tab == MenuTab.Home) session.Tab = MenuTab.Help;
                else if (name == "MenuHomeTeam" && session.Tab == MenuTab.Home) session.Tab = MenuTab.Team;
                else if (name == "MenuHomeEvents" && session.Tab == MenuTab.Home) session.Tab = MenuTab.Events;
                else if (name == "MenuHomeSettings" && session.Tab == MenuTab.Home) session.Tab = MenuTab.Settings;
                else if (name == "MenuPrevious") session.Tab = tabs[(index + tabs.Length - 1) % tabs.Length];
                else if (name == "MenuNext") session.Tab = tabs[(index + 1) % tabs.Length];
                else
                {
                    // Только точные имена наших кнопок, без произвольных Enum.Parse.
                    int target = Array.FindIndex(tabs, candidate => name == "MenuTab" + candidate);
                    if (target < 0) return;
                    session.Tab = tabs[target];
                }
                session.Notifications = false;
            }
            try { Render(session); }
            catch (Exception error)
            {
                Logger.LogException(error, "[MRE Menu] Не удалось отобразить вкладку.");
                Close(id);
            }
        }

        private static void Render(Session session)
        {
            var connection = session.Player.channel.GetOwnerTransportConnection();
            foreach (MenuTab tab in tabs)
            {
                bool selected = session.Tab == tab;
                EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSelected" + tab, selected);
                EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuPage" + tab, selected);
            }
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuNotificationsPanel", session.Notifications);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuNotificationsSelected", session.Notifications);
            if (session.Tab == MenuTab.Home) RenderHome(session);
        }

        private static void RenderHome(Session session)
        {
            var connection = session.Player.channel.GetOwnerTransportConnection();
            HomeData data = HomeData.Read(session.Player);
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuHomePlayerName", data.PlayerName);
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuHomeSteamId", data.SteamId);
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuHomeGroup", data.Group);
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuHomeGroupCount", data.GroupCount);
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuHomeServerName", data.ServerName);
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuHomeOnline", data.Online);
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuHomeMap", data.Map);
        }

        internal static void RefreshHome()
        {
            foreach (var viewer in viewers.ToArray())
            {
                if (viewer.Value.Tab != MenuTab.Home) continue;
                try { RenderHome(viewer.Value); }
                catch (Exception error)
                {
                    Logger.LogException(error, "[MRE Menu] Не удалось обновить Главную.");
                    Close(viewer.Key);
                }
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
            catch (Exception error) { Logger.LogException(error, "[MRE Menu] Не удалось убрать окно меню."); }
            try { if (player != null) player.disablePluginWidgetFlag(EPluginWidgetFlags.Modal); }
            catch (Exception error) { Logger.LogException(error, "[MRE Menu] Не удалось закрыть модальный режим."); }
        }

        private static void Died(PlayerLife life, EDeathCause cause, ELimb limb, CSteamID instigator)
        {
            if (life == null || life.channel == null || life.channel.owner == null) return;
            Close(life.channel.owner.playerID.steamID.m_SteamID);
        }
    }
}
