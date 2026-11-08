using System;
using System.Linq;
using MreHelp;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;

internal static class Program
{
    private static int assertions;
    private static readonly string[] tabNames = { "Home", "Help", "Team", "Events", "Stats", "Links", "Settings", "BattlePass", "Rewards", "Reports" };
    private static int Main()
    {
        try
        {
            UnturnedPlayer player = CreatePlayer(76561198000000001UL);
            Assert(!HelpUi.Open(player, MenuTab.Home), "Не запускаем UI до подписки на события");
            HelpUi.Start(); HelpUi.Start();
            Assets.Missing = true;
            Assert(!HelpUi.Open(player, MenuTab.Home), "Без asset не удерживаем курсор");
            Assert(!Modal(player), "Отсутствующий asset не включает Modal");
            Assets.Missing = false;
            player.Player.enablePluginWidgetFlag(EPluginWidgetFlags.Modal);
            Assert(!HelpUi.Open(player, MenuTab.Home), "Чужое модальное окно не перехватываем");
            Assert(Modal(player), "Чужой флаг остаётся включённым");
            player.Player.disablePluginWidgetFlag(EPluginWidgetFlags.Modal);
            Assert(!HelpUi.Open(player, (MenuTab)999), "Недопустимая вкладка отклонена");
            Assert(HelpUi.Open(player, MenuTab.Home), "/menu открывает оболочку");
            Assert(Modal(player), "Меню получает курсор");
            AssertTab("Home");
            foreach (string tab in tabNames)
            {
                EffectManager.RaiseButton(player.Player, "MenuTab" + tab);
                AssertTab(tab);
            }
            EffectManager.RaiseButton(player.Player, "MenuNext"); AssertTab("Home");
            EffectManager.RaiseButton(player.Player, "MenuPrevious"); AssertTab("Reports");
            Assert(HelpUi.Open(player, MenuTab.Help), "Повторный /help переключает текущее окно");
            AssertTab("Help");
            Assert(EffectManager.Sends == 1, "При переключении нет второго экземпляра эффекта");
            EffectManager.RaiseButton(player.Player, "MenuNotifications"); AssertNotifications(true);
            EffectManager.RaiseButton(player.Player, "MenuNotifications"); AssertNotifications(false);
            EffectManager.RaiseButton(player.Player, "MenuNotifications");
            EffectManager.RaiseButton(player.Player, "MenuNotificationsClose"); AssertNotifications(false);
            EffectManager.RaiseButton(player.Player, "MenuNotifications");
            EffectManager.RaiseButton(player.Player, "MenuTabSettings"); AssertNotifications(false); AssertTab("Settings");
            EffectManager.RaiseButton(player.Player, "MenuNotifications");
            Assert(HelpUi.Open(player, MenuTab.Home), "Повторный /menu выбирает Главную");
            AssertNotifications(false); AssertTab("Home");
            string[] unknown = { "MenuTab999", "MenuTabShop", "MenuTopProgress", "MenuTabhome", "MenuSectionCommands", "" };
            foreach (string name in unknown) { EffectManager.RaiseButton(player.Player, name); AssertTab("Home"); }
            EffectManager.RaiseButton(null, "MenuClose");
            EffectManager.RaiseButton(CreatePlayer(player.CSteamID.m_SteamID).Player, "MenuClose");
            EffectManager.RaiseButton(CreatePlayer(76561198000000002UL).Player, "MenuTabReports");
            Assert(Modal(player), "События другого Player не закрывают текущий UI"); AssertTab("Home");
            EffectManager.RaiseButton(player.Player, "MenuClose");
            Assert(!Modal(player), "Закрытие освобождает курсор");
            Assert(EffectManager.Clears == 1, "Закрытие убирает эффект");
            EffectManager.RaiseButton(player.Player, "MenuNext"); Assert(EffectManager.Clears == 1, "Клики после закрытия игнорируются");
            Assert(HelpUi.Open(player, MenuTab.Help), "Повторное открытие после закрытия");
            PlayerLife.RaiseDeath(new PlayerLife { channel = player.Player.channel });
            Assert(!Modal(player), "Смерть освобождает курсор");
            Assert(HelpUi.Open(player, MenuTab.Home), "Открытие перед disconnect");
            Provider.RaiseDisconnect(player.CSteamID);
            Assert(!Modal(player), "Disconnect убирает сессию и флаг");
            Assert(HelpUi.Open(player, MenuTab.Home), "Открытие перед Stop");
            HelpUi.Stop();
            Assert(!Modal(player), "Выгрузка плагина освобождает курсор");
            Assert(!HelpUi.Open(player, MenuTab.Home), "После Stop окно не создаётся");
            HelpUi.Start();
            EffectManager.FailRender = true;
            Assert(!HelpUi.Open(player, MenuTab.Home), "Ошибка первого рендера возвращает false");
            Assert(!Modal(player), "Ошибка рендера освобождает Modal");
            EffectManager.FailRender = false;
            Assert(HelpUi.Open(player, MenuTab.Home), "После ошибки можно заново открыть меню");
            EffectManager.FailRender = true;
            EffectManager.RaiseButton(player.Player, "MenuTabHelp");
            Assert(!Modal(player), "Ошибка переключения очищает сессию");
            EffectManager.FailRender = false;
            Assert(Rocket.Core.Logging.Logger.Errors.Count == 2, "Зарегистрированы только две ожидаемые fixture ошибки");
            HelpUi.Stop();
            Console.WriteLine("Пройдено: " + assertions + " проверок оболочки меню и жизненного цикла.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { EffectManager.FailRender = false; HelpUi.Stop(); }
    }
    private static void AssertTab(string active)
    {
        foreach (string tab in tabNames)
        {
            Assert(EffectManager.Visibility["MenuPage" + tab] == (tab == active), "Видимость страницы " + tab + " при " + active);
            Assert(EffectManager.Visibility["MenuSelected" + tab] == (tab == active), "Подсветка " + tab + " при " + active);
        }
        Assert(EffectManager.Visibility.Count(x => x.Key.StartsWith("MenuPage") && x.Value) == 1, "Ровно одна видимая страница");
        Assert(EffectManager.Visibility.Count(x => x.Key.StartsWith("MenuSelected") && x.Value) == 1, "Ровно одна активная вкладка");
        Assert(EffectManager.Text.Count == 0, "Пустые страницы не заполняются серверными данными");
    }
    private static void AssertNotifications(bool active)
    {
        Assert(EffectManager.Visibility["MenuNotificationsPanel"] == active, "Видимость уведомлений");
        Assert(EffectManager.Visibility["MenuNotificationsSelected"] == active, "Подсветка колокольчика");
    }
    private static bool Modal(UnturnedPlayer player) { return player.Player.isPluginWidgetFlagActive(EPluginWidgetFlags.Modal); }
    private static UnturnedPlayer CreatePlayer(ulong id)
    {
        return new UnturnedPlayer(new Player { channel = new PlayerChannel { owner = new PlayerOwner { playerID = new PlayerId { steamID = new SteamId { m_SteamID = id } } } } }, new CSteamID(id));
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
