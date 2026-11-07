using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Создаёт оригинальный MRE EffectAsset для общей серверной навигации.
public static class MreHelpBuilder
{
    private const string PrefabPath = "Assets/MreHelp/Effect.prefab";
    private const string EffectGuid = "6e4a31b49c874f428a622e1a68e4d203";
    private const ushort EffectId = 48625;
    // Backdrop dims the game world while keeping it clearly visible outside the menu.
    private static readonly Color Overlay = Hex("07110D", .54f);
    private static readonly Color Back = Hex("101A16", .97f);
    private static readonly Color Panel = Hex("17231D", .98f);
    private static readonly Color Raised = Hex("26332C", 1f);
    private static readonly Color Text = Hex("F0F3EC", 1f);
    private static readonly Color Muted = Hex("AAB5AA", 1f);
    private static readonly Color Lime = Hex("36E59A", 1f);
    private static readonly Color Gold = Hex("F1C94B", 1f);
    private static readonly Color Cyan = Hex("56D8F4", 1f);
    private static Font regular;
    private static Font bold;
    private static Sprite roundedSprite;

    [MenuItem("MRE/Создать UI меню")]
    public static void Generate()
    {
        regular = AssetDatabase.LoadAssetAtPath<Font>("Assets/MreFonts/PTSans-Regular.ttf");
        bold = AssetDatabase.LoadAssetAtPath<Font>("Assets/MreFonts/PTSans-Bold.ttf");
        roundedSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (regular == null || bold == null) throw new InvalidOperationException("Не найдены встроенные шрифты PT Sans.");

        var root = new GameObject("Effect", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        try
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            RectTransform content = Rect("MenuContent", root.transform, 0, 0, 0, 0, Vector2.zero, Vector2.one);
            content.offsetMin = content.offsetMax = Vector2.zero;
            Box("MenuBackdrop", content, 0, 0, 0, 0, Overlay, Vector2.zero, Vector2.one, true);
            BuildWindow(content);
            Validate(root);
            Directory.CreateDirectory("Assets/MreHelp");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("MRE Menu: создан " + PrefabPath + "; ID=" + EffectId + ", GUID=" + EffectGuid + ".");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [MenuItem("MRE/Собрать Windows bundle меню")]
    public static void BuildWindows()
    {
        Generate();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string output = Path.Combine(projectRoot, "Build/Windows");
        Directory.CreateDirectory(output);
        var build = new AssetBundleBuild { assetBundleName = "MreHelp.unity3d", assetNames = new[] { PrefabPath }, addressableNames = new[] { "Effect" } };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new InvalidOperationException("Unity не собрал MRE Menu bundle.");
        string bundlePath = Path.Combine(output, "MreHelp.unity3d");
        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null) throw new InvalidOperationException("Собранный bundle не читается Unity.");
        try
        {
            var prefab = bundle.LoadAsset<GameObject>("Effect");
            if (prefab == null || prefab.GetComponent<Canvas>() == null) throw new InvalidOperationException("В bundle отсутствует Canvas prefab Effect.");
            Validate(prefab);
        }
        finally { bundle.Unload(true); }

        string mod = Path.Combine(output, "MreHelp");
        Directory.CreateDirectory(mod);
        File.Copy(bundlePath, Path.Combine(mod, "MreHelp.unity3d"), true);
        File.WriteAllText(Path.Combine(mod, "MreHelp.dat"), "GUID " + EffectGuid + "\nType Effect\nID " + EffectId + "\nAsset_Bundle_Version 6\nExclude_From_Master_Bundle\n", new System.Text.UTF8Encoding(false));
        string sha;
        using (var hash = SHA256.Create()) sha = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(bundlePath))).Replace("-", "").ToLowerInvariant();
        string buttons = "[\"MenuClose\",\"MenuTopMain\",\"MenuTopProgress\",\"MenuTopSettings\",\"MenuSectionInformation\",\"MenuSectionGroup\",\"MenuSectionCommands\",\"MenuSectionLinks\",\"MenuSectionRewards\",\"MenuSectionTop\",\"MenuCommandsPrevious\",\"MenuCommandsNext\"]";
        File.WriteAllText(Path.Combine(output, "effect-manifest.json"),
            "{\n  \"effect_id\": " + EffectId + ",\n  \"effect_guid\": \"" + EffectGuid + "\",\n  \"effect_key\": 14825,\n  \"bundle\": \"MreHelp.unity3d\",\n  \"prefab_address\": \"Effect\",\n  \"unity_version\": \"" + Application.unityVersion + "\",\n  \"target\": \"StandaloneWindows64\",\n  \"bundle_sha256\": \"" + sha + "\",\n  \"prefab_address_verified\": true,\n  \"schema_version\": 2,\n  \"button_names\": " + buttons + ",\n  \"row_name_pattern\": \"MenuRow{01..10}\",\n  \"row_visibility_pattern\": \"MenuRow{01..10}\",\n  \"title_name_pattern\": \"MenuRowTitle{01..10}\",\n  \"description_name_pattern\": \"MenuRowDescription{01..10}\",\n  \"footer_text_name\": \"MenuFooter\",\n  \"window_title_text_name\": \"MenuTitle\",\n  \"window_subtitle_text_name\": \"MenuSubtitle\",\n  \"page_text_name\": \"MenuPage\",\n  \"in_game_verified\": false,\n  \"workshop_published\": false\n}\n", new System.Text.UTF8Encoding(false));
        Debug.Log("MRE Menu: Windows bundle собран: " + mod + ". Не опубликован и не установлен.");
    }

    public static void RenderPreview()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string path = Path.Combine(projectRoot, "Build/Windows/MreHelp/MreHelp.unity3d");
        var bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null) throw new InvalidOperationException("Не найден собранный MRE Menu bundle: " + path);
        var prefab = bundle.LoadAsset<GameObject>("Effect");
        if (prefab == null) throw new InvalidOperationException("Bundle не содержит Effect.");
        var root = UnityEngine.Object.Instantiate(prefab);
        var cameraObject = new GameObject("PreviewCamera", typeof(Camera));
        var texture = new RenderTexture(1920, 1080, 24);
        try
        {
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Hex("263531"); camera.orthographic = true; camera.targetTexture = texture;
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(projectRoot, "Build/MreHelp-preview.png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("MRE Menu preview: " + Path.Combine(projectRoot, "Build/MreHelp-preview.png"));
        }
        finally { RenderTexture.active = null; cameraObject.GetComponent<Camera>().targetTexture = null; texture.Release(); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(root); bundle.Unload(true); }
    }

    private static void BuildTopBar(RectTransform parent)
    {
        RectTransform bar = Box("MenuTopBar", parent, 0, 1004, 1920, 76, Hex("0B1510", .96f));
        Label("MenuTopBrand", bar, "MRE", 30, 17, 180, 45, 34, Lime, FontStyle.Bold, bold);
        Label("MenuTopTagline", bar, "СЕРВЕР  ·  PEI", 208, 23, 190, 28, 14, Muted, FontStyle.Bold, regular);
        TopButton(bar, "MenuTopMain", "Основные", 425, 15, 170);
        TopButton(bar, "MenuTopProgress", "Прогресс", 608, 15, 170);
        TopButton(bar, "MenuTopSettings", "Настройки", 1654, 15, 188);
        Box("MenuSelectedTopMain", bar, 425, 15, 3, 46, Lime);
        Box("MenuSelectedTopProgress", bar, 608, 15, 3, 46, Lime);
        Box("MenuSelectedTopSettings", bar, 1654, 15, 3, 46, Lime);
        bar.Find("MenuSelectedTopMain").SetAsLastSibling();
        bar.Find("MenuSelectedTopProgress").SetAsLastSibling();
        bar.Find("MenuSelectedTopSettings").SetAsLastSibling();
        bar.Find("MenuSelectedTopMain").gameObject.SetActive(true);
        bar.Find("MenuSelectedTopProgress").gameObject.SetActive(false);
        bar.Find("MenuSelectedTopSettings").gameObject.SetActive(false);
        Button("MenuClose", bar, "×", 1864, 15, 44, 46, Hex("361C1B"), Hex("FF6666"), 26);
    }

    private static void BuildSectionBar(RectTransform parent)
    {
        RectTransform bar = Box("MenuSectionBar", parent, 20, 936, 1880, 52, Hex("0D1711", .92f));
        SectionButton(bar, "MenuSectionInformation", "Информация", 10, 7, 164);
        SectionButton(bar, "MenuSectionGroup", "Группа", 184, 7, 150);
        SectionButton(bar, "MenuSectionCommands", "Команды", 344, 7, 150);
        SectionButton(bar, "MenuSectionLinks", "Ссылки", 504, 7, 140);
        SectionButton(bar, "MenuSectionRewards", "Награды", 10, 7, 164);
        SectionButton(bar, "MenuSectionTop", "Топ игроков", 184, 7, 164);
        foreach (string name in new[] { "Information", "Group", "Commands", "Links", "Rewards", "Top" })
        {
            int x = name == "Information" || name == "Rewards" ? 10 : name == "Group" || name == "Top" ? 184 : name == "Commands" ? 344 : 504;
            int width = name == "Information" || name == "Rewards" ? 164 : name == "Links" || name == "Top" ? 140 : 150;
            Box("MenuSection" + name + "Selected", bar, x, 7, 3, 38, Lime);
            bar.Find("MenuSection" + name + "Selected").SetAsLastSibling();
            bar.Find("MenuSection" + name + "Selected").gameObject.SetActive(name == "Information");
        }
        bar.Find("MenuSectionRewards").gameObject.SetActive(false);
        bar.Find("MenuSectionTop").gameObject.SetActive(false);
    }

    private static void BuildWindow(RectTransform parent)
    {
        RectTransform window = Box("MenuWindow", parent, 0, 0, 1190, 740, Back, new Vector2(.5f, .5f), new Vector2(.5f, .5f), true);
        window.pivot = new Vector2(.5f, .5f);
        Box("MenuWindowAccent", window, 0, 736, 1190, 4, Lime);

        RectTransform sidebar = Box("MenuSidebar", window, 0, 0, 238, 740, Hex("0C1712", 1f));
        Label("MenuTopBrand", sidebar, "MRE", 66, 674, 146, 34, 23, Text, FontStyle.Bold, bold);
        Box("MenuSidebarBrandMark", sidebar, 16, 672, 40, 40, Lime);
        Label("MenuSidebarBrandLetter", sidebar, "M", 16, 672, 40, 40, 20, Hex("102219"), FontStyle.Bold, bold, TextAnchor.MiddleCenter);
        Label("MenuTopTagline", sidebar, "PEI  \u00b7  \u0412\u042b\u0416\u0418\u0412\u0410\u041d\u0418\u0415", 28, 648, 184, 18, 10, Muted, FontStyle.Bold, regular);
        Label("MenuSidebarHeading", sidebar, "\u041d\u0410\u0412\u0418\u0413\u0410\u0426\u0418\u042f", 28, 600, 184, 18, 10, Muted, FontStyle.Bold, regular);
        SectionButton(sidebar, "MenuSectionInformation", "\u0418\u043d\u0444\u043e\u0440\u043c\u0430\u0446\u0438\u044f", 16, 562, 206, 44);
        SectionButton(sidebar, "MenuSectionCommands", "\u041a\u043e\u043c\u0430\u043d\u0434\u044b", 16, 512, 206, 44);
        SectionButton(sidebar, "MenuSectionLinks", "\u0421\u0441\u044b\u043b\u043a\u0438", 16, 462, 206, 44);
        SectionButton(sidebar, "MenuSectionGroup", "\u0413\u0440\u0443\u043f\u043f\u0430", 16, 412, 206, 44);
        RectTransform settingsItem = Box("MenuSectionSettings", sidebar, 16, 362, 206, 44, Hex("1B3528", 1f));
        BuildSectionGlyph(settingsItem, "Settings", Lime);
        Label("MenuSectionSettingsText", settingsItem, "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438", 42, 0, 154, 44, 14, Text, FontStyle.Bold, regular);
        settingsItem.gameObject.SetActive(false);
        RectTransform settingsSelected = Box("MenuSectionSettingsSelected", sidebar, 16, 362, 3, 44, Lime);
        settingsSelected.gameObject.SetActive(false);
        SectionButton(sidebar, "MenuSectionRewards", "\u041d\u0430\u0433\u0440\u0430\u0434\u044b", 16, 562, 206, 44);
        SectionButton(sidebar, "MenuSectionTop", "\u0422\u043e\u043f \u0438\u0433\u0440\u043e\u043a\u043e\u0432", 16, 512, 206, 44);
        foreach (string name in new[] { "Information", "Group", "Commands", "Links", "Rewards", "Top" })
        {
            int y = name == "Information" || name == "Rewards" ? 562 : name == "Commands" || name == "Top" ? 512 : name == "Links" ? 462 : 412;
            RectTransform selected = Box("MenuSection" + name + "Selected", sidebar, 16, y, 3, 44, Lime);
            selected.SetAsLastSibling();
            selected.gameObject.SetActive(name == "Information");
        }
        sidebar.Find("MenuSectionRewards").gameObject.SetActive(false);
        sidebar.Find("MenuSectionTop").gameObject.SetActive(false);
        Box("MenuSidebarRule", sidebar, 16, 98, 206, 1, Hex("304138"));
        Label("MenuSidebarBottomTitle", sidebar, "\u0421\u0435\u0440\u0432\u0435\u0440 MRE", 28, 66, 184, 19, 12, Text, FontStyle.Bold, bold);
        Label("MenuSidebarBottomText", sidebar, "\u041a\u043e\u043c\u0430\u043d\u0434\u044b \u0438 \u0438\u043d\u0444\u043e\u0440\u043c\u0430\u0446\u0438\u044f\\n\u0432 \u043e\u0434\u043d\u043e\u043c \u043c\u0435\u0441\u0442\u0435", 28, 22, 184, 38, 11, Muted, FontStyle.Normal, regular, TextAnchor.UpperLeft);

        RectTransform titleBar = Box("MenuTitleBar", window, 238, 660, 952, 80, Hex("121F19", 1f));
        TopButton(titleBar, "MenuTopMain", "\u041e\u0441\u043d\u043e\u0432\u043d\u044b\u0435", 48, 16, 132);
        TopButton(titleBar, "MenuTopProgress", "\u041f\u0440\u043e\u0433\u0440\u0435\u0441\u0441", 184, 16, 132);
        TopButton(titleBar, "MenuTopSettings", "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438", 320, 16, 132);
        Box("MenuSelectedTopMain", titleBar, 48, 0, 68, 3, Lime);
        Box("MenuSelectedTopProgress", titleBar, 184, 0, 68, 3, Lime);
        Box("MenuSelectedTopSettings", titleBar, 320, 0, 68, 3, Lime);
        titleBar.Find("MenuSelectedTopMain").gameObject.SetActive(true);
        titleBar.Find("MenuSelectedTopProgress").gameObject.SetActive(false);
        titleBar.Find("MenuSelectedTopSettings").gameObject.SetActive(false);
        Label("MenuTopBrandEcho", titleBar, "MRE / PEI", 740, 24, 132, 24, 10, Muted, FontStyle.Bold, regular, TextAnchor.MiddleRight);
        Button("MenuClose", titleBar, "X", 888, 16, 38, 38, Hex("192820"), Hex("D7E9DD"), 17);

        RectTransform body = Box("MenuBody", window, 238, 50, 952, 610, Hex("101A16", 0f));
        Label("MenuTitle", body, "\u0418\u043d\u0444\u043e\u0440\u043c\u0430\u0446\u0438\u044f \u043e \u0441\u0435\u0440\u0432\u0435\u0440\u0435", 38, 547, 760, 38, 26, Text, FontStyle.Bold, bold);
        Label("MenuSubtitle", body, "\u0412\u0441\u0451 \u043d\u0435\u043e\u0431\u0445\u043e\u0434\u0438\u043c\u043e\u0435 \u0434\u043b\u044f \u043d\u0430\u0447\u0430\u043b\u0430 \u0438\u0433\u0440\u044b \u0438 \u043d\u0430\u0432\u0438\u0433\u0430\u0446\u0438\u0438 \u043f\u043e \u0441\u0435\u0440\u0432\u0435\u0440\u0443.", 38, 522, 760, 24, 13, Muted, FontStyle.Normal, regular);

        RectTransform information = Box("MenuInformationPanel", body, 0, 0, 952, 610, Hex("101A16", 0f));
        Label("MenuInformationEyebrow", information, "\u0421\u0415\u0420\u0412\u0415\u0420 / \u041e\u0411\u0417\u041e\u0420", 38, 585, 780, 18, 10, Lime, FontStyle.Bold, regular);
        Label("MenuInformationWelcomeTitle", information, "\u0414\u043e\u0431\u0440\u043e \u043f\u043e\u0436\u0430\u043b\u043e\u0432\u0430\u0442\u044c \u043d\u0430 MRE", 64, 407, 700, 27, 17, Text, FontStyle.Bold, bold);
        Label("MenuInformationWelcomeDescription", information, "\u0418\u0441\u0441\u043b\u0435\u0434\u0443\u0439\u0442\u0435 PEI, \u0441\u0442\u0440\u043e\u0439\u0442\u0435 \u0431\u0430\u0437\u0443 \u0438 \u0438\u0433\u0440\u0430\u0439\u0442\u0435 \u0432\u043c\u0435\u0441\u0442\u0435 \u0441 \u0434\u0440\u0443\u0437\u044c\u044f\u043c\u0438.", 64, 381, 700, 20, 12, Muted, FontStyle.Normal, regular);
        RectTransform welcome = Box("MenuInformationWelcome", information, 38, 364, 876, 106, Hex("193428", 1f));
        welcome.SetAsFirstSibling();
        RectTransform welcomeIcon = Box("MenuInformationWelcomeIcon", welcome, 824, 21, 40, 64, Hex("234C39", 1f));
        BuildGlyph(welcomeIcon, 4, Lime);
        AddInformationCard(information, 1, "\u041d\u0430\u0431\u043e\u0440\u044b \u0432\u0435\u0449\u0435\u0439", "/kits \u2014 \u0434\u043e\u0441\u0442\u0443\u043f\u043d\u044b\u0435 \u043d\u0430\u0431\u043e\u0440\u044b \u0438 \u0438\u0445 \u043f\u043e\u043b\u0443\u0447\u0435\u043d\u0438\u0435.", 38, 268, Gold);
        AddInformationCard(information, 2, "\u0414\u043e\u043c", "/homes \u2014 \u0432\u0430\u0448\u0438 \u043a\u0440\u043e\u0432\u0430\u0442\u0438 \u0438 \u0442\u043e\u0447\u043a\u0438 \u0434\u043e\u043c\u0430.", 486, 268, Lime);
        AddInformationCard(information, 3, "\u0422\u0435\u043b\u0435\u043f\u043e\u0440\u0442\u0430\u0446\u0438\u044f", "/tpa \u2014 \u0437\u0430\u043f\u0440\u043e\u0441 \u043d\u0430 \u043f\u0435\u0440\u0435\u043d\u043e\u0441 \u043a \u0438\u0433\u0440\u043e\u043a\u0443.", 38, 164, Lime);
        AddInformationCard(information, 4, "\u0425\u0440\u0430\u043d\u0438\u043b\u0438\u0449\u0435 \u0438 \u043e\u0431\u043c\u0435\u043d", "/s \u0438 /trade \u2014 \u0445\u0440\u0430\u043d\u0435\u043d\u0438\u0435 \u0438 \u043e\u0431\u043c\u0435\u043d.", 486, 164, Lime);
        Label("MenuInformationHint", information, "\u0412\u0441\u0435 \u0434\u043e\u0441\u0442\u0443\u043f\u043d\u044b\u0435 \u043a\u043e\u043c\u0430\u043d\u0434\u044b \u043d\u0430\u0445\u043e\u0434\u044f\u0442\u0441\u044f \u0432 \u0440\u0430\u0437\u0434\u0435\u043b\u0435 \u00ab\u041a\u043e\u043c\u0430\u043d\u0434\u044b\u00bb.", 38, 119, 876, 20, 11, Muted, FontStyle.Normal, regular);

        RectTransform rows = Box("MenuRowsPanel", body, 0, 0, 952, 610, Hex("101A16", 0f));
        rows.gameObject.SetActive(false);
        Label("MenuRowsEyebrow", rows, "", 38, 585, 850, 18, 10, Lime, FontStyle.Bold, regular);
        Label("MenuRowsHint", rows, "", 38, 492, 850, 21, 12, Muted, FontStyle.Normal, regular);
        for (int i = 1; i <= 10; i++) MenuRow(rows, i);

        RectTransform planned = Box("MenuPlannedPanel", body, 0, 0, 952, 610, Hex("101A16", 0f));
        planned.gameObject.SetActive(false);
        Label("MenuPlannedEyebrow", planned, "\u041f\u041b\u0410\u041d\u042b MRE", 38, 585, 850, 18, 10, Lime, FontStyle.Bold, regular);
        Label("MenuPlannedTitle", planned, "\u0420\u0430\u0437\u0434\u0435\u043b \u0433\u043e\u0442\u043e\u0432\u0438\u0442\u0441\u044f", 38, 438, 850, 42, 26, Text, FontStyle.Bold, bold);
        Label("MenuPlannedDescription", planned, "", 38, 326, 820, 92, 16, Muted, FontStyle.Normal, regular, TextAnchor.UpperLeft);
        Box("MenuPlannedRule", planned, 38, 274, 850, 1, Hex("35433B"));
        Label("MenuPlannedStatus", planned, "\u0420\u0430\u0437\u0434\u0435\u043b \u0432 \u0440\u0430\u0437\u0440\u0430\u0431\u043e\u0442\u043a\u0435.", 38, 230, 850, 24, 13, Gold, FontStyle.Bold, regular);

        RectTransform pager = Rect("MenuCommandPager", rows, 0, 10, 952, 30);
        pager.gameObject.SetActive(false);
        Button("MenuCommandsPrevious", pager, "<", 38, 0, 34, 30, Raised, Text, 17);
        Label("MenuPage", pager, "1 / 1", 80, 0, 72, 30, 12, Text, FontStyle.Bold, regular, TextAnchor.MiddleCenter);
        Button("MenuCommandsNext", pager, ">", 162, 0, 34, 30, Raised, Text, 17);

        Box("MenuFooterBar", window, 238, 0, 952, 50, Hex("121F19", 1f));
        Label("MenuFooter", window, "\u041d\u0430\u0436\u043c\u0438\u0442\u0435 X, \u0447\u0442\u043e\u0431\u044b \u0437\u0430\u043a\u0440\u044b\u0442\u044c \u043c\u0435\u043d\u044e", 900, 14, 258, 20, 11, Muted, FontStyle.Normal, regular, TextAnchor.MiddleRight);
    }

    private static void AddInformationCard(RectTransform parent, int index, string title, string description, int x, int y, Color accent)
    {
        string suffix = index.ToString("00");
        RectTransform card = Box("MenuInformationCard" + suffix, parent, x, y, 426, 92, Hex("1A2B22", 1f));
        RectTransform icon = Box("MenuInformationCardIcon" + suffix, card, 18, 27, 38, 38, Hex("284736", 1f));
        BuildGlyph(icon, index, accent);
        Label("MenuInformationCardTitle" + suffix, card, title, 68, 50, 340, 22, 14, Text, FontStyle.Bold, bold);
        Label("MenuInformationCardDescription" + suffix, card, description, 68, 18, 340, 34, 11, Muted, FontStyle.Normal, regular, TextAnchor.UpperLeft);
    }

    private static void BuildGlyph(RectTransform parent, int kind, Color color)
    {
        // Basic line geometry only: no icon-font code points or extra asset dependencies.
        if (kind == 1) // box
        {
            Stroke(parent, "GlyphTop", 9, 29, 22, 2, 0, color); Stroke(parent, "GlyphBottom", 9, 9, 22, 2, 0, color);
            Stroke(parent, "GlyphLeft", 9, 9, 2, 22, 0, color); Stroke(parent, "GlyphRight", 29, 9, 2, 22, 0, color);
            Stroke(parent, "GlyphMiddle", 19, 10, 2, 20, 0, color); Stroke(parent, "GlyphFlapL", 11, 21, 10, 2, -24, color); Stroke(parent, "GlyphFlapR", 20, 21, 10, 2, 24, color);
        }
        else if (kind == 2) // home
        {
            Stroke(parent, "GlyphWallL", 10, 9, 2, 15, 0, color); Stroke(parent, "GlyphWallR", 28, 9, 2, 15, 0, color); Stroke(parent, "GlyphBase", 10, 9, 20, 2, 0, color);
            Stroke(parent, "GlyphRoofL", 7, 22, 15, 2, 43, color); Stroke(parent, "GlyphRoofR", 19, 22, 15, 2, -43, color);
            Stroke(parent, "GlyphDoor", 18, 9, 2, 9, 0, color);
        }
        else if (kind == 3) // opposing teleport arrows
        {
            Stroke(parent, "GlyphArrowA", 10, 23, 18, 2, 0, color); Stroke(parent, "GlyphArrowATop", 24, 27, 9, 2, 42, color); Stroke(parent, "GlyphArrowABottom", 24, 19, 9, 2, -42, color);
            Stroke(parent, "GlyphArrowB", 12, 13, 18, 2, 0, color); Stroke(parent, "GlyphArrowBTop", 7, 17, 9, 2, 42, color); Stroke(parent, "GlyphArrowBBottom", 7, 9, 9, 2, -42, color);
        }
        else // shield
        {
            Stroke(parent, "GlyphShieldTop", 10, 28, 20, 2, 0, color); Stroke(parent, "GlyphShieldL", 9, 17, 14, 2, 78, color);
            Stroke(parent, "GlyphShieldR", 21, 17, 14, 2, -78, color); Stroke(parent, "GlyphShieldBottomL", 12, 10, 13, 2, -36, color); Stroke(parent, "GlyphShieldBottomR", 18, 10, 13, 2, 36, color);
        }
    }

    private static void BuildSectionGlyph(RectTransform button, string section, Color color)
    {
        RectTransform icon = Rect(button.name + "Icon", button, 12, 12, 20, 20);
        if (section == "Information")
        {
            Stroke(icon, "GlyphFrameTop", 3, 17, 14, 2, 0, color); Stroke(icon, "GlyphFrameBottom", 3, 3, 14, 2, 0, color);
            Stroke(icon, "GlyphFrameLeft", 3, 3, 2, 14, 0, color); Stroke(icon, "GlyphFrameRight", 15, 3, 2, 14, 0, color);
            Stroke(icon, "GlyphInfoStem", 9, 6, 2, 7, 0, color); Stroke(icon, "GlyphInfoDot", 9, 14, 2, 2, 0, color);
        }
        else if (section == "Group")
        {
            Stroke(icon, "GlyphHeadA", 4, 12, 5, 5, 0, color); Stroke(icon, "GlyphHeadB", 11, 12, 5, 5, 0, color);
            Stroke(icon, "GlyphBodyA", 2, 4, 8, 2, 0, color); Stroke(icon, "GlyphBodyB", 10, 4, 8, 2, 0, color);
        }
        else if (section == "Commands")
        {
            Stroke(icon, "GlyphTerminalTop", 2, 15, 16, 2, 0, color); Stroke(icon, "GlyphTerminalBottom", 2, 3, 16, 2, 0, color);
            Stroke(icon, "GlyphTerminalLeft", 2, 3, 2, 14, 0, color); Stroke(icon, "GlyphTerminalRight", 16, 3, 2, 14, 0, color);
            Stroke(icon, "GlyphPromptA", 5, 10, 6, 2, 35, color); Stroke(icon, "GlyphPromptB", 5, 7, 6, 2, -35, color); Stroke(icon, "GlyphPromptLine", 12, 7, 3, 2, 0, color);
        }
        else if (section == "Links")
        {
            Stroke(icon, "GlyphLinkA", 2, 6, 11, 5, 42, color); Stroke(icon, "GlyphLinkB", 8, 9, 11, 5, 42, color);
            Stroke(icon, "GlyphLinkJoin", 7, 8, 7, 2, 0, color);
        }
        else if (section == "Rewards")
        {
            Stroke(icon, "GlyphGiftBase", 3, 3, 14, 2, 0, color); Stroke(icon, "GlyphGiftSideL", 3, 3, 2, 9, 0, color); Stroke(icon, "GlyphGiftSideR", 15, 3, 2, 9, 0, color);
            Stroke(icon, "GlyphGiftTop", 2, 11, 16, 2, 0, color); Stroke(icon, "GlyphGiftRibbon", 9, 3, 2, 10, 0, color);
        }
        else if (section == "Top")
        {
            Stroke(icon, "GlyphBar1", 3, 3, 3, 7, 0, color); Stroke(icon, "GlyphBar2", 8, 3, 3, 12, 0, color); Stroke(icon, "GlyphBar3", 13, 3, 3, 16, 0, color);
        }
        else // settings
        {
            Stroke(icon, "GlyphGearV", 8, 2, 4, 16, 0, color); Stroke(icon, "GlyphGearH", 2, 8, 16, 4, 0, color);
            Stroke(icon, "GlyphGearCore", 7, 7, 6, 6, 0, Hex("101A16"));
        }
    }

    private static void Stroke(Transform parent, string name, int x, int y, int width, int height, float angle, Color color)
    {
        RectTransform stroke = Box(parent.name + name, parent, x, y, width, height, color);
        stroke.pivot = new Vector2(.5f, .5f);
        stroke.anchoredPosition = new Vector2(x + width / 2f, y + height / 2f);
        stroke.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private static void MenuRow(RectTransform parent, int index)
    {
        string suffix = index.ToString("00");
        int rowIndex = index - 1;
        int y = 446 - rowIndex * 38;
        RectTransform row = Box("MenuRow" + suffix, parent, 38, y, 876, 35, index % 2 == 0 ? Hex("233229") : Hex("1D2B23"));
        row.gameObject.SetActive(true);
        Box("MenuRowAccent" + suffix, row, 0, 0, 3, 35, index == 1 ? Lime : Cyan);
        string[] commands = { "Наборы", "Дом", "TPA", "Хранилище", "Обмен", "Группы", "Ивенты", "Команды", "", "" };
        string[] descriptions = { "/kits и /kit <имя> — список и получение наборов.", "/homes — сохранённые точки; /home — перенос к кровати.", "/tpa <игрок> — запрос на телепортацию.", "/s — личное виртуальное хранилище.", "/trade <игрок> — безопасный обмен предметами.", "В одной группе может быть до четырёх игроков.", "/whenairdrop показывает время до следующего аирдропа.", "/help открывает этот список; /menu — главное меню.", "", "" };
        Label("MenuRowTitle" + suffix, row, commands[index - 1], 15, 0, 192, 35, 15, Gold, FontStyle.Bold, bold);
        Label("MenuRowDescription" + suffix, row, descriptions[index - 1], 222, 1, 642, 33, 13, Text, FontStyle.Normal, regular);
    }

    private static void TopButton(RectTransform parent, string name, string label, int x, int y, int width)
    {
        RectTransform button = Box(name, parent, x, y, width, 46, Raised, button: true);
        button.gameObject.AddComponent<Button>().targetGraphic = button.GetComponent<Image>();
        Label(name + "Text", button, label, 6, 0, width - 12, 46, 15, Text, FontStyle.Bold, regular, TextAnchor.MiddleCenter);
    }

    private static void SectionButton(RectTransform parent, string name, string label, int x, int y, int width)
    {
        RectTransform button = Box(name, parent, x, y, width, 44, Hex("101A16", 1f), button: true);
        button.gameObject.AddComponent<Button>().targetGraphic = button.GetComponent<Image>();
        BuildSectionGlyph(button, name.Replace("MenuSection", ""), Muted);
        Label(name + "Text", button, label, 42, 0, width - 54, 44, 13, Text, FontStyle.Bold, regular);
    }

    private static void Button(string name, RectTransform parent, string value, int x, int y, int w, int h, Color bg, Color fg, int size)
    {
        RectTransform rect = Box(name, parent, x, y, w, h, bg, button: true);
        rect.gameObject.AddComponent<Button>().targetGraphic = rect.GetComponent<Image>();
        Label(name + "Text", rect, value, 0, 0, w, h, size, fg, FontStyle.Bold, bold, TextAnchor.MiddleCenter);
    }

    private static RectTransform Box(string name, Transform parent, int x, int y, int w, int h, Color color, Vector2? min = null, Vector2? max = null, bool button = false)
    {
        RectTransform rect = Rect(name, parent, x, y, w, h, min ?? Vector2.zero, max ?? Vector2.zero);
        var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = button;
        if (roundedSprite != null && name != "MenuBackdrop")
        {
            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
        }
        return rect;
    }

    private static Text Label(string name, Transform parent, string value, int x, int y, int w, int h, int size, Color color, FontStyle style, Font font, TextAnchor align = TextAnchor.MiddleLeft)
    {
        RectTransform rect = Rect(name, parent, x, y, w, h);
        var text = rect.gameObject.AddComponent<Text>();
        text.text = value; text.font = font; text.fontSize = size; text.fontStyle = style; text.color = color;
        text.alignment = align; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
        text.supportRichText = false; text.raycastTarget = false;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent, int x, int y, int w, int h, Vector2? min = null, Vector2? max = null)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = min ?? Vector2.zero; rect.anchorMax = max ?? Vector2.zero;
        rect.pivot = new Vector2(0, 0); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static void Validate(GameObject root)
    {
        var all = root.GetComponentsInChildren<RectTransform>(true);
        var duplicate = all.GroupBy(x => x.name).FirstOrDefault(x => x.Count() > 1);
        if (duplicate != null) throw new InvalidOperationException("Повторяется имя элемента: " + duplicate.Key);
        string[] required = { "MenuContent", "MenuClose", "MenuTitle", "MenuSubtitle", "MenuTopBrand", "MenuTopMain", "MenuTopProgress", "MenuTopSettings", "MenuSelectedTopMain", "MenuSelectedTopProgress", "MenuSelectedTopSettings", "MenuSectionInformation", "MenuSectionGroup", "MenuSectionCommands", "MenuSectionLinks", "MenuSectionRewards", "MenuSectionTop", "MenuSectionInformationSelected", "MenuSectionGroupSelected", "MenuSectionCommandsSelected", "MenuSectionLinksSelected", "MenuSectionRewardsSelected", "MenuSectionTopSelected", "MenuRowsPanel", "MenuPlannedPanel", "MenuPlannedTitle", "MenuPlannedDescription", "MenuCommandPager", "MenuCommandsPrevious", "MenuCommandsNext", "MenuPage", "MenuFooter", "MenuRow01", "MenuRowTitle01", "MenuRowDescription01", "MenuRow10", "MenuRowTitle10", "MenuRowDescription10" };
        foreach (string name in required) if (!all.Any(x => x.name == name)) throw new InvalidOperationException("Отсутствует элемент " + name);
        if (root.GetComponent<Canvas>() == null || root.GetComponent<GraphicRaycaster>() == null) throw new InvalidOperationException("Effect должен иметь Canvas и GraphicRaycaster.");
        foreach (string name in new[] { "MenuClose", "MenuTopMain", "MenuTopProgress", "MenuTopSettings", "MenuSectionInformation", "MenuSectionGroup", "MenuSectionCommands", "MenuSectionLinks", "MenuSectionRewards", "MenuSectionTop", "MenuCommandsPrevious", "MenuCommandsNext" })
            if (root.transform.Find("MenuContent") == null || !all.Any(x => x.name == name && x.GetComponent<Button>() != null)) throw new InvalidOperationException("Отсутствует кнопка " + name);
    }

    private static Color Hex(string hex, float alpha = 1)
    {
        Color color; if (!ColorUtility.TryParseHtmlString("#" + hex, out color)) throw new ArgumentException(hex); color.a = alpha; return color;
    }
}
