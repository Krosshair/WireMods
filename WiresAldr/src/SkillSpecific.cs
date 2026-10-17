using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Event-based, bounded runtime state. No Update hooks, scene scanning or UI.
    internal static class SkillSpecific
    {
        [ThreadStatic] private static Attack activeAttack;
        
        [ThreadStatic] private static Recipe activeRecipe;
        [ThreadStatic] private static bool upgradingRecipe;
        [ThreadStatic] private static float craftModifier;
        [ThreadStatic] private static float forageModifier;
        [ThreadStatic] private static bool building;
        private static float lastDefense = -999f;
        [ThreadStatic] private static float resourceBonus;
        private static float buildTime;
        private static readonly AccessTools.FieldRef<Attack,int> ChainIndex = AccessTools.FieldRefAccess<Attack,int>("m_currentAttackCainLevel");
        private static readonly AccessTools.FieldRef<Humanoid,bool> Secondary = AccessTools.FieldRefAccess<Humanoid,bool>("m_currentAttackIsSecondary");
        private static readonly AccessTools.FieldRef<Attack,Humanoid> AttackOwner = AccessTools.FieldRefAccess<Attack,Humanoid>("m_character");
        private static readonly AccessTools.FieldRef<InventoryGui,Recipe> CraftRecipe = AccessTools.FieldRefAccess<InventoryGui,Recipe>("m_craftRecipe");
        private static readonly AccessTools.FieldRef<InventoryGui,ItemDrop.ItemData> UpgradeItem = AccessTools.FieldRefAccess<InventoryGui,ItemDrop.ItemData>("m_craftUpgradeItem");
        private static readonly Dictionary<long, JumpRecord> jumps = new Dictionary<long, JumpRecord>();
        private static readonly HashSet<int> jumpLandings = new HashSet<int>();
        private static readonly HashSet<int> registeredLand = new HashSet<int>();
        private struct JumpRecord { internal Vector3 From; internal float Started; }
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
        [ThreadStatic] private static bool attackConnected;
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
                extra += Mathf.Min(3, comboStreak - 1) * 0.12f;
            if (Time.time - lastDefense < 2.25f)
            {
                extra += 0.06f; // one successful counter, not each hit of a combo
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
            if (activeRecipe != null &&
                (skill == Skills.SkillType.Crafting || skill == Skills.SkillType.Cooking))
            {
                float familiarity = 0f;
                // Valheim already maintains persistent per-item craft counts
                // on the character's PlayerProfile. Its count is incremented
                // AFTER the crafting-skill XP award, so it is the precise
                // previous-craft count while this method runs.
                // Don't give a new-item bonus if the profile isn't ready.
                // Upgrades have their own complexity modifier. Their
                // existing vanilla craft count must not be treated as
                // first-craft familiarity on every quality increase.
                if (!upgradingRecipe && player != null && activeRecipe.m_item != null &&
                    activeRecipe.m_item.m_itemData != null &&
                    activeRecipe.m_item.m_itemData.m_shared != null &&
                    Game.instance != null)
                {
                    PlayerProfile profile = Game.instance.GetPlayerProfile();
                    if (profile != null && profile.m_playerStats != null &&
                        profile.m_playerStats.Length > 0 &&
                        profile.m_playerStats[0] != null &&
                        profile.m_playerStats[0].m_itemCraftStats != null)
                    {
                        float previousCount;
                        string itemName =
                            activeRecipe.m_item.m_itemData.m_shared.m_name;
                        // Same key as vanilla InventoryGui.DoCrafting ->
                        // PlayerProfile.IncrementStatItemCraft.
                        profile.m_playerStats[0].m_itemCraftStats
                            .TryGetValue(itemName, out previousCount);
                        if (previousCount < 3f)
                        {
                            int prior = Mathf.Max(0,
                                Mathf.FloorToInt(previousCount));
                            familiarity = 0f; // first crafts are rewarded by CraftingProgression
                            if (AldrDiagnostics.Active)
                                AldrDiagnostics.Sample("RECIPE_XP",
                                    "recipe=" +
                                    activeRecipe.m_item.gameObject.name +
                                    " prior_times_crafted=" + prior +
                                    " familiarity_bonus=" +
                                    familiarity.ToString("0.##"));
                        }
                    }
                }                return craftModifier + familiarity;
            }
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
            return Mathf.Clamp(bonus, 0f, 0.4f);
        }
        internal static float SneakBonus(float baseFactor) => baseFactor >= 0.9f ? 0.12f : 0f;

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
            return 0.4f * Mathf.Clamp01((progress - 0.25f) / 0.75f);
        }

        // Both the skill-technique and dodge systems use the same short-lived
        // combat context. One Harmony patch per attack type, not two.
        internal static Character DodgeAttacker;

        private struct AttackState
        {
            internal Attack PreviousAttack;
            internal Character PreviousDodgeAttacker;
            internal float PreviousResourceBonus;
            internal bool PreviousConnected;
        }

        private static AttackState EnterAttack(Attack attack)
        {
            var state = new AttackState
            {
                PreviousAttack = activeAttack,
                PreviousDodgeAttacker = DodgeAttacker,
                PreviousResourceBonus = resourceBonus,
                PreviousConnected = attackConnected
            };
            activeAttack = attack;
            attackConnected = false;
            DodgeAttacker = AttackOwner(attack);
            resourceBonus = 0f;
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
            resourceBonus = state.PreviousResourceBonus;
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
            private struct Prev { internal Recipe Recipe; internal float Modifier; internal bool Upgrading; }
            private static void Prefix(InventoryGui __instance, ref Prev __state)
            {
                __state = new Prev { Recipe = activeRecipe, Modifier = craftModifier, Upgrading = upgradingRecipe };
                activeRecipe = CraftRecipe(__instance);
                upgradingRecipe = UpgradeItem(__instance) != null;
                craftModifier = activeRecipe == null ? 0f :
                    Mathf.Clamp(IngredientTypes(activeRecipe.m_resources) * 0.025f, 0f, 0.12f);
                if (activeRecipe != null &&
                    UpgradeItem(__instance) != null)
                    craftModifier += 0.035f;
            }
            private static void Postfix(Prev __state) { activeRecipe = __state.Recipe; craftModifier = __state.Modifier; upgradingRecipe = __state.Upgrading; }
            private static Exception Finalizer(Exception __exception, Prev __state)
            { activeRecipe = __state.Recipe; craftModifier = __state.Modifier; upgradingRecipe = __state.Upgrading; return __exception; }
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
                craftModifier = Mathf.Min(0.12f, IngredientTypes(piece.m_resources) * 0.025f);
            }
        }
        internal static float ConsumeBuildBonus(Skills.SkillType skill)
        {
            if ((skill != Skills.SkillType.Crafting && skill != Skills.SkillType.Farming) || !building) return 0f;
            if (Time.time - buildTime > 0.25f) { building = false; craftModifier = 0f; return 0f; }
            building = false;
            float v = craftModifier; craftModifier = 0;
            return v;
        }
        [HarmonyPatch(typeof(Player), "OnJump")]
        private static class JumpStart
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                long id = __instance.GetPlayerID();
                jumps[id] = new JumpRecord { From = __instance.transform.position, Started = Time.time };
                if (registeredLand.Add(__instance.GetInstanceID()))
                    __instance.m_onLand += point => Land(__instance, point);
            }
        }
        private static void Land(Player p, Vector3 location)
        {
            if (!p || p != Player.m_localPlayer || p.IsDead() || Plugin.Enabled == null || !Plugin.Enabled.Value || !Plugin.XpEnabled.Value || !Plugin.XpChallenge.Value) return;
            JumpRecord jump;
            if (!jumps.TryGetValue(p.GetPlayerID(), out jump)) return;
            jumps.Remove(p.GetPlayerID());
            if (Time.time - jump.Started > 2.5f || Time.time - jump.Started < 0.25f) return;
            float rise = location.y - jump.From.y;
            float horizontal = Vector2.Distance(new Vector2(location.x,location.z),
                new Vector2(jump.From.x,jump.From.z));
            if (rise < 0.8f || horizontal < 0.75f) return;
            // Each approximate landing zone only qualifies once per session;
            // repeated jumping onto the same ledge does not produce bonus XP.
            int x = Mathf.FloorToInt(location.x / 3f);
            int y = Mathf.FloorToInt(location.y / 2f);
            int z = Mathf.FloorToInt(location.z / 3f);
            int key = unchecked(x * 73856093 ^ y * 19349663 ^ z * 83492791);
            if (jumpLandings.Count >= 2048 || !jumpLandings.Add(key)) return;
            // Ledge-landing rewards were removed in 0.4.1.
        }
    }
}
