using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Отдельный исходный эффект; не меняет действующий HUD MRE и не устанавливает ресурсы.
public static class MreDecayZoneHudBuilder
{
    private const string PrefabPath = "Assets/MreDecayZone/Effect.prefab";

    [MenuItem("MRE/Создать подсказку зоны гниения")]
    public static void Generate()
    {
        Directory.CreateDirectory("Assets/MreDecayZone");
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) throw new InvalidOperationException("Не найден шрифт Unity.");
        var root = new GameObject("Effect", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        try
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            var panel = new GameObject("DecayZonePanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, 158);
            rect.sizeDelta = new Vector2(410, 34);
            var background = panel.GetComponent<Image>();
            background.color = new Color(0.067f, 0.09f, 0.098f, 0.92f);
            background.raycastTarget = false;

            var textObject = new GameObject("DecayZoneText", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(panel.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8, 3);
            textRect.offsetMax = new Vector2(-8, -3);
            var label = textObject.GetComponent<Text>();
            label.font = font;
            label.fontSize = 15;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 11;
            label.resizeTextMaxSize = 15;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.933f, 0.949f, 0.91f);
            label.raycastTarget = false;
            label.text = "Гниение построек: ускоренное";
            panel.SetActive(false);

            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException("Не удалось создать prefab.");
            AssetDatabase.SaveAssets();
            Debug.Log("MRE_DECAY_ZONE_PREFAB_OK");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Вывод только в явно заданный тестовый каталог; игровые ID ещё не назначены.
    public static void BuildVisualBundle()
    {
        Generate();
        string output = Environment.GetEnvironmentVariable("MRE_DECAY_HUD_OUTPUT");
        if (String.IsNullOrWhiteSpace(output) || !Directory.Exists(output))
            throw new InvalidOperationException("MRE_DECAY_HUD_OUTPUT должен указывать на существующий тестовый каталог.");
        var build = new AssetBundleBuild {
            assetBundleName = "mre-decay-zone-visual.unity3d",
            assetNames = new[] { PrefabPath },
            addressableNames = new[] { "Effect" }
        };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new InvalidOperationException("Bundle не собран.");
        var bundle = AssetBundle.LoadFromFile(Path.Combine(output, build.assetBundleName));
        if (bundle == null) throw new InvalidOperationException("Bundle не читается.");
        try
        {
            var prefab = bundle.LoadAsset<GameObject>("Effect");
            if (prefab == null || prefab.GetComponent<Canvas>() == null ||
                prefab.transform.Find("DecayZonePanel/DecayZoneText") == null)
                throw new InvalidOperationException("Контракт Effect отсутствует.");
        }
        finally { bundle.Unload(true); }
        Debug.Log("MRE_DECAY_ZONE_BUNDLE_OK");
    }
}
