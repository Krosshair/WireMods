using UnityEngine;

namespace WiresHoodToggle
{
    // Purely cosmetic. Never queues or alters a real equipment action.
    // ZSyncAnimation broadcasts the native "equipping" state to other clients.
    internal static class HelmetToggleAnimation
    {
        private static Player _player;
        private static ZSyncAnimation _sync;
        private static float _remaining;
        private static bool _pending;
        private static bool _desiredHidden;

        internal static bool Pending => _pending;

        internal static void Start(Player player, bool desiredHidden)
        {
            Cancel(commit: true);
            _player = player;
            _sync = player != null ? player.GetComponent<ZSyncAnimation>() : null;

            if (player == null || _sync == null ||
                !Plugin.PlayEquipAnimation.Value ||
                player.GetActionQueueCount() != 0 ||
                player.InAttack() || player.IsDead() || player.IsStaggering() || !player.IsOnGround())
            {
                HoodVisuals.CommitToggle(player, desiredHidden);
                return;
            }

            _pending = true;
            _desiredHidden = desiredHidden;
            _remaining = Plugin.AnimationDuration.Value;
            _sync.SetBool("equipping", true);
        }

        internal static void Update(Player player)
        {
            if (!_pending)
                return;

            bool interrupted =
                player == null || player != _player ||
                player.IsDead() || player.InAttack() || player.IsStaggering() ||
                !player.IsOnGround() ||
                player.GetActionQueueCount() > 0 ||
                !Plugin.PlayEquipAnimation.Value;

            if (interrupted)
            {
                Cancel(commit: true);
                return;
            }

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
                Cancel(commit: true);
        }

        internal static void Cancel(bool commit)
        {
            if (!_pending)
                return;

            Player player = _player;
            bool hide = _desiredHidden;
            ZSyncAnimation sync = _sync;

            _pending = false;
            _player = null;
            _sync = null;
            _remaining = 0f;

            // If a genuine equip begins, its animation owns this parameter now.
            if (sync != null && (player == null || player.GetActionQueueCount() == 0))
                sync.SetBool("equipping", false);

            if (commit && player != null)
                HoodVisuals.CommitToggle(player, hide);
        }
    }
}
