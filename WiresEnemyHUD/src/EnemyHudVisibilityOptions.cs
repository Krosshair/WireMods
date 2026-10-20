using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Isolated, opt-in additions to the exact 0.5.0 HUD implementation.
    // No changes to the layout, stagger logic or player HUD.
    public sealed partial class EnemyHudReworkPlugin
    {
        private const float DamageShowSeconds = 3f;
        // 0.8.0 kept a crit counter in each creature's saved data; it is
        // now a one-off message instead, and old counters are removed.
        private static readonly int OldCriticalCounterKey = "wire.enemyhud.critical_id".GetStableHashCode();
        private const string CriticalRpc = "WireEnemyHUD_Crit";
        private bool _reportedHitReveal;
        private bool _reportedViewportClamp;
        private bool _reportedHpFont;

        private void ReportFontIfAdjusted(TMP_Text label)
        {
            if (label == null || _reportedHpFont) return;
            float scale = _hpFontScale.Value;
            if (Mathf.Abs(scale - 1f) < .01f)
                return;
            _reportedHpFont = true;
            Logger.LogInfo(
                "Font scale applied: HP x" + scale.ToString("0.##") +
                ", actual size " + label.fontSize.ToString("0.##") +
                ", auto sizing " + label.enableAutoSizing);
        }

        private ConfigEntry<bool> _showOnDamage;
        private ConfigEntry<bool> _showOnParry;
        private ConfigEntry<bool> _keepNameplatesOnScreen;
        private ConfigEntry<bool> _criticalHitFlash;
        private ConfigEntry<float> _hpFontScale;

        private sealed class HealthSample
        {
            public float LastHp;
            public float SeenAt;
            public float ShowUntil;
            public float ParryUntil;
            public float PendingLocalHitUntil = -999f;
        }

        private sealed class HealthOutline
        {
            public float StartsAt = -999f;
            public float EndsAt = -999f;
            public float ObservedCriticalUntil = -999f;

        }

        private readonly Dictionary<Character, HealthSample> _healthSamples =
            new Dictionary<Character, HealthSample>();
        private readonly Dictionary<Character, float> _lastCriticalHits =
            new Dictionary<Character, float>();
        private readonly Dictionary<EnemyUiState, HealthOutline> _healthOutlines =
            new Dictionary<EnemyUiState, HealthOutline>();

        private static readonly AccessTools.FieldRef<Character, float> BackstabTime =
            AccessTools.FieldRefAccess<Character, float>("m_backstabTime");

        // The game's hover timer on its private per-nameplate record, read
        // through a compiled getter (no reflection while playing).
        private static readonly Func<object, float> HoverTimer = MakeHoverTimer();

        private static Func<object, float> MakeHoverTimer()
        {
            try
            {
                FieldInfo field = HudDataType != null ? AccessTools.Field(HudDataType, "m_hoverTimer") : null;
                if (field == null) return null;
                var p = System.Linq.Expressions.Expression.Parameter(typeof(object), "record");
                var body = System.Linq.Expressions.Expression.Field(
                    System.Linq.Expressions.Expression.Convert(p, HudDataType), field);
                return System.Linq.Expressions.Expression.Lambda<Func<object, float>>(body, p).Compile();
            }
            catch (Exception) { return null; }
        }

        private void ApplyVisibilityPreset(bool enabled)
        {
            _keepNameplatesOnScreen.Value = enabled;
            _showOnDamage.Value = enabled;
            _showOnParry.Value = enabled;
            _criticalHitFlash.Value = enabled;
            _weaknessIcons.Value = enabled;
            _resistanceIcons.Value = enabled;
            _bossWeaknessIcons.Value = enabled;
            _bossResistanceIcons.Value = enabled;
            _hpFontScale.Value = 1f;
            _dpsFontScale.Value = 1f;
        }

        private static bool IsOrdinaryEnemy(Character c)
        {
            return c != null && !c.IsDead() &&
                !c.IsPlayer() && !c.IsBoss() && !c.m_hideHud;
        }

        // Compare network identity directly. GetAttacker() can fail
        // to resolve a valid network ID during a short client interval.
        private static bool IsLocalPlayerHit(HitData hit)
        {
            Player local = Player.m_localPlayer;
            return hit != null && local != null &&
                (hit.m_attacker.Equals(local.GetZDOID()) ||
                 hit.GetAttacker() == local);
        }

        // Called on the ATTACKER's client before Valheim forwards
        // the hit to the target's network owner. This lets clients
        // confirm their own hits when the NPC health sync arrives.
        // Another player's damage never creates this pending marker.
        private void NoteOutgoingLocalHit(Character target, HitData hit)
        {
            if (_showOnDamage == null || !_showOnDamage.Value ||
                !IsOrdinaryEnemy(target) || hit == null ||
                !IsLocalPlayerHit(hit) ||
                hit.GetTotalDamage() <= 0f)
                return;

            float now = Time.unscaledTime;
            PruneSamples(now);
            HealthSample sample;
            if (!_healthSamples.TryGetValue(target, out sample))
            {
                sample = new HealthSample();
                _healthSamples[target] = sample;
            }

            // Base the confirmation on health visible at the moment
            // our own hit is sent, not an unrelated earlier HP loss.
            sample.LastHp = Mathf.Max(0f, target.GetHealth());
            sample.SeenAt = now;
            sample.PendingLocalHitUntil = now + 4f;
            if (!_pendingHits.Contains(target)) _pendingHits.Add(target);
        }

        // Only creatures you just hit are watched, and only until their
        // health shows the hit landed (or 4 seconds pass). Hits on
        // creatures your own game runs are confirmed on the spot instead
        // (RecordDamage). With nothing pending this does nothing.
        private readonly List<Character> _pendingHits = new List<Character>();
        private readonly List<Character> _missingSamples = new List<Character>();

        private void SampleHealthForVisibility()
        {
            if (_pendingHits.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _pendingHits.Count - 1; i >= 0; i--)
            {
                Character c = _pendingHits[i];
                HealthSample sample;
                if (c == null || !IsOrdinaryEnemy(c) ||
                    !_healthSamples.TryGetValue(c, out sample) ||
                    now > sample.PendingLocalHitUntil)
                {
                    _pendingHits.RemoveAt(i);
                    continue;
                }

                // Only a confirmed HP decrease following our own
                // outgoing attack qualifies.
                float hp = Mathf.Max(0f, c.GetHealth());
                if (hp > 0f && sample.LastHp - hp > .01f)
                {
                    sample.PendingLocalHitUntil = -999f;
                    sample.ShowUntil = now + DamageShowSeconds;
                    sample.LastHp = hp;
                    sample.SeenAt = now;
                    _pendingHits.RemoveAt(i);
                    if (!_reportedHitReveal)
                    {
                        Logger.LogInfo(
                            "Visibility: local-player hit confirmed; 3s reveal active.");
                        _reportedHitReveal = true;
                    }
                }
            }
        }

        // Drops creatures whose reveal is over, when a new one is added.
        private void PruneSamples(float now)
        {
            if (_healthSamples.Count == 0) return;
            _missingSamples.Clear();
            foreach (var entry in _healthSamples)
            {
                HealthSample v = entry.Value;
                if (entry.Key == null ||
                    (now >= v.ShowUntil && now >= v.ParryUntil && now > v.PendingLocalHitUntil))
                    _missingSamples.Add(entry.Key);
            }
            foreach (Character c in _missingSamples) _healthSamples.Remove(c);
        }

        private void OnShowOnDamageChanged()
        {
            // Turning it off forgets pending hits and damage reveals, so
            // turning it on later can't reveal an old hit.
            if (_showOnDamage.Value) return;
            _pendingHits.Clear();
            foreach (var entry in _healthSamples)
            {
                entry.Value.ShowUntil = 0f;
                entry.Value.PendingLocalHitUntil = -999f;
            }
        }

        // Shown for a while after you damage or parry it.
        private bool ForceShowOnDamage(Character c)
        {
            if (_showOnDamage == null || !IsOrdinaryEnemy(c))
                return false;
            HealthSample sample;
            if (!_healthSamples.TryGetValue(c, out sample)) return false;
            float now = Time.unscaledTime;
            return (_showOnDamage.Value && now < sample.ShowUntil) ||
                (_showOnParry.Value && now < sample.ParryUntil);
        }

        // You parried this attacker: its bar shows like after a hit.
        internal void OnLocalParry(Character attacker)
        {
            if (_showOnParry == null || !_showOnParry.Value || !IsOrdinaryEnemy(attacker))
                return;
            PruneSamples(Time.unscaledTime);
            HealthSample sample;
            if (!_healthSamples.TryGetValue(attacker, out sample))
            {
                sample = new HealthSample();
                sample.LastHp = attacker.GetHealth();
                _healthSamples[attacker] = sample;
            }
            sample.SeenAt = Time.unscaledTime;
            sample.ParryUntil = Time.unscaledTime + DamageShowSeconds;
        }

        // Real local damage, including the very first hit that
        // happens before the enemy ever had a HUD or sample.
        private void RecordDamage(Character c,
            float previousHp, float currentHp)
        {
            if (!IsOrdinaryEnemy(c) ||
                _showOnDamage == null ||
                !_showOnDamage.Value ||
                currentHp <= 0f ||
                currentHp >= previousHp - .01f)
                return;

            PruneSamples(Time.unscaledTime);
            HealthSample sample;
            if (!_healthSamples.TryGetValue(c, out sample))
            {
                sample = new HealthSample();
                _healthSamples[c] = sample;
            }
            _pendingHits.Remove(c);
            sample.LastHp = Mathf.Max(0f, currentHp);
            sample.SeenAt = Time.unscaledTime;
            sample.PendingLocalHitUntil = -999f;
            sample.ShowUntil =
                Time.unscaledTime + DamageShowSeconds;
            if (!_reportedHitReveal)
            {
                Logger.LogInfo(
                    "Visibility: local-player damage confirmed; 3s reveal active.");
                _reportedHitReveal = true;
            }
        }

        private static bool HasNativeHoverVisibility(EnemyUiState state)
        {
            EnemyHud hud = EnemyHud.instance;
            return HoverTimer != null && hud != null && state.HudRecord != null &&
                HoverTimer(state.HudRecord) < hud.m_hoverShowDuration;
        }

        private void SetupCriticalOutline(EnemyUiState state)
        {
            if (state?.HealthRect == null ||
                state.Kind != HudKind.Enemy)
                return;

            _healthOutlines[state] = new HealthOutline();
        }

        private static void StartHealthFlash(HealthOutline outline)
        {
            outline.StartsAt = Time.unscaledTime;
            outline.EndsAt = outline.StartsAt +
                GetPlayerStaggerFlashDuration();
        }

        // Runs where the hit is decided (the creature's owner), which tells
        // everyone, itself included, with one small message. Players
        // without the mod ignore it. Nothing is stored.
        private void MarkCriticalHit(Character c)
        {
            if (!IsOrdinaryEnemy(c)) return; // sent even with your flash off: others may use it
            ZNetView view = c.GetComponent<ZNetView>();
            if (ZRoutedRpc.instance != null && view != null && view.IsValid())
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, CriticalRpc, view.GetZDO().m_uid);
            else
                ShowCriticalHit(c);
        }

        internal static void RegisterCriticalRpc(ZRoutedRpc rpc)
        {
            try { rpc.Register<ZDOID>(CriticalRpc, (sender, id) => Instance?.OnCriticalRpc(id)); }
            catch (ArgumentException) { } // already registered
        }

        private void OnCriticalRpc(ZDOID id)
        {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
            Character c = go != null ? go.GetComponent<Character>() : null;
            if (c != null) ShowCriticalHit(c);
        }

        private void ShowCriticalHit(Character c)
        {
            if (!_criticalHitFlash.Value || !IsOrdinaryEnemy(c) || c.GetHealth() <= 0f) return;
            float now = Time.unscaledTime;
            if (_lastCriticalHits.Count > 0)
            {
                _missingSamples.Clear();
                foreach (var entry in _lastCriticalHits)
                    if (entry.Key == null || entry.Value <= now) _missingSamples.Add(entry.Key);
                foreach (Character old in _missingSamples) _lastCriticalHits.Remove(old);
            }
            _lastCriticalHits[c] = now + GetPlayerStaggerFlashDuration();
        }

        // Old 0.8.0 counters, removed from the creature's saved data by the
        // machine that owns it.
        internal static void DropOldCriticalCounter(ZDO zdo)
        {
            if (zdo != null && zdo.GetInt(OldCriticalCounterKey) != 0) zdo.RemoveInt(OldCriticalCounterKey);
        }

        private void UpdateCriticalOutline(EnemyUiState state)
        {
            if (!_criticalHitFlash.Value)
            {
                state.CritFlashAlpha = 0f;
                return;
            }
            HealthOutline outline;
            if (!_healthOutlines.TryGetValue(state, out outline))
                return;
            bool enabled = _criticalHitFlash.Value;
            float now = Time.unscaledTime;

            if (enabled)
            {
                float localUntil;
                if (_lastCriticalHits.TryGetValue(
                        state.Character, out localUntil) &&
                    localUntil > now &&
                    localUntil > outline.ObservedCriticalUntil)
                {
                    outline.ObservedCriticalUntil = localUntil;
                    StartHealthFlash(outline);
                }
            }

            float duration = outline.EndsAt - outline.StartsAt;
            float progress = duration > .001f
                ? (now - outline.StartsAt) / duration : 1f;
            float criticalAlpha = enabled &&
                progress >= 0f && progress < 1f
                ? VanillaFlash(progress) : 0f;

            // Drawn like the game's own flash: the bar's dark frame itself
            // lights up (see ApplyFrameFlash).
            state.CritFlashAlpha = criticalAlpha;
        }

        private void DestroyCriticalOutline(EnemyUiState state)
        {
            HealthOutline outline;
            if (!_healthOutlines.TryGetValue(state, out outline))
                return;
            _healthOutlines.Remove(state);
        }

        private void UpdateViewportVisibility(
            EnemyUiState state)
        {
            if (state == null ||
                state.Gui == null ||
                state.Kind != HudKind.Enemy ||
                state.Character == null)
                return;

            bool damageReveal =
                ForceShowOnDamage(state.Character);
            // Nothing to do unless a reveal is on or bars are kept on screen.
            if (!damageReveal && !_keepNameplatesOnScreen.Value)
                return;
            bool ordinaryHover =
                HasNativeHoverVisibility(state);
            bool vanillaVisible = state.Gui.activeSelf;

            // Do not invent nameplates for enemies which would
            // not normally be shown. In particular: no always-on
            // distant troll marker or new boss HUD.
            if (!damageReveal && !ordinaryHover &&
                !vanillaVisible)
                return;

            Camera camera = Utils.GetMainCamera();
            if (camera == null)
                return;

            Vector3 projected =
                camera.WorldToScreenPointScaled(
                    state.Character.GetTopPoint());
            if (projected.z <= 0f)
            {
                // On very tall creatures the head can cross
                // *behind* the camera's near plane while the
                // actual body is still visibly in front.
                // Pin that nameplate to the top using the body's
                // screen X. Never project a creature which is
                // entirely behind us or off-screen.
                Vector3 body =
                    camera.WorldToScreenPointScaled(
                        state.Character.GetCenterPoint());
                bool visibleBody =
                    _keepNameplatesOnScreen.Value &&
                    body.z > 0f &&
                    body.x >= 0f &&
                    body.x <= Screen.width &&
                    body.y >= 0f &&
                    body.y <= Screen.height;
                if (!visibleBody)
                {
                    if (damageReveal)
                        state.Gui.SetActive(false);
                    return;
                }
                projected =
                    new Vector3(body.x,
                        Screen.height + 32f, body.z);
            }

            bool outside =
                projected.x < 0f ||
                projected.x > Screen.width ||
                projected.y < 0f ||
                projected.y > Screen.height;

            // Vanilla may hide a nameplate because its *head*
            // went outside the viewport even though the creature
            // is in front of us. The original hover timer is
            // better evidence than activeSelf in that case.
            if (damageReveal ||
                (_keepNameplatesOnScreen.Value &&
                 outside && ordinaryHover))
            {
                if (!state.Gui.activeSelf) ApplyLevelStars(state, state.Character);
                state.Gui.SetActive(true);
            }

            if (!state.Gui.activeSelf)
                return;

            if (!_keepNameplatesOnScreen.Value)
            {
                if (damageReveal)
                    state.Gui.transform.position = projected;
                return;
            }

            // Clamp the actual whole nameplate, not just its
            // health line: name, stars, stagger and numbers
            // all stay inside the viewport together.
            // Measuring the whole nameplate walks all its parts, so it's
            // only done near the screen edge, where it matters. Elsewhere
            // the last measurement says it fits with room to spare.
            Vector3 scale =
                state.Gui.transform.lossyScale;
            const float spare = 96f;
            if (state.ClampKnown)
            {
                Bounds b = state.ClampBounds;
                float sx0 = Mathf.Max(.01f, Mathf.Abs(scale.x));
                float sy0 = Mathf.Max(.01f, Mathf.Abs(scale.y));
                if (projected.x + b.min.x * sx0 > spare &&
                    projected.x + b.max.x * sx0 < Screen.width - spare &&
                    projected.y + b.min.y * sy0 > spare &&
                    projected.y + b.max.y * sy0 < Screen.height - spare)
                {
                    state.Gui.transform.position = projected;
                    return;
                }
            }
            Bounds bounds =
                RectTransformUtility
                    .CalculateRelativeRectTransformBounds(
                        state.Gui.transform,
                        state.Gui.transform);
            state.ClampBounds = bounds;
            state.ClampKnown = true;
            float sx = Mathf.Max(.01f, Mathf.Abs(scale.x));
            float sy = Mathf.Max(.01f, Mathf.Abs(scale.y));
            const float padding = 8f;

            float minX =
                padding - bounds.min.x * sx;
            float maxX =
                Screen.width - padding -
                bounds.max.x * sx;
            float minY =
                padding - bounds.min.y * sy;
            float maxY =
                Screen.height - padding -
                bounds.max.y * sy;

            if (maxX < minX)
                minX = maxX = Screen.width * .5f;
            if (maxY < minY)
                minY = maxY = Screen.height * .5f;

            float x = Mathf.Clamp(
                projected.x, minX, maxX);
            float y = Mathf.Clamp(
                projected.y, minY, maxY);

            if (!_reportedViewportClamp &&
                (Mathf.Abs(x - projected.x) > .01f ||
                 Mathf.Abs(y - projected.y) > .01f))
            {
                Logger.LogInfo(
                    "Visibility: off-screen enemy nameplate clamped to viewport.");
                _reportedViewportClamp = true;
            }

            // Still the same world-space nameplate. As the
            // troll moves or the camera pans, its projected
            // horizontal coordinate keeps moving normally.
            state.Gui.transform.position =
                new Vector3(x, y, projected.z);
        }

        // Final visibility pass after Valheim completes its native HUD.
        // Uses only confirmed local damage, not arbitrary HP loss.
        private void ApplyDamageRevealAfterHudUpdate(EnemyHud hud)
        {
            if (hud == null || _healthSamples.Count == 0 || Hud.IsUserHidden() ||
                _showOnDamage == null || (!_showOnDamage.Value && !_showOnParry.Value))
                return;

            Camera camera = Utils.GetMainCamera();
            if (camera == null)
                return;

            foreach (var pair in _healthSamples)
            {
                Character enemy = pair.Key;
                if (enemy == null || !ForceShowOnDamage(enemy) ||
                    enemy.GetHealth() <= 0f)
                    continue;

                Vector3 projected =
                    camera.WorldToScreenPointScaled(enemy.GetTopPoint());
                if (projected.z <= 0f)
                {
                    Vector3 body = camera.WorldToScreenPointScaled(
                        enemy.GetCenterPoint());
                    if (body.z <= 0f ||
                        body.x < 0f || body.x > Screen.width ||
                        body.y < 0f || body.y > Screen.height)
                        continue;
                }

                EnemyUiState state;
                if (!_states.TryGetValue(enemy, out state) ||
                    state == null || state.Gui == null)
                {
                    // A damage event may precede first HUD creation.
                    if (EnemyHudShowHudMethod == null)
                        continue;
                    EnemyHudShowHudMethod.Invoke(
                        hud, new object[] { enemy, false });
                    SetupHud(hud, enemy, false);
                    if (!_states.TryGetValue(enemy, out state) ||
                        state == null || state.Gui == null)
                        continue;
                }

                if (state.Kind != HudKind.Enemy)
                    continue;

                if (!state.Gui.activeSelf) ApplyLevelStars(state, state.Character);
                state.Gui.SetActive(true);
                UpdateViewportVisibility(state);
            }
        }

        private void DestroyVisibilityOptions()
        {
            _healthSamples.Clear();
            _pendingHits.Clear();
            _lastCriticalHits.Clear();
            _healthOutlines.Clear();
        }

        [HarmonyPatch(typeof(EnemyHud), "LateUpdate")]
        private static class WatchHealthBeforeHudCreation
        {
            private static void Prefix()
            {
                Instance?.SampleHealthForVisibility();
            }
        }

        // A parry is decided on your own machine, in your block: the timed
        // block window is read before the block runs, the same check the
        // game makes.
        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        private static class ParryVisibility
        {
            private static readonly AccessTools.FieldRef<Humanoid, float> BlockTimer =
                AccessTools.FieldRefAccess<Humanoid, float>("m_blockTimer");
            private static readonly Func<Humanoid, ItemDrop.ItemData> CurrentBlocker =
                AccessTools.MethodDelegate<Func<Humanoid, ItemDrop.ItemData>>(
                    AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker"));

            private static void Prefix(Humanoid __instance, out bool __state)
            {
                __state = false;
                if (Instance == null || !Instance._showOnParry.Value ||
                    !ReferenceEquals(__instance, Player.m_localPlayer)) return;
                ItemDrop.ItemData blocker = CurrentBlocker(__instance);
                float timer = BlockTimer(__instance);
                __state = blocker != null && blocker.m_shared.m_timedBlockBonus > 1f &&
                    timer != -1f && timer < 0.25f;
            }

            private static void Postfix(Character attacker, bool __result, bool __state)
            {
                if (__state && __result && attacker != null) Instance?.OnLocalParry(attacker);
            }
        }

        [HarmonyPatch(typeof(ZRoutedRpc), MethodType.Constructor, typeof(bool))]
        private static class RegisterRpcs
        {
            private static void Postfix(ZRoutedRpc __instance) { RegisterCriticalRpc(__instance); }
        }

        [HarmonyPatch(typeof(EnemyHud), "TestShow")]
        private static class DamageAdditionalVisibility
        {
            private static void Postfix(
                Character c, ref bool __result)
            {
                if (!__result && Instance != null &&
                    Instance._healthSamples.Count > 0 &&
                    Instance.ForceShowOnDamage(c))
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class RememberLocalOutgoingDamage
        {
            [HarmonyPrefix]
            private static void Prefix(Character __instance, HitData hit)
            {
                Instance?.NoteOutgoingLocalHit(__instance, hit);
            }
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class ActualCriticalHit
        {
            private struct HitState
            {
                internal Vector3 Snapshot;
                internal bool LocalAttacker;
                internal bool Active;
            }

            private static void Prefix(
                Character __instance, HitData hit,
                out HitState __state)
            {
                __state = default(HitState);
                if (Instance == null) return;
                float backstab =
BackstabTime(__instance);
                __state = new HitState {
                    Active = true,
                    Snapshot = new Vector3(
                        __instance.GetHealth(), backstab,
                        __instance.IsStaggering() ? 1f : 0f),
                    LocalAttacker = IsLocalPlayerHit(hit)
                };

            }

            private static void Postfix(
                Character __instance, HitState __state)
            {
                Vector3 before = __state.Snapshot;
                if (!__state.Active || Instance == null ||
                    !IsOrdinaryEnemy(__instance) ||
                    __instance.GetHealth() >= before.x - .01f)
                    return;

                // The RPC executes on the actual HP owner. Only the
                // local player's own confirmed hit reveals the bar.
                // Remote-client hits are confirmed by the HP sampler.
                if (__state.LocalAttacker)
                    Instance.RecordDamage(
                        __instance, before.x,
                        __instance.GetHealth());

                float backstab =
BackstabTime(__instance);
                // Not on the killing blow: the bar is gone with it.
                if ((backstab != before.y ||
                    before.z > .5f) &&
                    __instance.GetHealth() > 0f)
                    Instance.MarkCriticalHit(__instance);

            }
        }
    }
}
