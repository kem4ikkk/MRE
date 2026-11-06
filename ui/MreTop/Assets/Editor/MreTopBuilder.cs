using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class MreTopBuilder
{
    private const string PrefabPath = "Assets/MreTop/Effect.prefab";
    private const string EffectGuid = "a3484011ca53436e92008ee933211fb5";
    private static Font font;
    private static Color ColorOf(string value) { Color c; if (!ColorUtility.TryParseHtmlString("#" + value, out c)) throw new ArgumentException(value); return c; }
    public static void Generate()
    {
        if (Application.unityVersion != "2022.3.62f3") throw new InvalidOperationException("Unity 2022.3.62f3 required");
        PlayerSettings.colorSpace = ColorSpace.Linear;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) throw new InvalidOperationException("Font missing");
        Directory.CreateDirectory("Assets/MreTop");
        var root = new GameObject("Effect", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(GraphicRaycaster));
        try
        {
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var group = root.GetComponent<CanvasGroup>(); group.interactable = true; group.blocksRaycasts = true;
            var content = Rect("TopContent", root.transform, 800, 680, 0, 0);
            Box("TopBackground", content, 800, 680, 0, 0, "11191D").GetComponent<Image>().raycastTarget = true;
            Box("TopAccent", content, 800, 3, 0, 338, "B8D66B");
            Text("TopTitle", content, "MRE / \u0420\u0415\u0419\u0422\u0418\u041d\u0413", -365, 277, 500, 30, 23, "EDF1E8");
            Text("TopPeriod", content, "", -365, 246, 500, 22, 14, "AEBBB4");
            var close = Button("TopClose", content, 250, 294, 115, 40, "293632");
            var cl = Text("TopCloseLabel", close, "\u0417\u0410\u041a\u0420\u042b\u0422\u042c", -50, -13, 100, 26, 13, "EDF1E8"); cl.alignment = TextAnchor.MiddleCenter;
            string[] ids = { "Kills", "Deaths", "Kd", "Zombies", "Time" };
            string[] labels = { "\u0423\u0411\u0418\u0419\u0421\u0422\u0412\u0410", "\u0421\u041c\u0415\u0420\u0422\u0418", "K/D", "\u0417\u041e\u041c\u0411\u0418", "\u0412\u0420\u0415\u041c\u042f" };
            for (int i = 0; i < ids.Length; i++)
            {
                var button = Button("TopCategory" + ids[i], content, -365 + i * 148, 213, 140, 38, "25342D");
                Box("TopSelected" + ids[i], button, 140, 3, 0, -17.5f, "B8D66B").gameObject.SetActive(false);
                var label = Text("TopCategory" + ids[i] + "Label", button, labels[i], -65, -13, 130, 26, 12, "EDF1E8"); label.alignment = TextAnchor.MiddleCenter; label.resizeTextForBestFit = true; label.resizeTextMinSize = 9; label.resizeTextMaxSize = 12;
            }
            Text("TopRankHeading", content, "#", -350, 171, 50, 20, 12, "AEBBB4");
            Text("TopNameHeading", content, "\u0418\u0413\u0420\u041e\u041a", -285, 171, 380, 20, 12, "AEBBB4");
            Text("TopValueHeading", content, "\u0417\u041d\u0410\u0427\u0415\u041d\u0418\u0415", 190, 171, 170, 20, 12, "AEBBB4");
            for (int i = 0; i < 10; i++)
            {
                float y = 144 - i * 40;
                var row = Box("TopRow" + i.ToString("00"), content, 730, 36, 0, y, i % 2 == 0 ? "1B2727" : "172222");
                Text("TopRank" + i.ToString("00"), row, "", -350, -15, 50, 30, 15, "B8D66B");
                var name = Text("TopName" + i.ToString("00"), row, "", -285, -15, 450, 30, 16, "EDF1E8"); name.resizeTextForBestFit = true; name.resizeTextMinSize = 11; name.resizeTextMaxSize = 16;
                Text("TopValue" + i.ToString("00"), row, "", 190, -15, 170, 30, 16, "EDF1E8");
                row.gameObject.SetActive(false);
            }
            Text("TopStatus", content, "\u0414\u0430\u043d\u043d\u044b\u0435 \u0437\u0430\u0433\u0440\u0443\u0436\u0430\u044e\u0442\u0441\u044f\u2026", -365, -321, 730, 25, 14, "AEBBB4");
            Validate(root); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    public static void BuildWindows()
    {
        Generate();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        string output = Path.Combine(projectRoot, "runtime/ui-builds/MreTop/Windows"); Directory.CreateDirectory(output);
        var map = new AssetBundleBuild { assetBundleName = "MreTop.unity3d", assetNames = new[] { PrefabPath }, addressableNames = new[] { "Effect" } };
        if (BuildPipeline.BuildAssetBundles(output, new[] { map }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64) == null) throw new InvalidOperationException("Build failed");
        string source = Path.Combine(output, "MreTop.unity3d"); var bundle = AssetBundle.LoadFromFile(source);
        if (bundle == null) throw new InvalidDataException("Bundle unreadable");
        try { var prefab = bundle.LoadAsset<GameObject>("Effect"); if (prefab == null) throw new InvalidDataException("Effect missing"); Validate(prefab); }
        finally { bundle.Unload(true); }
        string mod = Path.Combine(output, "MreTop"); Directory.CreateDirectory(mod); File.Copy(source, Path.Combine(mod, "MreTop.unity3d"), true);
        File.WriteAllText(Path.Combine(mod, "MreTop.dat"), "GUID " + EffectGuid + "\nType Effect\nID 48624\nAsset_Bundle_Version 6\nExclude_From_Master_Bundle\n", new UTF8Encoding(false));
        using (var sha = SHA256.Create())
        {
            string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Path.Combine(output, "bundle-build.json"), "{\n  \"effect_id\":48624,\n  \"effect_guid\":\"" + EffectGuid + "\",\n  \"effect_key\":14824,\n  \"unity_version\":\"" + Application.unityVersion + "\",\n  \"target\":\"StandaloneWindows64\",\n  \"bundle_sha256\":\"" + hash + "\",\n  \"prefab_address_verified\":true,\n  \"in_game_verified\":false\n}\n", new UTF8Encoding(false));
        }
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
    private static Text Text(string name, Transform parent, string value, float x, float y, float width, float height, int size, string color)
    {
        var rect = Rect(name, parent, width, height, x + width / 2, y + height / 2);
        var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.color = ColorOf(color);
        text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
    }
    private static void Validate(GameObject root)
    {
        var all = root.GetComponentsInChildren<RectTransform>(true); var names = all.Select(x => x.name).ToArray();
        if (names.Distinct().Count() != names.Length) throw new InvalidDataException("Duplicate names");
        string[] ids = { "Kills", "Deaths", "Kd", "Zombies", "Time" };
        foreach (string n in new[] { "TopClose" }.Concat(ids.Select(x => "TopCategory" + x)))
            if (all.SingleOrDefault(x => x.name == n)?.GetComponent<Button>() == null) throw new InvalidDataException("Missing button " + n);
        foreach (string n in new[] { "TopTitle", "TopPeriod", "TopStatus" }.Concat(Enumerable.Range(0,10).SelectMany(i => new[] { "TopRank" + i.ToString("00"), "TopName" + i.ToString("00"), "TopValue" + i.ToString("00") })))
            if (all.SingleOrDefault(x => x.name == n)?.GetComponent<Text>() == null) throw new InvalidDataException("Missing text " + n);
        if (root.GetComponentsInChildren<Button>(true).Length != 6 || root.GetComponent<GraphicRaycaster>() == null || root.GetComponent<Canvas>().sortingOrder != 20 || root.GetComponentsInChildren<Text>(true).Any(x => x.supportRichText || x.raycastTarget)) throw new InvalidDataException("Invalid UI input contract");
        foreach (int i in Enumerable.Range(0,10)) if (all.Single(x => x.name == "TopRow" + i.ToString("00")).gameObject.activeSelf) throw new InvalidDataException("Rows must start hidden");
        foreach (string id in ids) if (all.Single(x => x.name == "TopSelected" + id).gameObject.activeSelf) throw new InvalidDataException("Selection must start hidden");
    }
}
