using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace WiresCombatFlow
{
    // All input and animation improvements stay local to the player.
    // Valheim still chooses when an attack may actually hit or chain.
    internal static class FlowEnhancements
    {
        private sealed class InputState
        {
            internal float JumpBufferUntil = -100f;
            internal float SprintBufferUntil = -100f;
            internal bool WasRunning;
            internal Animator Animator;
            internal int[] ReturnStateHashes;
        }

        private static ConditionalWeakTable<Player, InputState> States =
            new ConditionalWeakTable<Player, InputState>();

        internal static void Reset()
        {
            States = new ConditionalWeakTable<Player, InputState>();
        }

        internal static void OnAttackStarted(Player player)
        {
            if (!CombatFlow.IsLocal(player))
                return;

            // The animator still contains the pre-attack state when
            // Humanoid.StartAttack sets its trigger.
            CaptureNeutralAnimation(player);
        }

        internal static void CaptureNeutralAnimation(Player player)
        {
            if (!CombatFlow.IsLocal(player) || !Plugin.Enabled.Value
                || player.IsDead() || player.IsStaggering()
                || player.InDodge())
                return;

            InputState state = States.GetOrCreateValue(player);
            Animator animator = state.Animator;
            if (animator == null)
                state.Animator = animator =
                    player.GetComponentInChildren<Animator>(true);
            if (animator == null)
                return;

            // Never store an attacking/dodge state as the blend target.
            // Preserve each layer's last actual non-combat state.
            if (player.InAttack())
                return;

            if (state.ReturnStateHashes == null
                || state.ReturnStateHashes.Length != animator.layerCount)
                state.ReturnStateHashes = new int[animator.layerCount];

            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.IsInTransition(i))
                    continue;

                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(i);
                if (info.fullPathHash != 0
                    && !info.IsTag("attack")
                    && !info.IsTag("dodge")
                    && !info.IsTag("stagger"))
                    state.ReturnStateHashes[i] = info.fullPathHash;
            }
        }

        internal static void OnCancelled(
            Player player, DefensiveCancel kind)
        {
            if (!CombatFlow.IsLocal(player))
                return;

            InputState state = States.GetOrCreateValue(player);
            Animator animator = state.Animator;
            if (animator == null)
                animator = state.Animator =
                    player.GetComponentInChildren<Animator>(true);
            int[] returnStates = state.ReturnStateHashes;
            if (animator == null || returnStates == null)
                return;

            // Crossfade to a real previously visited neutral state,
            // not a guessed Animator Controller state name. Defensive
            // triggers (block / dodge / jump) stay owned by vanilla.
            float blend = Plugin.CancelBlendTime.Value;
            int count = Mathf.Min(animator.layerCount, returnStates.Length);
            for (int layer = 0; layer < count; layer++)
            {
                int hash = returnStates[layer];
                if (hash == 0 || !animator.HasState(layer, hash))
                    continue;

                AnimatorStateInfo current =
                    animator.GetCurrentAnimatorStateInfo(layer);
                AnimatorStateInfo next = animator.IsInTransition(layer)
                    ? animator.GetNextAnimatorStateInfo(layer) : current;
                if (current.IsTag("attack") || next.IsTag("attack"))
                    animator.CrossFadeInFixedTime(hash, blend, layer);
            }

            state.JumpBufferUntil = -100f;
            state.SprintBufferUntil = -100f;
        }

        internal static void BeforeControls(
            Player player, Vector3 movedir,
            bool blockHold, ref bool jump, bool run,
            bool dodge)
        {
            if (!CombatFlow.IsLocal(player) || !Plugin.Enabled.Value)
                return;

            InputState state = States.GetOrCreateValue(player);
            if (InventoryGui.IsVisible() || Menu.IsVisible()
                || Console.IsVisible() || Hud.InRadial())
            {
                state.JumpBufferUntil = -100f;
                state.SprintBufferUntil = -100f;
                state.WasRunning = false;
                return;
            }

            float now = Time.time;
            bool moving = movedir.sqrMagnitude > 0.04f;
            bool sprintEdge = run && !state.WasRunning && moving;
            state.WasRunning = run;

            bool windup = CombatFlow.IsWindup(player);
            bool recovery = CombatFlow.IsCommittedRecovery(player);
            float buffer = Plugin.InputBufferDuration.Value;

            // Keep an early jump through the brief hit follow-through.
            // Do not turn a block-jump, crouch-jump or dodge into a jump.
            float elapsed = recovery
                ? CombatFlow.RecoveryElapsed(player) : -1f;
            bool followThrough = recovery
                && elapsed < Plugin.RecoveryCancelDelay.Value;
            if (jump && (windup || followThrough)
                && !dodge && !blockHold
                && !player.IsCrouching())
            {
                float wait = followThrough
                    ? Plugin.RecoveryCancelDelay.Value - elapsed + 0.03f
                    : 0f;
                state.JumpBufferUntil =
                    now + Mathf.Max(buffer, wait);
            }
            else if (jump)
                state.JumpBufferUntil = -100f;

            if (!jump && state.JumpBufferUntil >= now
                && recovery && player.IsOnGround()
                && CombatFlow.RecoveryElapsed(player)
                   >= Plugin.RecoveryCancelDelay.Value)
            {
                jump = true;
                state.JumpBufferUntil = -100f;
            }
            else if (state.JumpBufferUntil < now)
                state.JumpBufferUntil = -100f;

            if (sprintEdge && (windup || recovery))
            {
                float wait = followThrough
                    ? Plugin.RecoveryCancelDelay.Value - elapsed + 0.03f
                    : 0f;
                state.SprintBufferUntil =
                    now + Mathf.Max(buffer, wait);
            }

            if (moving && run && recovery
                && !dodge && !blockHold
                && state.SprintBufferUntil >= now
                && CombatFlow.RecoveryElapsed(player)
                   >= Plugin.RecoveryCancelDelay.Value)
            {
                CombatFlow.BeforeSprint(player);
                state.SprintBufferUntil = -100f;
            }
            else if (!run || !moving
                || state.SprintBufferUntil < now)
                state.SprintBufferUntil = -100f;
        }

        internal static void SmoothMovement(
            Player player, ref float movementFactor, bool turning)
        {
            if (!CombatFlow.IsLocal(player) || !Plugin.Enabled.Value)
                return;

            float easeTime = Mathf.Max(0.45f,
                Plugin.MovementRecoveryTime.Value);

            // Sprint recovery cancels exit the attack immediately, but
            // release movement over time rather than instantly sprinting
            // at full speed.
            float sprintElapsed = CombatFlow.SprintReleaseElapsed(player);
            if (!turning && sprintElapsed >= 0f && player.IsRunning())
            {
                float t = Mathf.Clamp01(sprintElapsed / easeTime);
                t = t * t * (3f - 2f * t);
                float sprintCap = Mathf.Lerp(0.24f, 1f, t);
                movementFactor = Mathf.Min(movementFactor, sprintCap);
                return;
            }

            float elapsed = CombatFlow.RecoveryElapsed(player);
            float delay = Plugin.RecoveryCancelDelay.Value;
            if (elapsed <= delay || movementFactor >= 1f)
                return;

            // Preserve the individual weapon's attack slowdown.
            // Easing stays subtle, especially for heavy weapons.
            float fraction = Mathf.Clamp01(
                (elapsed - delay) / easeTime);
            fraction = fraction * fraction * (3f - 2f * fraction);

            if (turning)
                movementFactor = Mathf.Min(1f,
                    movementFactor + (1f - movementFactor)
                    * 0.35f * fraction);
            else
                movementFactor = Mathf.Min(1f,
                    movementFactor * (1f + 0.35f * fraction));
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class InputBufferPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance,
            Vector3 movedir, bool blockHold,
            ref bool jump, bool run, bool dodge)
        {
            FlowEnhancements.BeforeControls(
                __instance, movedir, blockHold,
                ref jump, run, dodge);
        }
    }

    [HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
    internal static class NeutralAnimationCapturePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (Player.m_localPlayer != null)
                FlowEnhancements.CaptureNeutralAnimation(
                    Player.m_localPlayer);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "GetAttackSpeedFactorMovement")]
    internal static class MovementRecoveryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance, ref float __result)
        {
            Player player = __instance as Player;
            if (player != null)
                FlowEnhancements.SmoothMovement(player, ref __result, false);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "GetAttackSpeedFactorRotation")]
    internal static class RotationRecoveryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance, ref float __result)
        {
            Player player = __instance as Player;
            if (player != null)
                FlowEnhancements.SmoothMovement(player, ref __result, true);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnWeaponTrailStart))]
    internal static class CancelledTrailGuardPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Humanoid __instance)
        {
            Player player = __instance as Player;
            return player == null
                || !CombatFlow.BlocksAttackTrigger(player);
        }
    }

    // A delayed animation event can arrive after recovery cancellation.
    // Guard Attack itself as well as Humanoid.OnAttackTrigger.
    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    internal static class CancelledHitGuardPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Attack __instance)
        {
            return !CombatFlow.BlocksCancelledAttack(__instance);
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class ComboGracePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Attack __instance, Humanoid character,
            Attack previousAttack, ref float timeSinceLastAttack)
        {
            if (!(character is Player player)
                || !CombatFlow.IsLocal(player)
                || !Plugin.Enabled.Value
                || __instance.m_attackChainLevels <= 1
                || previousAttack == null
                || previousAttack.m_attackAnimation
                    != __instance.m_attackAnimation)
                return;

            // Attack.Start resets combo after 0.2s. Grant a little
            // extra recovery grace without permitting earlier hits.
            float grace = Plugin.ComboGrace.Value;
            if (timeSinceLastAttack > 0.2f
                && timeSinceLastAttack <= 0.2f + grace)
                timeSinceLastAttack = 0.199f;
        }
    }
}
