using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // A hit pushes by how much of the target's maximum health it takes: 50%
    // pushes as in vanilla, 25% half as far, 100% and more twice as far.
    // Valheim pushes before it has worked out the damage, so the push waits
    // until the damage is done. A hit that gets through for nothing (blocked,
    // absorbed, immune) pushes nothing. Pushes without any damage in them,
    // like a block shoving the attacker away, stay as they are. Poison and
    // fire work over time, so they don't count.
    internal static class Knockback
    {
        private static readonly Action<Character, HitData> Vanilla =
            AccessTools.MethodDelegate<Action<Character, HitData>>(
                AccessTools.Method(typeof(Character), "ApplyPushback", new[] { typeof(HitData) }));

        // The hit being worked out right now, if its push is ours.
        private static HitData current;

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class Damage
        {
            // A block can send a second hit back from inside the first one, so
            // the outer hit is kept and put back.
            private static void Prefix(Character __instance, HitData hit, out HitData __state)
            {
                __state = current;
                current = Counts(__instance, hit) ? hit : null;
            }

            private static Exception Finalizer(Exception __exception, HitData __state)
            {
                current = __state;
                return __exception;
            }

            // Two places in the method: the push itself, and right after the
            // damage. Both or none.
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
            {
                MethodInfo push = AccessTools.Method(typeof(Character), "ApplyPushback", new[] { typeof(HitData) });
                MethodInfo damage = AccessTools.Method(typeof(Character), nameof(Character.ApplyDamage));
                var codes = new List<CodeInstruction>(input);
                int pushAt = push == null ? -1 : codes.FindIndex(c => c.Calls(push));
                int damageAt = damage == null ? -1 : codes.FindIndex(c => c.Calls(damage));
                if (pushAt < 0 || damageAt < pushAt)
                {
                    Debug.LogWarning("[Aldr] Knockback: hit code not found, knockback stays as in vanilla.");
                    return codes;
                }
                codes.InsertRange(damageAt + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Knockback), nameof(Push)))
                });
                codes[pushAt].opcode = OpCodes.Call;
                codes[pushAt].operand = AccessTools.Method(typeof(Knockback), nameof(Hold));
                return codes;
            }
        }

        private static bool Counts(Character target, HitData hit) =>
            Plugin.On && Plugin.KnockbackOn.Value && hit != null && hit.m_pushForce != 0f &&
            (target.IsPlayer() ? Plugin.KnockbackPlayers.Value : Plugin.KnockbackCreatures.Value) &&
            hit.GetTotalDamage() > 0f;

        private static void Hold(Character target, HitData hit)
        {
            if (hit != current) Vanilla(target, hit);
        }

        // By now the hit holds the damage it really did.
        private static void Push(Character target, HitData hit)
        {
            if (hit != current) return;
            float share = Mathf.Clamp01(hit.GetTotalDamage() / Mathf.Max(1f, target.GetMaxHealth()));
            float factor = share * 100f / Plugin.KnockbackVanillaPoint.Value * Plugin.KnockbackStrength.Value / 100f;
            target.ApplyPushback(hit.m_dir, hit.m_pushForce * factor);
            if (AldrDiagnostics.Active) AldrDiagnostics.Sample("KNOCKBACK", "target=" + target.name +
                " health_share=" + share.ToString("0.###") + " push_factor=" + factor.ToString("0.###"));
        }
    }
}
