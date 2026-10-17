using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Wild picking keeps Valheim's own bonus-yield path end to end:
    //  1. Chance bonus: the vanilla Farming roll's random input is shifted,
    //     so the normal "+N" text, bonus effect and Farming XP still apply.
    //  2. Veteran yield: extra native items per pick for every Forsaken
    //     defeated after the resource's own biome boss. Added to the same
    //     bonus count vanilla sends to the pickable's owner in RPC_Pick, so
    //     multiplayer authority and drop handling stay vanilla.
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    internal static class WildForage
    {
        [ThreadStatic] private static float _extraChance;
        [ThreadStatic] private static bool _active;
        [ThreadStatic] private static int _veteranExtra;
        [ThreadStatic] private static bool _veteranShown;
        [ThreadStatic] private static Pickable _pickable;
        [ThreadStatic] private static Player _player;

        private static readonly AccessTools.FieldRef<Pickable, bool> Picked =
            AccessTools.FieldRefAccess<Pickable, bool>("m_picked");
        private static readonly AccessTools.FieldRef<Pickable, bool> PickedLocal =
            AccessTools.FieldRefAccess<Pickable, bool>("m_pickedLocal");
        private static readonly Func<Pickable, Vector3> SpawnOffset =
            AccessTools.MethodDelegate<Func<Pickable, Vector3>>(
                AccessTools.Method(typeof(Pickable), "GetSpawnOffset"));

        [HarmonyPrefix]
        private static void Prefix(Pickable __instance, Humanoid character)
        {
            Cleanup();
            if (Plugin.Enabled == null || !Plugin.Enabled.Value ||
                !Plugin.Forage.Value || __instance == null ||
                !(character is Player player) || !WorldProgress.Ready ||
                __instance.m_pickRaiseSkill == Skills.SkillType.None)
                return;

            if (__instance.GetComponent<Piece>() != null ||
                __instance.m_itemPrefab == null)
                return; // No cultivated crops.
            WildResource resource = ForageRules.FromItem(__instance.m_itemPrefab);
            int sourceBiome = BiomeCatalog.FromBiome(
                Heightmap.FindBiome(__instance.transform.position));
            if (!ForageRules.ExistsIn(resource, sourceBiome))
                return;

            bool[] defeated = WorldProgress.ReadKeys();
            var portions = ForageRules.Parts(resource, sourceBiome,
                defeated, Plugin.ForageBonus.Value,
                Plugin.LaterFactor.Value / 100.0,
                Plugin.SkippedFactor.Value / 100.0,
                Plugin.MushroomBlackForestPremium.Value,
                Plugin.ThistleSwampElderPremium.Value,
                Plugin.ThistleSwampBonemassPremium.Value);
            double extra = ForageRules.Total(portions);
            float baseChance = Mathf.Clamp01(
                player.GetSkillFactor(__instance.m_pickRaiseSkill) *
                __instance.m_maxLevelBonusChance);
            if (extra > 0)
            {
                double improved = ProgressionMath.HarvestChance(
                    baseChance, extra, Plugin.ForageCap.Value);
                _extraChance = Mathf.Clamp01((float)(improved - baseChance));
                _active = _extraChance > 0;
            }

            // Only the first real pick of this pickable, like vanilla's
            // own stat and skill handling; repeated presses add nothing.
            if (!Picked(__instance) && !PickedLocal(__instance))
            {
                double veteran = ForageRules.Total(ForageRules.VeteranParts(
                    resource, sourceBiome, defeated,
                    Plugin.VeteranForage.Value,
                    Plugin.SkippedFactor.Value / 100.0));
                if (veteran > 0)
                    _veteranExtra = Mathf.Min(12, ProgressionMath.ExtraCount(
                        Mathf.Max(1, __instance.m_amount), veteran,
                        UnityEngine.Random.value));
            }
            _pickable = __instance;
            _player = player;

            if (AldrDiagnostics.Active && (_active || _veteranExtra > 0))
                AldrDiagnostics.Sample("FORAGE_ROLL",
                    "resource=" + resource + " biome_tier=" + (sourceBiome + 1) +
                    " vanilla=" + baseChance.ToString("0.###") +
                    " bonus_chance=" + _extraChance.ToString("0.###") +
                    " veteran_extra_items=" + _veteranExtra);
        }

        [HarmonyPostfix]
        private static void Postfix() => Cleanup();

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            Cleanup();
            return __exception;
        }

        private static void Cleanup()
        {
            _extraChance = 0;
            _active = false;
            _veteranExtra = 0;
            _veteranShown = false;
            _pickable = null;
            _player = null;
        }

        private static float AdjustRandom()
        {
            float normal = UnityEngine.Random.value;
            // Keep vanilla's comparison with its skill-derived chance,
            // including skill level zero. Negative values are intentional.
            return _active ? normal - _extraChance : normal;
        }

        // Vanilla bonus succeeded: fold the veteran extra into the same
        // number so vanilla's own "+N" text shows the full total once.
        private static int BonusYield(Pickable pickable)
        {
            int amount = pickable.m_bonusYieldAmount;
            if (_veteranExtra > 0 && ReferenceEquals(pickable, _pickable))
            {
                amount += _veteranExtra;
                _veteranShown = true;
            }
            return amount;
        }

        // Final bonus count sent to RPC_Pick.
        private static int FinalBonus(int vanillaBonus)
        {
            if (_veteranExtra <= 0 || _pickable == null) return vanillaBonus;
            if (_veteranShown) return vanillaBonus; // already included
            _veteranShown = true;
            try
            {
                // Same native feedback vanilla uses for its bonus yield.
                Vector3 at = _pickable.transform.position + SpawnOffset(_pickable);
                if (DamageText.instance != null)
                    DamageText.instance.ShowText(DamageText.TextType.Bonus, at,
                        "+" + _veteranExtra, player: true);
                if (_pickable.m_bonusEffect != null && _player != null)
                    _pickable.m_bonusEffect.Create(_pickable.transform.position,
                        Quaternion.identity, null, 1f, -1, _player.GetZDOID());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Aldr] Veteran yield feedback skipped: " + e.Message);
            }
            return vanillaBonus + _veteranExtra;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> source)
        {
            MethodInfo vanillaRandom = AccessTools.PropertyGetter(
                typeof(UnityEngine.Random), nameof(UnityEngine.Random.value));
            FieldInfo bonusAmount = AccessTools.Field(typeof(Pickable),
                nameof(Pickable.m_bonusYieldAmount));
            MethodInfo random = AccessTools.Method(typeof(WildForage), nameof(AdjustRandom));
            MethodInfo bonus = AccessTools.Method(typeof(WildForage), nameof(BonusYield));
            MethodInfo final = AccessTools.Method(typeof(WildForage), nameof(FinalBonus));
            bool afterRpcName = false, finalPatched = false, bonusPatched = false;
            foreach (CodeInstruction inst in source)
            {
                if (inst.Calls(vanillaRandom))
                {
                    yield return new CodeInstruction(OpCodes.Call, random)
                    { labels = inst.labels, blocks = inst.blocks };
                    continue;
                }
                if (!bonusPatched && inst.LoadsField(bonusAmount))
                {
                    // ldarg.0 is already on the stack: consume it here.
                    bonusPatched = true;
                    yield return new CodeInstruction(OpCodes.Call, bonus)
                    { labels = inst.labels, blocks = inst.blocks };
                    continue;
                }
                if (inst.opcode == OpCodes.Ldstr && (inst.operand as string) == "RPC_Pick")
                    afterRpcName = true;
                if (afterRpcName && !finalPatched && inst.opcode == OpCodes.Box &&
                    (inst.operand as Type) == typeof(int))
                {
                    finalPatched = true;
                    yield return new CodeInstruction(OpCodes.Call, final);
                }
                yield return inst;
            }
            if (!finalPatched || !bonusPatched)
                Debug.LogWarning("[Aldr] Wild foraging veteran yield hook not found; chance bonus still active.");
        }
    }
}
