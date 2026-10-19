using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresWeaponMaster
{
    internal static class WeaponVisuals
    {
        private const int LaneCount = 7;

        private static readonly FieldInfo VisEquipmentField =
            AccessTools.Field(typeof(Humanoid), "m_visEquipment");

        private sealed class VisualState
        {
            internal readonly GameObject[] Instances =
                new GameObject[LaneCount];

            internal readonly int[] Hashes =
                new int[LaneCount];

            internal readonly int[] Metas =
                new int[LaneCount];
        }

        private static readonly Dictionary<VisEquipment, VisualState> States =
            new Dictionary<VisEquipment, VisualState>();

        internal static VisEquipment GetVisEquipment(Humanoid humanoid)
        {
            return humanoid == null || VisEquipmentField == null
                ? null
                : VisEquipmentField.GetValue(humanoid) as VisEquipment;
        }

        internal static void RefreshLocal()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            VisEquipment vis = GetVisEquipment(player);
            if (vis == null)
                return;

            HolsterSelection[] selections = BuildLocalSelections();
            MultiplayerSync.Publish(vis, selections);
            ApplySelections(vis, selections);
        }

        internal static void RefreshConfig()
        {
            RefreshLocal();

            var visuals =
                new List<VisEquipment>(States.Keys);

            for (int i = 0; i < visuals.Count; i++)
            {
                VisEquipment vis = visuals[i];
                if (vis != null)
                    SyncNetwork(vis);
            }
        }

        private static HolsterSelection[] BuildLocalSelections()
        {
            HolsterSelection[] selections =
                new HolsterSelection[LaneCount];

            for (int laneValue = 1;
                 laneValue < LaneCount;
                 laneValue++)
            {
                HolsterLane lane = (HolsterLane)laneValue;

                if (!WeaponMasterConfig.IsLaneEnabled(lane))
                    continue;

                ItemDrop.ItemData item =
                    WeaponMasterController.GetVisibleForLane(lane);

                if (item == null || item.m_dropPrefab == null)
                    continue;

                selections[laneValue] =
                    new HolsterSelection(
                        item.m_dropPrefab.name.GetStableHashCode(),
                        item.m_variant,
                        item.m_quality);
            }

            return selections;
        }

        internal static void SyncNetwork(VisEquipment vis)
        {
            if (vis == null ||
                !vis.m_isPlayer ||
                vis.m_isArmorStand)
                return;

            VisualState state = GetOrCreateState(vis);
            ZDO zdo = MultiplayerSync.GetZdo(vis);
            int vanillaLeftBack =
                zdo != null ? zdo.GetInt(ZDOVars.s_leftBackItem) : 0;
            int vanillaRightBack =
                zdo != null ? zdo.GetInt(ZDOVars.s_rightBackItem) : 0;

            for (int laneValue = 1;
                 laneValue < LaneCount;
                 laneValue++)
            {
                HolsterLane lane = (HolsterLane)laneValue;
                ApplySelection(
                    vis,
                    state,
                    lane,
                    MultiplayerSync.Read(vis, lane),
                    vanillaLeftBack,
                    vanillaRightBack);
            }
        }

        private static void ApplySelections(
            VisEquipment vis,
            HolsterSelection[] selections)
        {
            if (vis == null || selections == null)
                return;

            VisualState state = GetOrCreateState(vis);
            ZDO zdo = MultiplayerSync.GetZdo(vis);
            int vanillaLeftBack =
                zdo != null ? zdo.GetInt(ZDOVars.s_leftBackItem) : 0;
            int vanillaRightBack =
                zdo != null ? zdo.GetInt(ZDOVars.s_rightBackItem) : 0;

            for (int laneValue = 1;
                 laneValue < LaneCount;
                 laneValue++)
            {
                ApplySelection(
                    vis,
                    state,
                    (HolsterLane)laneValue,
                    selections[laneValue],
                    vanillaLeftBack,
                    vanillaRightBack);
            }
        }

        private static VisualState GetOrCreateState(VisEquipment vis)
        {
            VisualState state;
            if (!States.TryGetValue(vis, out state))
            {
                state = new VisualState();
                States.Add(vis, state);
            }

            return state;
        }

        private static void ApplySelection(
            VisEquipment vis,
            VisualState state,
            HolsterLane lane,
            HolsterSelection selection,
            int vanillaLeftBack,
            int vanillaRightBack)
        {
            int index = (int)lane;

            if (!WeaponMasterConfig.IsLaneEnabled(lane))
                selection = default;

            int hash = selection.Hash;
            int meta = hash != 0 ? selection.PackedMeta : 0;

            // Vanilla may already be showing this exact prefab as one
            // of its normal two hidden/back items. Suppress the custom
            // copy while that is true so network timing can never
            // produce a duplicate model.
            if (hash != 0 &&
                (hash == vanillaLeftBack ||
                 hash == vanillaRightBack))
            {
                hash = 0;
                meta = 0;
                selection = default;
            }

            if (state.Hashes[index] == hash &&
                state.Metas[index] == meta)
                return;

            Replace(
                vis,
                state,
                lane,
                selection,
                hash,
                meta);
        }

        private static void Replace(
            VisEquipment vis,
            VisualState state,
            HolsterLane lane,
            HolsterSelection selection,
            int hash,
            int meta)
        {
            int index = (int)lane;

            DestroyInstance(state, index);

            state.Hashes[index] = hash;
            state.Metas[index] = meta;

            if (hash == 0)
                return;

            bool rightHand =
                ItemClassifier.UsesRightHandForBackAttach(lane);

            state.Instances[index] =
                VanillaAttachment.AttachBackItem(
                    vis,
                    hash,
                    selection.Variant,
                    rightHand,
                    selection.Quality);
        }

        private static void DestroyInstance(
            VisualState state,
            int index)
        {
            if (state.Instances[index] != null)
                Object.Destroy(state.Instances[index]);

            state.Instances[index] = null;
            state.Hashes[index] = 0;
            state.Metas[index] = 0;
        }

        internal static void Unregister(VisEquipment vis)
        {
            if (vis == null)
                return;

            VisualState state;
            if (!States.TryGetValue(vis, out state))
                return;

            for (int i = 1; i < LaneCount; i++)
                DestroyInstance(state, i);

            States.Remove(vis);
        }

        internal static void Cleanup()
        {
            foreach (KeyValuePair<VisEquipment, VisualState> pair
                     in States)
            {
                VisualState state = pair.Value;
                for (int i = 1; i < LaneCount; i++)
                    DestroyInstance(state, i);
            }

            States.Clear();
        }
    }
}
