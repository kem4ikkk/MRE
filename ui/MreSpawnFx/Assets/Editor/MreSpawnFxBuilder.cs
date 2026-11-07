using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MreSpawnFxBuilder
{
    private const string Prefab = "Assets/MreSpawnFx/Effect.prefab";
    [MenuItem("MRE/Spawn FX/Build Windows")]
    public static void BuildWindows()
    {
        Directory.CreateDirectory("Assets/MreSpawnFx");
        var shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) throw new InvalidOperationException("Particle shader missing");
        var material = new Material(shader);
        material.SetFloat("_Mode", 2); // Standard Unlit: Fade.
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetColor("_Color", Color.white);
        AssetDatabase.DeleteAsset("Assets/MreSpawnFx/Glow.mat");
        AssetDatabase.CreateAsset(material, "Assets/MreSpawnFx/Glow.mat");
        var root = new GameObject("Effect");
        var ps = root.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false; main.duration = 0.1f; main.startLifetime = 0.42f;
        main.startSpeed = 0.12f; main.startSize = 0.15f;
        main.startColor = new Color(0.65f, 1f, 0.55f, 0.95f);
        main.maxParticles = 32; main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 32) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.68f;
        shape.position = new Vector3(0, 1.0f, 0); shape.scale = new Vector3(1, 1.35f, 1);
        var colors = ps.colorOverLifetime; colors.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0.8f, 0.55f), new GradientAlphaKey(0, 1) }); colors.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        PrefabUtility.SaveAsPrefabAsset(root, Prefab); UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../runtime/ui-builds/MreSpawnFx/Windows")); Directory.CreateDirectory(output);
        var map = new AssetBundleBuild { assetBundleName = "MreSpawnFx.unity3d", assetNames = new[] { Prefab }, addressableNames = new[] { "Effect" } };
        if (BuildPipeline.BuildAssetBundles(output, new[] { map }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64) == null) throw new Exception("Build failed");
        var bundle = AssetBundle.LoadFromFile(Path.Combine(output, "MreSpawnFx.unity3d"));
        if (bundle == null) throw new Exception("Effect bundle could not be loaded");
        var builtEffect = bundle.LoadAsset<GameObject>("Effect");
        if (builtEffect == null || builtEffect.GetComponent<ParticleSystem>() == null || builtEffect.GetComponent<ParticleSystemRenderer>() == null)
            throw new Exception("Effect bundle is missing particle components");
        bundle.Unload(true);
        var mod = Path.Combine(output, "MreSpawnFx"); Directory.CreateDirectory(mod);
        File.Copy(Path.Combine(output, "MreSpawnFx.unity3d"), Path.Combine(mod, "MreSpawnFx.unity3d"), true);
        File.WriteAllText(Path.Combine(mod, "MreSpawnFx.dat"), "GUID 7744a548037a48a6a2ec469b54e7c2e1\nType Effect\nID 29065\nAsset_Bundle_Version 6\nExclude_From_Master_Bundle\nLifetime 0.5\nLifetime_Spread 0\nRelevant_Distance 64\nRandomize_Rotation false\n", new UTF8Encoding(false));
    }
}

