
using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx.Logging;
using BepInEx;

namespace WiresDLSS
{
    // Unity must discover the NVIDIA native plugin from its game-root
    // DLL search path at process startup. Loading it dynamically from
    // BepInEx/plugins did NOT register DLSS with Unity's render backend.
    // Only installs the two NVIDIA DLLs; never touches dxgi/ReShade/RenoDX.
    internal static class NativeBootstrap
    {
        private static readonly string[] NativeFiles = {
            "NVUnityPlugin.dll", "nvngx_dlss.dll"
        };

        internal static bool Ensure(string pluginLocation, ManualLogSource log)
        {
            string folder = Path.GetDirectoryName(pluginLocation);
            string game = Paths.GameRootPath;
            bool foundAll = true, foundAny = false;
            foreach (string name in NativeFiles)
            {
                bool exists = File.Exists(Path.Combine(game, name));
                foundAny |= exists;
                foundAll &= exists;
            }

            if (foundAll)
            {
                log.LogInfo("NVIDIA native libraries found in Valheim root. RenoDX's dxgi.dll is untouched.");
                return true;
            }
            if (foundAny)
            {
                log.LogWarning("Only one NVIDIA native runtime DLL is present. Refusing to overwrite or mix versions.");
                return false;
            }

            foreach (string name in NativeFiles)
                if (!File.Exists(Path.Combine(folder, name)))
                {
                    log.LogWarning("Missing bundled " + name + "; native DLSS unavailable.");
                    return false;
                }

            string marker = Path.Combine(folder, "InstalledNativeLibraries.txt");
            if (File.Exists(marker))
            {
                log.LogWarning("Runtime was previously installed then removed; not silently installing again.");
                return false;
            }

            try
            {
                var installed = new System.Collections.Generic.List<string>(2);
                foreach (string name in NativeFiles)
                {
                    string source = Path.Combine(folder, name);
                    string destination = Path.Combine(game, name);
                    string pending = destination + ".wiresdlss.tmp";
                    try
                    {
                        File.Copy(source, pending, false);
                        if (Hash(source) != Hash(pending))
                            throw new IOException("Native runtime hash mismatch: " + name);
                        File.Move(pending, destination);
                        installed.Add(destination);
                    }
                    finally
                    {
                        if (File.Exists(pending)) File.Delete(pending);
                    }
                }

                using (StreamWriter manifest = new StreamWriter(marker))
                    foreach (string filename in installed)
                        manifest.WriteLine(Path.GetFileName(filename) + "|" + Hash(filename));
                log.LogWarning("Installed two NVIDIA runtime DLLs. RESTART VALHEIM ONCE to register DLSS with Unity.");
                return false;
            }
            catch (Exception ex)
            {
                foreach (string name in NativeFiles)
                {
                    string destination = Path.Combine(game, name);
                    try
                    {
                        if (File.Exists(destination) &&
                            File.Exists(Path.Combine(folder, name)) &&
                            Hash(destination) == Hash(Path.Combine(folder, name)))
                            File.Delete(destination);
                    }
                    catch { }
                }
                log.LogError("Cannot install the native libraries: " + ex.Message);
                return false;
            }
        }

        private static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream file = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
    }
}
