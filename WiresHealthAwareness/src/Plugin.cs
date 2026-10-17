using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresDamageVignette
{
    internal enum VolumeCurve
    {
        Exponential,
        Linear
    }

    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.healthawareness";
        public const string Name = "Wire's Health Awareness";
        public const string Version = "0.3.7";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> StartHealthPercent;
        internal static ConfigEntry<float> MaxOpacity;
        internal static ConfigEntry<bool> Pulse;
        internal static ConfigEntry<bool> HeartbeatEnabled;
        internal static ConfigEntry<float> HeartbeatVolume;
        internal static ConfigEntry<float> HeartbeatStartPercent;
        internal static ConfigEntry<VolumeCurve> HeartbeatCurve;
        internal static ConfigEntry<float> HeartbeatTempoVariation;
        internal static ConfigEntry<bool> VanillaDamageFlashEnabled;
        internal static ConfigEntry<bool> HitFlashEnabled;
        internal static ConfigEntry<float> HitFlashOpacity;
        internal static ConfigEntry<float> HitFlashDuration;


        private Harmony _harmony;

        private void Awake()
        {
            Enabled = Config.Bind("Vignette", "Enabled", true,
                "Display a red, texture-free vignette while below the configured health percentage.");
            StartHealthPercent = Config.Bind("Vignette", "Start below health (%)", 85f,
                new ConfigDescription("Health percentage where the vignette begins. Intensity increases smoothly toward zero health.",
                    new AcceptableValueRange<float>(1f, 100f)));
            MaxOpacity = Config.Bind("Vignette", "Maximum opacity", 0.76f,
                new ConfigDescription("Maximum red-edge opacity at zero health. Does not darken the center of the screen.",
                    new AcceptableValueRange<float>(0f, 1f)));
            Pulse = Config.Bind("Vignette", "Pulse", true,
                "Subtle pulsing as health drops; pulse intensity increases the lower your health gets.");
            HeartbeatEnabled = Config.Bind("Heartbeat", "Enabled", true,
                "Play the included recorded heartbeat loop below the separate heartbeat threshold.");
            HeartbeatStartPercent = Config.Bind("Heartbeat", "Start below health (%)", 65f,
                new ConfigDescription("Health percentage where the heartbeat begins; independent of the red vignette.",
                    new AcceptableValueRange<float>(1f, 100f)));
            HeartbeatCurve = Config.Bind("Heartbeat", "Loudness curve", VolumeCurve.Exponential,
                "Exponential builds faster as health falls. Linear increases evenly toward zero health.");
            HeartbeatVolume = Config.Bind("Heartbeat", "Volume", 0.50f,
                new ConfigDescription("Heartbeat loudness, routed through the game's SFX/GUI mixer and therefore controlled by both the in-game SFX and master volume settings.",
                    new AcceptableValueRange<float>(0f, 1f)));
            HeartbeatTempoVariation = Config.Bind("Heartbeat",
                "Natural BPM variation (%)", 3f,
                new ConfigDescription(
                    "Natural variation in the time BETWEEN beats, without changing pitch. Default 3% combines a gentle breathing-like rhythm with subtle beat-to-beat timing variation; 0% gives an exact steady BPM.",
                    new AcceptableValueRange<float>(0f, 8f)));

            // The vanilla HUD has its own textured damage flash.
            // This option controls ONLY that original stock image,
            // independently of our smooth low-health/hit vignette.
            VanillaDamageFlashEnabled = Config.Bind(
                "Vanilla damage flash", "Enabled", false,
                "Show Valheim's original textured red damage screen on heavy hits. Disabled by default; enable to restore the vanilla visual. Wire's own smooth vignette and damage flash are unaffected.");

            HitFlashEnabled = Config.Bind("Damage flash", "Enabled", true,
                "Briefly flash a soft red vignette when a direct hit actually reduces health. Damage-over-time ticks never trigger the flash.");
            HitFlashOpacity = Config.Bind("Damage flash", "Flash opacity", 0.20f,
                new ConfigDescription("Strength of the direct-hit effect. At high health this controls the standalone flash opacity; at low health it controls how far the existing vignette expands inward without brightening. Harder hits have a stronger effect.",
                    new AcceptableValueRange<float>(0.10f, 0.50f)));
            HitFlashDuration = Config.Bind("Damage flash", "Flash duration", 0.60f,
                new ConfigDescription("Total lifetime of the direct-hit flash in seconds. It appears almost immediately (~0.025 seconds) then fades out smoothly over the remaining time.",
                    new AcceptableValueRange<float>(0.15f, 1.50f)));



            var volumeMigration = Config.Bind("Internal", "Heartbeat volume updated to 0.1.1", false,
                new ConfigDescription("Internal upgrade marker.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            bool hasOlderSettings = volumeMigration.Value;
            if (!volumeMigration.Value)
            {
                if (Mathf.Abs(HeartbeatVolume.Value - 0.28f) < 0.0001f)
                    HeartbeatVolume.Value = 0.38f;
                volumeMigration.Value = true;
                Config.Save();
            }

            var migration20 = Config.Bind("Internal", "Migrated to 0.2.0", false,
                new ConfigDescription("One-time default migration.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            bool previousVersionWas020 = migration20.Value;
            if (!migration20.Value)
            {
                if (Mathf.Abs(StartHealthPercent.Value - 50f) < 0.001f)
                    StartHealthPercent.Value = 65f;
                // Fresh installations already start at 0.50. Only adjust
                // values saved by an older version of this plugin.
                if (hasOlderSettings)
                {
                    float oldLoudness = HeartbeatVolume.Value;
                    if (Mathf.Abs(oldLoudness - 0.38f) < 0.001f)
                        HeartbeatVolume.Value = 0.50f;
                    else
                        HeartbeatVolume.Value = Mathf.Clamp01(oldLoudness / 1.2f);
                }
                migration20.Value = true;
                Config.Save();
            }

            // New 0.5 matches old 1.0; halve settings saved by 0.2.0.
            // Fresh installations keep 0.5 unchanged.
            var migration21 = Config.Bind("Internal", "Migrated to 0.2.1", false,
                new ConfigDescription("Heartbeat volume scale migration.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            if (!migration21.Value)
            {
                if (previousVersionWas020)
                    HeartbeatVolume.Value = Mathf.Clamp01(HeartbeatVolume.Value * 0.5f);
                migration21.Value = true;
                Config.Save();
            }

            // Migrate only the previous untouched 0.34-second default.
            // Customized durations are preserved.
            var migration24 = Config.Bind("Internal", "Migrated flash duration to 0.2.4", false,
                new ConfigDescription("One-time hit flash duration default upgrade.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            if (!migration24.Value)
            {
                if (Mathf.Abs(HitFlashDuration.Value - 0.34f) < 0.0001f)
                    HitFlashDuration.Value = 0.60f;
                migration24.Value = true;
                Config.Save();
            }

            // Keep the historic migration marker for old configurations,
            // but no longer change 0.20 -> 0.12: 0.20 is the new default.
            var migration28 = Config.Bind("Internal",
                "Migrated flash strength to 0.2.8", false,
                new ConfigDescription("One-time direct-hit flash balance.",
                    null, new ConfigurationManagerAttributes { Browsable = false }));
            if (!migration28.Value)
            {
                migration28.Value = true;
                Config.Save();
            }

            // Migrate previous untouched defaults, preserving alternate
            // custom values. Existing users explicitly asking for the
            // new thresholds may still edit the config themselves.
            var migration30 = Config.Bind("Internal",
                "Migrated thresholds to 0.3.0", false,
                new ConfigDescription("One-time threshold default update.",
                    null, new ConfigurationManagerAttributes { Browsable = false }));
            if (!migration30.Value)
            {
                if (Mathf.Abs(StartHealthPercent.Value - 65f) < 0.0001f)
                    StartHealthPercent.Value = 85f;
                if (Mathf.Abs(HeartbeatStartPercent.Value - 40f) < 0.0001f)
                    HeartbeatStartPercent.Value = 65f;
                migration30.Value = true;
                Config.Save();
            }

            RemoveLegacyColorSettings();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            gameObject.AddComponent<HealthVignetteDriver>();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            DamageGate.Reset();
            _harmony?.UnpatchSelf();
        }

        private void RemoveLegacyColorSettings()
        {
            string[] names = {
                "Freezing", "Frost damage", "Burning", "Poisoned",
                "Smoke", "Spirit", "Lightning", "Tared"
            };
            foreach (string name in names)
            {
                var old = Config.Bind("Status vignettes", name, true,
                    "Removed in 0.2.2.");
                Config.Remove(old.Definition);
            }
            Config.Save();
        }

    }

    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable = false;
    }

    internal sealed class HealthVignetteDriver : MonoBehaviour
    {
        // Render one continuous ellipse extending past screen corners.
        // No square normalization, angular corner transitions or flat edges.
        private const int Rings = 30;
        private const int Segments = 96;
        private const float OuterRadius = 1.41421356237f; // sqrt(2)
        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int Cull = Shader.PropertyToID("_Cull");
        private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int ZTest = Shader.PropertyToID("_ZTest");

        private Material _overlay;
        private Camera _worldCamera;
        private float _strength;
        private float _flashElapsed = 999f;
        private float _flashStrength;
        private float _flashAttackStartAlpha;
        private float _pulsePhase;
        private HeartbeatSound _sound;

        private void OnEnable()
        {
            Camera.onPostRender += OnCameraPostRender;
            _sound = gameObject.AddComponent<HeartbeatSound>();
        }

        private void OnDisable()
        {
            Camera.onPostRender -= OnCameraPostRender;
            if (_sound != null) _sound.StopNow();
            DamageGate.Reset();
        }

        private void OnDestroy()
        {
            if (_overlay != null)
                Destroy(_overlay);
        }

        private GameCamera _cachedGameCamera;

        private void Update()
        {
            GameCamera gameCam = GameCamera.instance;
            // Look the camera up only when the GameCamera changes, not every frame.
            if (gameCam != _cachedGameCamera)
            {
                _cachedGameCamera = gameCam;
                _worldCamera = gameCam != null ? gameCam.GetComponent<Camera>() : null;
            }

            Player player = Player.m_localPlayer;
            bool valid = player != null &&
                         !player.IsDead() &&
                         !player.IsTeleporting() &&
                         !GameCamera.InFreeFly() &&
                         _worldCamera != null &&
                         player.GetMaxHealth() > 0.001f;

            DamageGate.Observe(valid ? player : null);
            // Food-only HP gaps do nothing UNTIL a meaningful hit.
            // Once armed, intensity follows actual health percentage,
            // including the pre-existing food HP gap. Healing the real
            // injury disarms it without needing to reach full max HP.
            bool armed = valid && DamageGate.EffectsArmed;
            float healthPercent = armed
                ? 100f * Mathf.Clamp01(
                    player.GetHealth() / player.GetMaxHealth())
                : 100f;
            float stressBpmBoost = valid
                ? DamageGate.StressBpmBoost : 0f;
            float threshold = Mathf.Max(1f, Plugin.StartHealthPercent.Value);
            float newHit = DamageGate.ConsumeDirectFlash();
            if (!valid)
            {
                _flashElapsed = 999f;
                _flashStrength = 0f;
                _flashAttackStartAlpha = 0f;
            }
            else if (newHit > 0f)
            {
                // Preserve the current opacity when another hit arrives.
                // Otherwise resetting the attack would make repeated hits
                // briefly blink back to fully transparent.
                _flashAttackStartAlpha = EvaluateFlashAlpha();
                _flashElapsed = 0f;
                _flashStrength = Mathf.Clamp01(newHit);
            }
            else
            {
                _flashElapsed += Time.unscaledDeltaTime;
            }

            float desired = armed && Plugin.Enabled.Value
                ? Mathf.Clamp01((threshold - healthPercent) /
                    threshold) : 0f;
            _strength = Mathf.MoveTowards(_strength, desired, Time.unscaledDeltaTime * 2.2f);



            float cycleDuration = Mathf.Lerp(1.18f, 0.78f, _strength);
            _pulsePhase += Time.unscaledDeltaTime / cycleDuration;
            _pulsePhase -= Mathf.Floor(_pulsePhase);

            float heartbeatStart = Mathf.Max(1f,
                Plugin.HeartbeatStartPercent.Value);
            float severity = armed
                ? Mathf.Clamp01((heartbeatStart - healthPercent) /
                    heartbeatStart) : 0f;

            // Only genuine severe direct hits (>= ~40%), MULTI-HIT
            // damage-per-second streaks (>= 10% max HP/s), or actual
            // stagger can trigger stress above the normal threshold.
            // The surge adds to the current HEALTH-BASED heart rate;
            // after it fades, the heartbeat simply returns to that
            // rate or goes silent if the player is healthy again.
            bool heartbeat = valid && Plugin.HeartbeatEnabled.Value &&
                (severity > 0.001f || stressBpmBoost > 0.5f);
            float audibleSeverity = Mathf.Max(severity,
                Mathf.Min(0.45f, stressBpmBoost / 70f * 0.45f));
            float baseBpm = Mathf.Lerp(67f, 110f,
                Mathf.Pow(severity, 1.2f));
            float targetBpm = Mathf.Min(150f,
                baseBpm + stressBpmBoost);

            if (_sound != null)
            {
                if (player == null || player.IsDead())
                    _sound.StopOnDeath();
                else
                    _sound.UpdateSound(heartbeat, audibleSeverity,
                        Plugin.HeartbeatVolume.Value,
                        Plugin.HeartbeatCurve.Value,
                        targetBpm, Plugin.HeartbeatTempoVariation.Value);
            }
        }

        private void OnCameraPostRender(Camera cam)
        {
            if (_worldCamera == null || cam != _worldCamera)
                return;

            // Near-instant but not abrupt attack (~25ms), followed by
            // a smooth, much slower fade-out controlled by Flash duration.
            float flashAlpha = EvaluateFlashAlpha();

            if (_strength <= 0.001f && flashAlpha <= 0.001f)
                return;

            if (!EnsureMaterial())
                return;

            float beat = 0f;
            if (Plugin.Pulse.Value)
            {
                // Two small pulses per cardiac cycle, increasing with severity.
                beat = Peak(_pulsePhase, 0.12f, 0.068f) +
                       0.58f * Peak(_pulsePhase, 0.31f, 0.078f);
            }

            // The pulse fits INSIDE the selected opacity: with strength < 1
            // the peak stays strictly below maxOpacity. Max can only be
            // reached at exactly zero health, never prematurely clipped.
            float healthStrength = Mathf.Clamp01(_strength);
            float maxOpacity = Mathf.Clamp01(Plugin.MaxOpacity.Value);
            // Bring the persistent red vignette up sooner in the low-HP
            // range, but never reach full opacity before 0% HP.
            float visibleHealthStrength = Mathf.Pow(healthStrength, 0.72f);
            float pulseBoost = 0.12f * beat * visibleHealthStrength *
                               (1f - visibleHealthStrength);
            float lowHealthOpacity = maxOpacity *
                                     (visibleHealthStrength + pulseBoost);

            // Both effects are the SAME smooth gradient, not separate
            // shapes or layers. A hit briefly increases the opacity of
            // the existing low-health vignette. No inward expansion,
            // low-exponent red wash, texture or blood sprite is drawn.
            // Keep the user's configured flash envelope/strength.
            float opacity = Mathf.Clamp01(
                lowHealthOpacity + flashAlpha * 1.75f);
            const float falloffExponent = 1.25f;
            // Both the persistent low-health vignette and the hit flash
            // use the same TWO-PASS red treatment. The first pass colors
            // existing light, the second adds only a tiny red component
            // so that the vignette remains recognizable in dark areas.
            // This avoids the neon-bright fill of pre-0.2.6 while fixing
            // the barely visible low-health gradient of 0.2.6.
            Color tint = new Color(1.0f, 0.30f, 0.36f);

            _overlay.SetInt(SrcBlend, 2);  // DstColor
            _overlay.SetInt(DstBlend, 10); // OneMinusSrcAlpha
            RenderGradient(opacity, falloffExponent, tint);

            // Brighter persistent red, without the original neon overlay.
            // Deep black lifts at most 0.106 red at max opacity, versus
            // around 0.62 in the original solid-red blend.
            _overlay.SetInt(SrcBlend, 1);  // One
            _overlay.SetInt(DstBlend, 1);  // One
            RenderGradient(opacity, falloffExponent,
                new Color(0.14f, 0.004f, 0.008f));
        }

        private void RenderGradient(float opacity, float falloffExponent, Color tint)
        {
            _overlay.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.TRIANGLES);

            // One continuous screen-aspect ellipse, sampled with smooth
            // rings and no normalized-square corners. Its absolute peak is
            // only at the far screen corners, never a flat side plateau.
            for (int ring = 0; ring < Rings; ring++)
            {
                float r0 = OuterRadius * ring / Rings;
                float r1 = OuterRadius * (ring + 1f) / Rings;
                float a0 = RadialAlpha(r0, falloffExponent) * opacity;
                float a1 = RadialAlpha(r1, falloffExponent) * opacity;

                for (int wedge = 0; wedge < Segments; wedge++)
                {
                    float t0 = 2f * Mathf.PI * wedge / Segments;
                    float t1 = 2f * Mathf.PI * (wedge + 1f) / Segments;
                    Vector2 p00 = Point(r0, t0);
                    Vector2 p01 = Point(r0, t1);
                    Vector2 p10 = Point(r1, t0);
                    Vector2 p11 = Point(r1, t1);

                    Vertex(p00, a0, tint); Vertex(p10, a1, tint); Vertex(p11, a1, tint);
                    Vertex(p00, a0, tint); Vertex(p11, a1, tint); Vertex(p01, a0, tint);
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        private float EvaluateFlashAlpha()
        {
            if (!Plugin.HitFlashEnabled.Value)
                return 0f;

            float duration = Mathf.Max(0.06f, Plugin.HitFlashDuration.Value);
            if (_flashElapsed >= duration)
                return 0f;

            float attackSeconds = Mathf.Min(0.025f, duration * 0.12f);
            float peakAlpha = Mathf.Clamp(
                Plugin.HitFlashOpacity.Value * _flashStrength, 0f, 0.50f);
            float peak = Mathf.Max(_flashAttackStartAlpha, peakAlpha);

            if (_flashElapsed < attackSeconds)
            {
                float t = Mathf.Clamp01(_flashElapsed / attackSeconds);
                return Mathf.Lerp(_flashAttackStartAlpha, peak,
                    Mathf.SmoothStep(0f, 1f, t));
            }

            float release = Mathf.Clamp01(
                (_flashElapsed - attackSeconds) /
                Mathf.Max(0.001f, duration - attackSeconds));
            // Smoothstep falls away gently and reaches exact zero at
            // the configured duration, avoiding a hard visual cutoff.
            return peak * (1f - Mathf.SmoothStep(0f, 1f, release));
        }

        private bool EnsureMaterial()
        {
            if (_overlay != null) return true;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;

            _overlay = new Material(shader);
            _overlay.hideFlags = HideFlags.HideAndDontSave;
            // Multiplicative first pass; the additive low-brightness
            // color pass is configured explicitly when drawing.
            _overlay.SetInt(SrcBlend, 2);  // DstColor
            _overlay.SetInt(DstBlend, 10); // OneMinusSrcAlpha
            _overlay.SetInt(Cull, 0);
            _overlay.SetInt(ZWrite, 0);
            _overlay.SetInt(ZTest, 8);    // Always
            return true;
        }

        private static Vector2 Point(float r, float angle)
        {
            // Euclidean ellipse in viewport coordinates, covering all
            // corners without changing shape at 45-degree diagonals.
            return new Vector2(0.5f + 0.5f * r * Mathf.Cos(angle),
                               0.5f + 0.5f * r * Mathf.Sin(angle));
        }

        private static float RadialAlpha(float radius, float exponent)
        {
            // Never has a flat maximum over the visible screen. Smooth
            // monotonic growth from center to the furthest corner.
            float t = Mathf.Clamp01(radius / OuterRadius);
            return Mathf.Pow(t, exponent);
        }

        private static float Peak(float position, float center, float spread)
        {
            float d = Mathf.Abs(position - center);
            d = Mathf.Min(d, 1f - d);
            float x = d / spread;
            return Mathf.Exp(-x * x * 2f);
        }

        private static void Vertex(Vector2 p, float alpha, Color tint)
        {
            // Premultiplied RGB works in both passes:
            // multiply: Dst * (1 - alpha*(1-tint))
            // red lift:  Dst + (lift * alpha)
            GL.Color(new Color(tint.r * alpha, tint.g * alpha,
                               tint.b * alpha, alpha));
            GL.Vertex3(p.x, p.y, 0f);
        }
    }
}

namespace WiresDamageVignette
{
    // Suppress ONLY Valheim's stock Hud.m_damageScreen visual.
    // Returning true restores original Hud.DamageFlash() behavior.
    [HarmonyLib.HarmonyPatch(typeof(Hud), nameof(Hud.DamageFlash))]
    internal static class VanillaDamageFlashPatch
    {
        [HarmonyLib.HarmonyPrefix]
        private static bool Prefix()
        {
            return Plugin.VanillaDamageFlashEnabled == null ||
                   Plugin.VanillaDamageFlashEnabled.Value;
        }
    }
}