using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Renders the existing bundle at the shell's requested sizes and states.
public static class MreMenuPreview
{
    private const string BundleRelativePath = "Build/Windows/MreHelp/MreHelp.unity3d";

    [MenuItem("MRE/Создать превью меню (1920 и 1366)")]
    public static void RenderStates()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string bundle = Path.Combine(project, BundleRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string output = Path.Combine(project, "Build/Review");
        Directory.CreateDirectory(output);
        foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1366, 768) })
        {
            RenderBundle(bundle, Path.Combine(output, "Home-" + size.x + "x" + size.y + ".png"), size, "Home", false);
            RenderBundle(bundle, Path.Combine(output, "Help-" + size.x + "x" + size.y + ".png"), size, "Help", false);
            RenderBundle(bundle, Path.Combine(output, "Notifications-" + size.x + "x" + size.y + ".png"), size, "Home", true);
        }
        Debug.Log("MRE: созданы шесть превью существующего bundle в " + output + ". Сборка bundle не выполнялась.");
    }

    public static void RenderBundle(string bundlePath, string outputPath, Vector2Int size, string page, bool notifications)
    {
        if (!File.Exists(bundlePath)) throw new FileNotFoundException("Не найден существующий MRE menu bundle; сначала соберите его.", bundlePath);
        AssetBundle bundle = null; GameObject root = null; GameObject cameraObject = null; RenderTexture rt = null; Texture2D image = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            bundle = AssetBundle.LoadFromFile(bundlePath); if (bundle == null) throw new InvalidOperationException("Unity не смогла открыть bundle: " + bundlePath);
            GameObject prefab = bundle.LoadAsset<GameObject>("Effect"); if (prefab == null) throw new InvalidOperationException("В bundle отсутствует prefab Effect.");
            root = UnityEngine.Object.Instantiate(prefab); root.name = "MreMenuPreview";
            if (page == "Home") SetHomeSample(root.transform);
            foreach (string tabName in new[] { "Home", "Help", "Team", "Events", "Stats", "Links", "Settings", "BattlePass", "Rewards", "Reports" })
            {
                Transform marker = Find(root.transform, "MenuSelected" + tabName); if (marker != null) marker.gameObject.SetActive(tabName == page);
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("MenuPage") && t.parent != null && t.parent.name == "MenuPages") t.gameObject.SetActive(t.name == "MenuPage" + page);
                if (t.name == "MenuNotificationsPanel") t.gameObject.SetActive(notifications);
            }
            Transform bellMarker = Find(root.transform, "MenuNotificationsSelected"); if (bellMarker != null) bellMarker.gameObject.SetActive(notifications);

            cameraObject = new GameObject("MreMenuPreviewCamera", typeof(Camera)); Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.28f, .4f, .34f, 1); camera.orthographic = true; camera.nearClipPlane = .01f; camera.farClipPlane = 100; camera.allowHDR = false; camera.allowMSAA = false;
            Canvas canvas = root.GetComponent<Canvas>(); if (canvas == null) throw new InvalidOperationException("Effect prefab has no Canvas.");
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.overrideSorting = true; canvas.sortingOrder = 100;
            rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32); rt.Create(); camera.targetTexture = rt; camera.aspect = (float)size.x / size.y;
            foreach (UnityEngine.UI.Text label in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                if (label.font != null) label.font.RequestCharactersInTexture(label.text, label.fontSize, FontStyle.Normal);
            image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false, false); Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply(false, false);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)); File.WriteAllBytes(outputPath, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; if (cameraObject != null) cameraObject.GetComponent<Camera>().targetTexture = null;
            if (image != null) UnityEngine.Object.DestroyImmediate(image); if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject); if (root != null) UnityEngine.Object.DestroyImmediate(root); if (bundle != null) bundle.Unload(true);
        }
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    private static void SetHomeSample(Transform root)
    {
        SetText(root, "MenuHomePlayerName", "Игрок MRE");
        SetText(root, "MenuHomeSteamId", "—");
        SetText(root, "MenuHomeGroup", "Без группы");
        SetText(root, "MenuHomeGroupCount", "Играете самостоятельно");
        SetText(root, "MenuHomeServerName", "MRE");
        SetText(root, "MenuHomeOnline", "1 / 24");
        SetText(root, "MenuHomeMap", "PEI");
    }

    private static void SetText(Transform root, string name, string value)
    {
        Transform target = Find(root, name);
        if (target == null) return;
        UnityEngine.UI.Text text = target.GetComponent<UnityEngine.UI.Text>();
        if (text != null) text.text = value;
    }
}
