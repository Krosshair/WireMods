using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace EnemyHudRework
{
    // What you're looking at, and for how long it still counts.
    public sealed partial class EnemyHudReworkPlugin
    {
        // ---- What you're looking at ----
        // One check for everything that cares (hit predictions, Dynamic
        // icons, Dynamic HP numbers): the game's own hover creature, read
        // once a frame. Enemies you just looked away from are remembered
        // with the time, for the delay settings; that list is tiny and
        // empties itself.
        private ConfigEntry<bool> _dynamicHp;
        private ConfigEntry<bool> _starCount;
        private ConfigEntry<bool> _enemyAimFade;
        // Players: the crosshair check the nameplate fading uses, with one
        // delay for both it and their Dynamic HP numbers.
        private ConfigEntry<bool> _playerDynamicHp;
        private ConfigEntry<float> _hoverDelay; // one delay for all of them

        private bool AimedAtRecently(EnemyUiState state)
        {
            return Time.time <= state.LastAimedAt + _hoverDelay.Value;
        }
        private Character _looked;
        private struct LookedAway { public Character C; public float At; }
        private readonly List<LookedAway> _lookedAway = new List<LookedAway>();

        private void UpdateLookTarget(Character lookedAt)
        {
            // Nothing uses the memory with all three off: just follow the
            // target (hit predictions read it directly) and keep no list.
            if (!_dynamicMode.Value && !_dynamicHp.Value && !_enemyAimFade.Value)
            {
                if (_lookedAway.Count > 0) _lookedAway.Clear();
                _looked = lookedAt;
                return;
            }
            float now = Time.time;
            if (!ReferenceEquals(lookedAt, _looked))
            {
                if (_looked != null) KeepInFocusUntil(_looked, now);
                _looked = lookedAt;
                MarkIconsDirty(lookedAt);
            }
            if (_lookedAway.Count == 0) return;
            float delay = _hoverDelay.Value;
            for (int i = _lookedAway.Count - 1; i >= 0; i--)
            {
                LookedAway e = _lookedAway[i];
                bool over = now >= e.At + delay;
                if (over) MarkIconsDirty(e.C);
                if (over || e.C == null) _lookedAway.RemoveAt(i);
            }
        }

        // Counts a creature as looked at until the given time (then its
        // delays run from there). Looking away uses "now"; a hit or parry
        // reveal uses the end of the reveal, so revealed creatures get their
        // HP numbers, icons and full opacity exactly like looked-at ones.
        private void KeepInFocusUntil(Character c, float until)
        {
            if (c == null) return;
            for (int i = 0; i < _lookedAway.Count; i++)
            {
                if (!ReferenceEquals(_lookedAway[i].C, c)) continue;
                LookedAway e = _lookedAway[i];
                e.At = Mathf.Max(e.At, until);
                _lookedAway[i] = e;
                MarkIconsDirty(c);
                return;
            }
            _lookedAway.Add(new LookedAway { C = c, At = until });
            MarkIconsDirty(c);
        }

        internal void NoteReveal(Character c)
        {
            if (!_dynamicMode.Value && !_dynamicHp.Value && !_enemyAimFade.Value) return;
            KeepInFocusUntil(c, Time.time + DamageShowSeconds);
        }

        private void MarkIconsDirty(Character c)
        {
            EnemyUiState st;
            if (c != null && _dynamicMode.Value && _states.TryGetValue(c, out st) && st != null && st.Weakness != null)
                st.Weakness.Dirty = true;
        }

        // Looked at now, or looked away less than the hover delay ago.
        private bool LookedAtRecently(Character c)
        {
            if (c == null) return false;
            if (ReferenceEquals(c, _looked)) return true;
            float now = Time.time;
            for (int i = 0; i < _lookedAway.Count; i++)
                if (ReferenceEquals(_lookedAway[i].C, c)) return now < _lookedAway[i].At + _hoverDelay.Value;
            return false;
        }
    }
}
