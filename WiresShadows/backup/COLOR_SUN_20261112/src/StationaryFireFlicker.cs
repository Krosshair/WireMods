using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WiresLightOcclusion
{
    // A one-time IL replacement in vanilla's existing LightFlicker update:
    // replace ONLY its positional m_movement multiplier with constant zero.
    // Vanilla's intensity, fade, TTL and accessibility logic are unchanged.
    // Covers all LightFlicker instances, including character-held torches
    // whose genuine spatial shadows are handled by the game, not WPR.
    // No new Update/LateUpdate callback and no additional per-frame patch.
    [HarmonyPatch(typeof(LightFlicker), nameof(LightFlicker.CustomUpdate))]
    internal static class StationaryFireFlicker
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> RemovePositionalFlicker(
            IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo movement = AccessTools.Field(typeof(LightFlicker), "m_movement");
            if (movement == null)
                throw new InvalidOperationException("LightFlicker.m_movement was not found.");

            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld &&
                    Equals(instruction.operand, movement))
                {
                    // Previous IL pushes 'this' then loads m_movement.
                    // Consume 'this', push zero instead of the field value.
                    // The following Vector3 multiplication is unchanged.
                    instruction.opcode = OpCodes.Pop;
                    instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                    replaced++;
                }
                else
                {
                    yield return instruction;
                }
            }
            if (replaced != 1)
                throw new InvalidOperationException(
                    "Unexpected LightFlicker.CustomUpdate IL: expected one " +
                    "m_movement load, found " + replaced + ".");
        }
    }
}
