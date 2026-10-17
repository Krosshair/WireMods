using BepInEx.Configuration;
using UnityEngine;

namespace WiresHoodToggle
{
    internal static class HoodInput
    {
        private const string EmoteButton = "OpenEmote";

        private static float _lastTapAt = -10f;
        private static bool _consumeCurrentPress;

        internal static bool UsingEmoteButton =>
            Plugin.CustomToggleKey == null ||
            Plugin.CustomToggleKey.Value.MainKey == KeyCode.None;

        internal static void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                Reset();
                return;
            }

            bool canInput = CanTakeInput(player);

            if (!UsingEmoteButton)
            {
                _lastTapAt = -10f;
                _consumeCurrentPress = false;

                KeyboardShortcut shortcut =
                    Plugin.CustomToggleKey.Value;

                if (canInput && shortcut.IsDown())
                    HoodVisuals.ToggleLocal(player);

                return;
            }

            bool down =
                ZInput.GetButtonDown(EmoteButton);
            bool up =
                ZInput.GetButtonUp(EmoteButton);
            float now = Time.unscaledTime;

            if (_lastTapAt > -1f &&
                now - _lastTapAt >
                    Plugin.DoubleTapWindow.Value)
            {
                _lastTapAt = -10f;
            }

            if (down && canInput)
            {
                if (_lastTapAt > -1f &&
                    now - _lastTapAt <=
                        Plugin.DoubleTapWindow.Value)
                {
                    _lastTapAt = -10f;
                    _consumeCurrentPress = true;
                    HoodVisuals.ToggleLocal(player);
                }
                else
                {
                    _lastTapAt = now;
                }
            }

            if (up)
                _consumeCurrentPress = false;
        }

        internal static bool ShouldOpenEmoteRadial()
        {
            if (!UsingEmoteButton ||
                _consumeCurrentPress ||
                Plugin.EmoteHoldDuration == null)
                return false;

            return ZInput.GetButton(EmoteButton) &&
                   ZInput.GetButtonPressedTimer(
                       EmoteButton) >=
                   Plugin.EmoteHoldDuration.Value;
        }

        internal static void Reset()
        {
            _lastTapAt = -10f;
            _consumeCurrentPress = false;
        }

        private static bool CanTakeInput(Player player)
        {
            if (player == null ||
                player.IsDead() ||
                player.InCutscene() ||
                player.IsTeleporting() ||
                InventoryGui.IsVisible() ||
                Menu.IsVisible() ||
                GameCamera.InFreeFly() ||
                Minimap.IsOpen() ||
                Hud.IsPieceSelectionVisible() ||
                StoreGui.IsVisible() ||
                Console.IsVisible() ||
                TextInput.IsVisible() ||
                PlayerCustomizaton.IsBarberGuiVisible() ||
                Hud.InRadial())
            {
                return false;
            }

            if (Chat.instance != null &&
                Chat.instance.HasFocus())
                return false;

            return true;
        }
    }
}
