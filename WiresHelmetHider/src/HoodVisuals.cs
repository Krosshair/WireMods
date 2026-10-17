using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresHoodToggle
{
    internal static class HoodVisuals
    {
        // Keep the existing network key so 0.2.0 multiplayer state
        // continues cleanly across the update.
        private static readonly int HiddenKey =
            "wire.hoodtoggle.lowered".GetStableHashCode();

        private static readonly FieldInfo HelmetInstanceField =
            AccessTools.Field(
                typeof(VisEquipment),
                "m_helmetItemInstance");

        private static readonly FieldInfo CurrentHelmetHashField =
            AccessTools.Field(
                typeof(VisEquipment),
                "m_currentHelmetItemHash");

        private static readonly FieldInfo NViewField =
            AccessTools.Field(
                typeof(VisEquipment),
                "m_nview");

        private static readonly FieldInfo EquippedHelmetField =
            AccessTools.Field(
                typeof(Humanoid),
                "m_helmetItem");

        private static readonly MethodInfo
            UpdateEquipmentVisualsMethod =
                AccessTools.Method(
                    typeof(VisEquipment),
                    "UpdateEquipmentVisuals");

        private static readonly MethodInfo
            TriggerEquipEffectMethod =
                AccessTools.Method(
                    typeof(Humanoid),
                    "TriggerEquipEffect");

        private static readonly Dictionary<int, VisualState>
            States = new Dictionary<int, VisualState>();

        private static Player _trackedPlayer;
        private static ItemDrop.ItemData _trackedHelmet;
        private static bool _restoredForPlayer;
        private static VisEquipment _menuPreviewVis;
        private static long _menuPreviewPlayerId;

        private sealed class VisualState
        {
            internal GameObject Helmet;
            internal int HelmetId;
            internal int HelmetHash;
            internal bool Hidden;
        }

        internal static void UpdateLocal()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                HelmetToggleAnimation.Cancel(commit: false);
                _trackedPlayer = null;
                _trackedHelmet = null;
                _restoredForPlayer = false;
                return;
            }

            ItemDrop.ItemData helmet = EquippedHelmetField?.GetValue(player)
                as ItemDrop.ItemData;

            if (player != _trackedPlayer)
            {
                HelmetToggleAnimation.Cancel(commit: false);
                _trackedPlayer = player;
                _trackedHelmet = helmet;
                _restoredForPlayer = false;
            }

            // Restore after the player profile and inventory have loaded,
            // including on a fresh character spawn after restarting Valheim.
            if (!_restoredForPlayer)
                RestoreLocalSavedState(player);

            if (player == _trackedPlayer && _restoredForPlayer &&
                !ReferenceEquals(_trackedHelmet, helmet))
            {
                _trackedHelmet = helmet;
                // A real helmet change supersedes the visual-only animation.
                HelmetToggleAnimation.Cancel(commit: false);
                if (Plugin.ResetOnHeadgearChange.Value)
                {
                    ZNetView view = player.GetComponent<ZNetView>();
                    if (view != null && view.IsValid() && view.IsOwner())
                    {
                        // A real equipment change clears the persistent
                        // entry even if the network visual was not yet set.
                        SavedHelmetStates.SetHidden(player.GetPlayerID(), false);
                        ZDO zdo = view.GetZDO();
                        if (zdo != null && zdo.GetBool(HiddenKey, false))
                        {
                            zdo.Set(HiddenKey, false);
                            VisEquipment vis = player.GetComponent<VisEquipment>();
                            if (vis != null)
                            {
                                RefreshHairAndBeard(vis);
                                Apply(vis, force: true);
                            }
                        }
                    }
                }
            }

            HelmetToggleAnimation.Update(player);
        }

        internal static void ToggleLocal(Player player)
        {
            if (player == null)
                return;

            VisEquipment vis =
                player.GetComponent<VisEquipment>();
            ZNetView view =
                player.GetComponent<ZNetView>();

            if (vis == null ||
                view == null ||
                !view.IsValid() ||
                !view.IsOwner())
                return;

            ZDO zdo = view.GetZDO();
            if (zdo == null)
                return;

            bool hidden =
                !(HelmetToggleAnimation.Pending ? !zdo.GetBool(HiddenKey, false) : zdo.GetBool(HiddenKey, false));

            ItemDrop.ItemData helmetItem =
                EquippedHelmetField?.GetValue(player)
                as ItemDrop.ItemData;

            if (helmetItem == null)
                return;

            HelmetToggleAnimation.Start(player, hidden);
        }

        internal static void CommitToggle(Player player, bool hidden)
        {
            if (player == null)
                return;
            VisEquipment vis = player.GetComponent<VisEquipment>();
            ZNetView view = player.GetComponent<ZNetView>();
            if (vis == null || view == null || !view.IsValid() || !view.IsOwner())
                return;
            ZDO zdo = view.GetZDO();
            if (zdo == null)
                return;
            ItemDrop.ItemData helmetItem =
                EquippedHelmetField?.GetValue(player) as ItemDrop.ItemData;

            zdo.Set(HiddenKey, hidden);
            SavedHelmetStates.SetHidden(player.GetPlayerID(), hidden);

            // Update hair/beard while the old headpiece state is
            // still covering the transition, preventing a flash.
            RefreshHairAndBeard(vis);

            Apply(vis, force: true);
            PlayToggleEffects(
                player,
                vis,
                helmetItem,
                hidden);

            if (helmetItem != null)
                ShowNotification(hidden);
        }

        internal static bool IsHidden(
            VisEquipment vis)
        {
            if (vis == null ||
                !vis.m_isPlayer ||
                vis.m_isArmorStand)
                return false;

            // Menu previews have networking disabled: the persisted
            // player ID, not a ZDO, owns the preview's hidden state.
            if (vis == _menuPreviewVis)
                return SavedHelmetStates.IsHidden(_menuPreviewPlayerId);

            ZNetView view = GetView(vis);
            ZDO zdo =
                view != null && view.IsValid()
                    ? view.GetZDO()
                    : null;

            return zdo != null &&
                   zdo.GetBool(HiddenKey, false);
        }

        internal static void Apply(
            VisEquipment vis,
            bool force = false)
        {
            if (vis == null ||
                !vis.m_isPlayer ||
                vis.m_isArmorStand ||
                HelmetInstanceField == null ||
                CurrentHelmetHashField == null)
                return;

            int id = vis.GetInstanceID();

            if (!States.TryGetValue(
                    id,
                    out VisualState state))
            {
                state = new VisualState();
                States[id] = state;
            }

            GameObject helmet =
                HelmetInstanceField.GetValue(vis)
                as GameObject;

            int helmetId =
                helmet != null
                    ? helmet.GetInstanceID()
                    : 0;

            int helmetHash =
                (int)CurrentHelmetHashField
                    .GetValue(vis);

            bool hidden = IsHidden(vis);

            if (!force &&
                state.HelmetId == helmetId &&
                state.HelmetHash == helmetHash &&
                state.Hidden == hidden)
            {
                return;
            }

            Restore(state);

            state.Helmet = helmet;
            state.HelmetId = helmetId;
            state.HelmetHash = helmetHash;
            state.Hidden = hidden;

            if (hidden &&
                helmet != null &&
                helmetHash != 0)
            {
                helmet.SetActive(false);
            }
        }

        internal static void Clear()
        {
            foreach (VisualState state in
                     States.Values)
            {
                Restore(state);
            }

            States.Clear();
            _trackedPlayer = null;
            _trackedHelmet = null;
            _restoredForPlayer = false;
            _menuPreviewVis = null;
            _menuPreviewPlayerId = 0L;
        }

        internal static void RestoreLocalSavedState(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
                return;

            if (player != _trackedPlayer)
            {
                _trackedPlayer = player;
                _trackedHelmet = EquippedHelmetField?.GetValue(player)
                    as ItemDrop.ItemData;
                _restoredForPlayer = false;
            }

            if (_restoredForPlayer)
                return;

            long id = player.GetPlayerID();
            ZNetView view = player.GetComponent<ZNetView>();
            if (id == 0L || view == null || !view.IsValid() || !view.IsOwner())
                return;

            _restoredForPlayer = true;
            if (!SavedHelmetStates.IsHidden(id) ||
                _trackedHelmet == null)
                return;

            ZDO zdo = view.GetZDO();
            if (zdo == null)
                return;

            zdo.Set(HiddenKey, true);
            VisEquipment vis = player.GetComponent<VisEquipment>();
            if (vis != null)
            {
                RefreshHairAndBeard(vis);
                Apply(vis, force: true);
            }
        }

        internal static void SetMenuPreview(Player preview, long playerId)
        {
            _menuPreviewVis = preview != null
                ? preview.GetComponent<VisEquipment>() : null;
            _menuPreviewPlayerId = playerId;

            if (_menuPreviewVis != null)
            {
                RefreshHairAndBeard(_menuPreviewVis);
                Apply(_menuPreviewVis, force: true);
            }
        }

        private static void Restore(
            VisualState state)
        {
            if (state?.Helmet != null)
                state.Helmet.SetActive(true);
        }

        private static ZNetView GetView(
            VisEquipment vis)
        {
            ZNetView view =
                NViewField?.GetValue(vis)
                as ZNetView;

            if (view != null)
                return view;

            if (vis.m_nViewOverride != null)
                return vis.m_nViewOverride;

            return vis.GetComponent<ZNetView>();
        }

        private static void RefreshHairAndBeard(
            VisEquipment vis)
        {
            if (vis == null ||
                UpdateEquipmentVisualsMethod == null)
                return;

            UpdateEquipmentVisualsMethod.Invoke(
                vis,
                null);
        }

        private static void PlayToggleEffects(
            Player player,
            VisEquipment vis,
            ItemDrop.ItemData helmetItem,
            bool hidden)
        {
            if (player == null ||
                helmetItem == null)
                return;

            ZDOID playerId =
                player.GetZDOID();

            if (hidden)
            {
                helmetItem.m_shared.m_unequipEffect.Create(
                    player.transform.position,
                    Quaternion.identity,
                    null,
                    1f,
                    -1,
                    playerId);
            }
            else
            {
                Transform point =
                    vis != null
                        ? vis.m_helmet
                        : null;

                Vector3 position =
                    point != null
                        ? point.position
                        : player.transform.position;

                Quaternion rotation =
                    point != null
                        ? point.rotation
                        : player.transform.rotation;

                helmetItem.m_shared.m_equipEffect.Create(
                    position,
                    rotation,
                    null,
                    1f,
                    -1,
                    playerId);
            }

            TriggerEquipEffectMethod?.Invoke(
                player,
                new object[] { helmetItem });
        }

        private static void ShowNotification(
            bool hidden)
        {
            if (MessageHud.instance == null)
                return;

            MessageHud.instance.ShowMessage(
                MessageHud.MessageType.TopLeft,
                hidden
                    ? "Headpiece hidden"
                    : "Headpiece visible",
                0,
                null,
                false);
        }
    }
}
