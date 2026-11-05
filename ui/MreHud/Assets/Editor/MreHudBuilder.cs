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
    private static Font boldFont;

    [MenuItem("MRE/Создать HUD prefab")]
    public static void Generate()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        font = AssetDatabase.LoadAssetAtPath<Font>("Assets/MreFonts/PTSans-Regular.ttf");
        boldFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/MreFonts/PTSans-Bold.ttf");
        if (font == null || boldFont == null) throw new InvalidOperationException("Не найден встроенный LegacyRuntime.ttf Unity 2022.3.");
        Directory.CreateDirectory("Assets/MreHud");
        var root = new GameObject("Effect", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        try
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
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
            CreateDamageNumber(content);
            ValidateNames(root);
            ValidateHealthText(root);
            ValidateFontGlyphs();
            ValidateFireMode(root);
            ValidateTeleportKeys(root);
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

    public static void RenderCheck()
    {
        string bundlePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../runtime/ui-builds/MreHud/Windows/MreHud/MreHud.unity3d"));
        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null) throw new InvalidOperationException("Bundle unreadable");
        var prefab = bundle.LoadAsset<GameObject>("Effect");
        if (prefab == null) throw new InvalidOperationException("Effect missing");
        var root = UnityEngine.Object.Instantiate(prefab);
        var cameraObject = new GameObject("PreviewCamera", typeof(Camera));
        var texture = new RenderTexture(1920, 1080, 24);
        try
        {
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.2f, 0.16f); camera.orthographic = true; camera.targetTexture = texture;
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var rects = root.GetComponentsInChildren<RectTransform>(true).ToDictionary(x => x.name);
            foreach (string name in new[] { "HudContent", "VitalsLowLayout", "WeaponLowLayout", "StatsPanel", "HealthFill92", "FoodFill76", "WaterFill72", "StaminaFill85", "BuildingPanel", "DamageNumberPanel0", "DamageNumberPanel1", "DamageNumberPanel2", "DamageNumberPanel3", "DamageNumberPanel4", "DamageNumberPanel5", "DamageNumberPanel6", "DamageNumberPanel7" }) rects[name].gameObject.SetActive(true);
            string[] targets = { "HealthValue", "FoodValue", "WaterValue", "StaminaValue", "StatsKillsValue", "StatsKdValue", "StatsDeathsValue", "StatsZombiesValue" };
            string[] values = { "92", "76", "72", "85", "41", "2.05", "20", "368" };
            for (int i = 0; i < targets.Length; i++) rects[targets[i]].GetComponent<Text>().text = values[i];
            string[] damage = { "-1", "-70", "-225", "-65535", "-1", "-70", "-225", "-65535" };
            for (int i = 0; i < damage.Length; i++)
            {
                rects["DamageNumberPanel" + i].GetComponent<CanvasGroup>().alpha = 1;
                var damageText = rects["DamageNumberText" + i].GetComponent<Text>();
                damageText.text = i < 4 ? "<color=#E57669>" + damage[i] + "</color>" : "<color=#EEF2E8>" + damage[i] + "</color>"; damageText.color = TextColor;
            }
            Canvas.ForceUpdateCanvases(); camera.Render();
            foreach (var text in root.GetComponentsInChildren<Text>(false))
            {
                if (String.IsNullOrWhiteSpace(text.text)) continue;
                var mesh = text.canvasRenderer.GetMesh();
                if (mesh == null) throw new InvalidOperationException("Missing text mesh: " + text.name);
                var uv = mesh.uv;
                if (mesh.vertexCount == 0 || uv.Length != mesh.vertexCount || uv.Any(v => Single.IsNaN(v.x) || Single.IsNaN(v.y) || Single.IsInfinity(v.x) || Single.IsInfinity(v.y)) || uv.Max(v => v.x) <= uv.Min(v => v.x) || uv.Max(v => v.y) <= uv.Min(v => v.y))
                    throw new InvalidOperationException("Empty or invalid text mesh: " + text.name);
            }
            RenderTexture.active = texture;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../runtime/ui-builds/MreHud-render.png"));
            File.WriteAllBytes(output, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("MRE render: " + output);
        }
        finally { RenderTexture.active = null; cameraObject.GetComponent<Camera>().targetTexture = null; texture.Release(); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(root); bundle.Unload(true); }
    }

    private static void CreateVitals(RectTransform content)
    {
        RectTransform low = Rect("VitalsLowLayout", content, 0, 0, 0, 0);
        // Штатные значки рисуются поверх HUD и могут закрыть часть нижней карточки.
        RectTransform card = Box("VitalsPanel", low, 28, 28, 285, 129, Back);
        SmallMeter(card, "Health", "\u0417\u0414\u041e\u0420\u041e\u0412\u042c\u0415", 9, 97, Lime);
        SmallMeter(card, "Food", "\u0415\u0414\u0410", 9, 67, Lime);
        SmallMeter(card, "Water", "\u0412\u041e\u0414\u0410", 9, 37, Water);
        SmallMeter(card, "Stamina", "\u042d\u041d\u0415\u0420\u0413\u0418\u042f", 9, 7, Lime);
        Box("LowHealth", card, 0, 127, 285, 2, Red);

        RectTransform stats = Rect("StatsPanel", low, 28, 164, 285, 32);
        string[] statNames = { "Kills", "Kd", "Deaths", "Zombies" };
        string[] statCaptions = { "\u0423\u0411\u0418\u0419\u0421\u0422\u0412\u0410", "K/D", "\u0421\u041c\u0415\u0420\u0422\u0418", "\u0417\u041e\u041c\u0411\u0418" };
        for (int i = 0; i < statNames.Length; i++)
        {
            RectTransform tile = Box("Stats" + statNames[i] + "Tile", stats, i * 72, 0, 69, 32, Back);
            Label("Stats" + statNames[i] + "Caption", tile, statCaptions[i], 5, 16, 59, 16, 9, Muted);
            Label("Stats" + statNames[i] + "Value", tile, "", 5, 0, 59, 19, 12, TextColor, FontStyle.Bold);
        }
        stats.gameObject.SetActive(false);
        RectTransform bleeding = Box("BleedingPanel", low, 28, 204, 136, 25, Panel);
        Label("BleedingCaption", bleeding, "+  КРОВОТЕЧЕНИЕ", 9, 3, 122, 20, 9, Red, FontStyle.Bold);
        RectTransform broken = Box("BrokenPanel", low, 172, 204, 141, 25, Panel);
        Label("BrokenCaption", broken, "!  ПЕРЕЛОМ", 9, 3, 122, 20, 9, Red, FontStyle.Bold);
        RectTransform oxygen = Box("OxygenPanel", low, 28, 237, 285, 30, Panel);
        Label("OxygenCaption", oxygen, "КИСЛОРОД", 12, 6, 90, 18, 10, Water);
        Label("OxygenValue", oxygen, "100", 232, 5, 41, 20, 14, TextColor);
        Bar("Oxygen", oxygen, 100, 12, 117, 5, 100, Water);

        var high = UnityEngine.Object.Instantiate(low.gameObject, content, false);
        high.name = "VitalsHighLayout";
        high.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 232);
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
        RectTransform card = Box("WeaponPanel", low, -28, 28, 220, 76, Back, new Vector2(1, 0), new Vector2(1, 0));
        Box("WeaponRule", card, 0, 0, 2, 76, Lime);
        Label("WeaponName", card, "EAGLEFIRE", 10, 54, 200, 20, 12, TextColor, FontStyle.Bold);
        Label("AmmoValue", card, "30", 10, 20, 58, 32, 24, TextColor, FontStyle.Bold);
        Label("AmmoCapacity", card, "/ 30", 70, 23, 55, 24, 14, Muted);
        Label("MagazineName", card, "Cobra Magazine", 10, 1, 105, 20, 11, Muted);
        Label("FireModeValue", card, "\u0410\u0412\u0422\u041e", 120, 1, 90, 20, 11, Lime);
        Label("DurabilityValue", card, "100%", 164, 25, 46, 18, 11, Muted);
        Box("EmptyMagazine", card, 0, 0, 2, 76, Red);
        var high = UnityEngine.Object.Instantiate(low.gameObject, content, false);
        high.name = "WeaponHighLayout";
        high.GetComponent<RectTransform>().offsetMin = new Vector2(0, 352);
        foreach (var child in high.GetComponentsInChildren<RectTransform>(true))
            if (child.gameObject != high) child.name += "High";
        high.SetActive(false);
        low.gameObject.SetActive(false);
    }

    private static void CreateDamageNumber(RectTransform content)
    {
        // Shared clip restarts when a slot is hidden and shown again by the server.
        var clip = new AnimationClip { name = "DamagePulse", legacy = true, wrapMode = WrapMode.Once };
        clip.SetCurve("", typeof(CanvasGroup), "m_Alpha", new AnimationCurve(
            new Keyframe(0, 0.2f), new Keyframe(0.12f, 1f)));
        const string clipPath = "Assets/MreHud/DamagePulse.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (existing == null) AssetDatabase.CreateAsset(clip, clipPath);
        else { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); clip = existing; }
        for (int i = 0; i < 8; i++)
        {
            RectTransform card = Rect("DamageNumberPanel" + i, content, 0, 0, 68, 24);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(34 + (i % 2) * 72, 25 + (i / 2) * 25);
            Label("DamageNumberText" + i, card, "22", 0, 0, 68, 24, 14, TextColor, FontStyle.Bold);
            var damageText = card.Find("DamageNumberText" + i).GetComponent<Text>();
            damageText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            damageText.supportRichText = true;
            var outline = damageText.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1, -1);
            outline.useGraphicAlpha = true;
            card.gameObject.AddComponent<CanvasGroup>().alpha = 0.2f;
            var animation = card.gameObject.AddComponent<Animation>();
            animation.playAutomatically = true;
            animation.clip = clip;
            animation.AddClip(clip, clip.name);
            card.gameObject.SetActive(false);
        }
    }

    private static void CreateBuilding(RectTransform content)
    {
        RectTransform card = Box("BuildingPanel", content, 0, 198, 204, 46, Back, new Vector2(0.5f, 0), new Vector2(0.5f, 0));
        Box("BuildingAccent", card, 0, 44, 204, 2, Lime);
        Label("BuildingName", card, "Постройка", 9, 23, 186, 17, 13, TextColor, FontStyle.Bold);
        Label("BuildingHealth", card, "1000 / 1000", 9, 6, 186, 17, 12, Lime, FontStyle.Bold);
        Bar("Building", card, 9, 3, 186, 2, 100, Lime);
        card.gameObject.AddComponent<CanvasGroup>().alpha = 0;
        var clip = new AnimationClip { name = "BuildingFade", legacy = true, wrapMode = WrapMode.Once };
        clip.SetCurve("", typeof(CanvasGroup), "m_Alpha", new AnimationCurve(
            new Keyframe(0, 0), new Keyframe(0.18f, 1), new Keyframe(4.65f, 1), new Keyframe(5, 0)));
        const string clipPath = "Assets/MreHud/BuildingFade.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (existing == null) AssetDatabase.CreateAsset(clip, clipPath);
        else { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); clip = existing; }
        var animation = card.gameObject.AddComponent<Animation>();
        animation.playAutomatically = true;
        animation.clip = clip;
        animation.AddClip(clip, clip.name);
        card.gameObject.SetActive(false);
    }

    private static void CreateTeleport(RectTransform content)
    {
        RectTransform card = Box("TpaPanel", content, -28, 174, 370, 184, Back, new Vector2(1, 0), new Vector2(1, 0));
        Box("TpaAccent", card, 0, 181, 370, 3, Lime);
        Label("TpaTitle", card, "ЗАПРОС НА ТЕЛЕПОРТАЦИЮ", 16, 149, 281, 17, 10, Muted, FontStyle.Bold);
        Label("TpaTimer", card, "5 с", 305, 143, 49, 28, 21, Lime, FontStyle.Bold);
        Label("TpaPlayerName", card, "Игрок", 16, 108, 337, 31, 24, TextColor, FontStyle.Bold);
        Label("TpaHint", card, "хочет телепортироваться к вам", 16, 84, 337, 20, 12, Muted);
        Bar("TpaProgress", card, 16, 69, 338, 4, 100, Lime);
        RectTransform accept = Box("TpaAcceptPanel", card, 16, 16, 162, 37, Panel);
        Label("TpaAcceptKey", accept, "Б по умолчанию", 6, 3, 150, 15, 11, Lime);
        Label("TpaAcceptText", accept, "\u041f\u0420\u0418\u041d\u042f\u0422\u042c", 6, 19, 150, 15, 11, TextColor);
        RectTransform deny = Box("TpaDenyPanel", card, 190, 16, 164, 37, Panel);
        Label("TpaDenyKey", deny, "Ю по умолчанию", 6, 3, 152, 15, 11, Muted);
        Label("TpaDenyText", deny, "\u041e\u0422\u041a\u041b\u041e\u041d\u0418\u0422\u042c", 6, 19, 152, 15, 11, TextColor);
    }

    private static void CreateBlockPanels(RectTransform content)
    {
        RectTransform combat = Box("CombatPanel", content, 28, -95, 156, 40, Panel, new Vector2(0, 1), new Vector2(0, 1));
        BlockEmblem("CombatLogo", combat, false);
        Label("CombatCaption", combat, "\u0411\u041e\u0415\u0412\u041e\u0419 \u0411\u041b\u041e\u041a", 46, 9, 104, 22, 11, Lime, FontStyle.Bold);
        RectTransform raid = Box("RaidPanel", content, 192, -95, 156, 40, Panel, new Vector2(0, 1), new Vector2(0, 1));
        BlockEmblem("RaidLogo", raid, true);
        Label("RaidCaption", raid, "\u0420\u0415\u0419\u0414 \u0411\u041b\u041e\u041a", 46, 9, 104, 22, 11, Red, FontStyle.Bold);
    }

    // Original MRE emblems drawn from polygons; no runtime script or external artwork.
    private static void BlockEmblem(string name, Transform parent, bool raid)
    {
        const int size = 128;
        var polygons = new System.Collections.Generic.List<Vector2[]>();
        var colors = new System.Collections.Generic.List<Color>();
        Action<Color, float[]> polygon = (color, points) => {
            var vertices = new Vector2[points.Length / 2];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector2(points[i * 2], points[i * 2 + 1]);
            polygons.Add(vertices); colors.Add(color);
        };
        if (raid)
        {
            polygon(Red, new float[] { 3,27, 14,30, 29,27, 28,15, 24,8, 16,2, 8,8, 4,15 });
            polygon(Hex("111719"), new float[] { 6,25, 15,27, 26,25, 25,16, 22,10, 16,6, 10,10, 7,16 });
            polygon(Red, new float[] { 18,29, 12,19, 17,18, 12,3, 22,20, 17,21, 22,29 });
        }
        else
        {
            float[] blade = { 6,29, 10,27, 24,13, 21,10, 7,24 };
            polygon(TextColor, blade);
            polygon(TextColor, new float[] { 26,29, 22,27, 8,13, 11,10, 25,24 });
            polygon(Lime, new float[] { 17,10, 20,7, 28,15, 25,18 });
            polygon(Lime, new float[] { 22,8, 27,3, 30,6, 25,11 });
            polygon(Lime, new float[] { 15,10, 12,7, 4,15, 7,18 });
            polygon(Lime, new float[] { 10,8, 5,3, 2,6, 7,11 });
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        try
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                Color total = Color.clear;
                for (int sy = 0; sy < 2; sy++) for (int sx = 0; sx < 2; sx++)
                {
                    var p = new Vector2((x + (sx + 0.5f) / 2) * 32 / size, (y + (sy + 0.5f) / 2) * 32 / size);
                    Color sample = Color.clear;
                    for (int n = 0; n < polygons.Count; n++) if (InsidePolygon(p, polygons[n])) sample = colors[n];
                    total += sample * 0.25f;
                }
                if (total.a > 0) { total.r /= total.a; total.g /= total.a; total.b /= total.a; }
                pixels[y * size + x] = total;
            }
            texture.SetPixels(pixels); texture.Apply();
            string path = "Assets/MreHud/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var rect = Rect(name, parent, 6, 4, 32, 32);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path); image.raycastTarget = false;
            if (image.sprite == null) throw new InvalidOperationException("Missing block emblem: " + name);
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    private static bool InsidePolygon(Vector2 p, Vector2[] vertices)
    {
        bool inside = false;
        for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
            if ((vertices[i].y > p.y) != (vertices[j].y > p.y) &&
                p.x < (vertices[j].x - vertices[i].x) * (p.y - vertices[i].y) / (vertices[j].y - vertices[i].y) + vertices[i].x) inside = !inside;
        return inside;
    }

    private static void SmallMeter(RectTransform parent, string name, string caption, float x, float y, Color color)
    {
        RectTransform meter = Rect(name + "Meter", parent, x, y, 266, 25);
        Label(name + "Caption", meter, caption, 0, 10, 205, 18, 11, Muted, FontStyle.Bold);
        Label(name + "Value", meter, "100", 230, 8, 36, 20, 13, TextColor, FontStyle.Bold);
        Bar(name, meter, 0, 2, 266, 4, 100, color);
    }

    private static void CreateVote(RectTransform content)
    {
        RectTransform card = Box("VotePanel", content, -28, 0, 264, 108, Back, new Vector2(1, 0.5f), new Vector2(1, 0.5f));
        Box("VoteAccent", card, 0, 106, 264, 2, Hex("37433d"));
        Box("VoteSuccessAccent", card, 0, 106, 264, 2, Lime).gameObject.SetActive(false);
        Box("VoteFailureAccent", card, 0, 106, 264, 2, Red).gameObject.SetActive(false);
        var sun = Box("VoteSunCore", card, 20, 78, 10, 10, Lime);
        sun.localRotation = Quaternion.Euler(0, 0, 45);
        Box("VoteSunTop", card, 24, 93, 2, 4, Lime);
        Box("VoteSunBottom", card, 24, 68, 2, 4, Lime);
        Box("VoteSunLeft", card, 10, 82, 4, 2, Lime);
        Box("VoteSunRight", card, 35, 82, 4, 2, Lime);
        Label("VoteDayCaption", card, "\u0421\u041c\u0415\u041d\u0410 \u0414\u041d\u042f", 48, 72, 142, 25, 12, TextColor, FontStyle.Bold);
        Box("VoteTimerBack", card, 208, 72, 42, 25, Panel);
        Label("VoteTimer", card, "30 \u0441", 213, 73, 34, 24, 14, Lime, FontStyle.Bold);
        RectTransform yes = Box("VoteYesPanel", card, 14, 21, 112, 38, Panel);
        Box("VoteYesSelected", yes, 0, 0, 112, 38, Hex("3a542e", 0.98f)).gameObject.SetActive(false);
        Box("VoteYesKeyBack", yes, 6, 5, 28, 28, Hex("29352c"));
        Label("VoteYesKey", yes, "Q", 8, 6, 24, 26, 16, TextColor, FontStyle.Bold);
        Label("VoteYesLabel", yes, "\u0417\u0410", 43, 8, 61, 24, 11, Lime, FontStyle.Bold);
        RectTransform no = Box("VoteNoPanel", card, 138, 21, 112, 38, Panel);
        Box("VoteNoSelected", no, 0, 0, 112, 38, Hex("633a34", 0.98f)).gameObject.SetActive(false);
        Box("VoteNoKeyBack", no, 6, 5, 28, 28, Hex("342c2a"));
        Label("VoteNoKey", no, "E", 8, 6, 24, 26, 16, TextColor, FontStyle.Bold);
        Label("VoteNoLabel", no, "\u041f\u0420\u041e\u0422\u0418\u0412", 43, 8, 64, 24, 11, Red, FontStyle.Bold);
        Bar("VoteProgress", card, 14, 10, 236, 2, 100, Lime);
        // Hidden text targets preserve the existing server contract.
        foreach (string name in new[] { "VoteStatus", "VoteInitiator", "VoteEligible", "VoteHint", "VoteTopic", "VoteCount" })
        {
            Label(name, card, "", 0, 0, 1, 1, 9, Muted);
            card.Find(name).gameObject.SetActive(false);
        }
    }

    private static void Bar(string name, RectTransform parent, float x, float y, float width, float height, int count, Color color)
    {
        RectTransform bar = Box(name + "Track", parent, x, y, width, height, Hex("34413a", 0.75f));
        // Exactly one fill is enabled by the server; zero leaves all fills inactive.
        for (int i = 1; i <= count; i++)
        {
            RectTransform fill = Box(name + "Fill" + i.ToString("00"), bar, 0, 0, width * i / count, height, color);
            fill.gameObject.SetActive(false);
        }
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
        text.font = style == FontStyle.Bold ? boldFont : font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = FontStyle.Normal;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;
        if (name == "VoteYesKey" || name == "VoteNoKey")
        {
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 8;
            text.resizeTextMaxSize = size;
        }
        if (name == "TpaAcceptKey" || name == "TpaDenyKey")
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 8;
            text.resizeTextMaxSize = 11;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
        if (name == "FireModeValue")
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 7;
            text.resizeTextMaxSize = size;
        }
        if (name == "TpaPlayerName" || name == "WeaponName" || name == "MagazineName" || name == "VoteTopic" || name == "VoteInitiator" || name == "VoteHint" || name == "BuildingName")
        {
            text.resizeTextForBestFit = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.resizeTextMinSize = Math.Min(7, size);
            text.resizeTextMaxSize = size;
        }
    }

    private static void ValidateFontGlyphs()
    {
        const string sample = "\u0417\u0414\u041e\u0420\u041e\u0412\u042c\u0415 \u0415\u0414\u0410 \u0412\u041e\u0414\u0410 \u042d\u041d\u0415\u0420\u0413\u0418\u042f \u0423\u0411\u0418\u0419\u0421\u0422\u0412\u0410 \u0421\u041c\u0415\u0420\u0422\u0418 \u0417\u041e\u041c\u0411\u0418 \u041f\u0420\u0415\u0414\u041e\u0425\u0420\u0410\u041d\u0418\u0422\u0415\u041b\u042c 0123456789/%,.";
        font.RequestCharactersInTexture(sample, 16, FontStyle.Normal);
        foreach (char c in sample.Where(c => c != ' ').Distinct())
        {
            CharacterInfo info;
            if (!font.GetCharacterInfo(c, out info, 16, FontStyle.Normal))
                throw new InvalidOperationException("PT Sans glyph missing: " + c);
        }
    }

    private static void ValidateFireMode(GameObject root)
    {
        Text label = root.GetComponentsInChildren<Text>(true).Single(x => x.name == "FireModeValue");
        var rect = label.GetComponent<RectTransform>();
        var settings = label.GetGenerationSettings(rect.rect.size);
        settings.resizeTextForBestFit = false; settings.fontSize = label.resizeTextMinSize;
        foreach (string value in new[] { "\u041f\u0420\u0415\u0414\u041e\u0425\u0420\u0410\u041d\u0418\u0422\u0415\u041b\u042c", "\u041e\u0414\u0418\u041d\u041e\u0427\u041d\u042b\u0419", "\u0410\u0412\u0422\u041e", "\u041e\u0427\u0415\u0420\u0415\u0414\u042c" })
        {
            var generator = new TextGenerator();
            if (!generator.Populate(value, settings) || generator.characterCountVisible != value.Length || generator.GetPreferredWidth(value, settings) > rect.rect.width || generator.GetPreferredHeight(value, settings) > rect.rect.height)
                throw new InvalidOperationException("Fire mode does not fit: " + value);
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

    private static void ValidateTeleportKeys(GameObject root)
    {
        foreach (string name in new[] { "TpaAcceptKey", "TpaDenyKey" })
        {
            Text label = root.GetComponentsInChildren<Text>(true).Single(x => x.name == name);
            var rect = label.GetComponent<RectTransform>();
            var settings = label.GetGenerationSettings(rect.rect.size);
            settings.resizeTextForBestFit = false;
            settings.fontSize = label.fontSize;
            string value = label.text;
            var generator = new TextGenerator();
            if (!generator.Populate(value, settings) || generator.characterCountVisible != value.Length ||
                generator.GetPreferredWidth(value, settings) > rect.rect.width ||
                generator.GetPreferredHeight(value, settings) > rect.rect.height)
                throw new InvalidOperationException(name + " does not fit its label");
        }
    }

    private static void ValidateNames(GameObject root)
    {
        var duplicates = root.GetComponentsInChildren<RectTransform>(true).GroupBy(x => x.name).Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
        if (duplicates.Length > 0) throw new InvalidOperationException("Повторяются имена UI-элементов: " + String.Join(", ", duplicates));
        string[] required = { "HudContent", "HealthValue", "FoodValue", "WaterValue", "StaminaValue", "OxygenValue",
            "WeaponName", "MagazineName", "AmmoValue", "AmmoCapacity", "FireModeValue", "DurabilityValue", "VitalsLowLayout", "VitalsHighLayout", "WeaponLowLayout", "WeaponHighLayout",
            "TpaPanel", "TpaTitle", "TpaPlayerName", "TpaHint", "TpaTimer", "TpaAcceptPanel", "TpaAcceptKey", "TpaDenyKey", "TpaDenyText",
            "OxygenPanel", "LowHealth", "BleedingPanel", "BrokenPanel", "CombatPanel", "RaidPanel", "EmptyMagazine", "WeaponPanel",
            "VotePanel", "VoteTopic", "VoteInitiator", "VoteCount", "VoteEligible", "VoteTimer", "VoteStatus", "VoteHint", "VoteSuccessAccent", "VoteFailureAccent", "VoteYesPanel", "VoteYesSelected", "VoteYesKey", "VoteYesLabel", "VoteNoPanel", "VoteNoSelected", "VoteNoKey", "VoteNoLabel", "VoteSunCore", "VoteSunTop", "VoteSunBottom", "VoteSunLeft", "VoteSunRight", "BuildingPanel", "BuildingName", "BuildingHealth" };
        var rects = root.GetComponentsInChildren<RectTransform>(true).ToDictionary(x => x.name);
        if (rects["VitalsPanel"].anchoredPosition.y != 28 || rects["WeaponPanel"].anchoredPosition.y != 28 ||
            rects["VitalsPanelHigh"].anchoredPosition.y + rects["VitalsHighLayout"].anchoredPosition.y != 260 ||
            rects["BleedingPanel"].anchoredPosition.y != 204 || rects["BrokenPanel"].anchoredPosition.y != 204 ||
            rects["OxygenPanel"].anchoredPosition.y != 237)
            throw new InvalidOperationException("Unexpected HUD layout geometry");
        foreach (string suffix in new[] { "", "High" })
        {
            if (rects["StatsPanel" + suffix].gameObject.activeSelf)
                throw new InvalidOperationException("Statistics must remain hidden until data is connected");
            foreach (string stat in new[] { "Kills", "Kd", "Deaths", "Zombies" })
                if (rects["Stats" + stat + "Value" + suffix].GetComponent<Text>().text != "")
                    throw new InvalidOperationException("Statistics cannot contain placeholder values");
        }
        var vote = rects["VotePanel"];
        if (vote.anchorMin != new Vector2(1, 0.5f) || vote.anchorMax != vote.anchorMin ||
            vote.pivot != new Vector2(1, 0.5f) || vote.anchoredPosition != new Vector2(-28, 0) ||
            vote.sizeDelta != new Vector2(264, 108) || rects["VoteYesSelected"].gameObject.activeSelf ||
            rects["VoteNoSelected"].gameObject.activeSelf || rects["VoteYesKey"].GetComponent<Text>().text != "Q" ||
            rects["VoteNoKey"].GetComponent<Text>().text != "E" ||
            !rects["VoteYesKey"].GetComponent<Text>().resizeTextForBestFit ||
            !rects["VoteNoKey"].GetComponent<Text>().resizeTextForBestFit)
            throw new InvalidOperationException("Invalid vote panel geometry or default selection");
        for (int i = 0; i < 8; i++)
        {
            var damagePanel = rects["DamageNumberPanel" + i];
            var damageLabel = rects["DamageNumberText" + i].GetComponent<Text>();
            var outline = damageLabel.GetComponent<Outline>();
            if (outline == null || outline.effectColor != Color.black || outline.effectDistance != new Vector2(1, -1) || !outline.useGraphicAlpha)
                throw new InvalidOperationException("Invalid damage number outline " + i);
            var damageAnimation = damagePanel.GetComponent<Animation>();
            var damageGroup = damagePanel.GetComponent<CanvasGroup>();
            if (damageAnimation == null || !damageAnimation.playAutomatically || damageAnimation.clip == null ||
                !damageAnimation.clip.legacy || damageGroup == null || damageGroup.alpha != 0.2f)
                throw new InvalidOperationException("Invalid damage number animation " + i);
            var pulse = AnimationUtility.GetEditorCurve(damageAnimation.clip, EditorCurveBinding.FloatCurve("", typeof(CanvasGroup), "m_Alpha"));
            if (pulse == null || pulse.length != 2 || Math.Abs(pulse.keys[1].time - 0.12f) > 0.001f ||
                pulse.keys[0].value != 0.2f || pulse.keys[1].value != 1f || AnimationUtility.GetAnimationEvents(damageAnimation.clip).Length != 0)
                throw new InvalidOperationException("Invalid damage number pulse " + i);
            if (damagePanel.anchoredPosition != new Vector2(34 + (i % 2) * 72, 25 + (i / 2) * 25) ||
                damagePanel.sizeDelta != new Vector2(68, 24) || damagePanel.gameObject.activeSelf ||
                damageLabel == null || damageLabel.fontSize != 14 || !damageLabel.supportRichText)
                throw new InvalidOperationException("Invalid damage number slot " + i);
        }
        var outlines = root.GetComponentsInChildren<Outline>(true);
        if (outlines.Length != 8 || outlines.Any(x => !x.name.StartsWith("DamageNumberText", StringComparison.Ordinal)))
            throw new InvalidOperationException("Unexpected outline components");
        var building = rects["BuildingPanel"];
        var buildingAnimation = building.GetComponent<Animation>();
        var buildingGroup = building.GetComponent<CanvasGroup>();
        if (building.anchoredPosition != new Vector2(0, 198) || building.sizeDelta != new Vector2(204, 46) ||
            building.gameObject.activeSelf || buildingGroup == null || buildingAnimation == null ||
            !buildingAnimation.playAutomatically || buildingAnimation.clip == null || !buildingAnimation.clip.legacy)
            throw new InvalidOperationException("Invalid building panel geometry or animation: position=" + building.anchoredPosition + " size=" + building.sizeDelta + " active=" + building.gameObject.activeSelf + " group=" + (buildingGroup != null) + " anim=" + (buildingAnimation != null) + " autoplay=" + (buildingAnimation != null && buildingAnimation.playAutomatically) + " clip=" + (buildingAnimation != null && buildingAnimation.clip != null));
        var fade = AnimationUtility.GetEditorCurve(buildingAnimation.clip, EditorCurveBinding.FloatCurve("", typeof(CanvasGroup), "m_Alpha"));
        if (fade == null || fade.length != 4 || Math.Abs(fade.keys[3].time - 5f) > 0.001f || fade.keys[0].value != 0 || fade.keys[1].value != 1 || fade.keys[2].value != 1 || fade.keys[3].value != 0 || AnimationUtility.GetAnimationEvents(buildingAnimation.clip).Length != 0)
            throw new InvalidOperationException("Invalid building fade curve");
        var names = rects.Keys.ToArray();
        foreach (string name in required)
            if (!names.Contains(name)) throw new InvalidOperationException("Отсутствует UI-элемент: " + name);
        foreach (string name in new[] { "HealthValue", "FoodValue", "WaterValue", "StaminaValue", "OxygenValue", "OxygenPanel", "LowHealth", "BleedingPanel", "BrokenPanel", "WeaponName", "MagazineName", "AmmoValue", "AmmoCapacity", "FireModeValue", "DurabilityValue", "EmptyMagazine", "WeaponPanel" })
            if (!names.Contains(name + "High")) throw new InvalidOperationException("Отсутствует запасной UI-элемент: " + name);
        foreach (string bar in new[] { "Health", "Food", "Water", "Stamina", "Oxygen", "TpaProgress", "VoteProgress", "Building" })
        {
            int count = 100;
            for (int i = 1; i <= count; i++)
            {
                string name = bar + "Fill" + i.ToString("00");
                if (!names.Contains(name)) throw new InvalidOperationException("Отсутствует сегмент UI: " + name);
                ValidateFill(rects, name, i, count);
                if (bar == "Health" || bar == "Food" || bar == "Water" || bar == "Stamina" || bar == "Oxygen")
                {
                    if (!names.Contains(name + "High")) throw new InvalidOperationException("Missing high layout fill: " + name);
                    ValidateFill(rects, name + "High", i, count);
                }
            }
        }
    }

    private static void ValidateFill(System.Collections.Generic.Dictionary<string, RectTransform> rects, string name, int value, int count)
    {
        RectTransform fill = rects[name];
        RectTransform track = fill.parent as RectTransform;
        if (track == null || fill.gameObject.activeSelf || fill.anchorMin != Vector2.zero ||
            fill.anchorMax != Vector2.zero || fill.pivot != Vector2.zero || fill.anchoredPosition != Vector2.zero ||
            Mathf.Abs(fill.sizeDelta.x - track.rect.width * value / count) > 0.001f ||
            Mathf.Abs(fill.sizeDelta.y - track.rect.height) > 0.001f || fill.GetComponent<Image>() == null)
            throw new InvalidOperationException("Invalid fill geometry or initial state: " + name);
    }

    private static Color Hex(string hex, float alpha = 1)
    {
        Color value;
        if (!ColorUtility.TryParseHtmlString("#" + hex, out value)) throw new ArgumentException(hex);
        value.a = alpha;
        return value;
    }
}
