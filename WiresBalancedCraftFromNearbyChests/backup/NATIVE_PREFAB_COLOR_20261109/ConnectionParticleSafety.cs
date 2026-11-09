using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    // Connection visuals reuse StationExtension particle-effect prefabs.
    // Some effects have mixed velocity-axis curve modes, which Unity
    // continuously warns about while these cloned beams are active.
    // Only touch the mod's own effect instances, never vanilla prefabs
    // or any of the actual crafting/station systems.
    internal static class ConnectionParticleSafety
    {
        private static readonly HashSet<int> LoggedPrefabs = new HashSet<int>();

        internal static void Prepare(GameObject instance, GameObject prefab)
        {
            if (instance == null)
                return;

            int repaired = 0;
            foreach (ParticleSystem particle in
                     instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particle == null)
                    continue;

                // The mod borrows effects from different StationExtensions,
                // including the magical green version. Recolor ONLY our
                // cloned instance, preserving each particle's original
                // alpha/lifetime variation and shape/motion.
                ForceYellow(particle);

                ParticleSystem.VelocityOverLifetimeModule module =
                    particle.velocityOverLifetime;
                if (!module.enabled)
                    continue;

                ParticleSystem.MinMaxCurve x = module.x;
                ParticleSystem.MinMaxCurve y = module.y;
                ParticleSystem.MinMaxCurve z = module.z;

                if (x.mode == y.mode && x.mode == z.mode)
                    continue;

                // TwoCurves supports every original curve type. Convert
                // each axis without flattening animated velocity curves,
                // so the same visual motion is maintained as far as the
                // original Unity module permits.
                module.x = ToTwoCurves(x);
                module.y = ToTwoCurves(y);
                module.z = ToTwoCurves(z);
                repaired++;
            }

            if (repaired > 0 && prefab != null &&
                LoggedPrefabs.Add(prefab.GetInstanceID()) &&
                Plugin.Log != null)
            {
                Plugin.Log.LogInfo(
                    "Normalized mismatched particle velocity axes in " +
                    repaired + " connection effect(s) from " + prefab.name +
                    " (only Balanced Craft clones were modified).");
            }
        }

        private static readonly Color LinkYellow = new Color(1f, .82f, .08f, 1f);
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly MaterialPropertyBlock YellowMaterialBlock = new MaterialPropertyBlock();

        private static void ForceYellow(ParticleSystem particle)
        {
            ParticleSystem.MainModule main = particle.main;
            main.startColor = Yellow(main.startColor);

            // The mage prefab's MATERIAL independently emits almost pure
            // green (_EmissionColor = 0, 5.34, .0047). Tinting its particle
            // gradients ALONE cannot remove that green light. Override the
            // emissive color on each CLONED particle renderer, without
            // creating new materials or mutating vanilla shared assets.
            ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.GetPropertyBlock(YellowMaterialBlock);
                YellowMaterialBlock.SetColor(EmissionColorId, new Color(4f, 3.05f, .16f, 1f));
                renderer.SetPropertyBlock(YellowMaterialBlock);
                YellowMaterialBlock.Clear();
            }

            ParticleSystem.ColorOverLifetimeModule lifetime = particle.colorOverLifetime;
            if (lifetime.enabled) lifetime.color = Yellow(lifetime.color);

            ParticleSystem.ColorBySpeedModule speed = particle.colorBySpeed;
            if (speed.enabled) speed.color = Yellow(speed.color);

            ParticleSystem.TrailModule trails = particle.trails;
            if (trails.enabled)
            {
                trails.colorOverLifetime = Yellow(trails.colorOverLifetime);
                trails.colorOverTrail = Yellow(trails.colorOverTrail);
            }
        }

        private static ParticleSystem.MinMaxGradient Yellow(ParticleSystem.MinMaxGradient original)
        {
            switch (original.mode)
            {
                case ParticleSystemGradientMode.Color:
                    Color single = LinkYellow;
                    single.a = original.color.a;
                    return new ParticleSystem.MinMaxGradient(single);
                case ParticleSystemGradientMode.TwoColors:
                    Color min = LinkYellow, max = LinkYellow;
                    min.a = original.colorMin.a;
                    max.a = original.colorMax.a;
                    return new ParticleSystem.MinMaxGradient(min, max);
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(YellowGradient(original.gradient));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(
                        YellowGradient(original.gradientMin), YellowGradient(original.gradientMax));
                case ParticleSystemGradientMode.RandomColor:
                    return new ParticleSystem.MinMaxGradient(YellowGradient(original.gradient));
                default:
                    return original;
            }
        }

        private static Gradient YellowGradient(Gradient original)
        {
            if (original == null) return new Gradient();
            GradientColorKey[] keys = original.colorKeys;
            for (int i = 0; i < keys.Length; i++)
                keys[i] = new GradientColorKey(LinkYellow, keys[i].time);
            var result = new Gradient();
            result.SetKeys(keys, original.alphaKeys);
            return result;
        }

        private static ParticleSystem.MinMaxCurve ToTwoCurves(
            ParticleSystem.MinMaxCurve value)
        {
            switch (value.mode)
            {
                case ParticleSystemCurveMode.TwoCurves:
                    return value;

                case ParticleSystemCurveMode.Curve:
                    return new ParticleSystem.MinMaxCurve(
                        value.curveMultiplier,
                        value.curve,
                        value.curve);

                case ParticleSystemCurveMode.TwoConstants:
                    return new ParticleSystem.MinMaxCurve(
                        1f,
                        ConstantCurve(value.constantMin),
                        ConstantCurve(value.constantMax));

                case ParticleSystemCurveMode.Constant:
                default:
                    AnimationCurve curve = ConstantCurve(value.constant);
                    return new ParticleSystem.MinMaxCurve(
                        1f, curve, curve);
            }
        }

        private static AnimationCurve ConstantCurve(float value)
        {
            return AnimationCurve.Linear(0f, value, 1f, value);
        }

        internal static void Cleanup()
        {
            LoggedPrefabs.Clear();
        }
    }
}
