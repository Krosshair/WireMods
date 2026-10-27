using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
        public const string Version = "0.1.4";

        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(Guid);
            // One class at a time: a patch broken by a game update only
            // switches off its own part.
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try { _harmony.CreateClassProcessor(type).Patch(); }
                catch (Exception e) { Logger.LogError("Patch failed for " + type.FullName + ": " + e.Message); }
            }
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

            // A conditional comfort object (a fire's flames) only gives
            // comfort while it's on. A new fire is lit with its starting
            // fuel, so it counts; anything else that starts off doesn't.
            if (hypothetical.m_comfortObject != null &&
                !hypothetical.m_comfortObject.activeSelf)
            {
                Fireplace fire = hypothetical.GetComponent<Fireplace>();
                if (fire == null || fire.m_startFuel <= 0f)
                    return 0;
            }

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
        private int _shown;
        private float _retryFontAt;

        internal void Set(int value)
        {
            // Nothing to show: no label made for it yet (most comfort pieces
            // add nothing where you stand, and each new label costs a bit).
            if (_number == null && value <= 0)
                return;
            if (_number == null && Time.unscaledTime >= _retryFontAt)
            {
                Prof.Begin(Prof.NewNumber);
                Create();
                Prof.End(Prof.NewNumber);
            }

            if (_number == null)
                return;

            bool active = value > 0;
            if (active && value != _shown)
            {
                _shown = value;
                _number.text = "+" + value;
            }

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
    // Filled in when the build menu opens or shows a different set of
    // pieces, in the same frame, so the numbers are there as it appears.
    // Nothing runs while the menu just sits open.
    internal static class Icons
    {
        private static readonly AccessTools.FieldRef<BuildUi, List<BuildUiPieceButton>> PieceButtons =
            AccessTools.FieldRefAccess<BuildUi, List<BuildUiPieceButton>>("m_pieceButtons");

        internal static void Refresh(BuildUi ui)
        {
            Player player = Player.m_localPlayer;
            if (ui == null || player == null || !player.InPlaceMode())
                return;

            List<BuildUiPieceButton> buttons = PieceButtons(ui);
            if (buttons == null)
                return;

            Prof.Begin(Prof.Refresh);
            Prof.Begin(Prof.ComfortScan);
            Comfort.Begin(player);
            Prof.End(Prof.ComfortScan);
            foreach (BuildUiPieceButton button in buttons)
                RefreshButton(button);
            Prof.End(Prof.Refresh);
        }

        private static void RefreshButton(BuildUiPieceButton button)
        {
            if (button == null)
                return;

            ComfortNumber number = button.GetComponent<ComfortNumber>();
            Piece piece = button.Piece;
            if (piece == null || piece.m_comfort <= 0)
            {
                if (number != null)
                    number.Set(0);
                return;
            }

            if (number == null)
                number = button.gameObject.AddComponent<ComfortNumber>();
            Prof.Begin(Prof.Gain);
            int gain = Comfort.Gain(piece);
            Prof.End(Prof.Gain);
            number.Set(gain);
        }
    }

    [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.OpenBuildMenu))]
    internal static class OpenPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildUi __instance) => Icons.Refresh(__instance);
    }

    // Tabs, tags and favorites all rebuild the piece buttons here.
    [HarmonyPatch(typeof(BuildUi), "UpdatePieceButtons")]
    internal static class PieceButtonsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildUi __instance)
        {
            if (__instance.gameObject.activeInHierarchy)
                Icons.Refresh(__instance);
        }
    }

    // The game rewrites the selected piece's description every frame while
    // building. Its own Localize call is swapped for a cached text with the
    // comfort line, so this costs nothing extra (less than vanilla, even).
    [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
    internal static class RawComfortDescriptionPatch
    {
        private static Piece _piece;
        private static string _raw;
        private static string _text;
        private static Localization _localization;
        private static string _language;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
        {
            FieldInfo description = AccessTools.Field(typeof(Piece), nameof(Piece.m_description));
            MethodInfo localize = AccessTools.Method(typeof(Localization), nameof(Localization.Localize), new[] { typeof(string) });
            MethodInfo describe = AccessTools.Method(typeof(RawComfortDescriptionPatch), nameof(Describe));
            bool afterDescription = false, done = false;
            foreach (CodeInstruction code in input)
            {
                if (!done && afterDescription && code.Calls(localize))
                {
                    done = true;
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, describe) { labels = code.labels, blocks = code.blocks };
                    continue;
                }
                afterDescription = !done && code.LoadsField(description);
                yield return code;
            }
            if (!done)
                Debug.LogWarning("[Comfort Info] Piece description hook not found; the comfort line won't show.");
        }

        private static string Describe(Localization localization, string raw, Piece piece)
        {
            string language = localization.GetSelectedLanguage();
            if (piece != _piece || raw != _raw || localization != _localization || language != _language || _text == null)
            {
                _language = language;
                _piece = piece;
                _raw = raw;
                _localization = localization;
                Prof.Begin(Prof.Description);
                string text = localization.Localize(raw);
                Prof.End(Prof.Description);
                if (piece != null && piece.m_comfort > 0)
                    text += "\nComfort: <color=orange>" + piece.m_comfort + "</color>";
                _text = text;
            }
            return _text;
        }
    }
}
