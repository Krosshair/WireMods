using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ValheimWirePatches
{
    // InventoryGui calls SetActiveGroup(..., playSound: true) from its button
    // handlers, so m_setActiveGroupEffects (meant for gamepad bumper switching)
    // plays on top of the button's own ButtonSfx click.
    // Every caller passes false except the gamepad paths and OnSelectedRecipe
    // (recipe list entries have no ButtonSfx, that sound is their only click).
    [HarmonyPatch]
    internal static class DoubleClickSound
    {
        private static readonly HashSet<string> KeepSound = new HashSet<string>
        {
            "UpdateGamepad", "MoveToLowerInventoryGrid", "MoveToUpperInventoryGrid", "OnSelectedRecipe",
            "SetActiveGroup"
        };

        private static bool IsSetActiveGroup(object operand) =>
            operand is MethodInfo m && m.DeclaringType == typeof(InventoryGui) && m.Name == "SetActiveGroup";

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(typeof(InventoryGui)))
            {
                if (KeepSound.Contains(m.Name) || m.IsAbstract || m.ContainsGenericParameters) continue;
                List<CodeInstruction> code;
                try { code = PatchProcessor.GetOriginalInstructions(m); }
                catch { continue; }
                for (int i = 1; i < code.Count; i++)
                    if (IsSetActiveGroup(code[i].operand) && code[i - 1].LoadsConstant(1))
                    {
                        yield return m;
                        break;
                    }
            }
        }

        private static bool GroupSound() => !Plugin.DoubleClickSound.Value;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            for (int i = 1; i < code.Count; i++)
            {
                if (!IsSetActiveGroup(code[i].operand) || !code[i - 1].LoadsConstant(1)) continue;
                code[i - 1] = new CodeInstruction(System.Reflection.Emit.OpCodes.Call,
                    AccessTools.Method(typeof(DoubleClickSound), nameof(GroupSound))).MoveLabelsFrom(code[i - 1]);
            }
            return code;
        }
    }
}
