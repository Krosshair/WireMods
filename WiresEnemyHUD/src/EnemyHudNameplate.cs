using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Setting up a nameplate the game just made, and putting it back when it goes.
    public sealed partial class EnemyHudReworkPlugin
    {
        internal void SetupHud(EnemyHud enemyHud, Character character, bool isMount)
        {
            if (enemyHud == null || character == null)
                return;

            if (isMount || (!character.IsPlayer() && character.GetHealth() <= 0f))
                return;

            // The game calls this every frame for every shown nameplate:
            // one dictionary lookup once it is set up.
            EnemyUiState known;
            if (_states.TryGetValue(character, out known) && known != null && known.Gui != null)
                return;

            object record;
            GameObject gui = GetHudGui(enemyHud, character, out record);
            if (gui == null)
                return;

            EnemyUiState existing;
            if (_states.TryGetValue(character, out existing))
            {
                if (existing != null && existing.Gui == gui)
                    return;

                DestroyState(existing);
                _states.Remove(character);
            }

            EnemyUiState state = new EnemyUiState
            {
                Character = character,
                Ai = character.GetComponent<BaseAI>(),
                Gui = gui,
                HudRecord = record,
                Kind = character.IsBoss()
                    ? HudKind.Boss
                    : (character.IsPlayer() ? HudKind.Player : HudKind.Enemy)
            };

            CaptureName(state);

            Transform alerted = gui.transform.Find("Alerted");
            Transform aware = gui.transform.Find("Aware");

            state.AlertedIcon = alerted != null ? alerted.gameObject : null;

            state.AwareIcon = aware != null ? aware.gameObject : null;

            CaptureHealth(state);

            state.Level2 = gui.transform.Find("level_2") as RectTransform;
            state.Level3 = gui.transform.Find("level_3") as RectTransform;
            if (state.Level2 != null) state.Level2Original = state.Level2.anchoredPosition;
            if (state.Level3 != null) state.Level3Original = state.Level3.anchoredPosition;

            // Both star groups start switched on in the game's HUD prefab, and
            // the game only sorts them out while it is showing the HUD on
            // hover. A HUD this mod shows first (on damage, kept on screen)
            // would show stars on a normal creature. The level never changes,
            // so set the right ones once here.
            ApplyLevelStars(state, character);

            if (state.Kind == HudKind.Enemy || state.Kind == HudKind.Player)
            {
                CreateStaggerBar(state);
                // Made the same way and at the same moment as the stagger
                // bar: a copy of the fresh health bar, kept hidden until a
                // bubble shows.
                state.Bubble = CreateWorldBubble(state);
            }

            CreateHealthValue(state);
            SetupCriticalOutline(state);
            if (state.Kind == HudKind.Player) CreatePlayerIconRow(state);
            SetupWeaknessIcons(state);
            SetupEnhancedEffects(state);
            CaptureNativePalette(state);

            ApplyHealthBarScale(state);
            if (state.Kind == HudKind.Enemy || state.Kind == HudKind.Player)
                ApplyStaggerPosition(state);
            if (state.Kind == HudKind.Boss)
                ApplyBossNameColor(state);
            UpdateHealthValue(state);
            SetupBossDps(state);

            _states[character] = state;
        }

        private static void CaptureName(EnemyUiState state)
        {
            Transform t = state.Gui.transform.Find("Name");
            if (t == null) return;
            state.NameGraphic = t.GetComponent<Graphic>();
            state.NameText = t.GetComponent<TMP_Text>();
            state.NameRect = t as RectTransform;
            if (state.NameRect != null) state.NameOriginalPosition = state.NameRect.anchoredPosition;
            if (state.NameText != null) state.OriginalNameText = state.NameText.text;
            if (state.NameGraphic != null) state.NormalNameColor = state.NameGraphic.color;
        }

        private static void CaptureHealth(EnemyUiState state)
        {
            Transform t = state.Gui.transform.Find("Health");
            state.HealthRect = t as RectTransform;
            if (state.HealthRect == null) return;
            state.HealthOriginalPosition = state.HealthRect.anchoredPosition;
            state.HealthOriginalScale = state.HealthRect.localScale;
            state.HealthOriginalHeight = state.HealthRect.rect.height;
            CaptureHealthFill(t, "health_fast", out state.HealthFastFill, out state.HealthFastOriginalHeight);
            CaptureHealthFill(t, "health_slow", out state.HealthSlowFill, out state.HealthSlowOriginalHeight);
            CaptureHealthFill(t, "health_fast_friendly", out state.HealthFriendlyFill, out state.HealthFriendlyOriginalHeight);
        }

        private static GameObject GetHudGui(EnemyHud enemyHud, Character character, out object hudData)
        {
            hudData = null;
            if (EnemyHudHudsField == null || HudDataGuiField == null || enemyHud == null || character == null)
                return null;

            IDictionary huds = EnemyHudHudsField.GetValue(enemyHud) as IDictionary;

            if (huds == null || !huds.Contains(character))
                return null;

            hudData = huds[character];
            return hudData != null ? HudDataGuiField.GetValue(hudData) as GameObject : null;
        }

        private void DestroyState(EnemyUiState state)
        {
            if (state == null)
                return;

            RestorePlayerNameplateOpacity(state);
            DestroyCriticalOutline(state);
            DestroyWeaknessIcons(state);
            DestroyBossDps(state);
            DestroyEnhancedEffects(state);
            DestroyWorldBubble(state);
            ForgetNativePalette(state);

            if (state.HealthRect != null)
            {
                state.HealthRect.localScale = state.HealthOriginalScale;
                state.HealthRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical, state.HealthOriginalHeight);
                SetFillHeight(state.HealthFastFill, state.HealthFastOriginalHeight, 1f);
                SetFillHeight(state.HealthSlowFill, state.HealthSlowOriginalHeight, 1f);
                SetFillHeight(state.HealthFriendlyFill, state.HealthFriendlyOriginalHeight, 1f);
                state.HealthRect.anchoredPosition = state.HealthOriginalPosition;
            }

            if (state.StaggerRoot != null)
                Destroy(state.StaggerRoot);

            if (state.HealthValueObject != null)
                Destroy(state.HealthValueObject);
        }

        // Called exactly when the owning character reaches zero HP.
        // Hide the entire UI immediately, not a fade or a 0 HP frame.
        private void HideDeadHud(Character character)
        {
            if (character == null || character.IsPlayer() || character.GetHealth() > 0f)
                return;
            EnemyUiState state;
            if (_states.TryGetValue(character, out state) && state != null)
            {
                if (state.Gui != null) state.Gui.SetActive(false);
                if (state.HealthValueObject != null) state.HealthValueObject.SetActive(false);
            }
            // Also handles a vanilla HUD created before our SetupHud registered it.
            EnemyHud native = EnemyHud.instance;
            if (native != null)
            {
                object data;
                GameObject gui = GetHudGui(native, character, out data);
                if (gui != null && gui.activeSelf) gui.SetActive(false);
            }
        }
    }
}
