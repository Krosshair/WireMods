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
        private const string CriticalCounterKey = "wire.enemyhud.critical_id";
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
        private ConfigEntry<bool> _keepNameplatesOnScreen;
        private ConfigEntry<bool> _criticalHitFlash;
        private ConfigEntry<float> _hpFontScale;

        private sealed class HealthSample
        {
            public float LastHp;
            public float SeenAt;
            public float ShowUntil;
            public float PendingLocalHitUntil = -999f;
        }

        private sealed class HealthOutline
        {
            public Image[] Border;
            public float StartsAt = -999f;
            public float EndsAt = -999f;
            public float ObservedCriticalUntil = -999f;
            public bool HasNetworkId;
            public int LastNetworkId;

        }

        private readonly Dictionary<Character, HealthSample> _healthSamples =
            new Dictionary<Character, HealthSample>();
        private readonly Dictionary<Character, float> _lastCriticalHits =
            new Dictionary<Character, float>();
        private readonly Dictionary<EnemyUiState, HealthOutline> _healthOutlines =
            new Dictionary<EnemyUiState, HealthOutline>();

        private static readonly FieldInfo BackstabTimeField =
            AccessTools.Field(typeof(Character), "m_backstabTime");

        private static readonly FieldInfo HoverTimerField =
            HudDataType != null
                ? AccessTools.Field(HudDataType, "m_hoverTimer")
                : null;

        private void ApplyVisibilityPreset(bool enabled)
        {
            _keepNameplatesOnScreen.Value = enabled;
            _showOnDamage.Value = enabled;
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
        }

        // Sample actual HP changes, but only as confirmation of a
        // recently sent, locally attributed hit. HP loss alone never
        // reveals another player's or environmental damage.
        private void SampleHealthForVisibility()
        {
            if (_showOnDamage == null ||
                Player.m_localPlayer == null)
                return;
            if (!_showOnDamage.Value)
            {
                // Re-enabling it later must not treat old, unseen
                // damage as a fresh hit.
                _healthSamples.Clear();
                return;
            }

            float now = Time.unscaledTime;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (!IsOrdinaryEnemy(c)) continue;
                float hp = Mathf.Max(0f, c.GetHealth());
                HealthSample sample;
                if (!_healthSamples.TryGetValue(c, out sample))
                {
                    _healthSamples[c] = new HealthSample
                    {
                        LastHp = hp, SeenAt = now
                    };
                    continue;
                }

                // Only a confirmed HP decrease following our own
                // outgoing attack qualifies. Other HP loss is ignored.
                if (hp > 0f &&
                    sample.LastHp - hp > .01f &&
                    now <= sample.PendingLocalHitUntil)
                {
                    sample.PendingLocalHitUntil = -999f;
                    sample.ShowUntil = now + DamageShowSeconds;
                    if (!_reportedHitReveal)
                    {
                        Logger.LogInfo(
                            "Visibility: local-player hit confirmed; 3s reveal active.");
                        _reportedHitReveal = true;
                    }
                }
                sample.LastHp = hp;
                sample.SeenAt = now;
            }

            if (Time.frameCount % 120 == 0)
            {
                var missing = new List<Character>();
                foreach (var entry in _healthSamples)
                    if (entry.Key == null ||
                        now - entry.Value.SeenAt > 5f)
                        missing.Add(entry.Key);
                foreach (Character c in missing)
                {
                    _healthSamples.Remove(c);
                    _lastCriticalHits.Remove(c);
                }
            }
        }

        private bool ForceShowOnDamage(Character c)
        {
            if (_showOnDamage == null ||
                !_showOnDamage.Value ||
                !IsOrdinaryEnemy(c))
                return false;
            HealthSample sample;
            return _healthSamples.TryGetValue(c, out sample) &&
                Time.unscaledTime < sample.ShowUntil;
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

            HealthSample sample;
            if (!_healthSamples.TryGetValue(c, out sample))
            {
                sample = new HealthSample();
                _healthSamples[c] = sample;
            }
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

        private IDictionary GetVanillaHudData(EnemyHud hud)
        {
            return hud != null && EnemyHudHudsField != null
                ? EnemyHudHudsField.GetValue(hud) as IDictionary
                : null;
        }

        private bool HasNativeHoverVisibility(Character c)
        {
            if (c == null || HoverTimerField == null)
                return false;

            EnemyHud hud = EnemyHud.instance;
            IDictionary records = GetVanillaHudData(hud);
            if (records == null || !records.Contains(c))
                return false;

            object record = records[c];
            return record != null &&
                (float)HoverTimerField.GetValue(record) <
                    hud.m_hoverShowDuration;
        }

        private static Image MakeOutlineEdge(
            Transform parent, string name, Vector2 min,
            Vector2 max, Vector2 pivot, Vector2 pos,
            Vector2 size)
        {
            GameObject obj = new GameObject(
                name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            RectTransform r = obj.GetComponent<RectTransform>();
            r.anchorMin = min;
            r.anchorMax = max;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            Image image = obj.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.clear;
            return image;
        }

        private void SetupCriticalOutline(EnemyUiState state)
        {
            if (state?.HealthRect == null ||
                state.Kind != HudKind.Enemy)
                return;

            Transform hp = state.HealthRect;
            HealthOutline outline = new HealthOutline
            {
                Border = new[]
                {
                    MakeOutlineEdge(hp, "WireCritTop",
                        new Vector2(0f,1f), new Vector2(1f,1f),
                        new Vector2(.5f,1f), Vector2.zero,
                        new Vector2(0f,2f)),
                    MakeOutlineEdge(hp, "WireCritBottom",
                        new Vector2(0f,0f), new Vector2(1f,0f),
                        new Vector2(.5f,0f), Vector2.zero,
                        new Vector2(0f,2f)),
                    MakeOutlineEdge(hp, "WireCritLeft",
                        new Vector2(0f,0f), new Vector2(0f,1f),
                        new Vector2(0f,.5f), Vector2.zero,
                        new Vector2(2f,0f)),
                    MakeOutlineEdge(hp, "WireCritRight",
                        new Vector2(1f,0f), new Vector2(1f,1f),
                        new Vector2(1f,.5f), Vector2.zero,
                        new Vector2(2f,0f))
                }
            };

            ZNetView view =
                state.Character.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                outline.LastNetworkId =
                    view.GetZDO().GetInt(CriticalCounterKey);
                outline.HasNetworkId = true;
            }
            _healthOutlines[state] = outline;
        }

        private static void StartHealthFlash(HealthOutline outline)
        {
            outline.StartsAt = Time.unscaledTime;
            outline.EndsAt = outline.StartsAt +
                GetPlayerStaggerFlashDuration();
        }

        private void MarkCriticalHit(Character c)
        {
            if (!_criticalHitFlash.Value ||
                !IsOrdinaryEnemy(c))
                return;

            float until = Time.unscaledTime +
                GetPlayerStaggerFlashDuration();
            _lastCriticalHits[c] = until;

            ZNetView view = c.GetComponent<ZNetView>();
            if (view != null && view.IsValid() &&
                view.IsOwner())
            {
                ZDO zdo = view.GetZDO();
                zdo.Set(CriticalCounterKey,
                    zdo.GetInt(CriticalCounterKey) + 1);
            }
        }

        private void UpdateCriticalOutline(EnemyUiState state)
        {
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

                ZNetView view =
                    state.Character.GetComponent<ZNetView>();
                if (view != null && view.IsValid())
                {
                    int id = view.GetZDO().GetInt(
                        CriticalCounterKey);
                    if (outline.HasNetworkId)
                    {
                        if (id != outline.LastNetworkId)
                        {
                            outline.LastNetworkId = id;
                            StartHealthFlash(outline);
                        }
                    }
                    else
                    {
                        outline.LastNetworkId = id;
                        outline.HasNetworkId = true;
                    }
                }
            }
            else
            {
                // Disabling and re-enabling the option shouldn't
                // flash a critical that happened while disabled.
                ZNetView view =
                    state.Character.GetComponent<ZNetView>();
                if (view != null && view.IsValid())
                {
                    outline.LastNetworkId =
                        view.GetZDO().GetInt(CriticalCounterKey);
                    outline.HasNetworkId = true;
                }
            }

            float duration = outline.EndsAt - outline.StartsAt;
            float progress = duration > .001f
                ? (now - outline.StartsAt) / duration : 1f;
            float criticalAlpha = enabled &&
                progress >= 0f && progress < 1f
                ? Mathf.Sin(progress * Mathf.PI) : 0f;

            float alpha = criticalAlpha;

            // The four whites cover the existing dark HP frame's
            // entire thickness, following the real resized bar.
            if (outline.Border != null && outline.Border.Length == 4)
                NativeFrameFlash.Fit(state.HealthRect,
                    outline.Border[0], outline.Border[1],
                    outline.Border[2], outline.Border[3]);
            Color borderColor = _critFlashBase;
            borderColor.a *= alpha;
            foreach (Image piece in outline.Border)
                if (piece != null)
                    piece.color = borderColor;
        }

        private void DestroyCriticalOutline(EnemyUiState state)
        {
            HealthOutline outline;
            if (!_healthOutlines.TryGetValue(state, out outline))
                return;
            foreach (Image image in outline.Border)
                if (image != null)
                    Destroy(image.gameObject);
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
            bool ordinaryHover =
                HasNativeHoverVisibility(state.Character);
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
            Bounds bounds =
                RectTransformUtility
                    .CalculateRelativeRectTransformBounds(
                        state.Gui.transform,
                        state.Gui.transform);
            Vector3 scale =
                state.Gui.transform.lossyScale;
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
            if (hud == null || Hud.IsUserHidden() ||
                _showOnDamage == null || !_showOnDamage.Value)
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
            _lastCriticalHits.Clear();
            foreach (var entry in _healthOutlines)
                foreach (Image image in entry.Value.Border)
                    if (image != null)
                        Destroy(image.gameObject);
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

        [HarmonyPatch(typeof(EnemyHud), "TestShow")]
        private static class DamageAdditionalVisibility
        {
            private static void Postfix(
                Character c, ref bool __result)
            {
                if (!__result && Instance != null &&
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
            }

            private static void Prefix(
                Character __instance, HitData hit,
                out HitState __state)
            {
                float backstab =
                    BackstabTimeField != null
                        ? (float)BackstabTimeField.GetValue(
                            __instance)
                        : -999f;
                __state = new HitState {
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
                if (Instance == null ||
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
                    BackstabTimeField != null
                        ? (float)BackstabTimeField.GetValue(
                            __instance)
                        : -999f;
                if (backstab != before.y ||
                    before.z > .5f)
                    Instance.MarkCriticalHit(__instance);

            }
        }
    }
}
