using System.Collections.Generic;
using UnityEngine;

namespace EnemyHudRework
{
    // Damage type icons follow a creature's real resistances, including what
    // its status effects change (tar, potions, boss powers...). Status
    // effects only exist on the machine running the creature, so that one
    // works the result out whenever an effect starts or ends and shares it:
    // all ten damage types' strength in one number. Nameplates redo their
    // icons only when that number changes. Nothing runs between changes.
    public sealed partial class EnemyHudReworkPlugin
    {
        private static readonly int RanksKey = "wire.enemyhud.ranks".GetStableHashCode();
        private const int RanksPresent = 1 << 30;
        private readonly HashSet<Character> _ranksPending = new HashSet<Character>();
        private readonly List<Character> _ranksWork = new List<Character>();

        // 3 bits per damage type: its strength (-4 immune ... 3 very weak) + 4.
        private static int PackRanks(HitData.DamageModifiers mods)
        {
            int packed = RanksPresent;
            for (int i = 0; i < DamageKinds; i++)
                packed |= (Classify(Modifier(mods, (DamageKind)i)) + 4) << (i * 3);
            return packed;
        }

        private static int UnpackRank(int packed, int kind)
        {
            return ((packed >> (kind * 3)) & 7) - 4;
        }

        // A status effect started or ended on a creature this machine runs,
        // or a player here changed armor.
        // Worked out next frame: when an effect ends it's still listed.
        internal void OnResistancesMayChange(Character c)
        {
            if (c == null) return;
            _ranksPending.Add(c);
        }

        private void ProcessPendingRanks()
        {
            if (_ranksPending.Count == 0) return;
            _ranksWork.Clear();
            _ranksWork.AddRange(_ranksPending);
            _ranksPending.Clear();
            foreach (Character c in _ranksWork)
            {
                if (c == null || c.IsDead()) continue;
                ZNetView view = CharacterView(c);
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;
                ZDO zdo = view.GetZDO();
                int packed = PackRanks(c.GetDamageModifiers());
                if (GetSharedInt(zdo, RanksKey) != packed) SetSharedInt(zdo, RanksKey, packed);
                EnemyUiState state;
                if (_states.TryGetValue(c, out state) && state != null && state.Weakness != null)
                {
                    state.Weakness.KindsReady = false;
                    state.Weakness.Dirty = true;
                }
            }
        }

        // Other machines' creatures: redo the icons when the shared number
        // changed. One read, only when the creature's network data changed.
        private void UpdateSharedRanks(EnemyUiState state)
        {
            WeaknessUi ui = state.Weakness;
            if (ui == null || state.Character == null) return;
            if (state.BubbleView == null) state.BubbleView = CharacterView(state.Character);
            ZDO zdo = state.BubbleView != null && state.BubbleView.IsValid() ? state.BubbleView.GetZDO() : null;
            if (zdo == null) return;
            uint revision = SharedRevision(zdo);
            if (revision == ui.RanksRevision) return;
            ui.RanksRevision = revision;
            int packed = GetSharedInt(zdo, RanksKey);
            if (packed == ui.Ranks) return;
            ui.Ranks = packed;
            ui.KindsReady = false;
            ui.Dirty = true;
        }

        // The creature's strengths: shared by whoever runs it when known,
        // else read here (base resistances and armor).
        private static void ReadKinds(WeaknessUi ui, Character character)
        {
            ZNetView view = CharacterView(character);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            int packed = zdo != null ? GetSharedInt(zdo, RanksKey) : 0;
            if ((packed & RanksPresent) != 0 && (zdo == null || !zdo.IsOwner()))
            {
                for (int i = 0; i < DamageKinds; i++) ui.Kind[i] = UnpackRank(packed, i);
                return;
            }
            HitData.DamageModifiers mods = character.GetDamageModifiers();
            for (int i = 0; i < DamageKinds; i++)
                ui.Kind[i] = Classify(Modifier(mods, (DamageKind)i));
        }

        // Saved creatures lose their status effects on reload or when another
        // machine takes them over: the shared number goes with them.
        internal static void DropRanks(ZDO zdo)
        {
            if (zdo != null && zdo.GetInt(RanksKey) != 0) zdo.RemoveInt(RanksKey);
        }

        // Armor sets a player's resistances: shared again on every equip
        // or unequip (the game sets equipment up again each time).
        [HarmonyLib.HarmonyPatch(typeof(Humanoid), "SetupEquipment")]
        private static class SetupEquipmentRanksPatch
        {
            private static void Postfix(Humanoid __instance)
            {
                if (Instance != null && __instance is Player) Instance.OnResistancesMayChange(__instance);
            }
        }
    }
}
