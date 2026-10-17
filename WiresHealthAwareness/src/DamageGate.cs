using HarmonyLib;
using UnityEngine;

namespace WiresDamageVignette
{
    internal static class DamageGate
    {
        private static Player _player;
        private static float _previousHealth;
        private static float _previousMaximum;
        // Accumulated meaningful unhealed damage is used ONLY to
        // arm/disarm the effects, never to compute vignette intensity.
        // Intensity uses ACTUAL current HP / max HP once armed.
        private static float _injuryPoints;
        private static bool _effectsArmed;
        private const float ScratchFloor = 0.01f;
        private const float ActivationFloor = 0.02f;
        private static bool _initialized;
        private static float _pendingDirectFlash;
        // One-second rolling direct-hit DPS in fractions of MAX HP,
        // requiring two distinct meaningful hits in that same second.
        // Fixed ring buffer: no allocations during combat.
        private const float DpsWindowSeconds = 1.0f;
        private const float DpsTrigger = 0.10f;
        private const float MinStreakHit = 0.015f;
        private const int HitSlots = 32;
        private static readonly float[] RecentTimes = new float[HitSlots];
        private static readonly float[] RecentFractions = new float[HitSlots];
        private static int _recentCount;
        private static int _recentNext;

        // Additive pulse above the current HEALTH-BASED heartbeat BPM.
        // Short peak, subtle natural variation, then rapid return to base.
        private static float _surgeBpmBoost;
        private static float _surgeHoldSeconds;
        private static float _lastStressUpdate;
        private static readonly System.Random StressRandom =
            new System.Random(unchecked(System.Environment.TickCount ^ 0x6C4A32F));

        // Eating never arms the effects by itself. One hit worth
        // 2%+ of current max HP (or accumulated unhealed 1-2% hits)
        // arms them; <1% hits are treated as insignificant scratches.
        // Healing the qualifying injuries disarms the effects, so a
        // food-related HP gap does not leave a permanent heartbeat.
        internal static bool EffectsArmed => _effectsArmed;

        internal static float StressBpmBoost => _surgeBpmBoost;

        internal static void Observe(Player player)
        {
            if (player != _player)
            {
                Reset();
                _player = player;
            }
            if (player == null) return;

            float health = player.GetHealth();
            float maximum = player.GetMaxHealth();
            if (!_initialized)
            {
                // On load, an existing HP gap may be from food.
                _previousHealth = health;
                _previousMaximum = maximum;
                _initialized = true;
                _lastStressUpdate = Time.unscaledTime;
                return;
            }

            float deltaHealth = health - _previousHealth;
            float deltaMax = maximum - _previousMaximum;
            if (deltaHealth > 0.025f)
            {
                // Actual healing repays real injuries, not the
                // food-created gap between current and maximum HP.
                _injuryPoints = Mathf.Max(0f,
                    _injuryPoints - deltaHealth);
            }
            else if (deltaHealth < -0.025f &&
                     deltaMax >= -0.025f &&
                     !player.IsDead())
            {
                // Catch other mods that lower health without calling
                // ApplyDamage. Ignore food expiration (max also drops)
                // and tiny <1%-of-max scratches.
                float lost = -deltaHealth;
                float fraction = lost / Mathf.Max(1f, maximum);
                if (fraction >= ScratchFloor)
                {
                    _injuryPoints += lost;
                    if (fraction >= ActivationFloor ||
                        _injuryPoints >= maximum * ActivationFloor)
                        _effectsArmed = true;
                }
            }

            if (maximum > 0f && health >= maximum - 0.025f)
                _injuryPoints = 0f;

            _injuryPoints = Mathf.Clamp(
                _injuryPoints, 0f, Mathf.Max(0f, maximum));
            if (_injuryPoints <= 0.025f)
                _effectsArmed = false;
            _previousHealth = health;
            _previousMaximum = maximum;
            AdvanceStress();
        }

        internal static void OnActualDamage(Player player, float healthBefore,
            HitData hit, bool triggerEffects)
        {
            if (player == null || player != Player.m_localPlayer)
                return;

            if (player != _player)
            {
                Reset();
                _player = player;
            }

            float healthAfter = player.GetHealth();
            float lost = healthBefore - healthAfter;
            if (lost <= 0f)
                return;

            float maximum = Mathf.Max(1f, player.GetMaxHealth());
            float fraction = Mathf.Clamp01(lost / maximum);
            // Update the observation baseline even for insignificant
            // scratches so the next frame can't count them again.
            _previousHealth = healthAfter;
            _previousMaximum = player.GetMaxHealth();
            _initialized = true;
            if (_lastStressUpdate <= 0f)
                _lastStressUpdate = Time.unscaledTime;

            if (fraction < ScratchFloor)
                return;

            _injuryPoints = Mathf.Min(player.GetMaxHealth(),
                _injuryPoints + lost);

            // Use the HIT only to arm the effects. Their actual
            // intensity comes from CURRENT health percentage, which
            // immediately includes any existing food-created HP gap.
            if (fraction >= ActivationFloor ||
                _injuryPoints >= maximum * ActivationFloor)
                _effectsArmed = true;

            if (IsDirectHit(hit, triggerEffects))
            {
                _pendingDirectFlash = Mathf.Max(_pendingDirectFlash,
                    Mathf.Lerp(0.70f, 1f,
                        Mathf.Sqrt(Mathf.Clamp01(fraction * 3f))));

                AdvanceStress();

                // SINGLE HIT: 40% of max HP is the soft threshold.
                // ~40% -> +26 BPM (moderately stressed)
                // ~99% -> +46 BPM; a nearly fatal hit may reach ~150
                // after accounting for the usual low-health heartbeat.
                // Below 40%, there is NO single-hit surge.
                if (fraction >= 0.40f)
                {
                    float intensity = Mathf.Clamp01(
                        (fraction - 0.40f) / 0.60f);
                    float boost = Mathf.Lerp(26f, 46f,
                        Mathf.Pow(intensity, 0.85f));
                    TriggerStress(boost,
                        Mathf.Lerp(0.55f, 0.85f, intensity));
                }

                // STREAK: rolling, fixed one-second DPS of direct hits
                // in units of maximum HP per second. Must be 2+ hits
                // each at least 1.5% HP; never let one ~10% hit create
                // a streak by itself, even if it is very recent.
                if (fraction >= MinStreakHit)
                {
                    int slot = _recentNext;
                    RecentTimes[slot] = Time.unscaledTime;
                    RecentFractions[slot] = fraction;
                    _recentNext = (_recentNext + 1) % HitSlots;
                    _recentCount = Mathf.Min(_recentCount + 1, HitSlots);

                    float dps = 0f;
                    int hitsInWindow = 0;
                    for (int i = 0; i < _recentCount; ++i)
                    {
                        if (Time.unscaledTime - RecentTimes[i] <=
                            DpsWindowSeconds)
                        {
                            dps += RecentFractions[i] /
                                DpsWindowSeconds;
                            hitsInWindow++;
                        }
                    }

                    if (hitsInWindow >= 2 && dps >= DpsTrigger)
                    {
                        // Slowly scaled gain: 10%/s ~+12 BPM,
                        // 30%/s ~+21 BPM, 60%/s ~+35 BPM,
                        // 90%/s ~+50 BPM.
                        float intensity = Mathf.Clamp01(
                            (dps - DpsTrigger) / 0.80f);
                        float boost = 12f + 38f *
                            Mathf.Pow(intensity, 0.90f);
                        TriggerStress(boost,
                            Mathf.Lerp(0.42f, 0.70f, intensity));
                    }
                }
            }
        }

        internal static void OnStagger(Player player)
        {
            if (player == null || player != Player.m_localPlayer ||
                player.IsDead())
                return;
            if (player != _player)
            {
                Reset();
                _player = player;
            }
            AdvanceStress();
            // A real stagger is stressful on its own, but does not
            // reintroduce the old huge BPM spikes on ordinary hits.
            TriggerStress(19f, 0.55f);
        }

        private static void TriggerStress(float bpmBoost, float holdSeconds)
        {
            // A little event-level unpredictability in addition to
            // the normal per-beat 3% BPM variation. This does not
            // modify Unity's gameplay random state.
            float eventVariation = 0.95f +
                (float)StressRandom.NextDouble() * 0.10f;
            _surgeBpmBoost = Mathf.Max(_surgeBpmBoost,
                bpmBoost * eventVariation);
            _surgeHoldSeconds = Mathf.Max(_surgeHoldSeconds,
                holdSeconds);
        }

        private static void AdvanceStress()
        {
            float now = Time.unscaledTime;
            float elapsed = Mathf.Clamp(now - _lastStressUpdate, 0f, 0.5f);
            _lastStressUpdate = now;
            if (elapsed <= 0f) return;

            // Short plateau so more than one beat can sound stressed;
            // then the boost falls to zero quickly. The usual HP-based
            // tempo is always the independent baseline underneath.
            float fallingTime = Mathf.Max(0f,
                elapsed - _surgeHoldSeconds);
            _surgeHoldSeconds = Mathf.Max(0f,
                _surgeHoldSeconds - elapsed);
            if (fallingTime > 0f)
                _surgeBpmBoost = Mathf.MoveTowards(
                    _surgeBpmBoost, 0f, 70f * fallingTime);
        }

        internal static float ConsumeDirectFlash()
        {
            float value = _pendingDirectFlash;
            _pendingDirectFlash = 0f;
            return value;
        }

        private static bool IsDirectHit(HitData hit, bool triggerEffects)
        {
            if (hit == null) return false;
            switch (hit.m_hitType)
            {
                case HitData.HitType.Burning:
                case HitData.HitType.Poisoned:
                case HitData.HitType.Freezing:
                case HitData.HitType.Smoke:
                case HitData.HitType.Water:
                case HitData.HitType.Drowning:
                case HitData.HitType.EdgeOfWorld:
                case HitData.HitType.CinderFire:
                case HitData.HitType.AshlandsOcean:
                case HitData.HitType.AshlandsLava:
                    return false;
                case HitData.HitType.EnemyHit:
                case HitData.HitType.PlayerHit:
                case HitData.HitType.Fall:
                case HitData.HitType.Impact:
                case HitData.HitType.Cart:
                case HitData.HitType.Tree:
                case HitData.HitType.Self:
                case HitData.HitType.Structural:
                case HitData.HitType.Turret:
                case HitData.HitType.Boat:
                case HitData.HitType.Stalagtite:
                case HitData.HitType.Catapult:
                case HitData.HitType.Incinerator:
                case HitData.HitType.DrawBridge:
                    return true;
                case HitData.HitType.Undefined:
                    return triggerEffects;
                default:
                    return false;
            }
        }

        internal static void Reset()
        {
            _player = null;
            _previousHealth = 0f;
            _previousMaximum = 0f;
            _injuryPoints = 0f;
            _effectsArmed = false;
            _initialized = false;
            _pendingDirectFlash = 0f;
            _recentCount = 0;
            _recentNext = 0;
            _surgeBpmBoost = 0f;
            _surgeHoldSeconds = 0f;
            _lastStressUpdate = 0f;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class ActualDamagePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, out float __state)
        {
            __state = __instance == Player.m_localPlayer
                ? __instance.GetHealth() : 0f;
        }

        [HarmonyPostfix]
        private static void Postfix(Character __instance,
            HitData hit, bool triggerEffects, float __state)
        {
            if (__instance == Player.m_localPlayer)
                DamageGate.OnActualDamage((Player)__instance,
                    __state, hit, triggerEffects);
        }
    }

    [HarmonyPatch(typeof(Character), "RPC_Stagger")]
    internal static class ActualStaggerPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, out bool __state)
        {
            __state = __instance == Player.m_localPlayer &&
                !__instance.IsStaggering();
        }

        [HarmonyPostfix]
        private static void Postfix(Character __instance, bool __state)
        {
            if (__state && __instance == Player.m_localPlayer)
                DamageGate.OnStagger((Player)__instance);
        }
    }
}
