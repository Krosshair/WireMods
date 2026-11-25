using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EnemyHudRework
{
    // Sharing stagger: only a creature's owner knows it.
    public sealed partial class EnemyHudReworkPlugin
    {
        private float GetDisplayStaggerPercentage(Character character)
        {
            if (character == null)
                return 0f;

            if (!_syncStaggerForMultiplayer.Value)
                return character.GetStaggerPercentage();

            ZNetView nview = CharacterView(character);
            if (nview == null || !nview.IsValid() || nview.IsOwner())
                return character.GetStaggerPercentage();

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return character.GetStaggerPercentage();

            return Mathf.Clamp01(zdo.GetFloat(StaggerZdoKey, character.GetStaggerPercentage()));
        }

        private static readonly AccessTools.FieldRef<Character, ZNetView> CharacterView =
            AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        // Runs on every character's stagger tick, so it bails out as early
        // as it can: alone in the world (nobody to tell), or not staggered
        // and nothing left to send. Only staggered creatures are tracked.
        internal void SyncOwnedStagger(Character character)
        {
            if (!_syncStaggerForMultiplayer.Value || character == null || ZNet.instance == null ||
                ZNet.instance.GetPeers().Count == 0)
                return;

            float value = Mathf.Clamp01(character.GetStaggerPercentage());
            int id = character.GetInstanceID();
            StaggerSyncState sync;
            bool tracked = _syncStates.TryGetValue(id, out sync);
            if (value <= 0.0001f && (!tracked || sync.LastSent <= 0.0001f))
            {
                if (tracked) _syncStates.Remove(id);
                return;
            }

            ZNetView nview = CharacterView(character);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
                return;

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return;

            float now = Time.time;
            if (!tracked)
            {
                // Creatures that died or left while staggered are never
                // updated again: drop those first, so the list can't grow.
                if (_syncStates.Count >= 32)
                    PruneSyncStates(now);
                sync = new StaggerSyncState();
                _syncStates[id] = sync;
            }

            if (now < sync.NextSendTime)
                return;

            float delta = Mathf.Abs(value - sync.LastSent);

            bool endpointChanged =
                (value <= 0.0001f && sync.LastSent > 0.0001f) ||
                (value >= 0.9999f && sync.LastSent < 0.9999f);

            if (sync.LastSent < 0f || delta >= Mathf.Max(0.0001f, SyncMinimumDelta) || endpointChanged)
            {
                zdo.Set(StaggerZdoKey, value);
                sync.LastSent = value;
            }

            sync.NextSendTime = now + SyncInterval;
        }

        private readonly List<int> _staleSync = new List<int>();

        // A staggered creature is checked every tick; one not seen for a few
        // seconds is gone.
        private void PruneSyncStates(float now)
        {
            _staleSync.Clear();
            foreach (KeyValuePair<int, StaggerSyncState> pair in _syncStates)
                if (now - pair.Value.NextSendTime > 5f)
                    _staleSync.Add(pair.Key);
            foreach (int id in _staleSync)
                _syncStates.Remove(id);
        }
    }
}
