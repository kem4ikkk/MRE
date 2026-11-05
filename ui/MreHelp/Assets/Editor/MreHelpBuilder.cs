using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Редакторский генератор собственного Effect asset; игровой клиент не модифицируется.
public static class MreHelpBuilder
{
    private const string PrefabPath = "Assets/MreHelp/Effect.prefab";
    private const string EffectGuid = "6e4a31b49c874f428a622e1a68e4d203";
    private const ushort EffectId = 48625;
    private static readonly Color Overlay = Hex("080C0D", 0.76f);
    private static readonly Color Back = Hex("111719", 0.98f);
    private static readonly Color Panel = Hex("1B2325", 1f);
    private static readonly Color Raised = Hex("222D2F", 1f);
    private static readonly Color Text = Hex("EEF2E8", 1f);
    private static readonly Color Muted = Hex("9CA7A2", 1f);
    private static readonly Color Lime = Hex("B8D66B", 1f);
    private static readonly Color Water = Hex("79B9D0", 1f);
    private static Font regular;
    private static Font bold;

    [MenuItem("MRE/Создать UI помощи")]
    public static void Generate()
    {
        regular = AssetDatabase.LoadAssetAtPath<Font>("Assets/MreFonts/PTSans-Regular.ttf");
        bold = AssetDatabase.LoadAssetAtPath<Font>("Assets/MreFonts/PTSans-Bold.ttf");
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
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform content = Rect("HelpContent", root.transform, 0, 0, 0, 0, Vector2.zero, Vector2.one);
            content.offsetMin = content.offsetMax = Vector2.zero;
            Box("HelpBackdrop", content, 0, 0, 0, 0, Overlay, Vector2.zero, Vector2.one, true).offsetMin = Vector2.zero;

            RectTransform window = Box("HelpWindow", content, 0, 0, 1120, 740, Back, new Vector2(.5f, .5f), new Vector2(.5f, .5f), true);
            window.pivot = new Vector2(.5f, .5f);
            Box("HelpTopAccent", window, 0, 736, 1120, 4, Lime);
            Header(window);
            Sidebar(window);
            MainContent(window);
            Footer(window);
            Validate(root);
            Directory.CreateDirectory("Assets/MreHelp");
            content.gameObject.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("MRE Help: создан " + PrefabPath + "; ID=" + EffectId + ", GUID=" + EffectGuid + ".");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [MenuItem("MRE/Собрать Windows bundle помощи")]
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
        if (manifest == null) throw new InvalidOperationException("Unity не собрал MreHelp bundle.");
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
        File.WriteAllText(Path.Combine(output, "effect-manifest.json"),
            "{\n  \"effect_id\": " + EffectId + ",\n  \"effect_guid\": \"" + EffectGuid + "\",\n  \"effect_key\": 14825,\n  \"bundle\": \"MreHelp.unity3d\",\n  \"prefab_address\": \"Effect\",\n  \"unity_version\": \"" + Application.unityVersion + "\",\n  \"target\": \"StandaloneWindows64\",\n  \"bundle_sha256\": \"" + sha + "\",\n  \"prefab_address_verified\": true,\n  \"in_game_verified\": false,\n  \"workshop_published\": false\n}\n", new System.Text.UTF8Encoding(false));
        Debug.Log("MRE Help: Windows bundle собран: " + mod + ". Не опубликован и не установлен.");
    }

    public static void RenderPreview()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string path = Path.Combine(projectRoot, "Build/Windows/MreHelp/MreHelp.unity3d");
        var bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null) throw new InvalidOperationException("Не найден собранный MreHelp bundle: " + path);
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
            Debug.Log("MRE Help preview: " + Path.Combine(projectRoot, "Build/MreHelp-preview.png"));
        }
        finally { RenderTexture.active = null; cameraObject.GetComponent<Camera>().targetTexture = null; texture.Release(); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(root); bundle.Unload(true); }
    }

    private static void Header(RectTransform parent)
    {
        RectTransform header = Rect("HelpHeader", parent, 28, 650, 1064, 72);
        Label("HelpEyebrow", header, "MRE  /  СПРАВКА", 0, 43, 740, 22, 13, Lime, FontStyle.Bold, regular);
        Label("HelpTitle", header, "Команды сервера", 0, 7, 740, 38, 29, Text, FontStyle.Bold, bold);
        Label("HelpSubtitle", header, "Коротко о возможностях MRE и командах", 0, -17, 740, 22, 14, Muted, FontStyle.Normal, regular);
        Button("HelpClose", header, "×", 28, 15, 48, 42, Raised, Text, 25);
    }

    private static void Sidebar(RectTransform parent)
    {
        RectTransform side = Box("HelpSidebar", parent, 28, 30, 238, 600, Panel);
        Label("HelpSectionLabel", side, "РАЗДЕЛЫ", 18, 558, 202, 24, 11, Muted, FontStyle.Bold, regular);
        Tab(side, "Basics", "Основное", 18, 504, true);
        Tab(side, "Travel", "Перемещение", 18, 446, false);
        Tab(side, "Groups", "Группы", 18, 388, false);
        Tab(side, "Combat", "Бой и выживание", 18, 330, false);
        Box("HelpSidebarRule", side, 18, 292, 202, 1, Hex("364144"));
        Label("HelpTipTitle", side, "ПОДСКАЗКА", 18, 246, 202, 20, 11, Lime, FontStyle.Bold, regular);
        Label("HelpTipText", side, "Введите /help в любой момент, чтобы открыть эту справку.", 18, 166, 194, 72, 14, Muted, FontStyle.Normal, regular, TextAnchor.UpperLeft);
        Label("HelpBrand", side, "PEI  ·  MAX4  ·  PvP", 18, 20, 202, 20, 11, Muted, FontStyle.Normal, regular);
    }

    private static void MainContent(RectTransform parent)
    {
        RectTransform main = Rect("HelpMain", parent, 292, 30, 800, 600);
        Label("HelpListHeading", main, "КОМАНДЫ", 0, 561, 760, 22, 11, Muted, FontStyle.Bold, regular);
        Label("HelpListHint", main, "Введите выбранную команду в чат", 0, 535, 760, 22, 13, Muted, FontStyle.Normal, regular);
        HelpRow(main, 1, "/help", "Открыть эту справку по командам", true);
        HelpRow(main, 2, "/home", "Начать перемещение к своей кровати", false);
        HelpRow(main, 3, "/tpa <игрок>", "Отправить запрос на телепортацию", false);
        HelpRow(main, 4, "/tpa a", "Принять входящий запрос", false);
        HelpRow(main, 5, "/tpa d", "Отклонить запрос или отменить перенос", false);
        HelpRow(main, 6, "/kits", "Посмотреть список доступных наборов", false);
        HelpRow(main, 7, "/kit start", "Получить стартовый набор", false);
        HelpRow(main, 8, "/vote day", "Начать ночное голосование за день", false);
        HelpRow(main, 9, "/vote yes", "Проголосовать за наступление дня", false);
        HelpRow(main, 10, "/vote no", "Проголосовать против наступления дня", false);
        Label("HelpListStatus", main, "Команды могут требовать отдельное разрешение.", 0, 0, 760, 24, 12, Muted, FontStyle.Normal, regular);
    }

    private static void HelpRow(RectTransform parent, int index, string command, string description, bool active)
    {
        string suffix = index.ToString("00");
        int y = 484 - (index - 1) * 49;
        RectTransform visible = Rect("HelpContent" + suffix, parent, 0, y, 790, 43);
        RectTransform row = Box("HelpRow" + suffix, visible, 0, 0, 790, 43, index == 1 ? Hex("26312A") : Panel);
        Box("HelpRowAccent" + suffix, row, 0, 0, 3, 43, index == 1 ? Lime : Water);
        Label("HelpCommand" + suffix, row, command, 16, 10, 190, 23, 15, index == 1 ? Lime : Text, FontStyle.Bold, bold);
        Label("HelpDescription" + suffix, row, description, 218, 9, 550, 25, 13, Muted, FontStyle.Normal, regular);
    }

    private static void Footer(RectTransform parent)
    {
        RectTransform footer = Rect("HelpFooter", parent, 0, 0, 1120, 25);
        Box("HelpFooterRule", footer, 28, 24, 1064, 1, Hex("2B3537"));
        Label("HelpFooterLeft", footer, "MRE  ·  PEI  ·  Выживание", 28, 0, 300, 18, 11, Muted, FontStyle.Normal, regular);
        Label("HelpFooterText", footer, "Команды текущего раздела", 350, 0, 420, 18, 11, Muted, FontStyle.Normal, regular, TextAnchor.MiddleCenter);
        Label("HelpFooterRight", footer, "НАЖМИТЕ × ДЛЯ ЗАКРЫТИЯ", 820, 0, 272, 18, 11, Muted, FontStyle.Bold, regular, TextAnchor.MiddleRight);
    }

    private static void Tab(RectTransform parent, string suffix, string label, int x, int y, bool selected)
    {
        RectTransform button = Box("HelpTab" + suffix, parent, x, y, 202, 46, Panel, button: true);
        var selectable = button.gameObject.AddComponent<Button>();
        selectable.targetGraphic = button.GetComponent<Image>();
        Label("HelpTabText" + suffix, button, label, 14, 0, 174, 46, 13, Text, FontStyle.Bold, regular);
        Box("HelpSelected" + suffix, button, 0, 0, 3, 46, Lime);
        if (!selected) button.Find("HelpSelected" + suffix).gameObject.SetActive(false);
    }

    private static void Button(string name, RectTransform parent, string value, int x, int y, int w, int h, Color bg, Color fg, int size)
    {
        RectTransform rect = Box(name, parent, x, y, w, h, bg, new Vector2(1, 1), new Vector2(1, 1), true);
        rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-x, -y);
        rect.sizeDelta = new Vector2(w, h);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        Label(name + "Text", rect, value, 0, 0, w, h, size, fg, FontStyle.Bold, bold, TextAnchor.MiddleCenter);
    }

    private static RectTransform Box(string name, Transform parent, int x, int y, int w, int h, Color color, Vector2? min = null, Vector2? max = null, bool button = false)
    {
        RectTransform rect = Rect(name, parent, x, y, w, h, min ?? Vector2.zero, max ?? Vector2.zero);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = button;
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
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = min ?? Vector2.zero; rect.anchorMax = max ?? Vector2.zero;
        rect.pivot = new Vector2(0, 0); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static void Validate(GameObject root)
    {
        var all = root.GetComponentsInChildren<RectTransform>(true);
        var duplicate = all.GroupBy(x => x.name).FirstOrDefault(x => x.Count() > 1);
        if (duplicate != null) throw new InvalidOperationException("Повторяется имя элемента: " + duplicate.Key);
        foreach (string name in new[] { "HelpContent", "HelpClose", "HelpCloseText", "HelpTitle", "HelpSubtitle", "HelpTabBasics", "HelpTabTravel", "HelpTabGroups", "HelpTabCombat", "HelpSelectedBasics", "HelpSelectedTravel", "HelpSelectedGroups", "HelpSelectedCombat", "HelpContent01", "HelpRow01", "HelpCommand01", "HelpDescription01", "HelpContent10", "HelpRow10", "HelpCommand10", "HelpDescription10", "HelpFooter", "HelpFooterText" })
            if (!all.Any(x => x.name == name)) throw new InvalidOperationException("Отсутствует элемент " + name);
        if (root.GetComponent<Canvas>() == null || root.GetComponent<GraphicRaycaster>() == null) throw new InvalidOperationException("Effect должен иметь Canvas и GraphicRaycaster.");
    }

    private static Color Hex(string hex, float alpha = 1)
    {
        Color color;
        if (!ColorUtility.TryParseHtmlString("#" + hex, out color)) throw new ArgumentException(hex);
        color.a = alpha;
        return color;
    }
}
