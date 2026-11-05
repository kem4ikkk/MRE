using UnityEditor;
using UnityEngine;

// Самостоятельная эскизная модель из стандартных примитивов Unity.
public static class MreDecaySafeBuilder
{
    private static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(part.GetComponent<Collider>());
    }
    [MenuItem("MRE/Generate safe visual prototype")]
    public static void Generate()
    {
        const string folder = "Assets/Generated";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Generated");
        Shader shader = Shader.Find("Standard");
        if (shader == null) throw new System.InvalidOperationException("Standard shader unavailable.");
        var metal = new Material(shader); metal.color = new Color(.24f,.28f,.3f); metal.SetFloat("_Metallic", .7f); metal.SetFloat("_Glossiness", .3f);
        var dark = new Material(shader); dark.color = new Color(.07f,.08f,.09f); dark.SetFloat("_Metallic", .6f);
        AssetDatabase.DeleteAsset(folder + "/SafeMetal.mat");
        AssetDatabase.DeleteAsset(folder + "/SafeDark.mat");
        AssetDatabase.CreateAsset(metal, folder + "/SafeMetal.mat");
        AssetDatabase.CreateAsset(dark, folder + "/SafeDark.mat");
        var root = new GameObject("MRE_Safe_Visual");
        try {
            Part(root.transform,"Body",new Vector3(0,.6f,0),new Vector3(.8f,1.2f,.64f),metal);
            Part(root.transform,"Door",new Vector3(0,.61f,-.335f),new Vector3(.68f,1.04f,.055f),dark);
            Part(root.transform,"DoorPanel",new Vector3(0,.61f,-.37f),new Vector3(.60f,.96f,.025f),metal);
            Part(root.transform,"Handle",new Vector3(.18f,.61f,-.395f),new Vector3(.035f,.3f,.04f),dark);
            Part(root.transform,"Lock",new Vector3(-.13f,.62f,-.4f),new Vector3(.12f,.12f,.04f),dark);
            Part(root.transform,"HingeTop",new Vector3(-.36f,.95f,-.34f),new Vector3(.07f,.15f,.07f),dark);
            Part(root.transform,"HingeBottom",new Vector3(-.36f,.27f,-.34f),new Vector3(.07f,.15f,.07f),dark);
            foreach (int cells in new[] { 100, 200, 300 }) {
                root.name = "MRE_Safe_Visual_" + cells;
                string path = folder + "/" + root.name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) AssetDatabase.DeleteAsset(path);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new System.Exception("Prefab save failed.");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("MRE_SAFE_VISUAL_OK");
        } finally { Object.DestroyImmediate(root); }
    }

    // Visual-only export: not an Unturned item bundle or a deployable asset.
    public static void BuildVisualBundle()
    {
        Generate();
        string output = System.Environment.GetEnvironmentVariable("MRE_SAFE_VISUAL_OUTPUT");
        if (string.IsNullOrEmpty(output) || !System.IO.Directory.Exists(output))
            throw new System.InvalidOperationException("MRE_SAFE_VISUAL_OUTPUT must be an existing isolated directory.");
        var build = new AssetBundleBuild();
        build.assetBundleName = "mre-safe-visual-only";
        build.assetNames = new[] { "Assets/Generated/MRE_Safe_Visual_100.prefab", "Assets/Generated/MRE_Safe_Visual_200.prefab", "Assets/Generated/MRE_Safe_Visual_300.prefab" };
        if (BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64) == null)
            throw new System.Exception("Visual bundle build failed.");
        Debug.Log("MRE_SAFE_VISUAL_BUNDLE_OK");
    }

    // Own simple arm motion, using paths observed in the official barricade example.
    private static AnimationClip MakeClip(string folder, string name, float duration, float start, float middle, float end)
    {
        var clip = new AnimationClip { name = name, legacy = true, frameRate = 30 };
        foreach (string side in new[] { "Left", "Right" }) {
            string path = "Skeleton/Spine/" + side + "_Shoulder/" + side + "_Arm";
            float sign = side == "Left" ? 1f : -1f;
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.z", new AnimationCurve(
                new Keyframe(0, sign * start), new Keyframe(duration / 2, sign * middle), new Keyframe(duration, sign * end)));
        }
        string assetPath = folder + "/" + name + ".anim";
        AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.CreateAsset(clip, assetPath);
        return clip;
    }

    // Experimental legacy-shaped prefabs; client behaviour is not yet validated.
    public static void BuildExperimentalStorage()
    {
        Generate();
        string output = System.Environment.GetEnvironmentVariable("MRE_SAFE_VISUAL_OUTPUT");
        if (string.IsNullOrEmpty(output) || !System.IO.Directory.Exists(output)) throw new System.InvalidOperationException("Isolated output required.");
        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var list = tags.FindProperty("tags");
        foreach (string tag in new[] { "Item", "Barricade" }) {
            bool found = false;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).stringValue == tag) found = true;
            if (!found) { list.InsertArrayElementAtIndex(list.arraySize); list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = tag; }
        }
        tags.ApplyModifiedProperties();
        foreach (int cells in new[] { 100, 200, 300 }) {
            string folder = "Assets/Generated/Safe" + cells;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Generated", "Safe" + cells);
            var visual = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/MRE_Safe_Visual_" + cells + ".prefab");
            foreach (string kind in new[] { "Item", "Barricade" }) {
                GameObject root = Object.Instantiate(visual); root.name = kind;
                try {
                    int layer = kind == "Item" ? 13 : 27;
                    foreach (Transform child in root.GetComponentsInChildren<Transform>()) { child.gameObject.layer = layer; child.gameObject.tag = kind; }
                    var collider = root.AddComponent<BoxCollider>(); collider.center = new Vector3(0,.6f,0); collider.size = new Vector3(.8f,1.2f,.7f);
                    if (kind == "Item") { var icon = new GameObject("Icon"); icon.layer = layer; icon.tag = kind; icon.transform.SetParent(root.transform, false); }
                    if (PrefabUtility.SaveAsPrefabAsset(root, folder + "/" + kind + ".prefab") == null) throw new System.Exception("Save failed.");
                } finally { Object.DestroyImmediate(root); }
            }
            // Navigation collider remains provisional until placement testing.
            var nav = new GameObject("Nav"); nav.layer = 22;
            var navBox = nav.AddComponent<BoxCollider>(); navBox.center = new Vector3(0,.6f,0); navBox.size = new Vector3(.8f,1.2f,.7f);
            PrefabUtility.SaveAsPrefabAsset(nav, folder + "/Nav.prefab"); Object.DestroyImmediate(nav);
            var equip = MakeClip(folder, "Equip", .4f, 35f, 12f, 0f);
            var use = MakeClip(folder, "Use", .5f, 0f, 25f, 0f);
            var animations = new GameObject("Animations"); animations.layer = 8;
            var component = animations.AddComponent<Animation>();
            component.AddClip(equip, "Equip"); component.AddClip(use, "Use"); component.clip = equip;
            if (component.GetClip("Equip") == null || component.GetClip("Use") == null || equip.length <= 0 || use.length <= 0)
                throw new System.Exception("Animation clips missing or empty.");
            PrefabUtility.SaveAsPrefabAsset(animations, folder + "/Animations.prefab"); Object.DestroyImmediate(animations);
            AssetDatabase.SaveAssets();
            var build = new AssetBundleBuild { assetBundleName = "safe" + cells + ".unity3d", assetNames = new[] { folder + "/Item.prefab", folder + "/Barricade.prefab", folder + "/Nav.prefab", folder + "/Animations.prefab" }, addressableNames = new[] { "Item", "Barricade", "Nav", "Animations" } };
            if (BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64) == null) throw new System.Exception("Build failed.");
            var bundle = AssetBundle.LoadFromFile(System.IO.Path.Combine(output, build.assetBundleName));
            if (bundle == null) throw new System.Exception("Bundle reload failed.");
            try {
                var saved = bundle.LoadAsset<GameObject>("Animations");
                var savedAnimation = saved == null ? null : saved.GetComponent<Animation>();
                foreach (string clipName in new[] { "Equip", "Use" }) {
                    var savedClip = savedAnimation == null ? null : savedAnimation.GetClip(clipName);
                    if (savedClip == null || !savedClip.legacy || savedClip.length <= 0 || AnimationUtility.GetCurveBindings(savedClip).Length < 2)
                        throw new System.Exception("Bundle animation verification failed: " + clipName);
                    Debug.Log("MRE_SAFE_CLIP_OK " + cells + " " + clipName + " length=" + savedClip.length);
                }
            } finally { bundle.Unload(true); }
        }
        Debug.Log("MRE_SAFE_EXPERIMENTAL_OK");
    }
}
