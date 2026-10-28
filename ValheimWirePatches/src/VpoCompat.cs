using System;
using System.Reflection;
using HarmonyLib;

namespace ValheimWirePatches
{
    // Removes the VPO patches that replace components this mod relies on.
    internal static class VpoCompat
    {
        public const string Guid = "dev.ontrigger.vpo";

        public static void Remove(Harmony harmony, BepInEx.Logging.ManualLogSource log, MethodBase original, string vpoType)
        {
            Patches info = original != null ? Harmony.GetPatchInfo(original) : null;
            if (info == null) return;
            foreach (Patch p in info.Prefixes)
            {
                Type owner = p.PatchMethod.DeclaringType;
                if (owner == null || owner.FullName != vpoType) continue;
                harmony.Unpatch(original, p.PatchMethod);
                log.LogInfo("Disabled VPO " + owner.Name + "." + p.PatchMethod.Name);
            }
        }
    }
}
