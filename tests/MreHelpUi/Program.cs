using System;
using MreHelp;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        try
        {
            HelpUi.Start();
            UnturnedPlayer player = CreatePlayer(76561198000000001UL);
            Assert(HelpUi.Open(player, MenuPrimary.Main, MenuSection.Commands), "Открытие /help должно создать экран команд.");
            AssertVisible("MenuRowsPanel", true, "/help показывает строки");
            AssertVisible("MenuCommandPager", true, "/help показывает пейджер");
            AssertText("MenuPage", "1 / 2", "/help начинает с первой страницы");

            EffectManager.RaiseButton(player.Player, "MenuCommandsNext");
            AssertText("MenuPage", "2 / 2", "Пейджер команд открывает вторую страницу");

            EffectManager.RaiseButton(player.Player, "MenuSectionSettings");
            AssertVisible("MenuSectionSettings", true, "Sidebar показывает Settings как выбранный раздел");
            AssertVisible("MenuSectionSettingsSelected", true, "Sidebar показывает marker активного раздела Settings");
            AssertVisible("MenuPlannedPanel", true, "Settings показывает заглушку");
            AssertVisible("MenuInformationPanel", false, "Settings скрывает карточки");
            AssertVisible("MenuRowsPanel", false, "Settings с сохранённым Commands скрывает строки");
            AssertVisible("MenuCommandPager", false, "Settings с сохранённым Commands скрывает пейджер");
            AssertVisible("MenuRow01", false, "Settings скрывает отдельные строки");
            AssertText("MenuFooter", "Закрыть меню: × в правом верхнем углу", "Settings показывает подвал закрытия");

            EffectManager.RaiseButton(player.Player, "MenuTopMain");
            AssertVisible("MenuSectionSettingsSelected", false, "Переход в Main скрывает marker Settings");
            AssertVisible("MenuInformationPanel", true, "Main открывает статическую информационную панель");
            AssertVisible("MenuPlannedPanel", false, "Main скрывает заглушку");
            AssertVisible("MenuRowsPanel", false, "Информационные карточки отображаются без строк");
            AssertVisible("MenuCommandPager", false, "В информации пейджера нет");

            EffectManager.RaiseButton(player.Player, "MenuSectionLinks");
            AssertVisible("MenuInformationPanel", false, "Links скрывает карточки");
            AssertVisible("MenuRowsPanel", true, "Links показывает ссылки в строках");
            AssertVisible("MenuCommandPager", false, "Links не показывает пейджер");
            AssertVisible("MenuRow01", true, "Links показывает первую ссылку");
            AssertVisible("MenuRow03", false, "Links показывает только две строки");
            AssertText("MenuPage", "1 / 1", "Links остаётся одной страницей");
            EffectManager.RaiseButton(player.Player, "MenuCommandsNext");
            AssertText("MenuPage", "1 / 1", "Скрытый пейджер не меняет страницу Links");

            EffectManager.RaiseButton(player.Player, "MenuSectionCommands");
            AssertVisible("MenuInformationPanel", false, "Commands скрывает карточки");
            AssertVisible("MenuRowsPanel", true, "Commands показывает строки");
            AssertVisible("MenuCommandPager", true, "Commands показывает пейджер");
            AssertText("MenuPage", "1 / 2", "Переход в Commands сбрасывает страницу");
            AssertText("MenuFooter", "/help  ·  команды MRE", "Commands показывает подвал /help");

            Assert(Rocket.Core.Logging.Logger.Errors.Count == 0, "Во время переходов не должно возникать ошибок UI.");
            HelpUi.Stop();
            Console.WriteLine("Пройдено: " + assertions + " проверок переходов HelpUi.");
            return 0;
        }
        catch (Exception error)
        {
            HelpUi.Stop();
            Console.Error.WriteLine("ОШИБКА: " + error.Message);
            return 1;
        }
    }

    private static UnturnedPlayer CreatePlayer(ulong steamId)
    {
        var id = new CSteamID(steamId);
        var player = new Player
        {
            channel = new PlayerChannel
            {
                owner = new PlayerOwner
                {
                    playerID = new PlayerId { steamID = new SteamId { m_SteamID = id.m_SteamID } }
                }
            }
        };
        return new UnturnedPlayer(player, id);
    }

    private static void AssertVisible(string name, bool expected, string message)
    {
        bool actual;
        if (!EffectManager.Visibility.TryGetValue(name, out actual) || actual != expected)
            throw new InvalidOperationException(message + " Ожидалось " + expected + ", получено " + actual + ".");
        assertions++;
    }

    private static void AssertText(string name, string expected, string message)
    {
        string actual;
        if (!EffectManager.Text.TryGetValue(name, out actual) || actual != expected)
            throw new InvalidOperationException(message + " Ожидалось «" + expected + "», получено «" + actual + "».");
        assertions++;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
