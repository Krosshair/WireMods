using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresWeaponMaster
{
    internal static class HoldRController
    {
        private const float HoldSeconds = 0.60f;

        private static readonly MethodInfo ShowHandItemsMethod =
            AccessTools.Method(
                typeof(Humanoid),
                "ShowHandItems",
                new[] { typeof(bool), typeof(bool) });

        private static bool _tracking;
        private static bool _fired;
        private static bool _gamepadHold;
        private static float _downTime;
        private static bool _hadHandsBeforePress;
        private static bool _pressHolsteredItems;
        private static bool _pressUnholsteredItems;

        internal static void BeforePlayerUpdate(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
                return;

            if (!WeaponMasterConfig.IsEnabled)
            {
                ResetTracking();
                return;
            }

            if (InventoryGui.IsVisible() ||
                Menu.IsVisible() ||
                Console.IsVisible() ||
                TextInput.IsVisible() ||
                StoreGui.IsVisible() ||
                Minimap.IsOpen() ||
                Hud.InRadial())
                return;

            bool keyboardDown = ZInput.GetButtonDown("Hide");
            bool gamepadDown =
                ZInput.IsGamepadActive() &&
                ZInput.GetButtonDown("JoyHide") &&
                !ZInput.GetButton("JoyAltKeys") &&
                !player.InPlaceMode();

            if (!keyboardDown && !gamepadDown)
                return;

            _tracking = true;
            _fired = false;
            _gamepadHold = gamepadDown && !keyboardDown;
            _downTime = Time.realtimeSinceStartup;
            _hadHandsBeforePress =
                WeaponMasterController.HasHandItems(player);
            _pressHolsteredItems = false;
            _pressUnholsteredItems = false;
        }

        internal static void AfterPlayerUpdate(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
                return;

            if (!WeaponMasterConfig.IsEnabled)
            {
                ResetTracking();
                return;
            }

            if (!_tracking)
                return;

            if (!_gamepadHold && ZInput.GetButtonDown("Hide"))
                CaptureVanillaToggleResult(player);

            string button = _gamepadHold ? "JoyHide" : "Hide";

            if (!_fired &&
                ZInput.GetButton(button) &&
                Time.realtimeSinceStartup - _downTime >= HoldSeconds)
            {
                if (_gamepadHold)
                    PerformGamepadHoldToggle(player);

                Fire(player);
            }

            if (ZInput.GetButtonUp(button) ||
                (!ZInput.GetButton(button) && !ZInput.GetButtonDown(button)))
            {
                ResetTracking();
            }
        }

        private static void CaptureVanillaToggleResult(Player player)
        {
            bool hasHandsNow =
                WeaponMasterController.HasHandItems(player);
            bool hasHiddenNow =
                WeaponMasterController.HasHiddenHandItems(player);

            _pressHolsteredItems =
                _hadHandsBeforePress && !hasHandsNow && hasHiddenNow;

            _pressUnholsteredItems =
                !_hadHandsBeforePress && hasHandsNow;
        }

        private static void PerformGamepadHoldToggle(Player player)
        {
            // Vanilla JoyHide normally toggles on a short release (<0.33 s).
            // A long hold never reaches that vanilla action, so at our hold
            // threshold we perform the same toggle once, then run the normal
            // Weapon Master clear behavior.
            if (_hadHandsBeforePress)
            {
                if (!player.InAttack() && !player.InDodge())
                    player.HideHandItems();
            }
            else if ((!player.IsSwimming() || player.IsOnGround()) &&
                     !player.InDodge() &&
                     ShowHandItemsMethod != null)
            {
                ShowHandItemsMethod.Invoke(
                    player,
                    new object[] { false, true });
            }

            CaptureVanillaToggleResult(player);
        }

        private static void Fire(Player player)
        {
            _fired = true;

            WeaponMasterController.ClearHistoryPreserveCurrentHands(player);

            if (_pressHolsteredItems && !_pressUnholsteredItems)
                WeaponMasterController.ClearHiddenHandItems(player);

            WeaponMasterController.PlayClearFeedback(player);
            WeaponVisuals.RefreshLocal();
        }

        internal static void ResetTracking()
        {
            _tracking = false;
            _fired = false;
            _gamepadHold = false;
            _downTime = 0f;
            _hadHandsBeforePress = false;
            _pressHolsteredItems = false;
            _pressUnholsteredItems = false;
        }
    }
}
