using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildWireLightBundle
{
    private static readonly string[] ShaderPaths =
    {
        "Assets/Shaders/WireToonDeferredShading.shader",
        "Assets/Shaders/WireShadowDistance.shader",
        "Assets/Shaders/WireShadowCombine.shader",
        "Assets/Shaders/WireShadowNativeDepthCopy.shader",
        "Assets/Shaders/WireAO/WireAOApply.shader",
        "Assets/Shaders/WireShadowFrontBack.shader",
    };
    // Replacement shaders whose instancing variant must stay in the bundle
    // (it's stripped when no material in the bundle uses it).
    private static readonly string[] Instanced =
    {
        "Assets/Shaders/WireShadowDistance.shader",
        "Assets/Shaders/WireShadowFrontBack.shader",
    };
    private const string BundleName = "wirelightocclusion";
    private const string OutDir = @"C:\Users\kajet\WireMods\WiresShadows\build";

    [MenuItem("Wire/Build Light Occlusion bundle")]
    public static void Build()
    {
        // Anything still marked for this bundle that the lists above no longer name (shaders and
        // their instancing materials the mod stopped using) is taken out of it.
        var keep = new System.Collections.Generic.HashSet<string>(ShaderPaths);
        foreach (string path in Instanced) keep.Add(path.Replace(".shader", "Instanced.mat"));
        foreach (string path in AssetDatabase.GetAssetPathsFromAssetBundle(BundleName))
            if (!keep.Contains(path))
                AssetImporter.GetAtPath(path).SetAssetBundleNameAndVariant("", "");
        // Pick up shader edits made outside Unity, and never reuse an old build.
        foreach (string path in ShaderPaths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetImporter.GetAtPath(path).SetAssetBundleNameAndVariant(BundleName, "");
        }
        foreach (string path in Instanced)
        {
            string matPath = path.Replace(".shader", "Instanced.mat");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(AssetDatabase.LoadAssetAtPath<Shader>(path));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            AssetImporter.GetAtPath(matPath).SetAssetBundleNameAndVariant(BundleName, "");
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(OutDir);
        BuildPipeline.BuildAssetBundles(OutDir, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        Debug.Log("Wire: built " + Path.Combine(OutDir, BundleName));
    }
}
