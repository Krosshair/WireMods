using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WiresCombatFlow
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.combatflow";
        public const string Name = "Wire's Combat Flow";
        public const string Version = "2.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> RecoveryCancelDelay;
        internal static ConfigEntry<float> BlockCancelCost;
        internal static ConfigEntry<float> DodgeCancelCost;
        internal static ConfigEntry<float> JumpCancelCost;
        internal static ConfigEntry<float> SecondaryExtraCost;
        internal static ConfigEntry<float> SprintCancelCost;
        internal static ConfigEntry<float> InputBufferDuration;
        internal static ConfigEntry<float> ComboGrace;
        internal static ConfigEntry<float> MovementRecoveryTime;
        internal static ConfigEntry<float> CancelBlendTime;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable responsive defensive recovery cancels.");

            RecoveryCancelDelay = Config.Bind(
                "Recovery Cancelling",
                "Delay After Hit",
                0.16f,
                new ConfigDescription(
                    "Follow-through time after the attack's actual hit/projectile trigger before recovery may be cancelled.",
                    new AcceptableValueRange<float>(0f, 0.4f)));

            BlockCancelCost = Config.Bind(
                "Recovery Cancelling",
                "Block Cancel Extra Stamina",
                4f,
                new ConfigDescription(
                    "Extra stamina charged when cancelling attack recovery into block.",
                    new AcceptableValueRange<float>(0f, 40f)));

            DodgeCancelCost = Config.Bind(
                "Recovery Cancelling",
                "Dodge Cancel Extra Stamina",
                8f,
                new ConfigDescription(
                    "Extra stamina charged when cancelling attack recovery into dodge. The normal dodge stamina cost is still paid too.",
                    new AcceptableValueRange<float>(0f, 40f)));

            JumpCancelCost = Config.Bind(
                "Recovery Cancelling",
                "Jump Cancel Extra Stamina",
                6f,
                new ConfigDescription(
                    "Extra stamina charged for jumping out of committed attack recovery, in addition to the normal jump stamina.",
                    new AcceptableValueRange<float>(0f, 40f)));

            SecondaryExtraCost = Config.Bind(
                "Recovery Cancelling",
                "Secondary Attack Extra Stamina",
                4f,
                new ConfigDescription(
                    "Additional cancel surcharge after a secondary/special attack.",
                    new AcceptableValueRange<float>(0f, 40f)));

            SprintCancelCost = Config.Bind("Recovery Cancelling",
                "Sprint Cancel Extra Stamina", 6f,
                new ConfigDescription("Extra stamina for sprinting out of recovery.",
                    new AcceptableValueRange<float>(0f, 40f)));
            InputBufferDuration = Config.Bind("Flow", "Input Buffer Seconds",
                0.15f, new ConfigDescription("Brief grace period for early jump/sprint inputs.",
                    new AcceptableValueRange<float>(0.05f, 0.3f)));
            ComboGrace = Config.Bind("Flow", "Extra Combo Grace Seconds",
                0.12f, new ConfigDescription(
                    "Extra time for continuing the same melee combo after the previous attack ends.",
                    new AcceptableValueRange<float>(0f, 0.3f)));
            MovementRecoveryTime = Config.Bind("Flow",
                "Movement Recovery Seconds", 0.50f,
                new ConfigDescription(
                    "Duration of gradual post-hit movement and turning easing.",
                    new AcceptableValueRange<float>(0.25f, 1.0f)));
            CancelBlendTime = Config.Bind("Flow",
                "Cancel Animation Blend Seconds", 0.20f,
                new ConfigDescription(
                    "Smooth transition duration when leaving a cancelled attack.",
                    new AcceptableValueRange<float>(0.08f, 0.35f)));

            AutoVerticalAim.Bind(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            CombatFlow.Reset();

            if (_harmony != null)
                _harmony.UnpatchSelf();

            Log = null;
        }
    }

    internal enum DefensiveCancel
    {
        None,
        Block,
        Dodge,
        Jump,
        Sprint
    }

    internal static class CombatFlow
    {
        private sealed class State
        {
            internal Attack Attack;
            internal bool Secondary;
            internal bool Committed;
            internal float CommitTime;
            internal bool SuppressAttack;
            internal Attack CancelledAttack;
            internal float SuppressUntil;
            internal float SprintCancelledAt = -100f;
            internal DefensiveCancel Cancel;
        }

        private static ConditionalWeakTable<Player, State> States =
            new ConditionalWeakTable<Player, State>();

        private static readonly AccessTools.FieldRef<Humanoid, Attack>
            CurrentAttack =
                AccessTools.FieldRefAccess<Humanoid, Attack>(
                    "m_currentAttack");

        private static readonly AccessTools.FieldRef<Humanoid, bool>
            CurrentAttackIsSecondary =
                AccessTools.FieldRefAccess<Humanoid, bool>(
                    "m_currentAttackIsSecondary");

        private static readonly AccessTools.FieldRef<Player, float>
            QueuedDodgeTimer =
                AccessTools.FieldRefAccess<Player, float>(
                    "m_queuedDodgeTimer");

        private static readonly AccessTools.FieldRef<Player, float>
            QueuedAttackTimer =
                AccessTools.FieldRefAccess<Player, float>(
                    "m_queuedAttackTimer");

        private static readonly AccessTools.FieldRef<Player, float>
            QueuedSecondAttackTimer =
                AccessTools.FieldRefAccess<Player, float>(
                    "m_queuedSecondAttackTimer");

        private static readonly AccessTools.FieldRef<Character, bool>
            Blocking =
                AccessTools.FieldRefAccess<Character, bool>(
                    "m_blocking");

        private static readonly MethodInfo GetDodgeStaminaUseMethod =
            AccessTools.Method(
                typeof(Player),
                "GetDodgeStaminaUse");

        internal static bool IsLocal(Player player)
        {
            return player != null &&
                   player == Player.m_localPlayer;
        }

        internal static void AttackStarted(Player player)
        {
            if (!IsLocal(player))
                return;

            State state =
                States.GetOrCreateValue(player);

            state.Attack =
                CurrentAttack(player);
            state.Secondary =
                CurrentAttackIsSecondary(player);
            state.Committed = false;
            state.CommitTime = 0f;
            state.SuppressAttack = false;
            state.CancelledAttack = null;
            state.SuppressUntil = 0f;
            state.SprintCancelledAt = -100f;
            state.Cancel = DefensiveCancel.None;
            FlowEnhancements.OnAttackStarted(player);
        }

        internal static void AttackTriggered(Player player)
        {
            if (!IsLocal(player))
                return;

            Attack current =
                CurrentAttack(player);

            if (current == null ||
                current.m_attackType == Attack.AttackType.None)
            {
                return;
            }

            State state =
                States.GetOrCreateValue(player);

            if (state.Attack != current)
            {
                state.Attack = current;
                state.Secondary =
                    CurrentAttackIsSecondary(player);
                state.Committed = false;
                state.SuppressAttack = false;
                state.CancelledAttack = null;
                state.Cancel = DefensiveCancel.None;
            }

            if (!state.Committed)
            {
                state.Committed = true;
                state.CommitTime = Time.time;
            }
        }

        internal static void BeforeDodge(Player player)
        {
            if (!IsLocal(player) ||
                Plugin.Enabled == null ||
                !Plugin.Enabled.Value ||
                QueuedDodgeTimer(player) <= 0f)
            {
                return;
            }

            TryCancel(
                player,
                DefensiveCancel.Dodge);
        }

        internal static void BeforeJump(Player player, bool force)
        {
            // Let Valheim perform the actual jump. Never force airborne
            // movement, trigger a second jump or bypass its restrictions.
            if (!IsLocal(player) || force
                || Plugin.Enabled == null || !Plugin.Enabled.Value
                || QueuedDodgeTimer(player) > 0f
                || !player.IsOnGround() || player.IsEncumbered()
                || player.IsDead() || player.InDodge()
                || player.IsKnockedBack() || player.IsStaggering()
                || player.IsAttached())
                return;

            TryCancel(player, DefensiveCancel.Jump);
        }

        internal static void BeforeBlock(Player player)
        {
            if (!IsLocal(player) ||
                Plugin.Enabled == null ||
                !Plugin.Enabled.Value)
            {
                return;
            }

            // Defensive inputs are intentionally ordered:
            // a queued dodge wins over a simultaneously held block.
            if (QueuedDodgeTimer(player) > 0f)
                return;

            if (!Blocking(player))
                return;

            TryCancel(
                player,
                DefensiveCancel.Block);
        }

        internal static bool IsWindup(Player player)
        {
            if (!IsLocal(player) || !Plugin.Enabled.Value)
                return false;
            State state;
            return States.TryGetValue(player, out state)
                && state.Attack != null
                && !state.Attack.IsDone()
                && !state.Committed
                && !state.SuppressAttack
                && ReferenceEquals(CurrentAttack(player), state.Attack);
        }

        internal static bool IsCommittedRecovery(Player player)
        {
            if (!IsLocal(player) || !Plugin.Enabled.Value)
                return false;
            State state;
            return States.TryGetValue(player, out state)
                && state.Committed
                && state.Attack != null
                && !state.Attack.IsDone()
                && !state.SuppressAttack
                && ReferenceEquals(CurrentAttack(player), state.Attack);
        }

        internal static float RecoveryElapsed(Player player)
        {
            State state;
            return IsCommittedRecovery(player)
                && States.TryGetValue(player, out state)
                ? Mathf.Max(0f, Time.time - state.CommitTime) : -1f;
        }

        internal static float SprintReleaseElapsed(Player player)
        {
            if (!IsLocal(player))
                return -1f;
            State state;
            if (!States.TryGetValue(player, out state))
                return -1f;

            float elapsed = Time.time - state.SprintCancelledAt;
            return elapsed >= 0f
                && elapsed <= Mathf.Max(0.45f, Plugin.MovementRecoveryTime.Value)
                ? elapsed : -1f;
        }

        internal static bool BlocksAttackTrigger(Player player)
        {
            if (!IsLocal(player))
                return false;
            State state;
            return States.TryGetValue(player, out state)
                && state.CancelledAttack != null
                && ReferenceEquals(CurrentAttack(player), state.CancelledAttack);
        }

        internal static bool BlocksCancelledAttack(Attack attack)
        {
            Player player = Player.m_localPlayer;
            if (player == null || attack == null)
                return false;
            State state;
            return States.TryGetValue(player, out state)
                && ReferenceEquals(state.CancelledAttack, attack);
        }

        internal static void BeforeSprint(Player player)
        {
            if (IsCommittedRecovery(player)
                && QueuedDodgeTimer(player) <= 0f
                && !Blocking(player))
                TryCancel(player, DefensiveCancel.Sprint);
        }

        private static void TryCancel(
            Player player,
            DefensiveCancel cancel)
        {
            State state =
                States.GetOrCreateValue(player);

            Attack current =
                CurrentAttack(player);

            if (state.SuppressAttack ||
                current == null ||
                !ReferenceEquals(current, state.Attack) ||
                current.IsDone() ||
                current.m_attackType == Attack.AttackType.None ||
                current.m_attackKillsSelf ||
                player.InPlaceMode() ||
                player.IsDead() ||
                player.InDodge() ||
                player.IsStaggering() ||
                player.IsKnockedBack())
                return;

            // An attack must already have triggered its hit/projectile
            // before any recovery cancellation becomes available.
            if (!state.Committed ||
                Time.time - state.CommitTime
                    < Plugin.RecoveryCancelDelay.Value)
                return;

            float extraCost;
            switch (cancel)
            {
                case DefensiveCancel.Dodge:
                    extraCost = Plugin.DodgeCancelCost.Value;
                    break;
                case DefensiveCancel.Jump:
                    extraCost = Plugin.JumpCancelCost.Value;
                    break;
                case DefensiveCancel.Sprint:
                    extraCost = Plugin.SprintCancelCost.Value;
                    break;
                default:
                    extraCost = Plugin.BlockCancelCost.Value;
                    break;
            }

            if (state.Secondary)
                extraCost +=
                    Plugin.SecondaryExtraCost.Value;

            if (cancel == DefensiveCancel.Dodge)
            {
                float normalDodgeCost =
                    GetNormalDodgeCost(player);

                if (!player.HaveStamina(
                        normalDodgeCost +
                        extraCost +
                        0.01f))
                {
                    return;
                }
            }
            else if (cancel == DefensiveCancel.Jump)
            {
                // Valheim's Character.Jump first checks its standard
                // jump cost. Ensure enough stamina remains for that
                // check after the recovery-cancel surcharge is paid.
                if (!player.HaveStamina(
                        player.m_jumpStaminaUsage +
                        extraCost + 0.01f))
                    return;
            }
            else if (!player.HaveStamina(
                         extraCost +
                         0.01f))
            {
                return;
            }

            if (extraCost > 0f)
                player.UseStamina(extraCost);

            // Stop the gameplay-side attack immediately. The InAttack patch
            // below then suppresses the remainder of the attack-tagged
            // animator recovery until the animator actually leaves it.
            current.Stop();

            // A defensive cancel wins over any offensive input that was
            // buffered before it. Holding attack can still start a fresh
            // attack later, after the cancelled recovery has actually left
            // the animator.
            QueuedAttackTimer(player) = 0f;
            QueuedSecondAttackTimer(player) = 0f;

            state.SuppressAttack = true;
            state.CancelledAttack = current;
            state.SuppressUntil = Time.time
                + Plugin.CancelBlendTime.Value + 0.03f;
            state.SprintCancelledAt = cancel == DefensiveCancel.Sprint
                ? Time.time : -100f;
            state.Cancel = cancel;
            FlowEnhancements.OnCancelled(player, cancel);
        }

        private static float GetNormalDodgeCost(
            Player player)
        {
            if (GetDodgeStaminaUseMethod == null)
                return player.m_dodgeStaminaUsage;

            object value =
                GetDodgeStaminaUseMethod.Invoke(
                    player,
                    null);

            return value is float
                ? (float)value
                : player.m_dodgeStaminaUsage;
        }

        internal static void OverrideInAttack(
            Player player,
            ref bool result)
        {
            if (!IsLocal(player))
                return;

            State state;
            if (!States.TryGetValue(
                    player,
                    out state) ||
                !state.SuppressAttack)
            {
                return;
            }

            if (!result && Time.time >= state.SuppressUntil)
            {
                state.SuppressAttack = false;
                state.Cancel = DefensiveCancel.None;
                return;
            }

            result = false;
        }

        internal static bool ShouldRejectNewAttack(
            Player player)
        {
            if (!IsLocal(player))
                return false;

            State state;
            return States.TryGetValue(
                       player,
                       out state) &&
                   state.SuppressAttack;
        }

        internal static void Reset()
        {
            States = new ConditionalWeakTable<Player, State>();
            FlowEnhancements.Reset();
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class StartAttackPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Humanoid __instance,
            ref bool __result)
        {
            Player player =
                __instance as Player;

            if (player != null &&
                CombatFlow.ShouldRejectNewAttack(
                    player))
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(
            Humanoid __instance,
            bool __result)
        {
            if (!__result)
                return;

            Player player =
                __instance as Player;

            if (player != null)
                CombatFlow.AttackStarted(player);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
    internal static class AttackTriggerPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Humanoid __instance)
        {
            Player player = __instance as Player;
            return player == null || !CombatFlow.BlocksAttackTrigger(player);
        }

        [HarmonyPostfix]
        private static void Postfix(
            Humanoid __instance)
        {
            Player player =
                __instance as Player;

            if (player != null &&
                !CombatFlow.BlocksAttackTrigger(player))
                CombatFlow.AttackTriggered(player);
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateDodge")]
    internal static class UpdateDodgePatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Player __instance)
        {
            CombatFlow.BeforeDodge(
                __instance);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "UpdateBlock")]
    internal static class UpdateBlockPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Humanoid __instance)
        {
            Player player =
                __instance as Player;

            if (player != null)
                CombatFlow.BeforeBlock(player);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
    internal static class JumpCancelPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, bool force)
        {
            Player player = __instance as Player;
            if (player != null)
                CombatFlow.BeforeJump(player, force);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.InAttack))]
    internal static class PlayerInAttackPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Player __instance,
            ref bool __result)
        {
            CombatFlow.OverrideInAttack(
                __instance,
                ref __result);
        }
    }
}
