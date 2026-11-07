using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreHelp
{
    internal enum MenuPrimary { Main, Progress, Settings }
    internal enum MenuSection { Information, Group, Commands, Links, Rewards, Top }

    internal static class HelpUi
    {
        internal const ushort EffectId = 48625;
        internal const short EffectKey = 14825;
        internal const string EffectGuid = "6e4a31b4-9c87-4f42-8a62-2e1a68e4d203";

        private sealed class Session
        {
            internal Player Player;
            internal MenuPrimary Primary;
            internal MenuSection Section;
            internal int Page;
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

        internal static bool Open(UnturnedPlayer player, MenuPrimary primary, MenuSection section)
        {
            if (!started || player == null || player.Player == null || player.Player.channel == null) return false;
            Player p = player.Player;
            ulong id = player.CSteamID.m_SteamID;
            Session existing;
            if (viewers.TryGetValue(id, out existing))
            {
                if (!ReferenceEquals(existing.Player, p)) { Close(id); return false; }
                existing.Primary = primary;
                existing.Section = section;
                existing.Page = 0;
                try { Render(existing); return true; }
                catch (Exception error)
                {
                    Logger.LogException(error, "[MRE Menu] Не удалось переключить раздел меню.");
                    Close(id);
                    return false;
                }
            }
            if (p.isPluginWidgetFlagActive(EPluginWidgetFlags.Modal)) return false;
            EffectAsset asset = Assets.find(Guid.Parse(EffectGuid)) as EffectAsset;
            if (asset == null || asset.id != EffectId) return false;
            try
            {
                var session = new Session { Player = p, Primary = primary, Section = section };
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
            if (name == "MenuCommandsPrevious" || name == "MenuCommandsNext")
            {
                if (session.Primary != MenuPrimary.Main || session.Section != MenuSection.Commands) return;
                MenuRow[] rows = Rows(session, UnturnedPlayer.FromPlayer(player));
                int pageCount = Math.Max(1, (rows.Length + 9) / 10);
                session.Page = name == "MenuCommandsNext"
                    ? (session.Page + 1) % pageCount
                    : (session.Page + pageCount - 1) % pageCount;
                try { Render(session); }
                catch (Exception error) { Logger.LogException(error, "[MRE Menu] Не удалось перелистнуть команды."); Close(id); }
                return;
            }

            switch (name)
            {
                case "MenuTopMain": session.Primary = MenuPrimary.Main; session.Section = MenuSection.Information; break;
                case "MenuTopProgress": session.Primary = MenuPrimary.Progress; session.Section = MenuSection.Rewards; break;
                case "MenuTopSettings": session.Primary = MenuPrimary.Settings; break;
                case "MenuSectionSettings": session.Primary = MenuPrimary.Settings; break;
                case "MenuSectionInformation": session.Primary = MenuPrimary.Main; session.Section = MenuSection.Information; break;
                case "MenuSectionGroup": session.Primary = MenuPrimary.Main; session.Section = MenuSection.Group; break;
                case "MenuSectionCommands": session.Primary = MenuPrimary.Main; session.Section = MenuSection.Commands; break;
                case "MenuSectionLinks": session.Primary = MenuPrimary.Main; session.Section = MenuSection.Links; break;
                case "MenuSectionRewards": session.Primary = MenuPrimary.Progress; session.Section = MenuSection.Rewards; break;
                case "MenuSectionTop": session.Primary = MenuPrimary.Progress; session.Section = MenuSection.Top; break;
                default: return;
            }
            session.Page = 0;
            try { Render(session); }
            catch (Exception error)
            {
                Logger.LogException(error, "[MRE Menu] Не удалось отобразить раздел меню.");
                Close(id);
            }
        }

        private static void Render(Session session)
        {
            Player player = session.Player;
            var connection = player.channel.GetOwnerTransportConnection();
            UnturnedPlayer unturnedPlayer = UnturnedPlayer.FromPlayer(player);
            bool isMain = session.Primary == MenuPrimary.Main;
            bool isProgress = session.Primary == MenuPrimary.Progress;
            bool isSettings = session.Primary == MenuPrimary.Settings;
            bool information = isMain && session.Section == MenuSection.Information;
            bool commands = isMain && session.Section == MenuSection.Commands;
            bool links = isMain && session.Section == MenuSection.Links;
            bool showRows = commands || links;
            bool planned = isSettings || session.Section == MenuSection.Group || session.Section == MenuSection.Rewards || session.Section == MenuSection.Top;

            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuTitle", WindowTitle(session));
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuSubtitle", WindowSubtitle(session));
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuFooter", Footer(session));
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuRowsEyebrow", RowsEyebrow(session));
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuRowsHint", RowsHint(session));
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuTopBrand", "MRE  /  PEI");
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionCommandsSelected", session.Section == MenuSection.Commands && isMain);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionInformationSelected", session.Section == MenuSection.Information && isMain);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionGroupSelected", session.Section == MenuSection.Group && isMain);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionLinksSelected", session.Section == MenuSection.Links && isMain);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionRewardsSelected", session.Section == MenuSection.Rewards && isProgress);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionTopSelected", session.Section == MenuSection.Top && isProgress);
            foreach (MenuPrimary primary in Enum.GetValues(typeof(MenuPrimary)))
                EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSelectedTop" + primary, session.Primary == primary);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionInformation", isMain);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionGroup", isMain);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionCommands", isMain);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionLinks", isMain);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionRewards", isProgress);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionTop", isProgress);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionSettings", isSettings);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuSectionSettingsSelected", isSettings);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuInformationPanel", information);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuPlannedPanel", planned);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuRowsPanel", showRows);
            EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuCommandPager", commands);

            MenuRow[] allRows = Rows(session, unturnedPlayer);
            int pageCount = Math.Max(1, (allRows.Length + 9) / 10);
            session.Page = Math.Max(0, Math.Min(session.Page, pageCount - 1));
            MenuRow[] rows = allRows.Skip(session.Page * 10).Take(10).ToArray();
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuPage", (session.Page + 1) + " / " + pageCount);
            for (int i = 0; i < 10; i++)
            {
                string number = (i + 1).ToString("00");
                bool visible = showRows && i < rows.Length;
                EffectManager.sendUIEffectVisibility(EffectKey, connection, true, "MenuRow" + number, visible);
                if (!visible) continue;
                EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuRowTitle" + number, rows[i].Title);
                EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuRowDescription" + number, rows[i].Description);
            }

            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuPlannedTitle", PlannedTitle(session));
            EffectManager.sendUIEffectText(EffectKey, connection, true, "MenuPlannedDescription", planned ? PlannedDescription(session) : String.Empty);
        }

        private static MenuRow[] Rows(Session session, UnturnedPlayer player)
        {
            if (session.Primary == MenuPrimary.Settings || session.Section == MenuSection.Group || session.Section == MenuSection.Rewards || session.Section == MenuSection.Top)
                return new MenuRow[0];
            switch (session.Section)
            {
                case MenuSection.Commands: return HelpCatalog.ForCommands(player).Select(x => new MenuRow(x.Command, x.Description)).ToArray();
                case MenuSection.Links: return new[] {
                    new MenuRow("Репозиторий MRE", "Исходный код и документация проекта: https://github.com/kem4ikkk/MRE"),
                    new MenuRow("Сообщество", "Ссылки VK и Discord для MRE пока не настроены.")
                };
                default: return new MenuRow[0];
            }
        }

        private static string WindowTitle(Session session)
        {
            if (session.Primary == MenuPrimary.Settings) return "Настройки";
            if (session.Section == MenuSection.Commands) return "Команды сервера";
            if (session.Section == MenuSection.Links) return "Ресурсы MRE";
            if (session.Section == MenuSection.Group) return "Менеджер группы";
            if (session.Section == MenuSection.Rewards) return "Ежедневные награды";
            if (session.Section == MenuSection.Top) return "Топ игроков";
            return "Информация о сервере";
        }

        private static string WindowSubtitle(Session session)
        {
            if (session.Primary == MenuPrimary.Settings || session.Section == MenuSection.Group || session.Section == MenuSection.Rewards || session.Section == MenuSection.Top)
                return "РАЗДЕЛ MRE";
            if (session.Section == MenuSection.Commands) return "КОМАНДЫ ИГРОКА";
            if (session.Section == MenuSection.Links) return "ССЫЛКИ И ДОКУМЕНТАЦИЯ";
            return "MRE  ·  PEI  ·  MAX4";
        }

        private static string PlannedTitle(Session session)
        {
            if (session.Primary == MenuPrimary.Settings) return "Раздел настроек готовится";
            if (session.Section == MenuSection.Group) return "Менеджер группы готовится";
            if (session.Section == MenuSection.Rewards) return "Награды готовятся";
            return "Рейтинг готовится";
        }

        private static string PlannedDescription(Session session)
        {
            if (session.Primary == MenuPrimary.Settings) return "Настройки появятся позже.";
            if (session.Section == MenuSection.Group) return "Управление группой появится позже. Пока используйте встроенное меню групп Unturned.";
            if (session.Section == MenuSection.Rewards) return "Раздел наград находится в разработке.";
            return "Рейтинг появится позже.";
        }

        private static string Footer(Session session)
        {
            return session.Primary == MenuPrimary.Main && session.Section == MenuSection.Commands
                ? "/help  ·  команды MRE"
                : "Закрыть меню: × в правом верхнем углу";
        }

        private static string RowsEyebrow(Session session)
        {
            if (session.Primary != MenuPrimary.Main) return String.Empty;
            if (session.Section == MenuSection.Commands) return "КОМАНДЫ ИГРОКА";
            if (session.Section == MenuSection.Links) return "ССЫЛКИ И ДОКУМЕНТАЦИЯ";
            return String.Empty;
        }

        private static string RowsHint(Session session)
        {
            if (session.Primary != MenuPrimary.Main) return String.Empty;
            if (session.Section == MenuSection.Commands) return "Доступные вам команды сервера";
            if (session.Section == MenuSection.Links) return "Официальные ресурсы проекта MRE";
            return String.Empty;
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

    internal sealed class MenuRow
    {
        internal readonly string Title;
        internal readonly string Description;
        internal MenuRow(string title, string description) { Title = title; Description = description; }
    }
}
