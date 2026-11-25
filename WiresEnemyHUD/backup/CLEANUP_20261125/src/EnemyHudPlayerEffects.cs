using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnemyHudRework
{
    public sealed partial class EnemyHudReworkPlugin
    {
        // Other-player nameplates have four status-icon places. Only the
        // owner knows its live SEMan; it shares the currently selected icon
        // hashes using the existing four ZDO keys.
        //
        // This is lifecycle-driven. Rebuild the cached groups when a status
        // starts, ends, or the character acquires network ownership. Only
        // while a VISIBLE group contains multiple effects, an InvokeRepeating
        // switches that group's icon every half second. No frame polling.
        // Strictly six groups, in display priority order. No fallback group.
        private const int PlayerRested = 0;
        private const int PlayerPower = 1;
        private const int PlayerPotionBuff = 2;
        private const int PlayerPotionCooldown = 3;
        private const int PlayerWeather = 4;
        private const int PlayerEncumbered = 5;

        private sealed class PlayerEffectCycle
        {
            internal readonly List<int>[] Groups = new List<int>[6];
            internal readonly HashSet<int> Seen = new HashSet<int>();
            internal readonly int[] Visible = new int[MaxOtherEffects];
            internal ZDO Zdo;
            internal int PowerIndex;
            internal int PotionBuffIndex;
            internal int CooldownIndex;
            internal bool CyclePower, CyclePotionBuff, CycleCooldown;

            internal PlayerEffectCycle()
            {
                for (int i = 0; i < Groups.Length; i++)
                    Groups[i] = new List<int>();
            }

            internal void Clear()
            {
                foreach (List<int> group in Groups) group.Clear();
                Seen.Clear();
                PowerIndex = PotionBuffIndex = CooldownIndex = 0;
            }

            internal bool NeedsCycles => CyclePower || CyclePotionBuff || CycleCooldown;
        }

        private readonly Dictionary<Player, PlayerEffectCycle> _playerEffectsByOwner =
            new Dictionary<Player, PlayerEffectCycle>();
        private readonly List<Player> _goneEffectPlayers = new List<Player>();
        private bool _playerEffectTimerOn;

        // Match real Valheim prefab IDs, not localized display strings.
        // The game's active potion effects use "Potion_*", including
        // Potion_hasty (Ratatosk) and Potion_LightFoot (Lightfoot).
        // m_cooldownIcon is the same cooldown flag shown by vanilla HUD.
        private static int PlayerEffectGroup(StatusEffect effect)
        {
            if (effect == null || effect.m_hidden || effect.m_icon == null ||
                StatusBit(effect.NameHash()) != 0 || effect is SE_Shield)
                return -1;

            int hash = effect.NameHash();
            if (hash == SEMan.s_statusEffectRested) return PlayerRested;

            string name = effect.name ?? string.Empty;
            if (name.StartsWith("GP_", StringComparison.OrdinalIgnoreCase))
                return PlayerPower;

            if (name.StartsWith("Potion_", StringComparison.OrdinalIgnoreCase))
                return effect.m_cooldownIcon ? PlayerPotionCooldown : PlayerPotionBuff;

            if (hash == SEMan.s_statusEffectWet ||
                hash == SEMan.s_statusEffectCold ||
                hash == SEMan.s_statusEffectFreezing)
                return PlayerWeather;

            // Last-priority visible impediments: encumbered, tarred, harpooned.
            // Valheim's actual prefab hash is "Tared", not "Tarred".
            // Harpooned has a visible rope and shares the same low priority.
            if (hash == SEMan.s_statusEffectEncumbered ||
                hash == SEMan.s_statusEffectTared ||
                effect is SE_Harpooned)
                return PlayerEncumbered;

            // No debuffs, generic timed effects, gear bonuses or "useful"
            // guesses. Any effect not in the six requested groups is hidden.
            return -1;
        }
        private static void CollectPlayerEffect(PlayerEffectCycle state,
            StatusEffect effect, StatusEffect leaving)
        {
            if (effect == null || ReferenceEquals(effect, leaving)) return;
            int group = PlayerEffectGroup(effect);
            if (group < 0) return;
            int hash = effect.NameHash();
            if (hash == 0 || !state.Seen.Add(hash)) return;
            if (group == PlayerWeather && hash == SEMan.s_statusEffectWet)
                state.Groups[group].Insert(0, hash);
            else
                state.Groups[group].Add(hash);
        }

        private void RebuildPlayerEffects(Player player, ZDO zdo,
            StatusEffect leaving, StatusEffect starting)
        {
            if (player == null || zdo == null) return;
            if (!_playerEffectsByOwner.TryGetValue(player, out PlayerEffectCycle state))
                _playerEffectsByOwner[player] = state = new PlayerEffectCycle();

            state.Zdo = zdo;
            state.Clear();
            SEMan effects = player.GetSEMan();
            if (effects != null)
                foreach (StatusEffect e in effects.GetStatusEffects())
                    CollectPlayerEffect(state, e, leaving);
            // StatusEffect.Setup can run before SEMan adds the new instance.
            if (starting != null)
                CollectPlayerEffect(state, starting, leaving);

            WritePlayerEffectSelection(state);
            if (state.Seen.Count == 0)
                _playerEffectsByOwner.Remove(player);
            RefreshPlayerEffectTimer();
        }

        private void WritePlayerEffectSelection(PlayerEffectCycle state)
        {
            int[] visible = state.Visible;
            Array.Clear(visible, 0, visible.Length);
            int n = 0;

            List<int> rested = state.Groups[PlayerRested];
            if (rested.Count != 0)
                visible[n++] = rested[0];

            // Rested is permanently in slot zero whenever active.
            // Only the three multi-effect groups cycle within one icon each.
            List<int> powers = state.Groups[PlayerPower];
            state.CyclePower = powers.Count > 1 && n < MaxOtherEffects;
            if (powers.Count != 0 && n < MaxOtherEffects)
                visible[n++] = powers[state.PowerIndex % powers.Count];

            List<int> buffs = state.Groups[PlayerPotionBuff];
            state.CyclePotionBuff = buffs.Count > 1 && n < MaxOtherEffects;
            if (buffs.Count != 0 && n < MaxOtherEffects)
                visible[n++] = buffs[state.PotionBuffIndex % buffs.Count];

            List<int> cooldowns = state.Groups[PlayerPotionCooldown];
            state.CycleCooldown = cooldowns.Count > 1 && n < MaxOtherEffects;
            if (cooldowns.Count != 0 && n < MaxOtherEffects)
                visible[n++] = cooldowns[state.CooldownIndex % cooldowns.Count];

            foreach (int hash in state.Groups[PlayerWeather])
            {
                if (n == MaxOtherEffects) break;
                visible[n++] = hash;
            }

            foreach (int hash in state.Groups[PlayerEncumbered])
            {
                if (n == MaxOtherEffects) break;
                visible[n++] = hash;
            }
            for (int i = 0; i < MaxOtherEffects; i++)
                SetSharedInt(state.Zdo, OtherKeys[i], visible[i]);
        }

        private void RefreshPlayerEffectTimer()
        {
            bool needed = false;
            foreach (PlayerEffectCycle s in _playerEffectsByOwner.Values)
                if (s.NeedsCycles) { needed = true; break; }

            if (needed == _playerEffectTimerOn) return;
            _playerEffectTimerOn = needed;
            if (needed)
                InvokeRepeating(nameof(CyclePlayerEffects), 0.5f, 0.5f);
            else
                CancelInvoke(nameof(CyclePlayerEffects));
        }

        private void CyclePlayerEffects()
        {
            _goneEffectPlayers.Clear();
            foreach (KeyValuePair<Player, PlayerEffectCycle> pair in _playerEffectsByOwner)
            {
                Player player = pair.Key;
                PlayerEffectCycle s = pair.Value;
                ZNetView view = player != null ? CharacterView(player) : null;
                if (player == null || view == null || !view.IsValid() ||
                    !view.IsOwner() || view.GetZDO() != s.Zdo)
                {
                    _goneEffectPlayers.Add(player);
                    continue;
                }

                if (s.CyclePower) s.PowerIndex = (s.PowerIndex + 1) % s.Groups[PlayerPower].Count;
                if (s.CyclePotionBuff) s.PotionBuffIndex = (s.PotionBuffIndex + 1) % s.Groups[PlayerPotionBuff].Count;
                if (s.CycleCooldown) s.CooldownIndex = (s.CooldownIndex + 1) % s.Groups[PlayerPotionCooldown].Count;
                if (s.NeedsCycles)
                    WritePlayerEffectSelection(s);
            }
            foreach (Player player in _goneEffectPlayers)
                _playerEffectsByOwner.Remove(player);
            if (_goneEffectPlayers.Count != 0)
                RefreshPlayerEffectTimer();
        }

        // Event-driven cleanup for player despawn/death. In particular,
        // players with a single Rested/power/mead effect never start the
        // 0.5s cycling timer, so the timer cannot prune their cache.
        internal void ForgetPlayerEffectOwner(Player player)
        {
            if (ReferenceEquals(player, null)) return;
            if (_playerEffectsByOwner.Remove(player))
                RefreshPlayerEffectTimer();
        }

        private void StopPlayerEffectCycles()
        {
            if (_playerEffectTimerOn)
                CancelInvoke(nameof(CyclePlayerEffects));
            _playerEffectTimerOn = false;
            _playerEffectsByOwner.Clear();
            _goneEffectPlayers.Clear();
        }
    }
}