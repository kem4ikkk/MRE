using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the static MRE menu shell. Runtime navigation is owned by HelpUi.
public static class MreHelpBuilder
{
    private const string PrefabPath = "Assets/MreHelp/Effect.prefab";
    private const string EffectGuid = "6e4a31b49c874f428a622e1a68e4d203";
    private const ushort EffectId = 48625;
    private const ushort EffectKey = 14825;
    private static readonly string[] Tabs = { "Home", "Help", "Team", "Events", "Stats", "Links", "Settings", "BattlePass", "Rewards", "Reports" };
    private static readonly string[] Titles = { "Главная", "Справка", "Команда", "Ивенты", "Статистика", "Ссылки", "Настройки", "BattlePass", "Награды", "Репорты" };
    private static readonly string[] Icons = { "home", "help", "team", "events", "stats", "links", "settings", "battlepass", "rewards", "reports", "bell", "close", "left", "right" };
    private static readonly Color Overlay = Hex("040A07", .52f);
    private static readonly Color Surface = Hex("111513");
    private static readonly Color Raised = Hex("1B211E");
    private static readonly Color Line = Hex("2A322D");
    private static readonly Color TextColor = Hex("E9EFEB");
    private static readonly Color Muted = Hex("929F96");
    private static readonly Color Accent = Hex("83DCA9");
    private static readonly Color Active = Hex("1A2C21");
    private static Font regular;
    private static Font semibold;
    private static Sprite rounded;
    private static Sprite logo;
    private static readonly Dictionary<int, Sprite> roundSprites = new Dictionary<int, Sprite>();

    [MenuItem("MRE/Создать UI меню")]
    public static void Generate()
    {
        roundSprites.Clear();
        ImportSprites();
        regular = AssetDatabase.LoadAssetAtPath<Font>("Assets/MreFonts/NotoSans-Regular.ttf");
        semibold = AssetDatabase.LoadAssetAtPath<Font>("Assets/MreFonts/NotoSans-SemiBold.ttf");
        logo = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MreMenuArt/server-logo.png");
        rounded = RoundSprite(12);
        if (regular == null || semibold == null) throw new InvalidOperationException("Missing NotoSans Regular/SemiBold under Assets/MreFonts.");
        if (logo == null) throw new InvalidOperationException("Missing server logo sprite Assets/MreMenuArt/server-logo.png.");

        var root = new GameObject("Effect", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        try
        {
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = .5f;

            RectTransform content = Stretch("MenuContent", root.transform, 0, 0, 0, 0);
            RectTransform backdrop = Stretch("MenuBackdrop", content, 0, 0, 0, 0);
            Image backdropImage = backdrop.gameObject.AddComponent<Image>(); backdropImage.color = Overlay; backdropImage.raycastTarget = true;
            BuildWindow(content);
            Validate(root);
            Directory.CreateDirectory("Assets/MreHelp"); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("MRE Menu: создан " + PrefabPath + "; ID=" + EffectId + ", GUID=" + EffectGuid + ".");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [MenuItem("MRE/Собрать Windows bundle меню")]
    public static void BuildWindows()
    {
        Generate();
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string output = Path.Combine(project, "Build/Windows"); Directory.CreateDirectory(output);
        var build = new AssetBundleBuild { assetBundleName = "MreHelp.unity3d", assetNames = new[] { PrefabPath }, addressableNames = new[] { "Effect" } };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build }, AssetBundleBuildOptions(), BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new InvalidOperationException("Unity не собрал MRE Menu bundle.");
        string bundlePath = Path.Combine(output, "MreHelp.unity3d"); AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null) throw new InvalidOperationException("Собранный bundle не читается Unity.");
        try { GameObject prefab = bundle.LoadAsset<GameObject>("Effect"); if (prefab == null || prefab.GetComponent<Canvas>() == null) throw new InvalidOperationException("В bundle отсутствует Canvas prefab Effect."); Validate(prefab); }
        finally { bundle.Unload(true); }
        string mod = Path.Combine(output, "MreHelp"); Directory.CreateDirectory(mod); File.Copy(bundlePath, Path.Combine(mod, "MreHelp.unity3d"), true);
        File.WriteAllText(Path.Combine(mod, "MreHelp.dat"), "GUID " + EffectGuid + "\nType Effect\nID " + EffectId + "\nAsset_Bundle_Version 6\nExclude_From_Master_Bundle\n", new System.Text.UTF8Encoding(false));
        string sha; using (var hash = SHA256.Create()) sha = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(bundlePath))).Replace("-", "").ToLowerInvariant();
        string buttons = JsonArray(new[] { "MenuTabHome", "MenuTabHelp", "MenuTabTeam", "MenuTabEvents", "MenuTabStats", "MenuTabLinks", "MenuTabSettings", "MenuTabBattlePass", "MenuTabRewards", "MenuTabReports", "MenuPrevious", "MenuNext", "MenuNotifications", "MenuNotificationsClose", "MenuClose" });
        string pages = JsonArray(Tabs.Select(x => "MenuPage" + x).ToArray());
        string homeButtons = JsonArray(new[] { "MenuHomeHelp", "MenuHomeTeam", "MenuHomeEvents", "MenuHomeSettings" });
        string homeText = JsonArray(new[] { "MenuHomePlayerName", "MenuHomeSteamId", "MenuHomeGroup", "MenuHomeGroupCount", "MenuHomeServerName", "MenuHomeOnline", "MenuHomeMap" });
        string json = "{\n  \"effect_id\": " + EffectId + ",\n  \"effect_guid\": \"" + EffectGuid + "\",\n  \"effect_key\": " + EffectKey + ",\n  \"bundle\": \"MreHelp.unity3d\",\n  \"prefab_address\": \"Effect\",\n  \"unity_version\": \"" + Application.unityVersion + "\",\n  \"target\": \"StandaloneWindows64\",\n  \"bundle_sha256\": \"" + sha + "\",\n  \"prefab_address_verified\": true,\n  \"schema_version\": 5,\n  \"button_names\": " + buttons + ",\n  \"home_button_names\": " + homeButtons + ",\n  \"home_text_names\": " + homeText + ",\n  \"page_names\": " + pages + ",\n  \"notifications_panel_name\": \"MenuNotificationsPanel\",\n  \"backdrop_alpha\": 0.52,\n  \"font_regular\": \"NotoSans-Regular.ttf\",\n  \"font_semibold\": \"NotoSans-SemiBold.ttf\",\n  \"font_provenance\": \"ui/MreHelp/Assets/MreFonts\",\n  \"window_name\": \"MenuWindow\",\n  \"in_game_verified\": false,\n  \"workshop_published\": false\n}\n";
        File.WriteAllText(Path.Combine(output, "effect-manifest.json"), json, new System.Text.UTF8Encoding(false));
        Debug.Log("MRE Menu: Windows bundle собран: " + mod + ". Не опубликован и не установлен.");
    }

    private static BuildAssetBundleOptions AssetBundleBuildOptions() { return BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle; }

    public static void RenderPreview()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        MreMenuPreview.RenderStates();
        File.Copy(Path.Combine(project, "Build/Review/Home-1920x1080.png"), Path.Combine(project, "Build/MreHelp-preview.png"), true);
    }

    private static void BuildWindow(RectTransform parent)
    {
        // Fractional anchors reproduce 40/28 margins at 1920x1080 and ~32/24 at 1366x768.
        RectTransform window = Stretch("MenuWindow", parent, 0, 0, 0, 0, new Vector2(.0208333f, .0259259f), new Vector2(.9791667f, .9740741f));
        Image wi = window.gameObject.AddComponent<Image>(); wi.color = Surface; wi.sprite = RoundSprite(20); AddBorder(wi); wi.type = Image.Type.Sliced; wi.raycastTarget = true;

        RectTransform header = Rect("MenuHeader", window, 0, 0, 0, 0, new Vector2(0, 1), new Vector2(1, 1)); header.pivot = new Vector2(0, 1); header.offsetMin = new Vector2(32, -112); header.offsetMax = new Vector2(-32, 0);
        RectTransform brand = Rect("MenuBrand", header, 0, 0, 340, 72, new Vector2(0, .5f), new Vector2(0, .5f));
        RectTransform logoRect = Rect("MenuBrandLogo", brand, 0, 0, 72, 72, new Vector2(0, .5f), new Vector2(0, .5f)); Image logoImage = logoRect.gameObject.AddComponent<Image>(); logoImage.sprite = logo; logoImage.preserveAspect = true; logoImage.raycastTarget = false;
        RectTransform titleRect = Rect("MenuBrandTitle", header, 88, 11, 170, 48, new Vector2(0, .5f), new Vector2(0, .5f)); Text title = titleRect.gameObject.AddComponent<Text>(); ConfigureText(title, "MRE", 29, TextColor, semibold, TextAnchor.MiddleLeft);
        RectTransform subtitleRect = Rect("MenuBrandSubtitle", header, 88, -17, 190, 20, new Vector2(0, .5f), new Vector2(0, .5f)); Text subtitle = subtitleRect.gameObject.AddComponent<Text>(); ConfigureText(subtitle, "Меню сервера", 12, Muted, regular, TextAnchor.MiddleLeft);
        RectTransform actions = Rect("MenuHeaderActions", header, 0, 0, 108, 46, new Vector2(1, .5f), new Vector2(1, .5f)); actions.anchoredPosition = Vector2.zero;
        IconButton("MenuNotifications", actions, "bell", 0, 0, Raised);
        IconButton("MenuClose", actions, "close", 62, 0, Raised);
        RectTransform headerRule = Rect("MenuHeaderRule", window, 0, 0, 0, 1, new Vector2(0, 1), new Vector2(1, 1)); headerRule.pivot = new Vector2(0, 1); headerRule.offsetMin = new Vector2(32, -105); headerRule.offsetMax = new Vector2(-32, -104); Image headerRuleImage = headerRule.gameObject.AddComponent<Image>(); headerRuleImage.color = Color.clear; headerRuleImage.raycastTarget = false;

        RectTransform navigation = Rect("MenuNavigation", window, 32, -112, -64, 68, new Vector2(0, 1), new Vector2(1, 1)); navigation.pivot = new Vector2(0, 1); navigation.anchoredPosition = new Vector2(32, -112);
        float tabWidth = .1f;
        for (int i = 0; i < Tabs.Length; i++)
        {
            string tab = Tabs[i]; RectTransform button = Rect("MenuTab" + tab, navigation, 0, 0, 0, 64, new Vector2(i * tabWidth, 0), new Vector2((i + 1) * tabWidth, 1));
            button.offsetMin = new Vector2(3, 0); button.offsetMax = new Vector2(-3, 0);
            Image hit = button.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            Button b = button.gameObject.AddComponent<Button>(); b.targetGraphic = hit;
            Image icon = ChildIcon("MenuTab" + tab + "Icon", button, tab.ToLowerInvariant(), Muted, 18, 18); icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f); icon.rectTransform.pivot = new Vector2(0, .5f); icon.rectTransform.anchoredPosition = new Vector2(14, 0);
            Label("MenuTab" + tab + "Label", button, Titles[i], 40, 0, 125, 64, 13, Muted, semibold, TextAnchor.MiddleLeft);
            RectTransform selected = Stretch("MenuSelected" + tab, button, 0, 0, 0, 0);
            Image selectedBg = selected.gameObject.AddComponent<Image>(); selectedBg.color = Active; selectedBg.sprite = RoundSprite(8); selectedBg.type = Image.Type.Sliced; selectedBg.raycastTarget = false;
            Image selectedIcon = ChildIcon("MenuSelected" + tab + "Icon", selected, tab.ToLowerInvariant(), Accent, 18, 18); selectedIcon.rectTransform.anchorMin = selectedIcon.rectTransform.anchorMax = new Vector2(0, .5f); selectedIcon.rectTransform.pivot = new Vector2(0, .5f); selectedIcon.rectTransform.anchoredPosition = new Vector2(14, 0);
            Text selectedLabel = Label("MenuSelected" + tab + "Label", selected, Titles[i], 40, 0, 125, 64, 13, Accent, semibold, TextAnchor.MiddleLeft); selectedLabel.raycastTarget = false;
            Box("MenuSelected" + tab + "Underline", selected, 12, 0, -24, 3, Accent, false, new Vector2(0, 0), new Vector2(1, 0));
            selected.SetAsLastSibling(); selected.gameObject.SetActive(i == 0);
        }
        Box("MenuNavigationRule", navigation, 0, 0, 0, 1, Line, false, new Vector2(0, 0), new Vector2(1, 0)).offsetMax = new Vector2(0, 0);

        RectTransform pages = Rect("MenuPages", window, 0, 0, 0, 0, new Vector2(0, 0), new Vector2(1, 1)); pages.pivot = new Vector2(0, 1); pages.offsetMin = new Vector2(32, 80); pages.offsetMax = new Vector2(-32, -212);
        for (int i = 0; i < Tabs.Length; i++)
        {
            string tab = Tabs[i]; RectTransform page = Stretch("MenuPage" + tab, pages, 0, 0, 0, 0);
            Image pageBg = page.gameObject.AddComponent<Image>(); pageBg.color = Hex("151A17"); pageBg.sprite = RoundSprite(16); AddBorder(pageBg); pageBg.type = Image.Type.Sliced; pageBg.raycastTarget = false;
            RectTransform heading = Rect("MenuPage" + tab + "Heading", page, 0, 0, 0, 0, new Vector2(0, 1), new Vector2(1, 1)); heading.pivot = new Vector2(0, 1); heading.offsetMin = new Vector2(28, -66); heading.offsetMax = new Vector2(-28, 0);
            Image pageIcon = ChildIcon("MenuPage" + tab + "Icon", heading, tab.ToLowerInvariant(), Accent, 21, 21); pageIcon.rectTransform.anchorMin = pageIcon.rectTransform.anchorMax = new Vector2(0, .5f); pageIcon.rectTransform.pivot = new Vector2(0, .5f); pageIcon.rectTransform.anchoredPosition = Vector2.zero;
            RectTransform pageTitleRect = Rect("MenuPage" + tab + "Title", heading, 33, 0, 600, 66, new Vector2(0, .5f), new Vector2(0, .5f)); Text pageTitle = pageTitleRect.gameObject.AddComponent<Text>(); ConfigureText(pageTitle, Titles[i], 19, TextColor, semibold, TextAnchor.MiddleLeft);
            RectTransform pageRule = Rect("MenuPage" + tab + "Rule", heading, 0, 0, 0, 1, Vector2.zero, new Vector2(1, 0)); pageRule.pivot = Vector2.zero; Image pageRuleImage = pageRule.gameObject.AddComponent<Image>(); pageRuleImage.color = Hex("232D26"); pageRuleImage.raycastTarget = false;
            page.gameObject.SetActive(i == 0);
        }

        BuildHome(FindRect(parent, "MenuPageHome"));

        RectTransform footer = Rect("MenuFooter", window, 32, 24, -64, 32, new Vector2(0, 0), new Vector2(1, 0)); footer.anchoredPosition = new Vector2(32, 24);
        IconButton("MenuPrevious", footer, "left", 0, 0, Raised, true);
        IconButton("MenuNext", footer, "right", 40, 0, Raised, true);
        Label("MenuFooterHint", footer, "Переключить вкладку", 84, 0, 220, 32, 12, Muted, regular);
        Label("MenuFooterBrand", footer, "MRE", 0, 0, 60, 32, 12, Hex("627468"), semibold, TextAnchor.MiddleRight, true);

        RectTransform notifications = Rect("MenuNotificationsPanel", window, 0, 0, 360, 280, new Vector2(1, 1), new Vector2(1, 1)); notifications.anchoredPosition = new Vector2(-32, -94); notifications.pivot = new Vector2(1, 1);
        Image ni = notifications.gameObject.AddComponent<Image>(); ni.color = Hex("19211C"); ni.sprite = RoundSprite(16); AddBorder(ni); ni.type = Image.Type.Sliced; ni.raycastTarget = true;
        RectTransform nh = Rect("MenuNotificationsHeader", notifications, 0, 0, 0, 0, new Vector2(0, 1), new Vector2(1, 1)); nh.pivot = new Vector2(0, 1); nh.offsetMin = new Vector2(20, -66); nh.offsetMax = new Vector2(-20, -18);
        RectTransform notificationTitleRect = Rect("MenuNotificationsTitle", nh, 0, 0, 220, 48, new Vector2(0, 1), new Vector2(0, 1)); Text notificationTitle = notificationTitleRect.gameObject.AddComponent<Text>(); ConfigureText(notificationTitle, "Уведомления", 16, TextColor, semibold, TextAnchor.MiddleLeft);
        IconButton("MenuNotificationsClose", nh, "close", 0, 0, Raised, true);
        RectTransform closeRect = nh.Find("MenuNotificationsClose").GetComponent<RectTransform>(); closeRect.anchorMin = closeRect.anchorMax = new Vector2(1, .5f); closeRect.pivot = new Vector2(1, .5f); closeRect.anchoredPosition = Vector2.zero;
        RectTransform notificationRule = Rect("MenuNotificationsRule", notifications, 0, 0, 0, 1, new Vector2(0, 1), new Vector2(1, 1)); notificationRule.pivot = new Vector2(0, 1); notificationRule.offsetMin = new Vector2(0, -67); notificationRule.offsetMax = new Vector2(0, -66); Image notificationRuleImage = notificationRule.gameObject.AddComponent<Image>(); notificationRuleImage.color = Line; notificationRuleImage.raycastTarget = false;
        Image emptyBell = ChildIcon("MenuNotificationsEmptyIcon", notifications, "bell", Hex("6C8575"), 30, 30);
        emptyBell.rectTransform.anchorMin = emptyBell.rectTransform.anchorMax = new Vector2(.5f, .5f); emptyBell.rectTransform.pivot = new Vector2(.5f, .5f); emptyBell.rectTransform.anchoredPosition = new Vector2(0, -35);
        Label("MenuNotificationsEmptyText", notifications, "Новых уведомлений нет", -130, 0, 260, 28, 13, Muted, regular, TextAnchor.MiddleCenter);
        RectTransform emptyText = notifications.Find("MenuNotificationsEmptyText").GetComponent<RectTransform>(); emptyText.anchorMin = emptyText.anchorMax = new Vector2(.5f, .5f); emptyText.pivot = new Vector2(.5f, .5f); emptyText.anchoredPosition = new Vector2(0, -68);
        notifications.gameObject.SetActive(false);
        RectTransform bellSelected = Stretch("MenuNotificationsSelected", actions.Find("MenuNotifications"), 0, 0, 0, 0);
        Image bellBackground = bellSelected.gameObject.AddComponent<Image>(); bellBackground.sprite = rounded; bellBackground.type = Image.Type.Sliced; bellBackground.color = Active; bellBackground.raycastTarget = false; AddBorder(bellBackground);
        ChildIcon("MenuNotificationsSelectedIcon", bellSelected, "bell", Accent, 21, 21);
        bellSelected.gameObject.SetActive(false);
    }

    private static RectTransform FindRect(Transform parent, string name) { return parent.Find("MenuWindow/MenuPages/" + name).GetComponent<RectTransform>(); }

    private static void BuildHome(RectTransform page)
    {
        RectTransform player = HomeCard("MenuHomePlayerCard", page, 0f, .5f, 28, 82, 9, 220);
        RectTransform server = HomeCard("MenuHomeServerCard", page, .5f, 1f, 9, 82, 28, 220);
        HomeCardHeading(player, "Профиль игрока", "team");
        HomeCardHeading(server, "Сервер", "home");

        HomeLabel("MenuHomePlayerNameLabel", player, "Никнейм", 24, 57, -48, 22, 13, Muted, regular);
        HomeLabel("MenuHomePlayerName", player, "—", 24, 78, -48, 40, 28, TextColor, semibold);
        HomeLabel("MenuHomeSteamIdLabel", player, "SteamID64", 24, 132, 230, 22, 13, Muted, regular);
        HomeLabel("MenuHomeSteamId", player, "—", 24, 154, 250, 26, 13, TextColor, regular);
        HomeLabel("MenuHomeGroupLabel", player, "Группа", 24, 132, 220, 22, 13, Muted, regular, TextAnchor.MiddleLeft, true);
        HomeLabel("MenuHomeGroup", player, "—", 24, 154, 220, 24, 16, TextColor, semibold, TextAnchor.MiddleLeft, true);
        HomeLabel("MenuHomeGroupCount", player, "—", 24, 182, 220, 24, 13, Muted, regular, TextAnchor.MiddleLeft, true);

        HomeLabel("MenuHomeServerNameLabel", server, "Название сервера", 24, 57, -48, 22, 13, Muted, regular);
        HomeLabel("MenuHomeServerName", server, "—", 24, 78, -48, 48, 18, TextColor, semibold, TextAnchor.MiddleLeft);
        HomeLabel("MenuHomeOnlineLabel", server, "Игроки онлайн", 24, 132, 220, 22, 13, Muted, regular);
        HomeLabel("MenuHomeOnline", server, "—", 24, 154, 220, 40, 28, TextColor, semibold);
        HomeLabel("MenuHomeMapLabel", server, "Карта", 270, 132, 160, 22, 13, Muted, regular);
        HomeLabel("MenuHomeMap", server, "—", 270, 154, 200, 40, 16, TextColor, semibold);

        RectTransform quick = Rect("MenuHomeQuickLinks", page, 0, 0, 0, 100, new Vector2(0, 1), new Vector2(1, 1));
        quick.offsetMin = new Vector2(28, -418); quick.offsetMax = new Vector2(-28, -318);
        HomeQuickCard("MenuHomeHelp", quick, "Справка", "Основные правила и команды", "help", 0);
        HomeQuickCard("MenuHomeTeam", quick, "Команда", "Игроки и состав группы", "team", 1);
        HomeQuickCard("MenuHomeEvents", quick, "Ивенты", "Расписание событий сервера", "events", 2);
        HomeQuickCard("MenuHomeSettings", quick, "Настройки", "Параметры интерфейса", "settings", 3);
    }

    private static RectTransform HomeCard(string name, RectTransform parent, float anchorMinX, float anchorMaxX, int left, int top, int right, int height)
    {
        RectTransform card = Rect(name, parent, 0, 0, 0, height, new Vector2(anchorMinX, 1), new Vector2(anchorMaxX, 1));
        card.pivot = new Vector2(anchorMinX, 1); card.offsetMin = new Vector2(left, -top - height); card.offsetMax = new Vector2(-right, -top);
        Image bg = card.gameObject.AddComponent<Image>(); bg.color = Raised; bg.sprite = RoundSprite(12); bg.type = Image.Type.Sliced; bg.raycastTarget = false; AddBorder(bg);
        return card;
    }

    private static void HomeCardHeading(RectTransform card, string title, string icon)
    {
        Image glyph = ChildIcon(card.name + "Icon", card, icon, Accent, 20, 20);
        glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax = new Vector2(0, 1); glyph.rectTransform.pivot = new Vector2(0, 1); glyph.rectTransform.anchoredPosition = new Vector2(24, -24);
        HomeLabel(card.name + "Heading", card, title, 54, 12, 340, 32, 16, TextColor, semibold);
        RectTransform rule = Rect(card.name + "Rule", card, 0, 0, 0, 1, new Vector2(0, 1), new Vector2(1, 1)); rule.pivot = new Vector2(0, 1); rule.offsetMin = new Vector2(20, -49); rule.offsetMax = new Vector2(-20, -48);
        Image ruleImage = rule.gameObject.AddComponent<Image>(); ruleImage.color = Line; ruleImage.raycastTarget = false;
    }

    private static void HomeQuickCard(string name, RectTransform page, string title, string description, string icon, int index)
    {
        float min = index * .25f, max = (index + 1) * .25f;
        RectTransform card = Rect(name, page, 0, 0, 0, 100, new Vector2(min, 1), new Vector2(max, 1));
        card.pivot = new Vector2(min, 1); card.offsetMin = new Vector2(index == 0 ? 0 : 6, -100); card.offsetMax = new Vector2(index == 3 ? 0 : -6, 0);
        Image bg = card.gameObject.AddComponent<Image>(); bg.color = Raised; bg.sprite = RoundSprite(12); bg.type = Image.Type.Sliced; bg.raycastTarget = true; AddBorder(bg);
        Button button = card.gameObject.AddComponent<Button>(); button.targetGraphic = bg;
        Image glyph = ChildIcon(name + "Icon", card, icon, Accent, 20, 20);
        glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax = new Vector2(0, 1); glyph.rectTransform.pivot = new Vector2(0, 1); glyph.rectTransform.anchoredPosition = new Vector2(18, -18);
        HomeLabel(name + "Title", card, title, 47, 9, -59, 30, 15, TextColor, semibold);
        HomeLabel(name + "Description", card, description, 18, 48, -36, 36, 13, Muted, regular);
    }

    private static Text HomeLabel(string name, RectTransform parent, string value, int x, int top, int width, int height, int size, Color color, Font font, TextAnchor anchor = TextAnchor.MiddleLeft, bool right = false)
    {
        bool stretch = !right && width < 0;
        Vector2 origin = right ? new Vector2(1, 1) : new Vector2(0, 1);
        RectTransform r = Rect(name, parent, 0, 0, 0, height, origin, stretch ? new Vector2(1, 1) : origin);
        r.pivot = right ? new Vector2(1, 1) : new Vector2(0, 1); r.offsetMin = new Vector2(right ? -width - x : x, -top - height); r.offsetMax = new Vector2(right ? -x : (stretch ? width + x : x + width), -top);
        Text text = r.gameObject.AddComponent<Text>(); ConfigureText(text, value, size, color, font, anchor); return text;
    }

    private static void IconButton(string name, RectTransform parent, string iconName, int x, int y, Color fill, bool small = false)
    {
        int size = small ? 32 : 46; RectTransform r = Box(name, parent, x, y, size, size, fill, true);
        r.GetComponent<Image>().sprite = RoundSprite(small ? 8 : 12); AddBorder(r.GetComponent<Image>()); r.GetComponent<Image>().type = Image.Type.Sliced;
        Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = r.GetComponent<Image>();
        ColorBlock colors = b.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1); colors.pressedColor = new Color(.85f, .85f, .85f, 1); colors.selectedColor = Color.white; b.colors = colors;
        ChildIcon(name + "Icon", r, iconName, Muted, small ? 16 : 21, small ? 16 : 21);
    }

    private static Image ChildIcon(string name, RectTransform parent, string iconName, Color tint, int w, int h)
    {
        RectTransform r = Rect(name, parent, 0, 0, w, h, new Vector2(.5f, .5f), new Vector2(.5f, .5f)); Image image = r.gameObject.AddComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MreMenuArt/shell/" + iconName + ".png"); if (image.sprite == null) throw new InvalidOperationException("Missing shell icon Assets/MreMenuArt/shell/" + iconName + ".png.");
        image.color = tint; image.preserveAspect = true; image.raycastTarget = false; return image;
    }

    private static void ImportSprites()
    {
        string[] paths = new[] { "Assets/MreMenuArt/server-logo.png" }.Concat(Icons.Select(x => "Assets/MreMenuArt/shell/" + x + ".png")).ToArray();
        foreach (string path in paths)
        {
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter; if (imp == null) throw new InvalidOperationException("Missing menu art " + path + ".");
            bool changed = imp.textureType != TextureImporterType.Sprite || imp.spriteImportMode != SpriteImportMode.Single || imp.mipmapEnabled || !imp.alphaIsTransparency || imp.textureCompression != TextureImporterCompression.Uncompressed;
            imp.filterMode = FilterMode.Bilinear; imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; imp.mipmapEnabled = false; imp.alphaIsTransparency = true; imp.textureCompression = TextureImporterCompression.Uncompressed;
            if (changed) imp.SaveAndReimport();
        }
    }

    private static Sprite RoundSprite(int radius)
    {
        Sprite cached;
        if (roundSprites.TryGetValue(radius, out cached)) return cached;
        string path = "Assets/MreMenuArt/shell/round-" + radius + ".png";
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing round sprite " + path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = new Vector4(radius, radius, radius, radius); importer.spritePixelsPerUnit = 100;
        importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        roundSprites.Add(radius, sprite);
        return sprite;
    }
    private static void AddBorder(Image image)
    {
        Outline outline = image.gameObject.AddComponent<Outline>(); outline.effectColor = Line;
        outline.effectDistance = new Vector2(1, -1); outline.useGraphicAlpha = false;
    }

    private static void Validate(GameObject root)
    {
        RectTransform[] all = root.GetComponentsInChildren<RectTransform>(true);
        var duplicate = all.GroupBy(x => x.name).FirstOrDefault(x => x.Count() > 1); if (duplicate != null) throw new InvalidOperationException("Повторяется имя элемента: " + duplicate.Key);
        foreach (string name in new[] { "MenuContent", "MenuWindow", "MenuBackdrop", "MenuNotificationsPanel", "MenuNotificationsSelected", "MenuNotificationsClose", "MenuPrevious", "MenuNext", "MenuClose" }.Concat(Tabs.SelectMany(x => new[] { "MenuTab" + x, "MenuSelected" + x, "MenuPage" + x, "MenuPage" + x + "Title", "MenuPage" + x + "Icon" })))
            if (!all.Any(x => x.name == name)) throw new InvalidOperationException("Отсутствует элемент " + name + ".");
        foreach (string name in new[] { "MenuPrevious", "MenuNext", "MenuClose", "MenuNotifications", "MenuNotificationsClose" }.Concat(Tabs.Select(x => "MenuTab" + x)))
            if (all.First(x => x.name == name).GetComponent<Button>() == null) throw new InvalidOperationException("Элемент не является кнопкой: " + name);
        if (all.Any(x => x.name == "MenuHeaderDivider")) throw new InvalidOperationException("MenuHeaderDivider должен быть удален.");
        foreach (string name in new[] { "MenuHomePlayerName", "MenuHomeSteamId", "MenuHomeGroup", "MenuHomeGroupCount", "MenuHomeServerName", "MenuHomeOnline", "MenuHomeMap" })
            if (!all.Any(x => x.name == name && x.GetComponent<Text>() != null)) throw new InvalidOperationException("Отсутствует текстовый элемент главной страницы " + name + ".");
        foreach (string name in new[] { "MenuHomeHelp", "MenuHomeTeam", "MenuHomeEvents", "MenuHomeSettings" })
            if (!all.Any(x => x.name == name && x.GetComponent<Button>() != null)) throw new InvalidOperationException("Отсутствует кнопка главной страницы " + name + ".");
        Image window = all.First(x => x.name == "MenuWindow").GetComponent<Image>(); if (window == null || window.color.a < 1) throw new InvalidOperationException("MenuWindow должен быть непрозрачным.");
        Image backdrop = all.First(x => x.name == "MenuBackdrop").GetComponent<Image>(); if (backdrop.color.a <= 0 || backdrop.color.a >= 1 || !backdrop.raycastTarget) throw new InvalidOperationException("MenuBackdrop должен затемнять мир и перехватывать клики.");
        if (all.Count(x => x.name.StartsWith("MenuPage") && Tabs.Any(t => x.name == "MenuPage" + t) && x.gameObject.activeSelf) != 1 || !all.First(x => x.name == "MenuPageHome").gameObject.activeSelf) throw new InvalidOperationException("По умолчанию должна быть открыта только MenuPageHome.");
        foreach (string tab in Tabs)
        {
            RectTransform selected = all.First(x => x.name == "MenuSelected" + tab); if (selected.GetComponent<Graphic>() != null && selected.GetComponent<Graphic>().raycastTarget) throw new InvalidOperationException("Маркер выбора не должен блокировать клики: " + selected.name);
        }
        if (all.First(x => x.name == "MenuNotificationsPanel").gameObject.activeSelf || all.First(x => x.name == "MenuNotificationsSelected").gameObject.activeSelf) throw new InvalidOperationException("Уведомления и маркер должны быть скрыты по умолчанию.");
        if (root.GetComponent<Canvas>() == null || root.GetComponent<GraphicRaycaster>() == null) throw new InvalidOperationException("Effect должен иметь Canvas и GraphicRaycaster.");
    }

    private static string JsonArray(string[] values) { return "[" + string.Join(",", values.Select(x => "\"" + x + "\"")) + "]"; }
    private static Color Hex(string value, float alpha = 1) { Color c; if (!ColorUtility.TryParseHtmlString("#" + value, out c)) throw new ArgumentException(value); c.a = alpha; return c; }
    private static RectTransform Stretch(string name, Transform parent, float left, float bottom, float right, float top, Vector2? min = null, Vector2? max = null)
    {
        RectTransform r = Rect(name, parent, 0, 0, 0, 0, min ?? Vector2.zero, max ?? Vector2.one); r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top); return r;
    }
    private static RectTransform Box(string name, Transform parent, int x, int y, int w, int h, Color color, bool raycast = false, Vector2? min = null, Vector2? max = null)
    {
        RectTransform r = Rect(name, parent, x, y, w, h, min ?? Vector2.zero, max ?? Vector2.zero); Image i = r.gameObject.AddComponent<Image>(); i.color = color; i.raycastTarget = raycast; if (rounded != null && w >= 8 && h >= 8) { i.sprite = rounded; i.type = Image.Type.Sliced; } return r;
    }
    private static Text Label(string name, Transform parent, string value, int x, int y, int w, int h, int size, Color color, Font font, TextAnchor anchor = TextAnchor.MiddleLeft, bool right = false)
    {
        RectTransform r = Rect(name, parent, x, y, w, h, right ? new Vector2(1, .5f) : Vector2.zero, right ? new Vector2(1, .5f) : Vector2.zero); Text t = r.gameObject.AddComponent<Text>(); ConfigureText(t, value, size, color, font, anchor); return t;
    }
    private static void ConfigureText(Text text, string value, int size, Color color, Font font, TextAnchor anchor)
    {
        text.text = value; text.font = font; text.fontSize = size; text.fontStyle = FontStyle.Normal; text.color = color; text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.supportRichText = false; text.raycastTarget = false;
    }
    private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h, Vector2? min = null, Vector2? max = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); RectTransform r = go.GetComponent<RectTransform>(); r.anchorMin = min ?? Vector2.zero; r.anchorMax = max ?? Vector2.zero;
        r.pivot = r.anchorMin == r.anchorMax ? r.anchorMin : new Vector2(0, 0); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h); return r;
    }
}
