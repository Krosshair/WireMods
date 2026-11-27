using System.IO;
using UnityEditor;
using UnityEngine;

// Wire's Camera Tweaks' lens shader (depth of field) and bloom compute shader into its own bundle,
// embedded in WiresCameraTweaks.dll.
public static class BuildWireCameraTweaksBundle
{
    private static readonly string[] ShaderPaths =
    {
        "Assets/Shaders/WireLens.shader",
        "Assets/Shaders/WireBloom.compute",
    };
    private const string BundleName = "wirecameratweaks";
    private const string OutDir = @"C:\Users\kajet\WireMods\WiresCameraTweaks\build";

    [MenuItem("Wire/Build Camera Tweaks bundle")]
    public static void Build()
    {
        foreach (string path in ShaderPaths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetImporter.GetAtPath(path).SetAssetBundleNameAndVariant(BundleName, "");
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(OutDir);
        var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = ShaderPaths };
        BuildPipeline.BuildAssetBundles(OutDir, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        Debug.Log("Wire: built " + Path.Combine(OutDir, BundleName));
    }
}
