using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WiresComfortInfo
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.comfortinfo";
        public const string Name = "Wire's Comfort Info";
        public const string Version = "0.1.2";

        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    // Calculate the hypothetical additional comfort using Valheim's own
    // 10-metre radius, shelter rule, grouping, priority and name rules.
    // Never spawn a dummy piece (which could enter the global comfort list).
    internal static class Comfort
    {
        private struct Entry
        {
            internal Piece Piece;
            internal int Value;
        }

        private static readonly List<Piece> Nearby = new List<Piece>(64);
        private static readonly List<Entry> Entries = new List<Entry>(65);
        private static readonly Comparison<Entry> SortEntries = CompareEntries;

        private static int _baseline;
        private static bool _sheltered;

        private static int CompareEntries(Entry a, Entry b)
        {
            int group = a.Piece.m_comfortGroup.CompareTo(
                b.Piece.m_comfortGroup);
            if (group != 0)
                return group;

            if (a.Value != b.Value)
                return b.Value.CompareTo(a.Value);

            return b.Piece.m_name.CompareTo(a.Piece.m_name);
        }

        internal static void Begin(Player player)
        {
            _sheltered = player != null && player.InShelter();
            Nearby.Clear();
            if (!_sheltered)
            {
                _baseline = 1;
                return;
            }

            Vector3 pos = player.transform.position;
            Piece.GetAllComfortPiecesInRadius(pos, 10f, Nearby);
            _baseline = SE_Rested.CalculateComfortLevel(
                true, pos);
        }

        internal static int Gain(Piece hypothetical)
        {
            if (!_sheltered || hypothetical == null ||
                hypothetical.m_comfort <= 0)
                return 0;

            // A conditional comfort object (e.g. fire) that starts
            // inactive will not give comfort until its condition is met.
            if (hypothetical.m_comfortObject != null &&
                !hypothetical.m_comfortObject.activeSelf)
                return 0;

            Entries.Clear();
            foreach (Piece existing in Nearby)
            {
                if (existing == null)
                    continue;
                Entries.Add(new Entry
                {
                    Piece = existing,
                    Value = existing.GetComfort()
                });
            }

            Entries.Add(new Entry
            {
                Piece = hypothetical,
                Value = hypothetical.m_comfort
            });

            Entries.Sort(SortEntries);
            int result = 2; // Vanilla base 1 + sheltered 1.
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry entry = Entries[i];
                if (i != 0)
                {
                    Entry prev = Entries[i - 1];
                    if ((entry.Piece.m_comfortGroup !=
                            Piece.ComfortGroup.None &&
                         entry.Piece.m_comfortGroup ==
                            prev.Piece.m_comfortGroup) ||
                        entry.Piece.m_name == prev.Piece.m_name)
                    {
                        continue;
                    }
                }
                result += entry.Value;
            }

            return Mathf.Max(0, result - _baseline);
        }
    }

    internal sealed class ComfortNumber : MonoBehaviour
    {
        private TextMeshProUGUI _number;
        private float _retryFontAt;

        internal void Set(int value)
        {
            if (_number == null && Time.unscaledTime >= _retryFontAt)
                Create();

            if (_number == null)
                return;

            bool active = value > 0;
            if (active)
                _number.text = "+" + value;

            if (_number.gameObject.activeSelf != active)
                _number.gameObject.SetActive(active);
        }

        // Reuse a font and material that the active build HUD already
        // renders successfully. Do not assume TextMeshPro's packaged
        // default LiberationSans asset exists in Valheim.
        private TMP_Text FindStyleSource()
        {
            Hud hud = Hud.instance;
            if (hud != null)
            {
                TMP_Text candidate = Valid(hud.m_buildSelection);
                if (candidate != null)
                    return candidate;

                candidate = Valid(hud.m_pieceDescription);
                if (candidate != null)
                    return candidate;

                candidate = Valid(hud.m_hoverName);
                if (candidate != null)
                    return candidate;
            }

            // Both the vanilla build grid and the optional advanced
            // BuildUi can contain usable styled TMP labels.
            TMP_Text[] siblings = transform.parent != null
                ? transform.parent.GetComponentsInChildren<TMP_Text>(true)
                : transform.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text candidate in siblings)
            {
                TMP_Text usable = Valid(candidate);
                if (usable != null)
                    return usable;
            }

            return null;
        }

        private static TMP_Text Valid(TMP_Text text)
        {
            return text != null && text.font != null &&
                text.fontSharedMaterial != null ? text : null;
        }

        private void Create()
        {
            TMP_Text source = FindStyleSource();
            if (source == null)
            {
                // If the HUD/font is not ready yet, silently try again
                // later instead of creating a broken TMP component.
                _retryFontAt = Time.unscaledTime + 5f;
                return;
            }

            GameObject go = new GameObject(
                "Wire Comfort Addition",
                typeof(RectTransform));

            // Inactive BEFORE TMP is added: TextMeshPro must never run
            // OnEnable while it lacks a valid font and font material.
            go.SetActive(false);
            go.transform.SetParent(transform, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-3f, -2f);
            rect.sizeDelta = new Vector2(34f, 22f);
            rect.SetAsLastSibling();

            _number = go.AddComponent<TextMeshProUGUI>();
            _number.font = source.font;
            _number.fontSharedMaterial = source.fontSharedMaterial;
            _number.fontSize = 15f;
            _number.alignment = TextAlignmentOptions.TopRight;
            _number.color = Color.white;
            _number.textWrappingMode = TextWrappingModes.NoWrap;
            _number.raycastTarget = false;

            // No outlineWidth setter. TMP invokes it on a material
            // that could still be uninitialized, causing the logged
            // NullReferenceExceptions and repeated font warnings.
            // The already-loaded HUD material supplies native styling.
            _number.text = "";
        }
    }
    internal static class Icons
    {
        private static float _nextRefresh;
        private static float _nextLegacyRefresh;

        // Looked up once, not on every refresh.
        private static readonly AccessTools.FieldRef<BuildUi, List<BuildUiPieceButton>> PieceButtons =
            AccessTools.FieldRefAccess<BuildUi, List<BuildUiPieceButton>>("m_pieceButtons");

        internal static void UpdateAdvanced(BuildUi ui)
        {
            Player player = Player.m_localPlayer;
            if (ui == null || !ui.isActiveAndEnabled ||
                player == null || !player.InPlaceMode() ||
                Time.unscaledTime < _nextRefresh)
                return;

            _nextRefresh = Time.unscaledTime + 0.45f;

            List<BuildUiPieceButton> buttons = PieceButtons(ui);

            if (buttons == null)
                return;

            Comfort.Begin(player);
            foreach (BuildUiPieceButton button in buttons)
                RefreshButton(button);
        }

        internal static void RefreshButton(
            BuildUiPieceButton button)
        {
            if (button == null)
                return;

            ComfortNumber number =
                button.GetComponent<ComfortNumber>();

            Piece piece = button.Piece;
            if (piece == null || piece.m_comfort <= 0)
            {
                if (number != null)
                    number.Set(0);
                return;
            }

            if (number == null)
                number =
                    button.gameObject.AddComponent<ComfortNumber>();

            number.Set(Comfort.Gain(piece));
        }

        internal static void UpdateLegacy(
            Hud hud,
            Player player)
        {
            if (hud == null || player == null ||
                hud.m_pieceListRoot == null ||
                Time.unscaledTime < _nextLegacyRefresh)
                return;

            _nextLegacyRefresh = Time.unscaledTime + 0.45f;
            Comfort.Begin(player);
            List<Piece> pieces = player.GetBuildPieces();

            RectTransform root = hud.m_pieceListRoot;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                ComfortNumber number =
                    child.GetComponent<ComfortNumber>();

                Piece piece = i < pieces.Count ? pieces[i] : null;
                if (piece == null || piece.m_comfort <= 0)
                {
                    if (number != null)
                        number.Set(0);
                    continue;
                }

                if (number == null)
                    number = child.gameObject
                        .AddComponent<ComfortNumber>();

                number.Set(Comfort.Gain(piece));
            }
        }
    }

    [HarmonyPatch(typeof(BuildUi), "Update")]
    internal static class AdvancedUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildUi __instance)
        {
            Icons.UpdateAdvanced(__instance);
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdatePieceList")]
    internal static class LegacyUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Hud __instance,
            Player player)
        {
            Icons.UpdateLegacy(__instance, player);
        }
    }

    [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
    internal static class RawComfortDescriptionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Hud __instance, Piece piece)
        {
            if (piece == null ||
                piece.m_comfort <= 0 ||
                __instance.m_pieceDescription == null)
                return;

            __instance.m_pieceDescription.text +=
                "\nComfort: " + piece.m_comfort;
        }
    }
}
