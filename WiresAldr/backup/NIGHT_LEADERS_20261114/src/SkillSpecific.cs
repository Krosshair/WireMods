using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Event-based, bounded runtime state. No Update hooks, scene scanning or UI.
    internal static class SkillSpecific
    {
        private static Attack activeAttack;
        private static Recipe activeRecipe;
        private static float craftModifier;
        private static float forageModifier;
        private static bool building;
        private static float buildModifier;
        private static float cookingFood;   // food value of the dish being made
        private static float lastDefense = -999f;
        private static float buildTime;
        private static readonly AccessTools.FieldRef<Attack,int> ChainIndex = AccessTools.FieldRefAccess<Attack,int>("m_currentAttackCainLevel");
        private static readonly AccessTools.FieldRef<Attack,Humanoid> AttackOwner = AccessTools.FieldRefAccess<Attack,Humanoid>("m_character");
        private static readonly AccessTools.FieldRef<InventoryGui,Recipe> CraftRecipe = AccessTools.FieldRefAccess<InventoryGui,Recipe>("m_craftRecipe");
        private static readonly AccessTools.FieldRef<InventoryGui,ItemDrop.ItemData> UpgradeItem = AccessTools.FieldRefAccess<InventoryGui,ItemDrop.ItemData>("m_craftUpgradeItem");
        private static int IngredientTypes(Piece.Requirement[] ingredients)
        {
            if (ingredients == null) return 0;
            int count = 0;
            foreach (var ingredient in ingredients)
                if (ingredient != null && ingredient.m_resItem != null) ++count;
            return count;
        }
        internal static void DefenseSucceeded(Player player)
        {
            if (player != null && player == Player.m_localPlayer) lastDefense = Time.time;
        }
        // ---- Combos: consecutive chain steps that actually connected ----
        // Vanilla only raises weapon skill when a swing hits something; a
        // swing that hit nothing breaks the streak, so only genuinely
        // landed combos earn the extra.
        private static bool attackConnected;
        private static int comboStreak, lastComboIndex = -1;
        private static float lastComboTime = -999f;

        // Called from the local player's damage context for hostile targets.
        internal static void NoteConnectedHit()
        {
            Attack a = activeAttack;
            if (a == null || attackConnected) return;
            attackConnected = true;
            if (a.m_attackChainLevels <= 1) { comboStreak = 0; lastComboIndex = -1; return; }
            int index = ChainIndex(a);
            if (index > 0 && lastComboIndex == index - 1 && Time.time - lastComboTime < 3f)
                comboStreak++;
            else
                comboStreak = 1;
            lastComboIndex = index;
            lastComboTime = Time.time;
        }

        private static float HitTechniqueBonus(Skills.SkillType skill)
        {
            if (!IsWeapon(skill) || activeAttack == null) return 0f;
            float extra = 0f;
            // 2nd landed step +12%, 3rd +24%, longer chains up to +36%.
            if (attackConnected && comboStreak > 1)
                extra += Mathf.Min(3, comboStreak - 1) * Plugin.XpComboStep.Value / 100f;
            if (Time.time - lastDefense < 2.25f)
            {
                extra += Plugin.XpCounterBonus.Value / 100f; // one successful counter, not each hit of a combo
                lastDefense = -999f;
            }
            return extra;
        }
        private static bool IsWeapon(Skills.SkillType s)
        {
            return (s >= Skills.SkillType.Swords && s <= Skills.SkillType.Axes) ||
                   s == Skills.SkillType.Bows || s == Skills.SkillType.Crossbows ||
                   s == Skills.SkillType.Unarmed || s == Skills.SkillType.ElementalMagic;
        }
        private static float ProductionModifier(Player player, Skills.SkillType skill)
        {
            // Recipe complexity: +2.5% per ingredient type (max 12%), +3.5% for
            // upgrades. First crafts are rewarded by CraftingProgression.
            if (skill == Skills.SkillType.Cooking)
                return (activeRecipe != null ? craftModifier : 0f) + FoodBonus(cookingFood);
            if (activeRecipe != null && skill == Skills.SkillType.Crafting)
                return craftModifier;
            if (skill == Skills.SkillType.Farming) return forageModifier;
            return 0f;
        }
        internal static float Multiplier(Player player, Skills.SkillType skill)
        {
            float bonus = ProductionModifier(player, skill);
            if (skill == Skills.SkillType.Sneak)
            {
                // Vanilla awards factor 1 when sneaking in range of unaware hostiles
                // and 0.1 elsewhere. Only the genuine in-range gain should get this.
                // Called by SkillXpPatch which also has the original factor.
                return Mathf.Max(0f, bonus);
            }
            bonus += HitTechniqueBonus(skill);
            bonus += TravelBonus(player, skill);
            return Mathf.Max(0f, bonus);
        }
        // Sneaking in range of unaware hostiles (vanilla's full-XP case).
        // Read from vanilla's own check, made right before it raises Sneak,
        // so XP multipliers from other mods can't fake it. No extra scan.
        private static int stealthFrame = -1;
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.InStealthRange))]
        private static class StealthRangeResult
        {
            private static void Postfix(Character me, bool __result)
            {
                if (__result && me != null && me == Player.m_localPlayer) stealthFrame = Time.frameCount;
            }
        }
        internal static float SneakBonus() => stealthFrame == Time.frameCount ? Plugin.XpSneakBonus.Value / 100f : 0f;

        // ---- Cooking: better food, more XP ----
        // Food value = health + stamina + eitr of the dish, straight from the
        // item. Early dishes (about 40) get nothing extra, up to +50% for
        // dishes worth 160 or more.
        private static float FoodValue(ItemDrop.ItemData.SharedData shared) =>
            shared == null ? 0f : shared.m_food + shared.m_foodStamina + shared.m_foodEitr;
        internal static float FoodBonus(float value) => Plugin.XpCookingFood.Value / 100f * Mathf.Clamp01((value - 40f) / 120f);

        // Taking a finished dish off a cooking station (vanilla raises Cooking
        // here): the dish vanilla is about to hand you, i.e. the first done
        // slot. Burnt food and putting raw food on earn no food bonus.
        private static readonly string[] SlotKeys = { "slot0", "slot1", "slot2", "slot3", "slot4", "slot5", "slot6", "slot7" };
        [HarmonyPatch(typeof(CookingStation), "OnInteract")]
        private static class CookingStationScope
        {
            private static void Prefix(CookingStation __instance, out float __state)
            {
                __state = cookingFood;
                cookingFood = 0f;
                ZNetView view = __instance != null ? __instance.GetComponent<ZNetView>() : null;
                if (view == null || !view.IsValid() || __instance.m_slots == null ||
                    __instance.m_conversion == null || ObjectDB.instance == null) return;
                ZDO zdo = view.GetZDO();
                string burnt = __instance.m_overCookedItem != null ? __instance.m_overCookedItem.name : null;
                for (int i = 0; i < __instance.m_slots.Length; i++)
                {
                    string name = zdo.GetString(i < SlotKeys.Length ? SlotKeys[i] : "slot" + i);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name == burnt) return; // vanilla removes this one: no bonus
                    foreach (var c in __instance.m_conversion)
                    {
                        if (c == null || c.m_to == null || c.m_to.name != name) continue;
                        cookingFood = FoodValue(c.m_to.m_itemData.m_shared);
                        return;
                    }
                }
            }
            private static void Postfix(float __state) { cookingFood = __state; }
            private static Exception Finalizer(Exception __exception, float __state) { cookingFood = __state; return __exception; }
        }

        // ---- Movement skills: real ground covered ----
        // Vanilla raises Run/Swim about once per second while moving. The
        // bonus uses straight-line progress over the last ~5 events, so
        // running in circles or into a wall earns nothing extra.
        private const int TravelWindow = 6;
        private static readonly Vector3[][] travelHistory = new Vector3[3][];
        private static readonly float[][] travelTimes = new float[3][];
        private static readonly int[] travelCount = new int[3];

        private static float TravelBonus(Player player, Skills.SkillType skill)
        {
            int slot; float speed;
            switch (skill)
            {
                case Skills.SkillType.Run: slot = 0; speed = 6f; break;    // sprint m/s
                case Skills.SkillType.Swim: slot = 1; speed = 1.8f; break;
                case Skills.SkillType.Ride: slot = 2; speed = 7f; break;
                default: return 0f;
            }
            if (player == null) return 0f;
            if (skill == Skills.SkillType.Swim && !player.HaveStamina()) return 0f;
            if (travelHistory[slot] == null)
            { travelHistory[slot] = new Vector3[TravelWindow]; travelTimes[slot] = new float[TravelWindow]; }
            Vector3[] h = travelHistory[slot]; float[] t = travelTimes[slot];
            float now = Time.time;
            Vector3 pos = player.transform.position;
            // A gap in movement (stopped, teleported) restarts the window.
            if (travelCount[slot] > 0)
            {
                int last = (travelCount[slot] - 1) % TravelWindow;
                if (now - t[last] > 4f || Vector3.Distance(pos, h[last]) > speed * 6f) travelCount[slot] = 0;
            }
            int i = travelCount[slot] % TravelWindow;
            h[i] = pos; t[i] = now; travelCount[slot]++;
            if (travelCount[slot] < 3) return 0f;
            int oldest = travelCount[slot] >= TravelWindow ? travelCount[slot] % TravelWindow : 0;
            float seconds = Mathf.Max(0.5f, now - t[oldest]);
            Vector2 a = new Vector2(h[oldest].x, h[oldest].z), b = new Vector2(pos.x, pos.z);
            float progress = Vector2.Distance(a, b) / (seconds * speed);
            // Up to +40% at full speed in a steady direction.
            return Plugin.XpTravelBonus.Value / 100f * Mathf.Clamp01((progress - 0.25f) / 0.75f);
        }

        // Both the skill-technique and dodge systems use the same short-lived
        // combat context. One Harmony patch per attack type, not two.
        internal static Character DodgeAttacker;

        private struct AttackState
        {
            internal Attack PreviousAttack;
            internal Character PreviousDodgeAttacker;
            internal bool PreviousConnected;
        }

        private static AttackState EnterAttack(Attack attack)
        {
            var state = new AttackState
            {
                PreviousAttack = activeAttack,
                PreviousDodgeAttacker = DodgeAttacker,
                PreviousConnected = attackConnected
            };
            activeAttack = attack;
            attackConnected = false;
            DodgeAttacker = AttackOwner(attack);
            return state;
        }

        private static void LeaveAttack(AttackState state)
        {
            // A chain step that hit nothing ends the combo.
            if (activeAttack != null && !attackConnected && activeAttack.m_attackChainLevels > 1)
            { comboStreak = 0; lastComboIndex = -1; }
            attackConnected = state.PreviousConnected;
            activeAttack = state.PreviousAttack;
            DodgeAttacker = state.PreviousDodgeAttacker;
        }

        [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
        private static class MeleeScope
        {
            [HarmonyPrefix]
            private static void Prefix(Attack __instance, out AttackState __state)
                => __state = EnterAttack(__instance);
            [HarmonyPostfix]
            private static void Postfix(AttackState __state) => LeaveAttack(__state);
            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, AttackState __state)
            { LeaveAttack(__state); return __exception; }
        }

        [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
        private static class AreaScope
        {
            [HarmonyPrefix]
            private static void Prefix(Attack __instance, out AttackState __state)
                => __state = EnterAttack(__instance);
            [HarmonyPostfix]
            private static void Postfix(AttackState __state) => LeaveAttack(__state);
            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, AttackState __state)
            { LeaveAttack(__state); return __exception; }
        }
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class CraftingScope
        {
            private struct Prev { internal Recipe Recipe; internal float Modifier; internal float Food; }
            private static void Prefix(InventoryGui __instance, ref Prev __state)
            {
                __state = new Prev { Recipe = activeRecipe, Modifier = craftModifier, Food = cookingFood };
                activeRecipe = CraftRecipe(__instance);
                cookingFood = activeRecipe != null && activeRecipe.m_item != null
                    ? FoodValue(activeRecipe.m_item.m_itemData.m_shared) : 0f;
                craftModifier = activeRecipe == null ? 0f :
                    Mathf.Clamp(IngredientTypes(activeRecipe.m_resources) * Plugin.XpRecipeIngredient.Value / 100f, 0f, 0.12f);
                if (activeRecipe != null &&
                    UpgradeItem(__instance) != null)
                    craftModifier += 0.035f;
            }
            private static void Postfix(Prev __state) { activeRecipe = __state.Recipe; craftModifier = __state.Modifier; cookingFood = __state.Food; }
            private static Exception Finalizer(Exception __exception, Prev __state)
            { activeRecipe = __state.Recipe; craftModifier = __state.Modifier; cookingFood = __state.Food; return __exception; }
        }
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        private static class FarmingHarvestScope
        {
            private static void Prefix(Pickable __instance, ref float __state)
            {
                __state = forageModifier; forageModifier = 0f;
                if (__instance.m_pickRaiseSkill != Skills.SkillType.Farming || __instance.m_itemPrefab == null) return;
                int biome = BiomeCatalog.FromBiome(Heightmap.FindBiome(__instance.transform.position));
                int tier = ForageRules.HomeTier(ForageRules.FromItem(__instance.m_itemPrefab));
                if (tier >= 0 && biome >= tier)
                    forageModifier = Math.Min(0.10f, tier * 0.015f + (biome > tier ? 0.025f : 0f));
            }
            private static void Postfix(float __state) { forageModifier = __state; }
            private static Exception Finalizer(Exception __exception, float __state)
            { forageModifier = __state; return __exception; }
        }
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class BuildSuccess
        {
            private static void Postfix(Player __instance, Piece piece, bool __result)
            {
                if (__instance != Player.m_localPlayer || !__result || piece == null || Plugin.Enabled == null || !Plugin.Enabled.Value || !Plugin.XpEnabled.Value || !Plugin.XpChallenge.Value) return;
                // Consumed only on the immediately following skill award.
                building = true; buildTime = Time.time;
                buildModifier = Mathf.Min(0.12f, IngredientTypes(piece.m_resources) * Plugin.XpRecipeIngredient.Value / 100f);
            }
        }
        internal static float ConsumeBuildBonus(Skills.SkillType skill)
        {
            if ((skill != Skills.SkillType.Crafting && skill != Skills.SkillType.Farming) || !building) return 0f;
            building = false;
            float v = Time.time - buildTime > 0.25f ? 0f : buildModifier;
            buildModifier = 0f;
            return v;
        }
    }
}
