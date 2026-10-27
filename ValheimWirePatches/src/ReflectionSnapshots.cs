using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimWirePatches
{
    // ReflectionUpdate alternates two realtime probes, re-rendering one every
    // m_interval at the reference position and growing its box over
    // m_transitionDuration. Outside both boxes everything falls back to the
    // skybox, which is what the "broken" lighting is.
    //
    // 1. The probes are children of an object that follows the player. When a
    //    teleport ends that object jumps, dragging a probe that was rendered
    //    at the destination thousands of meters past it. Once the other probe
    //    is re-rendered, nothing covers the player until it has grown back.
    //    Fix: unparent the probes. Their position is set on every render anyway.
    // 2. Probes rendered mid-teleport capture an unloaded area. Fix: no renders
    //    while teleporting, one instant render on arrival (with the reference
    //    position forced to the player, since short teleports end before it
    //    updates), and another once TerrainLod has rebuilt the distant terrain.
    // 3. A render during a Thunder flash bakes the flash in. Fix: push the
    //    timer so the render happens after the flash.
    //
    // UpdateReflection gets inlined into ReflectionUpdate.Update, so it can't
    // be patched; everything goes through m_interval, m_updateTimer and
    // m_transitionDuration instead.
    internal static class ReflectionSnapshots
    {
        private const float FrozenInterval = 1000000f;
        private const float GiveUpSeconds = 40f;
        private const float TerrainWaitSeconds = 20f;
        private const float InstantFade = 0.01f;
        private const float FadeBackAfter = 2.9f;

        private const float FlashMargin = 0.3f;
        private const float FlashMax = 4f;
        private const float FlashDefault = 1f;
        private const float RecentRender = 0.5f;

        private enum Phase { None, Teleporting, WaitingForTerrain }
        private static Phase _phase;

        private static ReflectionUpdate _frozen;
        private static float _savedInterval = -1f;
        private static ReflectionUpdate _faded;
        private static float _savedFade = -1f;
        private static Transform _follower;

        private static readonly AccessTools.FieldRef<ReflectionUpdate, float> UpdateTimer =
            AccessTools.FieldRefAccess<ReflectionUpdate, float>("m_updateTimer");

        private static bool On => Plugin.TeleportLighting.Value;

        internal static void SettingChanged()
        {
            if (!On)
            {
                GiveUp();
                RestoreFade();
            }
            Unparent(ReflectionUpdate.instance);
        }

        private static void Unparent(ReflectionUpdate r)
        {
            if (r == null || r.m_probe1 == null || r.m_probe2 == null) return;
            if (_follower == null) _follower = r.m_probe1.transform.parent;
            if (_follower == null) return;
            Transform parent = On ? _follower.parent : _follower;
            r.m_probe1.transform.SetParent(parent, true);
            r.m_probe2.transform.SetParent(parent, true);
        }

        private static void Freeze()
        {
            ReflectionUpdate r = ReflectionUpdate.instance;
            if (!On || r == null) return;
            RestoreFade();
            // Raising the interval instead of holding the timer back keeps the
            // current probe fully grown.
            if (_frozen != r)
            {
                Unfreeze();
                _frozen = r;
                _savedInterval = r.m_interval;
                r.m_interval = FrozenInterval;
            }
            _phase = Phase.Teleporting;
            Plugin.Instance.CancelInvoke(nameof(Plugin.ReflectionGiveUp));
            Plugin.Instance.Invoke(nameof(Plugin.ReflectionGiveUp), GiveUpSeconds);
        }

        private static void Unfreeze()
        {
            if (_frozen != null && _savedInterval > 0f) _frozen.m_interval = _savedInterval;
            _frozen = null;
            _savedInterval = -1f;
        }

        private static void Arrived()
        {
            if (_phase != Phase.Teleporting) return;
            Plugin.Instance.CancelInvoke(nameof(Plugin.ReflectionGiveUp));
            Unfreeze();
            Render(true);
            if (DistantTerrainReady())
            {
                _phase = Phase.None;
                return;
            }
            _phase = Phase.WaitingForTerrain;
            Plugin.Instance.CancelInvoke(nameof(Plugin.ReflectionTerrainTimeout));
            Plugin.Instance.Invoke(nameof(Plugin.ReflectionTerrainTimeout), TerrainWaitSeconds);
        }

        internal static void TerrainTimeout()
        {
            if (_phase == Phase.WaitingForTerrain) _phase = Phase.None;
        }

        internal static void GiveUp()
        {
            Plugin.Instance.CancelInvoke(nameof(Plugin.ReflectionGiveUp));
            Unfreeze();
            _phase = Phase.None;
        }

        private static void Render(bool instant)
        {
            ReflectionUpdate r = ReflectionUpdate.instance;
            if (r == null || ZNet.instance == null) return;
            RestoreFade();
            UpdateTimer(r) = 0f;
            Player player = Player.m_localPlayer;
            if (player != null) ZNet.instance.SetReferencePosition(player.transform.position);
            if (instant)
            {
                _faded = r;
                _savedFade = r.m_transitionDuration;
                r.m_transitionDuration = InstantFade;
                Plugin.Instance.CancelInvoke(nameof(Plugin.ReflectionFadeBack));
                Plugin.Instance.Invoke(nameof(Plugin.ReflectionFadeBack), FadeBackAfter);
            }
            r.UpdateReflection();
        }

        internal static void RestoreFade()
        {
            if (_faded != null && _savedFade > 0f) _faded.m_transitionDuration = _savedFade;
            _faded = null;
            _savedFade = -1f;
        }

        [HarmonyPatch(typeof(ReflectionUpdate), "Start")]
        private static class ReflectionUpdateStart
        {
            private static void Postfix(ReflectionUpdate __instance)
            {
                _follower = null;
                _frozen = null;
                _savedInterval = -1f;
                _faded = null;
                _savedFade = -1f;
                _phase = Phase.None;
                if (!On) return;
                Unparent(__instance);
                Freeze();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
        private static class TeleportTo
        {
            private static void Postfix(Player __instance, bool __result)
            {
                if (__result && __instance == Player.m_localPlayer && __instance.IsTeleporting())
                    Freeze();
            }
        }

        // Called by UpdateTeleport when the teleport ends.
        [HarmonyPatch(typeof(Character), nameof(Character.ResetCloth))]
        private static class ResetCloth
        {
            private static void Postfix(Character __instance)
            {
                if (_phase == Phase.Teleporting && __instance == Player.m_localPlayer && !Player.m_localPlayer.IsTeleporting() &&
                    Game.instance != null && !Game.instance.WaitingForRespawn())
                    Arrived();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class OnSpawned
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                if (_phase == Phase.None) Freeze();
                Arrived();
            }
        }

        private static readonly List<TerrainLod> Lods = new List<TerrainLod>();
        private static readonly FieldInfo LodState = AccessTools.Field(typeof(TerrainLod), "m_heightmapState");
        private static readonly AccessTools.FieldRef<TerrainLod, Vector3> LodPoint =
            AccessTools.FieldRefAccess<TerrainLod, Vector3>("m_lastPoint");
        private static readonly AccessTools.FieldRef<TerrainLod, float> LodStep =
            AccessTools.FieldRefAccess<TerrainLod, float>("m_updateStepDistance");
        private const int LodDone = 2;

        private static bool DistantTerrainReady()
        {
            Player player = Player.m_localPlayer;
            if (LodState == null || player == null) return true;
            Vector3 pos = player.transform.position;
            foreach (TerrainLod lod in Lods)
            {
                if (lod == null || !lod.isActiveAndEnabled) continue;
                if (System.Convert.ToInt32(LodState.GetValue(lod)) != LodDone) return false;
                if (Utils.DistanceXZ(pos, LodPoint(lod)) > LodStep(lod)) return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(TerrainLod), "RebuildAllHeightmaps")]
        private static class RebuildAllHeightmaps
        {
            private static void Postfix()
            {
                if (_phase != Phase.WaitingForTerrain || !DistantTerrainReady()) return;
                _phase = Phase.None;
                Plugin.Instance.CancelInvoke(nameof(Plugin.ReflectionTerrainTimeout));
                Render(false);
            }
        }

        [HarmonyPatch(typeof(TerrainLod), "OnEnable")]
        private static class LodEnable
        {
            private static void Postfix(TerrainLod __instance)
            {
                Lods.RemoveAll(l => l == null);
                if (!Lods.Contains(__instance)) Lods.Add(__instance);
            }
        }

        [HarmonyPatch(typeof(TerrainLod), "OnDisable")]
        private static class LodDisable
        {
            private static void Postfix(TerrainLod __instance) => Lods.Remove(__instance);
        }

        private static readonly Dictionary<GameObject, float> FlashLength = new Dictionary<GameObject, float>();

        [HarmonyPatch(typeof(Thunder), "DoFlash")]
        private static class DoFlash
        {
            private static void Postfix(Thunder __instance)
            {
                ReflectionUpdate r = ReflectionUpdate.instance;
                if (!Plugin.LightningLighting.Value || r == null || _phase == Phase.Teleporting) return;

                float length = 0f;
                EffectList.EffectData[] effects = __instance.m_flashEffect?.m_effectPrefabs;
                if (effects != null)
                    foreach (EffectList.EffectData effect in effects)
                        if (effect != null && effect.m_enabled && effect.m_prefab != null)
                            length = Mathf.Max(length, LengthOf(effect.m_prefab));
                if (length <= 0f) length = FlashDefault;

                // Render due during the flash, or one that just started and may
                // still be catching it: move it to right after the flash.
                float latest = r.m_interval - Mathf.Min(FlashMax, length + FlashMargin);
                float timer = UpdateTimer(r);
                if (timer > latest || timer < RecentRender)
                    UpdateTimer(r) = Mathf.Max(0f, latest);
            }

            private static float LengthOf(GameObject prefab)
            {
                if (FlashLength.TryGetValue(prefab, out float length)) return length;
                length = 0f;
                foreach (LightFlicker light in prefab.GetComponentsInChildren<LightFlicker>(true))
                    if (light.m_ttl > 0f) length = Mathf.Max(length, light.m_ttl);
                if (length <= 0f)
                    foreach (TimedDestruction timed in prefab.GetComponentsInChildren<TimedDestruction>(true))
                        length = Mathf.Max(length, timed.m_timeout);
                FlashLength[prefab] = length;
                return length;
            }
        }
    }
}
