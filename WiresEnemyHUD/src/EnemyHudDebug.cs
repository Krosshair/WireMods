using System.Collections.Generic;
using UnityEngine;

namespace EnemyHudRework
{
    // The test boss bar and your own nameplate.
    public sealed partial class EnemyHudReworkPlugin
    {
        internal void UpdateDebugHuds(EnemyHud enemyHud)
        {
            if (enemyHud == null)
                return;

            Player localPlayer = Player.m_localPlayer;
            // Like vanilla shows you to other players: hidden while sneaking.
            if (_debugSeeOwnHealthBar.Value && localPlayer != null && !localPlayer.IsCrouching())
            {
                if (EnemyHudShowHudMethod != null)
                {
                    ShowHudArgs[0] = localPlayer;
                    EnemyHudShowHudMethod.Invoke(enemyHud, ShowHudArgs);
                    ShowHudArgs[0] = null;
                }

                SetupHud(enemyHud, localPlayer, false);

                EnemyUiState ownState;
                if (_states.TryGetValue(localPlayer, out ownState) && ownState != null && ownState.Gui != null)
                {
                    // EnemyHud.UpdateHuds positions player HUDs using the game's
                    // normal multiplayer head-point logic. We only keep it visible.
                    // The vanilla update already ran this frame, so a hud
                    // made just now gets placed and filled here, the same way.
                    PlaceOwnNameplate(ownState, localPlayer);
                    ApplyHealthBarScale(ownState);
                    UpdateHealthValue(ownState);
                }
            }
            else if (localPlayer != null)
            {
                EnemyUiState ownState;
                if (_states.TryGetValue(localPlayer, out ownState) &&
                    ownState != null && ownState.Kind == HudKind.Player)
                {
                    enemyHud.RemoveCharacterHud(localPlayer);
                    DestroyState(ownState);
                    _states.Remove(localPlayer);
                }
            }

            UpdateDebugBossHud(enemyHud);
        }

        private static void PlaceOwnNameplate(EnemyUiState state, Player player)
        {
            Camera camera = Utils.GetMainCamera();
            if (camera == null) return;
            Vector3 position = camera.WorldToScreenPointScaled(player.GetHeadPoint() + Vector3.up * 0.3f);
            bool visible = position.z > 0f;
            if (state.Gui.activeSelf != visible) state.Gui.SetActive(visible);
            if (!visible) return;
            state.Gui.transform.position = position;

            float health = player.GetHealthPercentage();
            if (state.OwnBars == null)
            {
                Transform t = state.Gui.transform;
                var bars = new List<GuiBar>();
                foreach (string path in new[] { "Health/health_fast", "Health/health_slow", "Health/health_fast_friendly" })
                {
                    Transform child = t.Find(path);
                    GuiBar found = child != null ? child.GetComponent<GuiBar>() : null;
                    if (found != null) bars.Add(found);
                }
                state.OwnBars = bars.ToArray();
            }
            foreach (GuiBar bar in state.OwnBars)
                if (bar != null) bar.SetValue(health);
        }

        private static string DebugBossName(string player)
        {
            return "EVIL " + player.ToUpperInvariant() + " ALTER EGO";
        }

        private void UpdateDebugBossHud(EnemyHud enemyHud)
        {
            Player localPlayer = Player.m_localPlayer;

            if (!_debugBossHealthBar.Value || localPlayer == null)
            {
                DestroyDebugBossHud();
                return;
            }

            if (_debugBossGui == null || _debugBossState == null)
                CreateDebugBossHud(enemyHud, localPlayer);

            if (_debugBossGui == null || _debugBossState == null)
                return;

            _debugBossState.Character = localPlayer;
            _debugBossGui.SetActive(true);

            string playerName = localPlayer.GetPlayerName();
            bool renamed = !ReferenceEquals(playerName, _debugBossNameSource);
            if (_debugBossState.NameText != null && renamed)
            {
                _debugBossNameSource = playerName;
                _debugBossState.NameText.text = DebugBossName(playerName);
            }

            ApplyBossNameColor(_debugBossState);

            float healthPercentage = localPlayer.GetHealthPercentage();

            if (!_debugBossBarsLooked)
            {
                _debugBossBarsLooked = true;
                Transform fast = _debugBossGui.transform.Find("Health/health_fast");
                Transform slow = _debugBossGui.transform.Find("Health/health_slow");
                _debugBossFast = fast != null ? fast.GetComponent<GuiBar>() : null;
                _debugBossSlow = slow != null ? slow.GetComponent<GuiBar>() : null;
            }
            GuiBar fastBar = _debugBossFast;
            GuiBar slowBar = _debugBossSlow;

            if (fastBar != null)
                fastBar.SetValue(healthPercentage);
            if (slowBar != null)
                slowBar.SetValue(healthPercentage);

            ApplyHealthBarScale(_debugBossState);
            UpdateBarBackground(_debugBossState);
            UpdateHealthValue(_debugBossState);
            if (renamed && _debugBossState.Weakness != null) _debugBossState.Weakness.Dirty = true; // the name moved the icons
            UpdateWeaknessIcons(_debugBossState, true);
            UpdateBossDps(_debugBossState);
        }

        private void CreateDebugBossHud(EnemyHud enemyHud, Player localPlayer)
        {
            if (enemyHud == null || enemyHud.m_baseHudBoss == null || enemyHud.m_hudRoot == null)
                return;

            _debugBossGui = Instantiate(enemyHud.m_baseHudBoss, enemyHud.m_hudRoot.transform);
            _debugBossGui.name = "EnemyHudRework_DebugBoss";
            _debugBossGui.SetActive(true);

            EnemyUiState state = new EnemyUiState
            {
                Character = localPlayer,
                Gui = _debugBossGui,
                Kind = HudKind.Boss,
                IsDebug = true
            };
            CaptureName(state);
            if (state.NameText != null) state.NameText.text = DebugBossName(localPlayer.GetPlayerName());
            CaptureHealth(state);

            CreateHealthValue(state);
            CaptureNativePalette(state);
            _debugBossState = state;
            ApplyHealthBarScale(state);
            ApplyBossNameColor(state);
            UpdateHealthValue(state);
            SetupWeaknessIcons(state);
            SetupBossDps(state);
        }

        private void DestroyDebugBossHud()
        {
            if (_debugBossState != null)
            {
                DestroyWeaknessIcons(_debugBossState);
                DestroyBossDps(_debugBossState);
                ForgetNativePalette(_debugBossState);
                if (_debugBossState.HealthValueObject != null)
                    Destroy(_debugBossState.HealthValueObject);
                _debugBossState = null;
            }

            if (_debugBossGui != null)
            {
                Destroy(_debugBossGui);
                _debugBossGui = null;
            }
            _debugBossNameSource = null;
            _debugBossFast = _debugBossSlow = null;
            _debugBossBarsLooked = false;
        }
    }
}
