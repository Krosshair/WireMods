using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    // Vanilla destroys a StationExtension connection effect the instant
    // StopConnectionEffect runs. Keep the old effect at its original
    // location and let it finish emitting/playing instead. The next
    // vanilla StartConnectionEffect can spawn normally immediately.
    //
    // This is a client-side presentation change only. No station
    // placement, station connectivity or gameplay methods are changed.
    internal static class VanillaConnectionFade
    {
        private sealed class Tail
        {
            internal GameObject Instance;
            internal ParticleSystem[] Particles;
            internal float Started;
        }

        private static readonly List<Tail> Tails = new List<Tail>();
        private const float MaximumDrainSeconds = 6f;
        private const int MaximumOutstandingTails = 128;

        internal static void StopVanillaConnection(GameObject instance)
        {
            if (instance == null)
                return;

            // Vanilla non-particle prefabs don't benefit from delaying
            // destruction, so leave those behaving as they did.
            ParticleSystem[] particles =
                instance.GetComponentsInChildren<ParticleSystem>(true);
            bool alive = false;

            if (instance.activeInHierarchy)
            {
                foreach (ParticleSystem particle in particles)
                {
                    if (particle == null ||
                        !particle.gameObject.activeInHierarchy)
                        continue;

                    particle.Stop(
                        false, ParticleSystemStopBehavior.StopEmitting);

                    if (particle.IsAlive(false))
                        alive = true;
                }
            }

            if (!alive)
            {
                UnityEngine.Object.Destroy(instance);
                return;
            }

            if (Tails.Count >= MaximumOutstandingTails)
            {
                // Bound retained particles even if a player rapidly
                // hovers hundreds of extensions.
                Tail oldest = Tails[0];
                Tails.RemoveAt(0);
                if (oldest.Instance != null)
                    UnityEngine.Object.Destroy(oldest.Instance);
            }

            Tails.Add(new Tail
            {
                Instance = instance,
                Particles = particles,
                Started = Time.unscaledTime
            });
        }

        internal static void Tick()
        {
            for (int i = Tails.Count - 1; i >= 0; --i)
            {
                Tail tail = Tails[i];

                if (tail.Instance == null)
                {
                    Tails.RemoveAt(i);
                    continue;
                }

                bool stillAlive = false;
                if (Time.unscaledTime - tail.Started <
                    MaximumDrainSeconds)
                {
                    foreach (ParticleSystem particle in tail.Particles)
                    {
                        if (particle != null &&
                            particle.IsAlive(false))
                        {
                            stillAlive = true;
                            break;
                        }
                    }
                }

                if (stillAlive)
                    continue;

                Tails.RemoveAt(i);
                UnityEngine.Object.Destroy(tail.Instance);
            }
        }

        internal static void Cleanup()
        {
            foreach (Tail tail in Tails)
                if (tail.Instance != null)
                    UnityEngine.Object.Destroy(tail.Instance);

            Tails.Clear();
        }
    }

    [HarmonyPatch(typeof(StationExtension),
        nameof(StationExtension.StopConnectionEffect))]
    internal static class VanillaStationConnectionStopPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref GameObject ___m_connection)
        {
            if (___m_connection == null)
                return true;

            // Release the actual vanilla reference immediately. This
            // prevents a subsequent StartConnectionEffect from moving
            // or replacing the effect whose particles are fading out.
            GameObject old = ___m_connection;
            ___m_connection = null;
            VanillaConnectionFade.StopVanillaConnection(old);
            return false;
        }
    }
}
