using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // The level stars, and the row the damage icons line up with.
    public sealed partial class EnemyHudReworkPlugin
    {
        private static void ApplyLevelStars(EnemyUiState state, Character character)
        {
            if (state == null || character == null) return;
            int level = character.GetLevel();
            state.StarLevel = level;
            if (Instance != null && !Instance._starCount.Value)
            {
                // Off: exactly the game's own stars (none past two), and
                // other mods' star rows are left alone.
                if (state.Level2 != null && state.Level2.gameObject.activeSelf != (level == 2))
                    state.Level2.gameObject.SetActive(level == 2);
                if (state.Level3 != null && state.Level3.gameObject.activeSelf != (level == 3))
                    state.Level3.gameObject.SetActive(level == 3);
                if (state.LevelSecondStar != null && !state.LevelSecondStar.activeSelf)
                    state.LevelSecondStar.SetActive(true);
                if (state.LevelNumber != null && state.LevelNumber.gameObject.activeSelf)
                    state.LevelNumber.gameObject.SetActive(false);
                return;
            }
            // Other mods' own star rows (any "level_" object besides the
            // game's two) give way to ours; their pictures aren't touched.
            if (state.OtherStars == null && state.Gui != null)
            {
                var others = new List<GameObject>();
                foreach (Transform t in state.Gui.transform)
                    if (t.name.StartsWith("level_", StringComparison.Ordinal) &&
                        t != state.Level2 && t != state.Level3)
                        others.Add(t.gameObject);
                state.OtherStars = others.ToArray();
            }
            if (state.OtherStars != null)
                foreach (GameObject o in state.OtherStars)
                    if (o != null && o.activeSelf) o.SetActive(false);
            // Past two stars (level mods): the two-star row with its second
            // star swapped for the star count, in the stars' own yellow and
            // the HP numbers' font.
            bool high = level >= 4;
            if (state.Level2 != null && state.Level2.gameObject.activeSelf != (level == 2))
                state.Level2.gameObject.SetActive(level == 2);
            bool three = level == 3 || high;
            if (state.Level3 != null && state.Level3.gameObject.activeSelf != three)
                state.Level3.gameObject.SetActive(three);
            if (state.Level3 == null) return;
            if (high && state.LevelNumber == null) CreateLevelNumber(state);
            if (state.LevelSecondStar != null && state.LevelSecondStar.activeSelf == high)
                state.LevelSecondStar.SetActive(!high);
            if (state.LevelNumber != null)
            {
                if (state.LevelNumber.gameObject.activeSelf != high)
                    state.LevelNumber.gameObject.SetActive(high);
                if (high) state.LevelNumber.text = (level - 1).ToString();
            }
        }

        // Runs after every other mod's nameplate update (ours is last): if
        // anything switched our stars around, they're put back. A few
        // reads; only acts when something actually changed them.
        private static void GuardLevelStars(EnemyUiState state)
        {
            int level = state.StarLevel;
            if (level < 1 || !Instance._starCount.Value) return;
            bool high = level >= 4;
            bool wrong =
                (state.Level2 != null && state.Level2.gameObject.activeSelf != (level == 2)) ||
                (state.Level3 != null && state.Level3.gameObject.activeSelf != (level == 3 || high)) ||
                (state.LevelSecondStar != null && state.LevelSecondStar.activeSelf == high) ||
                (state.LevelNumber != null && state.LevelNumber.gameObject.activeSelf != high);
            if (!wrong && state.OtherStars != null)
                foreach (GameObject o in state.OtherStars)
                    if (o != null && o.activeSelf) { wrong = true; break; }
            if (wrong && state.Character != null) ApplyLevelStars(state, state.Character);
        }

        private static readonly Color StarYellow = new Color(1f, 0.8174443f, 0.3382353f, 1f);

        private static void CreateLevelNumber(EnemyUiState state)
        {
            Transform second = state.Level3.Find("star (1)");
            TMP_Text font = state.HealthValueText != null ? state.HealthValueText : state.NameText;
            if (second == null || font == null) return;
            state.LevelSecondStar = second.gameObject;
            RectTransform starRect = (RectTransform)second;
            GameObject go = new GameObject("WireLevelNumber", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform r = (RectTransform)go.transform;
            r.SetParent(state.Level3, false);
            r.anchorMin = starRect.anchorMin;
            r.anchorMax = starRect.anchorMax;
            r.pivot = starRect.pivot;
            r.anchoredPosition = starRect.anchoredPosition;
            r.sizeDelta = starRect.sizeDelta;
            TMP_Text t = go.GetComponent<TMP_Text>();
            t.font = font.font;
            t.fontSharedMaterial = font.fontSharedMaterial;
            t.fontStyle = font.fontStyle;
            t.fontSize = starRect.sizeDelta.y;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.Center;
            t.color = StarYellow;
            t.raycastTarget = false;
            state.LevelNumber = t;
        }

        // Made once per player nameplate: the creature HUD's two-star row,
        // placed the same distance from the health bar, nothing drawn.
        private static void CreatePlayerIconRow(EnemyUiState state)
        {
            if (state.Level2 != null || state.HealthRect == null || state.Gui == null) return;
            EnemyHud hud = EnemyHud.instance;
            if (hud == null || hud.m_baseHud == null) return;
            RectTransform star = hud.m_baseHud.transform.Find("level_2") as RectTransform;
            RectTransform health = hud.m_baseHud.transform.Find("Health") as RectTransform;
            if (star == null || health == null) return;
            GameObject copy = UnityEngine.Object.Instantiate(star.gameObject, state.Gui.transform, false);
            copy.name = "WireIconRow";
            foreach (Graphic g in copy.GetComponentsInChildren<Graphic>(true)) g.enabled = false;
            copy.SetActive(true);
            RectTransform r = (RectTransform)copy.transform;
            // The health bar hangs from the top of its nameplate and the
            // star row sits on the middle, and a player's nameplate is
            // shorter: measure both from the middle so the gap matches.
            RectTransform playerHealth = state.HealthRect;
            Vector2 baseBar = FromMiddle(health, health.anchoredPosition);
            Vector2 playerBar = FromMiddle(playerHealth, state.HealthOriginalPosition);
            r.anchoredPosition = FromMiddle(star, star.anchoredPosition) - baseBar + playerBar -
                FromMiddle(r, Vector2.zero);
            state.IconRow = r;
            state.IconRowOriginal = r.anchoredPosition;
            state.LastStarY = float.NaN;
        }

        // Where a child's anchored position puts it, measured from the
        // middle of its parent.
        private static Vector2 FromMiddle(RectTransform r, Vector2 anchored)
        {
            RectTransform parent = r.parent as RectTransform;
            if (parent == null) return anchored;
            Vector2 a = (r.anchorMin + r.anchorMax) * 0.5f - new Vector2(0.5f, 0.5f);
            return anchored + Vector2.Scale(a, parent.rect.size);
        }
    }
}
