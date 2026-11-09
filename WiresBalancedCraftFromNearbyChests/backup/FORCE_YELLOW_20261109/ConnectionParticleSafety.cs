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
