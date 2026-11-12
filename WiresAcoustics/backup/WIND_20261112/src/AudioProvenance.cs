using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // The vanilla game instantiates the SAME random ambient prefab for both
    // biome calls and real POSITIONAL lava noises. They have the same parent,
    // so the parent-only classification cannot distinguish their physics.
    // Replace exactly the producer's accepted ZSFX.Play call with a wrapper
    // that tags the instance BEFORE ZSFX.Play invokes UpdateReverb.
    // This is a spawn/play EVENT, never an Update or a scene-wide scan.
    internal static class AudioProvenance
    {
        private static int s_randomStarted, s_lavaStarted;
        internal static string Summary()
        {
            int random = s_randomStarted, lava = s_lavaStarted;
            s_randomStarted = s_lavaStarted = 0;
            return "producer calls: random ambience " + random + ", positional lava " + lava;
        }

        private static readonly MethodInfo NativePlay =
            AccessTools.Method(typeof(ZSFX), nameof(ZSFX.Play), System.Type.EmptyTypes);
        private static readonly MethodInfo PlayRandomMethod =
            AccessTools.Method(typeof(AudioProvenance), nameof(PlayRandom));
        private static readonly MethodInfo PlayLavaMethod =
            AccessTools.Method(typeof(AudioProvenance), nameof(PlayLava));

        private static IEnumerable<CodeInstruction> ReplacePlay(
            IEnumerable<CodeInstruction> original, MethodInfo replacement, string name)
        {
            var code = new List<CodeInstruction>(original);
            int count = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].operand is MethodInfo call &&
                    call.Name == NativePlay.Name &&
                    call.DeclaringType == NativePlay.DeclaringType &&
                    call.GetParameters().Length == 0)
                    count++;
            if (count != 1)
            {
                Plugin.Log.LogWarning("[acoustics-source] " + name + " expected one " +
                    "ZSFX.Play producer call; found " + count + ". Leaving vanilla unchanged.");
                return code;
            }
            foreach (var ins in code)
            {
                if (!(ins.operand is MethodInfo call) ||
                    call.Name != NativePlay.Name ||
                    call.DeclaringType != NativePlay.DeclaringType ||
                    call.GetParameters().Length != 0) continue;
                ins.opcode = OpCodes.Call;
                ins.operand = replacement;
                break;
            }
            return code;
        }

        private static void MarkAndPlay(ZSFX sfx, bool lava)
        {
            if (sfx == null) return;
            if (lava) s_lavaStarted++; else s_randomStarted++;
            if (Plugin.Enabled.Value)
            {
                AcousticFilter filter = sfx.GetComponent<AcousticFilter>();
                if (filter == null) filter = sfx.gameObject.AddComponent<AcousticFilter>();
                filter.WorldRandomAmbient = !lava;
            }
            sfx.Play();
        }

        private static void PlayRandom(ZSFX sfx) => MarkAndPlay(sfx, false);
        internal static void Play(ZSFX sfx) => MarkAndPlay(sfx, false); // the mod's own biome calls (Biomes): the same as the game's
        private static void PlayLava(ZSFX sfx) => MarkAndPlay(sfx, true);

        [HarmonyPatch(typeof(AudioMan), "UpdateRandomAmbient")]
        internal static class Random
        {
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> code) =>
                ReplacePlay(code, PlayRandomMethod, "AudioMan.UpdateRandomAmbient");
        }

        [HarmonyPatch(typeof(AudioMan), "UpdateLavaAmbient")]
        internal static class Lava
        {
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> code) =>
                ReplacePlay(code, PlayLavaMethod, "AudioMan.UpdateLavaAmbient");
        }
    }
}
