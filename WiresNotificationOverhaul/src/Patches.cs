using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    internal static class PotionEffectTracker
    {
        private sealed class TrackedPotion
        {
            internal string ItemNameToken;
            internal Sprite Icon;
            internal bool IsCooldown;
        }

        internal sealed class UpdateState
        {
            internal readonly List<ActivePotion> Active =
                new List<ActivePotion>();

            internal bool Finished;
        }

        internal struct ActivePotion
        {
            internal int Hash;
            internal StatusEffect Effect;
            internal string StopMessage;
        }

        private static readonly Dictionary<int, TrackedPotion> Tracked =
            new Dictionary<int, TrackedPotion>();

        // SEMan.Update runs every frame: one state and one list, reused,
        // instead of new ones each frame while a potion is tracked.
        private static readonly UpdateState Shared = new UpdateState();
        private static readonly List<int> Stale = new List<int>();

        internal static void Track(
            Player player,
            ItemDrop.ItemData item)
        {
            if (player == null ||
                player != Player.m_localPlayer ||
                item == null ||
                item.m_shared == null ||
                item.m_shared.m_food > 0f ||
                item.m_shared.m_consumeStatusEffect == null)
                return;

            StatusEffect source =
                item.m_shared.m_consumeStatusEffect;

            int hash = source.NameHash();

            if (!player.GetSEMan().HaveStatusEffect(hash))
                return;

            Tracked[hash] =
                new TrackedPotion
                {
                    ItemNameToken = item.m_shared.m_name,
                    Icon = item.GetIcon(),
                    IsCooldown = IsCooldownMead(source, item.m_shared.m_name)
                };
        }

        internal static UpdateState BeginUpdate(
            SEMan seman)
        {
            if (!IsLocalPlayerSEMan(seman) ||
                Tracked.Count == 0)
                return null;

            UpdateState state = Shared;
            state.Active.Clear();
            state.Finished = false;
            Stale.Clear();

            foreach (KeyValuePair<int, TrackedPotion> pair in Tracked)
            {
                StatusEffect effect =
                    seman.GetStatusEffect(pair.Key);

                if (effect == null)
                {
                    Stale.Add(pair.Key);
                    continue;
                }

                state.Active.Add(
                    new ActivePotion
                    {
                        Hash = pair.Key,
                        Effect = effect,
                        StopMessage = effect.m_stopMessage
                    });

                // We replace any vanilla stop text with one consistent yellow
                // potion-expiry notice. If this effect survives the update,
                // the original stop text is restored immediately in EndUpdate.
                effect.m_stopMessage = "";
            }

            for (int i = 0; i < Stale.Count; i++)
                Tracked.Remove(Stale[i]);

            return state;
        }

        internal static void EndUpdate(
            SEMan seman,
            UpdateState state,
            bool notify)
        {
            if (state == null ||
                state.Finished)
                return;

            state.Finished = true;

            for (int i = 0; i < state.Active.Count; i++)
            {
                ActivePotion active = state.Active[i];

                bool stillActive =
                    seman != null &&
                    seman.HaveStatusEffect(active.Hash);

                if (stillActive)
                {
                    if (active.Effect != null)
                        active.Effect.m_stopMessage =
                            active.StopMessage;

                    continue;
                }

                if (notify &&
                    Tracked.TryGetValue(
                        active.Hash,
                        out TrackedPotion potion))
                {
                    // Only a timer reaching zero is an actual expiry,
                    // not a manually removed / canceled status effect.
                    if (active.Effect != null &&
                        active.Effect.m_ttl > 0f &&
                        active.Effect.GetRemaningTime() <= 0.05f)
                    {
                        NotificationSystem.ShowPotionEffectEnded(
                            potion.ItemNameToken,
                            potion.Icon,
                            potion.IsCooldown);
                    }
                }

                Tracked.Remove(active.Hash);
            }
        }

        // Immediate consumable with a reuse timer:
        // health/stamina/eitr restored UP FRONT, with no sustained
        // benefit. Resistances and lingering timed-benefit meads
        // are NOT "Available" merely because they have a timer.
        private static bool IsCooldownMead(
            StatusEffect effect, string itemNameToken)
        {
            // Common instant Valheim meads may leave a cooldown-only
            // status effect after the immediate HP/stamina/eitr gain.
            // They can therefore have NO upfront gain on that effect.
            // Use the item identity in addition to the effect data.
            string name = (itemNameToken ?? "").ToLowerInvariant();
            if (name.Contains("lingering") || name.Contains("resist") ||
                name.Contains("ratatoskr") || name.Contains("tasty"))
                return false;

            bool instantNamedMead =
                (name.Contains("mead") || name.Contains("potion")) &&
                (name.Contains("health") || name.Contains("heal") ||
                 name.Contains("stamina") || name.Contains("eitr") ||
                 name.Contains("_hp_"));

            // The game marks use cooldowns with a cooldown icon.
            if (effect.m_cooldownIcon)
                return true;

            SE_Stats stats = effect as SE_Stats;
            if (stats == null)
                return instantNamedMead;

            // Lasting benefits (resistances, regen) make it a buff. Healing
            // over a few seconds doesn't: a healing mead heals for 10 s and
            // the rest of its timer is only the wait until the next one.
            float ttl = effect.m_ttl;
            bool lasting =
                Lasts(stats.m_healthOverTime, stats.m_healthOverTimeDuration, ttl) ||
                Lasts(stats.m_staminaOverTime, stats.m_staminaOverTimeDuration, ttl) ||
                Lasts(stats.m_eitrOverTime, stats.m_eitrOverTimeDuration, ttl) ||
                stats.m_healthRegenMultiplier != 1f ||
                stats.m_staminaRegenMultiplier != 1f ||
                stats.m_eitrRegenMultiplier != 1f ||
                (stats.m_mods != null && stats.m_mods.Count > 0);

            if (lasting)
                return false;

            bool restores =
                stats.m_healthUpFront > 0f ||
                stats.m_staminaUpFront > 0f ||
                stats.m_eitrUpFront > 0f ||
                stats.m_healthOverTime > 0f ||
                stats.m_staminaOverTime > 0f ||
                stats.m_eitrOverTime > 0f;

            return restores || instantNamedMead;
        }

        // An over-time effect that runs for the whole timer (no duration
        // means the whole timer, as in the game).
        private static bool Lasts(float amount, float duration, float ttl)
        {
            return amount != 0f && (duration <= 0f || ttl <= 0f || duration >= ttl - 0.01f);
        }

        internal static void Clear()
        {
            Tracked.Clear();
        }

        private static bool IsLocalPlayerSEMan(SEMan seman)
        {
            return seman != null &&
                   Player.m_localPlayer != null &&
                   Player.m_localPlayer.GetSEMan() == seman;
        }
    }

    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
    internal static class MessageHudShowMessagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            MessageHud __instance,
            MessageHud.MessageType type,
            string text,
            int amount = 0,
            Sprite icon = null,
            bool showDespiteHiddenHUD = false)
        {
            Sprite repairedIcon;
            bool isRepair = RepairMessageCapture.TryGetRepairedIcon(
                type, text, out repairedIcon);

            // Vanilla repair notifications have no icon. Fill in only
            // the true repaired item's icon for the custom stack.
            if (isRepair && repairedIcon != null)
                icon = repairedIcon;

            return !NotificationSystem.TryHandleMessage(
                __instance,
                type,
                text,
                amount,
                icon,
                showDespiteHiddenHUD,
                isRepair);
        }
    }

    [HarmonyPatch(typeof(MessageHud), "UpdateMessage")]
    internal static class MessageHudUpdateMessagePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            MessageHud __instance,
            float dt)
        {
            NotificationSystem.Tick(
                __instance,
                dt);
        }
    }

    [HarmonyPatch(typeof(MessageHud), "HideAll")]
    internal static class MessageHudHideAllPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            NotificationSystem.ClearNotifications();
        }
    }

    [HarmonyPatch(typeof(MessageHud), "OnDestroy")]
    internal static class MessageHudOnDestroyPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            MessageHud __instance)
        {
            NotificationSystem.OnHudDestroyed(
                __instance);

            PotionEffectTracker.Clear();

        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class PlayerConsumePotionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Player __instance,
            ItemDrop.ItemData item,
            bool __result)
        {
            if (__result)
                PotionEffectTracker.Track(
                    __instance,
                    item);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Eat))]
    internal static class ItemDropEatPotionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ItemDrop __instance,
            bool __result)
        {
            if (!__result ||
                __instance == null ||
                Player.m_localPlayer == null)
                return;

            PotionEffectTracker.Track(
                Player.m_localPlayer,
                __instance.m_itemData);
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.Update))]
    internal static class SEManPotionExpiryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            SEMan __instance,
            float dt,
            out PotionEffectTracker.UpdateState __state)
        {
            __state =
                PotionEffectTracker.BeginUpdate(
                    __instance);
        }

        [HarmonyPostfix]
        private static void Postfix(
            SEMan __instance,
            PotionEffectTracker.UpdateState __state)
        {
            PotionEffectTracker.EndUpdate(
                __instance,
                __state,
                true);
        }

        [HarmonyFinalizer]
        private static System.Exception Finalizer(
            SEMan __instance,
            PotionEffectTracker.UpdateState __state,
            System.Exception __exception)
        {
            if (__exception != null)
            {
                PotionEffectTracker.EndUpdate(
                    __instance,
                    __state,
                    false);
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    internal static class SkillsRaiseSkillPatch
    {
        private static readonly System.Reflection.MethodInfo
            GetNextLevelRequirementMethod =
                AccessTools.Method(
                    typeof(Skills.Skill),
                    "GetNextLevelRequirement");
        [HarmonyPrefix]
        private static void Prefix(
            Skills __instance,
            Skills.SkillType skillType,
            float factor,
            out bool __state)
        {
            __state =
                factor > 0f &&
                skillType != Skills.SkillType.None &&
                Player.m_localPlayer != null &&
                __instance == Player.m_localPlayer.GetSkills() &&
                __instance.GetSkillLevel(skillType) < 100f;
        }

        [HarmonyPostfix]
        private static void Postfix(
            Skills __instance,
            Skills.SkillType skillType,
            float factor,
            bool __state)
        {
            bool stack = WiresNotificationOverhaulPlugin.XpGainNotifications.Value ==
                WiresNotificationOverhaulPlugin.Toggle.On;
            bool world = WiresNotificationOverhaulPlugin.InWorldXpPopups.Value ==
                WiresNotificationOverhaulPlugin.Toggle.On;
            // Running and jumping gain XP all the time just by moving.
            if (stack && (skillType == Skills.SkillType.Run || skillType == Skills.SkillType.Jump) &&
                WiresNotificationOverhaulPlugin.HideRunJumpXp.Value == WiresNotificationOverhaulPlugin.Toggle.On)
                stack = false;
            if (!__state || (!stack && !world))
                return;

            Skills.SkillDef definition = null;

            for (int i = 0;
                 i < __instance.m_skills.Count;
                 i++)
            {
                Skills.SkillDef candidate =
                    __instance.m_skills[i];

                if (candidate != null &&
                    candidate.m_skill == skillType)
                {
                    definition = candidate;
                    break;
                }
            }

            if (definition == null)
                return;

            float gained =
                definition.m_increseStep *
                factor *
                Game.m_skillGainRate;

            if (gained <= 0f)
                return;

            if (world)
                WorldXpPopups.Show(skillType, gained);
            if (!stack)
                return;

            float currentXp;
            float requiredXp;

            bool hasProgress =
                TryGetProgress(
                    __instance,
                    skillType,
                    out currentXp,
                    out requiredXp);

            NotificationSystem.ShowSkillXp(
                definition,
                gained,
                currentXp,
                requiredXp,
                hasProgress);
        }

        private static bool TryGetProgress(
            Skills skills,
            Skills.SkillType skillType,
            out float currentXp,
            out float requiredXp)
        {
            currentXp = 0f;
            requiredXp = 0f;

            if (skills == null)
                return false;

            System.Collections.Generic.List<Skills.Skill>
                skillList = skills.GetSkillList();

            for (int i = 0;
                 i < skillList.Count;
                 i++)
            {
                Skills.Skill skill = skillList[i];

                if (skill == null ||
                    skill.m_info == null ||
                    skill.m_info.m_skill != skillType ||
                    skill.m_level >= 100f)
                    continue;

                currentXp = skill.m_accumulator;

                if (GetNextLevelRequirementMethod != null)
                {
                    object result =
                        GetNextLevelRequirementMethod.Invoke(
                            skill,
                            null);

                    if (result is float)
                    {
                        requiredXp = (float)result;
                        return requiredXp > 0f;
                    }
                }

                // Exact current vanilla fallback if reflection ever fails.
                requiredXp =
                    Mathf.Pow(
                        Mathf.Floor(
                            skill.m_level + 1f),
                        1.5f) *
                    0.5f +
                    0.5f;

                return requiredXp > 0f;
            }

            return false;
        }
    }
}
