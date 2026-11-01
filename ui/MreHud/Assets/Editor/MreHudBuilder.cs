using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Только Editor. В bundle нет своих MonoBehaviour или модификации клиента игры.
public static class MreHudBuilder
{
    private const string PrefabPath = "Assets/MreHud/Effect.prefab";
    private const string Guid = "4f11731bb53e4a55b588b97c2c2f61b5";
    private static readonly Color Back = Hex("111719", 0.94f);
    private static readonly Color Panel = Hex("1b2325", 0.96f);
    private static readonly Color TextColor = Hex("eef2e8");
    private static readonly Color Muted = Hex("9ca7a2");
    private static readonly Color Lime = Hex("b8d66b");
    private static readonly Color Red = Hex("e57669");
    private static readonly Color Water = Hex("79b9d0");
    private static Font font;

    [MenuItem("MRE/Создать HUD prefab")]
    public static void Generate()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) throw new InvalidOperationException("Не найден встроенный LegacyRuntime.ttf Unity 2022.3.");
        Directory.CreateDirectory("Assets/MreHud");
        var root = new GameObject("Effect", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        try
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 25;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            RectTransform content = Rect("HudContent", root.transform, 0, 0, 0, 0);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = content.offsetMax = Vector2.zero;

            CreateVitals(content);
            CreateWeapon(content);
            CreateTeleport(content);
            CreateBlockPanels(content);
            CreateVote(content);
            CreateBuilding(content);
            ValidateNames(root);
            ValidateHealthText(root);
            content.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("MRE: создан " + PrefabPath + ". Это source prefab; игровой тест ещё не выполнен.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [MenuItem("MRE/Собрать Windows bundle")]
    public static void BuildWindows()
    {
        Generate();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        string output = Path.Combine(projectRoot, "runtime/ui-builds/MreHud/Windows");
        Directory.CreateDirectory(output);
        var map = new AssetBundleBuild {
            assetBundleName = "MreHud.unity3d", assetNames = new[] { PrefabPath }, addressableNames = new[] { "Effect" }
        };
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(output, new[] { map },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new InvalidOperationException("Unity не собрал MRE HUD asset bundle.");
        string sourceBundle = Path.Combine(output, "MreHud.unity3d");
        if (!File.Exists(sourceBundle)) throw new FileNotFoundException("Unity не создал MreHud.unity3d", sourceBundle);
        AssetBundle builtBundle = AssetBundle.LoadFromFile(sourceBundle);
        if (builtBundle == null) throw new InvalidOperationException("Собранный MRE bundle не читается Unity.");
        try
        {
            var prefab = builtBundle.LoadAsset<GameObject>("Effect");
            if (prefab == null || prefab.GetComponent<Canvas>() == null)
                throw new InvalidOperationException("Собранный bundle не содержит Canvas prefab по адресу Effect.");
            ValidateNames(prefab);
        }
        finally { builtBundle.Unload(true); }
        string mod = Path.Combine(output, "MreHud");
        Directory.CreateDirectory(mod);
        File.Copy(sourceBundle, Path.Combine(mod, "MreHud.unity3d"), true);
        File.WriteAllText(Path.Combine(mod, "MreHud.dat"),
            "GUID " + Guid + "\nType Effect\nID 48621\nAsset_Bundle_Version 6\nExclude_From_Master_Bundle\n", new System.Text.UTF8Encoding(false));
        using (var sha = SHA256.Create())
        {
            string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourceBundle))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Path.Combine(output, "bundle-build.json"),
                "{\n  \"effect_id\":48621,\n  \"effect_guid\":\"" + Guid + "\",\n  \"unity_version\":\"" + Application.unityVersion +
                "\",\n  \"target\":\"StandaloneWindows64\",\n  \"bundle_sha256\":\"" + hash +
                "\",\n  \"prefab_address_verified\":true,\n  \"in_game_verified\":false\n}\n", new System.Text.UTF8Encoding(false));
        }
        Debug.Log("MRE: Windows bundle собран в " + mod + ". Не опубликован и не установлен. Клиентская приемка обязательна.");
    }

    private static void CreateVitals(RectTransform content)
    {
        RectTransform low = Rect("VitalsLowLayout", content, 0, 0, 0, 0);
        RectTransform card = Box("VitalsPanel", low, 28, 120, 370, 168, Back);
        Box("VitalsRule", card, 0, 165, 370, 3, Lime);
        Label("VitalsCaption", card, "MRE  /  СОСТОЯНИЕ", 16, 138, 220, 17, 11, Muted, FontStyle.Bold);
        Label("HealthValue", card, "100", 14, 82, 103, 58, 42, TextColor, FontStyle.Bold);
        Label("HealthCaption", card, "/ 100     ЗДОРОВЬЕ", 118, 98, 225, 20, 12, Muted);
        Bar("Health", card, 16, 77, 338, 5, 20, Lime);
        Box("LowHealth", card, 0, 165, 370, 3, Red);
        SmallMeter(card, "Food", "ЕДА", 16, 13, Lime);
        SmallMeter(card, "Water", "ВОДА", 132, 13, Water);
        SmallMeter(card, "Stamina", "ЭНЕРГИЯ", 248, 13, Lime);

        RectTransform bleeding = Box("BleedingPanel", low, 28, 296, 178, 25, Panel);
        Label("BleedingCaption", bleeding, "+  КРОВОТЕЧЕНИЕ", 9, 3, 163, 20, 10, Red, FontStyle.Bold);
        RectTransform broken = Box("BrokenPanel", low, 218, 296, 180, 25, Panel);
        Label("BrokenCaption", broken, "!  ПЕРЕЛОМ", 9, 3, 163, 20, 10, Red, FontStyle.Bold);
        RectTransform oxygen = Box("OxygenPanel", low, 28, 329, 370, 30, Panel);
        Label("OxygenCaption", oxygen, "КИСЛОРОД", 12, 6, 90, 18, 10, Water);
        Label("OxygenValue", oxygen, "100", 317, 5, 41, 20, 14, TextColor);
        Bar("Oxygen", oxygen, 113, 12, 188, 5, 10, Water);

        var high = UnityEngine.Object.Instantiate(low.gameObject, content, false);
        high.name = "VitalsHighLayout";
        high.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 140);
        foreach (var child in high.GetComponentsInChildren<RectTransform>(true))
            if (child.gameObject != high) child.name += "High";
        high.SetActive(false);
        low.gameObject.SetActive(false);
    }

    private static void CreateWeapon(RectTransform content)
    {
        RectTransform low = Rect("WeaponLowLayout", content, 0, 0, 0, 0);
        low.anchorMin = Vector2.zero; low.anchorMax = Vector2.one;
        low.offsetMin = low.offsetMax = Vector2.zero;
        RectTransform card = Box("WeaponPanel", low, -28, 28, 326, 126, Back, new Vector2(1, 0), new Vector2(1, 0));
        Box("WeaponRule", card, 0, 123, 326, 3, Lime);
        Label("WeaponName", card, "EAGLEFIRE", 16, 95, 290, 18, 13, TextColor, FontStyle.Bold);
        Label("AmmoValue", card, "30", 13, 33, 110, 55, 48, TextColor, FontStyle.Bold);
        Label("AmmoCapacity", card, "/ 30", 126, 39, 115, 34, 24, Muted);
        Label("MagazineName", card, "Cobra Magazine", 16, 13, 180, 15, 11, Muted);
        Label("FireModeValue", card, "АВТО", 205, 13, 104, 17, 11, Lime);
        Label("DurabilityValue", card, "100%", 253, 53, 55, 20, 13, Muted);
        Box("WeaponSeparator", card, 16, 87, 293, 1, Hex("37413e"));
        RectTransform empty = Box("EmptyMagazine", card, 0, 124, 326, 2, Red);
        var high = UnityEngine.Object.Instantiate(low.gameObject, content, false);
        high.name = "WeaponHighLayout";
        high.GetComponent<RectTransform>().offsetMin = new Vector2(0, 352);
        foreach (var child in high.GetComponentsInChildren<RectTransform>(true))
            if (child.gameObject != high) child.name += "High";
        high.SetActive(false);
        low.gameObject.SetActive(false);
    }

    private static void CreateBuilding(RectTransform content)
    {
        RectTransform card = Box("BuildingPanel", content, 28, -139, 280, 90, Back, new Vector2(0, 1), new Vector2(0, 1));
        Box("BuildingAccent", card, 0, 87, 280, 3, Lime);
        Label("BuildingCaption", card, "СОСТОЯНИЕ ПОСТРОЙКИ", 14, 66, 252, 15, 10, Muted, FontStyle.Bold);
        Label("BuildingName", card, "Постройка", 14, 38, 252, 24, 17, TextColor, FontStyle.Bold);
        Label("BuildingHealth", card, "1000 / 1000", 14, 14, 252, 20, 14, Lime, FontStyle.Bold);
        Bar("Building", card, 14, 8, 252, 4, 20, Lime);
    }

    private static void CreateTeleport(RectTransform content)
    {
        RectTransform card = Box("TpaPanel", content, -28, 174, 370, 184, Back, new Vector2(1, 0), new Vector2(1, 0));
        Box("TpaAccent", card, 0, 181, 370, 3, Lime);
        Label("TpaTitle", card, "ЗАПРОС НА ТЕЛЕПОРТАЦИЮ", 16, 149, 281, 17, 10, Muted, FontStyle.Bold);
        Label("TpaTimer", card, "5 с", 305, 143, 49, 28, 21, Lime, FontStyle.Bold);
        Label("TpaPlayerName", card, "Игрок", 16, 108, 337, 31, 24, TextColor, FontStyle.Bold);
        Label("TpaHint", card, "хочет телепортироваться к вам", 16, 84, 337, 20, 12, Muted);
        Bar("TpaProgress", card, 16, 69, 338, 4, 20, Lime);
        RectTransform accept = Box("TpaAcceptPanel", card, 16, 16, 162, 37, Panel);
        Label("TpaAcceptKey", accept, "<plugin_0/>", 9, 7, 39, 23, 17, Lime, FontStyle.Bold);
        Label("TpaAcceptText", accept, "ПРИНЯТЬ", 52, 10, 103, 18, 11, TextColor);
        RectTransform deny = Box("TpaDenyPanel", card, 190, 16, 164, 37, Panel);
        Label("TpaDenyKey", deny, "<plugin_1/>", 9, 7, 39, 23, 17, Muted, FontStyle.Bold);
        Label("TpaDenyText", deny, "ОТКЛОНИТЬ", 52, 10, 105, 18, 11, TextColor);
    }

    private static void CreateBlockPanels(RectTransform content)
    {
        RectTransform combat = Box("CombatPanel", content, 28, -95, 112, 27, Panel, new Vector2(0, 1), new Vector2(0, 1));
        Label("CombatCaption", combat, "БОЕВОЙ БЛОК", 8, 5, 100, 17, 10, Red, FontStyle.Bold);
        RectTransform raid = Box("RaidPanel", content, 149, -95, 112, 27, Panel, new Vector2(0, 1), new Vector2(0, 1));
        Label("RaidCaption", raid, "РЕЙД БЛОК", 8, 5, 100, 17, 10, Red, FontStyle.Bold);
    }

    private static void SmallMeter(RectTransform parent, string name, string caption, float x, float y, Color color)
    {
        RectTransform meter = Rect(name + "Meter", parent, x, y, 106, 52);
        Label(name + "Caption", meter, caption, 0, 34, 106, 15, 9, Muted);
        Label(name + "Value", meter, "100", 0, 12, 106, 22, 17, TextColor, FontStyle.Bold);
        Bar(name, meter, 0, 3, 106, 4, 10, color);
    }

    private static void CreateVote(RectTransform content)
    {
        RectTransform card = Box("VotePanel", content, 0, -28, 370, 164, Back, new Vector2(0.5f, 1), new Vector2(0.5f, 1));
        Box("VoteAccent", card, 0, 161, 370, 3, Lime);
        Box("VoteSuccessAccent", card, 0, 161, 370, 3, Lime);
        Box("VoteFailureAccent", card, 0, 161, 370, 3, Red);
        Label("VoteStatus", card, "ГОЛОСОВАНИЕ", 16, 135, 286, 15, 9, Muted, FontStyle.Bold);
        Label("VoteTimer", card, "60 с", 307, 125, 47, 27, 19, Lime, FontStyle.Bold);
        Label("VoteTopic", card, "Наступление дня", 16, 101, 338, 28, 22, TextColor, FontStyle.Bold);
        Label("VoteInitiator", card, "Инициатор: Игрок", 16, 80, 338, 18, 11, Muted);
        Label("VoteCount", card, "1 / 2", 16, 51, 90, 25, 21, Lime, FontStyle.Bold);
        Label("VoteEligible", card, "голосов за · участников 2", 112, 54, 242, 20, 10, Muted);
        Bar("VoteProgress", card, 16, 40, 338, 4, 20, Lime);
        Label("VoteHint", card, "За день: /vote yes  ·  /cv", 16, 12, 338, 20, 12, TextColor);
    }

    private static void Bar(string name, RectTransform parent, float x, float y, float width, float height, int count, Color color)
    {
        RectTransform bar = Box(name + "Track", parent, x, y, width, height, Hex("34413a", 0.75f));
        float gap = 1;
        float segment = (width - gap * (count - 1)) / count;
        for (int i = 1; i <= count; i++)
            Box(name + "Fill" + i.ToString("00"), bar, (i - 1) * (segment + gap), 0, segment, height, color);
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static RectTransform Box(string name, Transform parent, float x, float y, float width, float height, Color color,
        Vector2? anchor = null, Vector2? pivot = null)
    {
        RectTransform rect = Rect(name, parent, x, y, width, height);
        if (anchor.HasValue) rect.anchorMin = rect.anchorMax = anchor.Value;
        if (pivot.HasValue) rect.pivot = pivot.Value;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private static void Label(string name, Transform parent, string value, float x, float y, float width, float height,
        int size, Color color, FontStyle style = FontStyle.Normal)
    {
        RectTransform rect = Rect(name, parent, x, y, width, height);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = name == "HealthValue" ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;
        text.verticalOverflow = name == "HealthValue" ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate;
        text.supportRichText = false;
        text.raycastTarget = false;
        if (name == "TpaPlayerName" || name == "WeaponName" || name == "MagazineName" || name == "VoteTopic" || name == "VoteInitiator" || name == "VoteHint" || name == "BuildingName")
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Math.Min(10, size);
            text.resizeTextMaxSize = size;
        }
    }

    private static void ValidateHealthText(GameObject root)
    {
        Text label = root.GetComponentsInChildren<Text>(true).Single(x => x.name == "HealthValue");
        var rect = label.GetComponent<RectTransform>();
        var settings = label.GetGenerationSettings(rect.rect.size);
        foreach (string value in new[] { "0", "99", "100" })
        {
            var generator = new TextGenerator();
            if (!generator.Populate(value, settings) || generator.characterCountVisible != value.Length ||
                generator.GetPreferredWidth(value, settings) > rect.rect.width ||
                generator.GetPreferredHeight(value, settings) > rect.rect.height)
                throw new InvalidOperationException("HealthValue не вмещает значение " + value);
        }
        Debug.Log("MRE: TextGenerator проверил размеры HealthValue для 0, 99 и 100.");
    }

    private static void ValidateNames(GameObject root)
    {
        var duplicates = root.GetComponentsInChildren<RectTransform>(true).GroupBy(x => x.name).Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
        if (duplicates.Length > 0) throw new InvalidOperationException("Повторяются имена UI-элементов: " + String.Join(", ", duplicates));
        string[] required = { "HudContent", "HealthValue", "FoodValue", "WaterValue", "StaminaValue", "OxygenValue",
            "WeaponName", "MagazineName", "AmmoValue", "AmmoCapacity", "FireModeValue", "DurabilityValue", "VitalsLowLayout", "VitalsHighLayout", "WeaponLowLayout", "WeaponHighLayout",
            "TpaPanel", "TpaTitle", "TpaPlayerName", "TpaHint", "TpaTimer", "TpaAcceptPanel", "TpaAcceptKey", "TpaDenyKey", "TpaDenyText",
            "OxygenPanel", "LowHealth", "BleedingPanel", "BrokenPanel", "CombatPanel", "RaidPanel", "EmptyMagazine", "WeaponPanel",
            "VotePanel", "VoteTopic", "VoteInitiator", "VoteCount", "VoteEligible", "VoteTimer", "VoteStatus", "VoteHint", "VoteSuccessAccent", "VoteFailureAccent", "BuildingPanel", "BuildingName", "BuildingHealth" };
        var names = root.GetComponentsInChildren<RectTransform>(true).Select(x => x.name).ToArray();
        foreach (string name in required)
            if (!names.Contains(name)) throw new InvalidOperationException("Отсутствует UI-элемент: " + name);
        foreach (string name in new[] { "HealthValue", "FoodValue", "WaterValue", "StaminaValue", "OxygenValue", "OxygenPanel", "LowHealth", "BleedingPanel", "BrokenPanel", "WeaponName", "MagazineName", "AmmoValue", "AmmoCapacity", "FireModeValue", "DurabilityValue", "EmptyMagazine", "WeaponPanel" })
            if (!names.Contains(name + "High")) throw new InvalidOperationException("Отсутствует запасной UI-элемент: " + name);
        foreach (string bar in new[] { "Health", "Food", "Water", "Stamina", "Oxygen", "TpaProgress", "VoteProgress", "Building" })
        {
            int count = bar == "Health" || bar == "TpaProgress" || bar == "VoteProgress" || bar == "Building" ? 20 : 10;
            for (int i = 1; i <= count; i++)
            {
                string name = bar + "Fill" + i.ToString("00");
                if (!names.Contains(name)) throw new InvalidOperationException("Отсутствует сегмент UI: " + name);
                if ((bar == "Health" || bar == "Food" || bar == "Water" || bar == "Stamina" || bar == "Oxygen") && !names.Contains(name + "High"))
                    throw new InvalidOperationException("Отсутствует запасной сегмент UI: " + name);
            }
        }
    }

    private static Color Hex(string hex, float alpha = 1)
    {
        Color value;
        if (!ColorUtility.TryParseHtmlString("#" + hex, out value)) throw new ArgumentException(hex);
        value.a = alpha;
        return value;
    }
}
