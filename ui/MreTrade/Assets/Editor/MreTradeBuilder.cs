using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Только Editor; игровой bundle содержит стандартный uGUI без клиентского кода.
public static class MreTradeBuilder
{
    private const string PrefabPath = "Assets/MreTrade/Effect.prefab";
    private const string EffectGuid = "a986933995df4d29816efcc37f87b220";
    private const string ExperimentalAsset = "Assets/MreTradeLocalExperiment/maplestrike-icon.png";
    private const string ExperimentalPrefab = "Assets/MreTradeLocalExperiment/Effect.prefab";
    private static Font font;
    private static Color ColorOf(string value) { Color color; if (!ColorUtility.TryParseHtmlString("#" + value, out color)) throw new ArgumentException(value); return color; }

    [MenuItem("MRE/Trade/Создать окно обмена")]
    public static void Generate() { GeneratePrefab(PrefabPath, null); }

    private static void GeneratePrefab(string prefabPath, Sprite icon)
    {
        if (Application.unityVersion != "2022.3.62f3") throw new InvalidOperationException("Нужен Unity 2022.3.62f3.");
        PlayerSettings.colorSpace = ColorSpace.Linear;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) throw new InvalidOperationException("Встроенный шрифт Unity недоступен.");
        Directory.CreateDirectory("Assets/MreTrade");
        var root = new GameObject("Effect", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(GraphicRaycaster));
        try
        {
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var group = root.GetComponent<CanvasGroup>(); group.interactable = true; group.blocksRaycasts = true;
            var content = Rect("TradeContent", root.transform, 1100, 750, 0, 0);
            Box("TradeBackground", content, 1100, 750, 0, 0, "11191D99").GetComponent<Image>().raycastTarget = true;
            Text("TradeCaption", content, "MRE  /  ОБМЕН ПРЕДМЕТАМИ", -510, 325, 1020, 30, 24, "B8D66B");
            Text("TradePartner", content, "Партнёр: ожидание сервера", -510, 289, 1020, 28, 20, "EDF1E8");
            Text("TradeOwnOffer", content, "ВАШЕ ПРЕДЛОЖЕНИЕ", -510, 243, 490, 26, 18, "B8D66B");
            Text("TradePartnerOffer", content, "ПРЕДЛОЖЕНИЕ ПАРТНЁРА", 20, 243, 490, 26, 18, "8ABCCF");
            for (int i = 0; i < 10; i++)
            {
                float x = -510 + (i % 2) * 250, y = 174 - (i / 2) * 72;
                var own = Button("TradeOwnSlot" + i, content, x, y, 240, 62, "25342D99");
                Icon("TradeOwnIcon" + i, own, icon);
                Text("TradeOwn" + i, own, "Пустая ячейка", -52, -20, 160, 40, 17, "EDF1E8");
                var other = Box("TradeOtherSlot" + i, content, 240, 62, x + 650, y, "202B3199");
                Icon("TradeOtherIcon" + i, other, icon);
                Text("TradeOther" + i, other, "Пустая ячейка", -52, -20, 160, 40, 17, "C3CFD4");
            }
            Text("TradeHint", content, "Нажмите свою ячейку, чтобы выбрать предметы в обычном инвентаре. Чужие ячейки доступны только для просмотра.", -510, -205, 1020, 42, 17, "AEBBB4");
            Text("TradeStatus", content, "Сервер готовит обмен…", -510, -260, 1020, 45, 19, "EDF1E8");
            var confirm = Button("TradeConfirm", content, -510, -315, 490, 58, "B8D66B");
            var confirmText = Text("TradeConfirmLabel", confirm, "ПОДТВЕРДИТЬ", -225, -17, 450, 34, 20, "172018"); confirmText.alignment = TextAnchor.MiddleCenter;
            var cancel = Button("TradeCancel", content, 20, -315, 490, 58, "49322F");
            var cancelText = Text("TradeCancelLabel", cancel, "ОТМЕНИТЬ ОБМЕН", -225, -17, 450, 34, 20, "FFE8E4"); cancelText.alignment = TextAnchor.MiddleCenter;
            Validate(root, icon != null);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("MRE Trade: создан prefab. Это не подтверждает отображение в Unturned.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [MenuItem("MRE/Trade/Собрать Windows bundle")]
    public static void BuildWindows()
    {
        Generate();
        BuildBundle(PrefabPath, false);
    }

    // The extracted game icon is only imported for this explicit local experiment.
    public static void BuildWindowsExperimental()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        string source = Path.Combine(root, "runtime/maplestrike-extract/maplestrike-icon.png");
        if (!File.Exists(source)) throw new FileNotFoundException("Local experiment icon is missing.", source);
        string folder = "Assets/MreTradeLocalExperiment";
        if (AssetDatabase.IsValidFolder(folder) || Directory.Exists(folder)) throw new InvalidOperationException("Temporary experiment folder already exists; inspect it before removing it.");
        Directory.CreateDirectory(folder);
        try
        {
            File.Copy(source, ExperimentalAsset);
            AssetDatabase.ImportAsset(ExperimentalAsset, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(ExperimentalAsset);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ExperimentalAsset);
            if (sprite == null) throw new InvalidDataException("Failed to import local experiment sprite.");
            GeneratePrefab(ExperimentalPrefab, sprite);
            BuildBundle(ExperimentalPrefab, true);
        }
        finally { AssetDatabase.DeleteAsset(folder); AssetDatabase.Refresh(); }
    }

    private static void BuildBundle(string prefabPath, bool experimental)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        string output = Path.Combine(projectRoot, experimental ? "runtime/ui-builds/MreTrade/ExperimentalMaplestrike/Windows" : "runtime/ui-builds/MreTrade/Windows"); Directory.CreateDirectory(output);
        var map = new AssetBundleBuild { assetBundleName = "MreTrade.unity3d", assetNames = new[] { prefabPath }, addressableNames = new[] { "Effect" } };
        var result = BuildPipeline.BuildAssetBundles(output, new[] { map }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        if (result == null) throw new InvalidOperationException("Unity не создал bundle обмена.");
        string source = Path.Combine(output, "MreTrade.unity3d");
        var bundle = AssetBundle.LoadFromFile(source); if (bundle == null) throw new InvalidDataException("Bundle не читается Unity.");
        try { var prefab = bundle.LoadAsset<GameObject>("Effect"); if (prefab == null) throw new InvalidDataException("Нет prefab по адресу Effect."); Validate(prefab, experimental); }
        finally { bundle.Unload(true); }
        string mod = Path.Combine(output, "MreTrade"); Directory.CreateDirectory(mod);
        File.Copy(source, Path.Combine(mod, "MreTrade.unity3d"), true);
        File.WriteAllText(Path.Combine(mod, "MreTrade.dat"), "GUID " + EffectGuid + "\nType Effect\nID 48622\nAsset_Bundle_Version 6\nExclude_From_Master_Bundle\n", new UTF8Encoding(false));
        using (var sha = SHA256.Create())
        {
            string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Path.Combine(output, "bundle-build.json"), "{\n  \"effect_id\":48622,\n  \"effect_guid\":\"" + EffectGuid + "\",\n  \"effect_key\":14822,\n  \"unity_version\":\"" + Application.unityVersion + "\",\n  \"target\":\"StandaloneWindows64\",\n  \"bundle_sha256\":\"" + hash + "\",\n  \"prefab_address_verified\":true,\n  \"in_game_verified\":false\n}\n", new UTF8Encoding(false));
        }
        Debug.Log("MRE Trade: bundle собран, не установлен и не опубликован: " + mod);
    }

    private static RectTransform Rect(string name, Transform parent, float width, float height, float x, float y)
    {
        var obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false);
        var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, y); return rect;
    }
    private static RectTransform Box(string name, Transform parent, float width, float height, float x, float y, string color)
    { var rect = Rect(name, parent, width, height, x, y); var image = rect.gameObject.AddComponent<Image>(); image.color = ColorOf(color); image.raycastTarget = false; return rect; }
    private static RectTransform Button(string name, Transform parent, float x, float y, float width, float height, string color)
    {
        var rect = Box(name, parent, width, height, x + width / 2, y, color); var image = rect.GetComponent<Image>(); image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; return rect;
    }
    private static void Icon(string name, Transform parent, Sprite sprite)
    {
        var rect = Rect(name, parent, 52, 48, -86, 0);
        var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = sprite == null ? Color.clear : Color.white; image.preserveAspect = true; image.raycastTarget = false;
        rect.gameObject.SetActive(false);
    }
    private static Text Text(string name, Transform parent, string value, float x, float y, float width, float height, int size, string color)
    {
        var rect = Rect(name, parent, width, height, x + width / 2, y + height / 2);
        var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.color = ColorOf(color);
        text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
    }
    private static void Validate(GameObject root, bool expectIcon)
    {
        var transforms = root.GetComponentsInChildren<RectTransform>(true);
        var names = transforms.Select(x => x.name).ToArray();
        if (names.Distinct().Count() != names.Length) throw new InvalidDataException("Повторные имена UI объектов.");
        foreach (string n in new[] { "TradePartner", "TradeOwnOffer", "TradePartnerOffer", "TradeStatus" }.Concat(Enumerable.Range(0,10).Select(i=>"TradeOwn"+i)).Concat(Enumerable.Range(0,10).Select(i=>"TradeOther"+i)))
            if (transforms.SingleOrDefault(x=>x.name==n)?.GetComponent<Text>() == null) throw new InvalidDataException("Нет поля " + n);
        foreach (string n in new[] { "TradeConfirm", "TradeCancel" }.Concat(Enumerable.Range(0,10).Select(i=>"TradeOwnSlot"+i)))
            if (transforms.SingleOrDefault(x=>x.name==n)?.GetComponent<Button>() == null) throw new InvalidDataException("Нет кнопки " + n);
        foreach (string n in Enumerable.Range(0,10).Select(i=>"TradeOwnIcon"+i).Concat(Enumerable.Range(0,10).Select(i=>"TradeOtherIcon"+i)))
        {
            var icon = transforms.SingleOrDefault(x=>x.name==n);
            var image = icon == null ? null : icon.GetComponent<Image>();
            if (image == null || image.raycastTarget || icon.gameObject.activeSelf || (image.sprite != null) != expectIcon)
                throw new InvalidDataException("Invalid icon contract: " + n);
        }
        if (root.GetComponentsInChildren<Button>(true).Length != 12 || root.GetComponent<GraphicRaycaster>() == null || root.GetComponent<Canvas>().sortingOrder != 20)
            throw new InvalidDataException("Неверный контракт ввода окна обмена.");
        if (root.GetComponentsInChildren<Text>(true).Any(x=>x.supportRichText || x.raycastTarget)) throw new InvalidDataException("Текст должен быть без rich text и без перехвата кликов.");
        var content = transforms.Single(x => x.name == "TradeContent");
        foreach (var rect in transforms.Where(x => x.parent == content))
            if (Math.Abs(rect.anchoredPosition.x) + rect.sizeDelta.x/2 > 550 || Math.Abs(rect.anchoredPosition.y) + rect.sizeDelta.y/2 > 375)
                throw new InvalidDataException("Объект выходит за границы окна: " + rect.name);
    }
}
