using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace WiresAltBiomeInfo
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.altbiomeinfo";
        public const string Name = "Wire's Alt Biome Info";
        public const string Version = "0.1.6";

        private Harmony _harmony;

        private void Awake()
        {
            TranslationFiles.Initialize(
                Path.GetDirectoryName(Info.Location),
                Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            TranslationFiles.Shutdown();
            _harmony?.UnpatchSelf();
        }
    }

    internal static class TranslationFiles
    {
        private const string DefaultFormat =
            "{modifiers} {biome}";

        // English is part of the DLL. External language files are
        // overlays on top of these defaults, never a requirement
        // for the mod's normal English behavior.
        private static readonly Dictionary<string, string>
            BuiltInEnglish =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "Mushroom", "Fungal" },
                    { "Lantern", "Lantern" },
                    { "Bones", "Skeletal" },
                    { "Menhir", "Menhir" },
                    { "Dark Meadows", "Dark" },
                    { "Peaceful Meadows", "Peaceful" },
                    { "Dandelion Meadows", "Dandelion" },
                    { "Raspberry Meadows", "Bountiful" },
                    { "Smalltree Meadows", "Sapling" },
                    { "Birch Meadows", "Birch" },
                    { "Troll Black Forest", "Trollish" },
                    { "Root Black Forest", "Rooty" },
                    { "Ruin Black Forest", "Ruined" },
                    { "Rock Black Forest", "Rocky" },
                    { "Pinetree Black Forest", "Piney" },
                    { "Blueberry Black Forest", "Bountiful" },
                    { "Kalhygge Black Forest", "Kalhygge" },
                    { "Hut Swamp", "Hut" },
                    { "Bog Swamp", "Boggy" },
                    { "Bat Swamp", "Swarming" },
                    { "Abomination Swamp", "Abominable" },
                    { "Wolf Mountain", "Lupine" },
                    { "Drake Mountain", "Drake" },
                    { "Fortress Mountain", "Fortified" },
                    { "Lox Plains", "Teeming" },
                    { "Goblin Plains", "Overrun" },
                    { "Death Plains", "Death" },
                    { "Rockless Mistlands", "Rockless" },
                    { "Trees Mistlands", "Sylvan" },
                    { "Swords Mistlands", "Gladial" },
                    { "Hare Mistlands", "Teeming" },
                    { "BroodSwarm Mistlands", "Infested" }
                };

        private static readonly Dictionary<string, string>
            Values =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

        private static string _pluginDirectory;
        private static string _translationDirectory;
        private static string _nameFormat =
            DefaultFormat;
        private static ConfigEntry<string> _language;

        internal static void Initialize(
            string pluginDirectory,
            ConfigFile config)
        {
            _pluginDirectory = pluginDirectory ?? "";
            _translationDirectory =
                Path.Combine(
                    _pluginDirectory,
                    "translations");

            List<string> languages =
                new List<string> { "English" };

            AddLanguagesFromDirectory(
                _translationDirectory,
                languages);

            // Gale can flatten packaged subfolders. Supporting the
            // plugin root keeps translated files working either way.
            AddLanguagesFromDirectory(
                _pluginDirectory,
                languages);

            languages.Sort(
                StringComparer.OrdinalIgnoreCase);

            _language =
                config.Bind(
                    "Translations",
                    "Language",
                    "English",
                    new ConfigDescription(
                        "Language overlay to load. English defaults are built into the DLL.",
                        new AcceptableValueList<string>(
                            languages.ToArray())));

            _language.SettingChanged +=
                OnLanguageChanged;

            LoadSelectedLanguage();
        }

        internal static void Shutdown()
        {
            if (_language != null)
                _language.SettingChanged -=
                    OnLanguageChanged;
        }

        private static void OnLanguageChanged(
            object sender,
            EventArgs args)
        {
            LoadSelectedLanguage();
        }

        internal static void LoadSelectedLanguage()
        {
            Values.Clear();
            _nameFormat = DefaultFormat;

            foreach (KeyValuePair<string, string> entry
                     in BuiltInEnglish)
            {
                Values[entry.Key] = entry.Value;
            }

            string language =
                _language != null
                    ? _language.Value
                    : "English";

            if (string.IsNullOrWhiteSpace(language) ||
                string.Equals(
                    language,
                    "English",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string path =
                FindLanguageFile(language);

            if (!string.IsNullOrWhiteSpace(path))
                LoadFile(path);
        }

        internal static string Get(
            string key,
            string fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
                return fallback ?? "";

            if (Values.TryGetValue(
                    key.Trim(),
                    out string translated) &&
                !string.IsNullOrWhiteSpace(translated))
            {
                return translated.Trim();
            }

            return fallback ?? "";
        }

        internal static string FormatName(
            string modifiers,
            string biome)
        {
            string format =
                string.IsNullOrWhiteSpace(_nameFormat)
                    ? DefaultFormat
                    : _nameFormat;

            return format
                .Replace(
                    "{modifiers}",
                    modifiers ?? "")
                .Replace(
                    "{biome}",
                    biome ?? "");
        }

        private static void AddLanguagesFromDirectory(
            string directory,
            List<string> languages)
        {
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
                return;

            string[] files =
                Directory.GetFiles(
                    directory,
                    "*.txt");

            for (int i = 0; i < files.Length; i++)
            {
                string language =
                    Path.GetFileNameWithoutExtension(
                        files[i]);

                if (string.IsNullOrWhiteSpace(language) ||
                    languages.Exists(
                        value => string.Equals(
                            value,
                            language,
                            StringComparison.OrdinalIgnoreCase)))
                    continue;

                languages.Add(language);
            }
        }

        private static string FindLanguageFile(
            string language)
        {
            string safeName =
                Path.GetFileName(language);

            string nested =
                Path.Combine(
                    _translationDirectory ?? "",
                    safeName + ".txt");

            if (File.Exists(nested))
                return nested;

            string flat =
                Path.Combine(
                    _pluginDirectory ?? "",
                    safeName + ".txt");

            return File.Exists(flat)
                ? flat
                : null;
        }

        private static void LoadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
                return;

            foreach (string rawLine in
                     File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                string line = rawLine.Trim();

                if (line.StartsWith("#"))
                    continue;

                int equals = line.IndexOf('=');

                if (equals <= 0)
                    continue;

                string key =
                    line.Substring(0, equals).Trim();

                string value =
                    line.Substring(equals + 1).Trim();

                if (string.Equals(
                        key,
                        "Format",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        _nameFormat = value;

                    continue;
                }

                if (!string.IsNullOrWhiteSpace(key))
                    Values[key] = value;
            }
        }
    }
    internal static class BiomeSubtypeNames
    {
        private static readonly Dictionary<string, string>
            SingularModifierWords =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "Tree", "Wooded" },
                    { "Trees", "Wooded" },
                    { "Rock", "Rocky" },
                    { "Rocks", "Rocky" },
                    { "Bone", "Bone-strewn" },
                    { "Bones", "Bone-strewn" },
                    { "Bat", "Bat-haunted" },
                    { "Bats", "Bat-haunted" },
                    { "Abomination", "Abominable" },
                    { "Abominations", "Abominable" },
                    { "Blueberry", "Blueberry-rich" },
                    { "Blueberries", "Blueberry-rich" },
                    { "Pinetree", "Pine-clad" },
                    { "Pinetrees", "Pine-clad" },
                    { "Pines", "Pine-clad" },
                    { "Birch", "Birch-clad" },
                    { "Birches", "Birch-clad" },
                    { "Oak", "Oak-clad" },
                    { "Oaks", "Oak-clad" },
                    { "Mushroom", "Fungal" },
                    { "Mushrooms", "Fungal" },
                    { "Drake", "Drake" },
                    { "Drakes", "Drake" },
                    { "Wolf", "Wolven" },
                    { "Wolves", "Wolven" },
                    { "Troll", "Troll-haunted" },
                    { "Trolls", "Troll-haunted" },
                    { "Skeleton", "Skeleton-haunted" },
                    { "Skeletons", "Skeleton-haunted" },
                    { "Draugr", "Draugr-haunted" },
                    { "Draugrs", "Draugr-haunted" },
                    { "Greydwarf", "Greydwarf-haunted" },
                    { "Greydwarfs", "Greydwarf-haunted" },
                    { "Greydwarves", "Greydwarf-haunted" }
                };

        internal static string Get(BiomeSector sector)
        {
            if (sector == null)
                return "";

            string baseName =
                Localize(BiomeSector.GetBiomeName(sector.Biome));

            string core = baseName;
            List<string> prefixes = new List<string>();
            List<string> suffixes = new List<string>();

            if (sector.AltBiomes != null)
            {
                for (int i = 0; i < sector.AltBiomes.Count; i++)
                {
                    AltBiome alt = sector.AltBiomes[i];
                    if (alt == null)
                        continue;

                    bool hasOverride =
                        !string.IsNullOrWhiteSpace(alt.m_nameOverride);
                    bool hasPrefix =
                        !string.IsNullOrWhiteSpace(alt.m_namePrefix);
                    bool hasSuffix =
                        !string.IsNullOrWhiteSpace(alt.m_nameSuffix);

                    if (hasOverride)
                    {
                        core =
                            CollapseWhitespace(
                                Localize(alt.m_nameOverride));
                    }

                    if (hasPrefix)
                    {
                        AddUnique(
                            prefixes,
                            NormalizeModifier(
                                alt.m_namePrefix,
                                baseName));
                    }
                    else if (!hasOverride &&
                             !hasSuffix &&
                             !string.IsNullOrWhiteSpace(alt.m_name))
                    {
                        AddUnique(
                            prefixes,
                            NormalizeModifier(
                                alt.m_name,
                                baseName));
                    }

                    if (hasSuffix)
                    {
                        AddUnique(
                            suffixes,
                            NormalizeModifier(
                                alt.m_nameSuffix,
                                baseName));
                    }
                }
            }

            string modifiers =
                string.Join(" ", prefixes);

            StringBuilder builder = new StringBuilder();

            AppendPart(
                builder,
                TranslationFiles.FormatName(
                    modifiers,
                    core));

            for (int i = 0; i < suffixes.Count; i++)
                AppendPart(builder, suffixes[i]);

            return CollapseWhitespace(builder.ToString());
        }

        private static string NormalizeModifier(
            string raw,
            string baseName)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            string translationKey = raw.Trim();

            string source =
                translationKey.StartsWith("$")
                    ? Localize(translationKey)
                    : translationKey;

            string cleaned =
                CollapseWhitespace(
                    PrettyInternalName(source));

            // The AltBiome data stores names such as
            // "Kalhygge Black Forest" in English even when Valheim itself
            // is running in another language. Strip both the localized
            // biome name and the canonical English one so translation keys
            // remain stable in every game language.
            cleaned =
                StripBiomeName(
                    cleaned,
                    baseName);

            cleaned =
                StripKnownEnglishBiomeName(
                    cleaned);

            if (SingularModifierWords.TryGetValue(
                    cleaned,
                    out string singular))
            {
                cleaned = singular;
            }

            return TranslationFiles.Get(
                translationKey,
                cleaned);
        }

        private static string StripKnownEnglishBiomeName(
            string text)
        {
            string cleaned =
                CollapseWhitespace(text);

            string[] biomeNames =
            {
                "Black Forest",
                "Deep North",
                "Meadows",
                "Swamp",
                "Mountain",
                "Plains",
                "Ashlands",
                "Ash Lands",
                "Ocean",
                "Mistlands"
            };

            for (int i = 0; i < biomeNames.Length; i++)
            {
                cleaned =
                    StripBiomeName(
                        cleaned,
                        biomeNames[i]);
            }

            return cleaned;
        }

        private static string StripBiomeName(
            string text,
            string baseName)
        {
            string cleaned = CollapseWhitespace(text);
            string biome = CollapseWhitespace(baseName);

            if (string.IsNullOrEmpty(cleaned) ||
                string.IsNullOrEmpty(biome))
                return cleaned;

            bool changed;

            do
            {
                changed = false;

                if (string.Equals(
                        cleaned,
                        biome,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }

                string suffix = " " + biome;

                if (cleaned.EndsWith(
                        suffix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    cleaned =
                        cleaned.Substring(
                            0,
                            cleaned.Length - suffix.Length).Trim();

                    changed = true;
                    continue;
                }

                string prefix = biome + " ";

                if (cleaned.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    cleaned =
                        cleaned.Substring(prefix.Length).Trim();

                    changed = true;
                }
            }
            while (changed);

            return cleaned;
        }

        private static void AddUnique(
            List<string> parts,
            string part)
        {
            if (string.IsNullOrWhiteSpace(part))
                return;

            for (int i = 0; i < parts.Count; i++)
            {
                if (string.Equals(
                        parts[i],
                        part,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            parts.Add(part);
        }

        private static void AppendPart(
            StringBuilder builder,
            string part)
        {
            if (string.IsNullOrWhiteSpace(part))
                return;

            if (builder.Length > 0)
                builder.Append(' ');

            builder.Append(part.Trim());
        }

        private static string Localize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return Localization.instance != null
                ? Localization.instance.Localize(value.Trim())
                : value.Trim();
        }

        private static string PrettyInternalName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string trimmed =
                value.Trim()
                     .Replace('_', ' ')
                     .Replace('-', ' ');

            StringBuilder builder =
                new StringBuilder(trimmed.Length + 8);

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];

                if (i > 0 &&
                    char.IsUpper(c) &&
                    char.IsLower(trimmed[i - 1]) &&
                    trimmed[i - 1] != ' ')
                {
                    builder.Append(' ');
                }

                builder.Append(c);
            }

            return CollapseWhitespace(builder.ToString());
        }

        private static string CollapseWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string[] words =
                value.Split(
                    (char[])null,
                    StringSplitOptions.RemoveEmptyEntries);

            return string.Join(" ", words);
        }
    }

    [HarmonyPatch(typeof(BiomeSector), nameof(BiomeSector.GetName))]
    internal static class BiomeSectorNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BiomeSector __instance,
            bool debug,
            ref string __result)
        {
            if (debug ||
                __instance == null ||
                __instance.AltBiomes == null ||
                __instance.AltBiomes.Count == 0)
                return;

            __result = BiomeSubtypeNames.Get(__instance);
        }
    }
    [HarmonyPatch(typeof(Minimap), "Start")]
    internal static class MinimapLayoutPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap __instance)
        {
            if (__instance == null)
                return;

            ConfigureSmall(
                __instance.m_biomeNameSmall);

            ConfigureLarge(
                __instance.m_biomeNameLarge);
        }

        private static void ConfigureSmall(
            TMP_Text text)
        {
            if (text == null)
                return;

            // Small HUD minimap: keep the vanilla horizontal box,
            // pin text to its right edge, and wrap extra words onto
            // lines BELOW it. Do not borrow the large-map width.
            text.enableAutoSizing = false;
            text.textWrappingMode =
                TextWrappingModes.Normal;
            text.overflowMode =
                TextOverflowModes.Overflow;
            text.alignment =
                TextAlignmentOptions.TopRight;

            Vector4 margin = text.margin;
            margin.y = Mathf.Max(0f, margin.y);
            margin.w = Mathf.Max(0f, margin.w);
            text.margin = margin;

            RectTransform rect =
                text.rectTransform;

            if (rect != null)
            {
                Vector2 bottomLeft =
                    rect.offsetMin;

                // Height only: right edge and width remain vanilla.
                bottomLeft.y -= 56f;
                rect.offsetMin = bottomLeft;
            }
        }

        private static void ConfigureLarge(
            TMP_Text text)
        {
            if (text == null)
                return;

            // Large map: one right-aligned line with effectively
            // unlimited room to continue LEFT.
            text.enableAutoSizing = false;
            text.textWrappingMode =
                TextWrappingModes.NoWrap;
            text.overflowMode =
                TextOverflowModes.Overflow;
            text.horizontalAlignment =
                HorizontalAlignmentOptions.Right;

            Vector4 margin = text.margin;
            margin.x = 0f;
            margin.z = 0f;
            text.margin = margin;

            RectTransform rect =
                text.rectTransform;

            if (rect != null)
            {
                Vector2 bottomLeft =
                    rect.offsetMin;
                bottomLeft.x -= 4096f;
                rect.offsetMin = bottomLeft;
            }
        }
    }
    [HarmonyPatch(typeof(MessageHud), "UpdateBiomeFound")]
    internal static class BiomeFoundBannerLayoutPatch
    {
        private static int _configuredInstanceId = int.MinValue;

        [HarmonyPostfix]
        private static void Postfix(
            GameObject ___m_biomeMsgInstance)
        {
            if (___m_biomeMsgInstance == null)
            {
                _configuredInstanceId = int.MinValue;
                return;
            }

            int instanceId =
                ___m_biomeMsgInstance.GetInstanceID();

            if (instanceId == _configuredInstanceId)
                return;

            Transform titleTransform =
                Utils.FindChild(
                    ___m_biomeMsgInstance.transform,
                    "Title");

            if (titleTransform == null)
                return;

            TMP_Text title =
                titleTransform.GetComponent<TMP_Text>();

            if (title == null)
                return;

            title.enableAutoSizing = false;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Overflow;
            title.horizontalAlignment =
                HorizontalAlignmentOptions.Center;

            Vector4 margin = title.margin;
            margin.x = 0f;
            margin.z = 0f;
            title.margin = margin;

            RectTransform rect = title.rectTransform;

            if (rect != null)
            {
                Vector2 anchorMin = rect.anchorMin;
                Vector2 anchorMax = rect.anchorMax;
                Vector2 pivot = rect.pivot;
                Vector2 position = rect.anchoredPosition;
                Vector2 size = rect.sizeDelta;

                anchorMin.x = 0.5f;
                anchorMax.x = 0.5f;
                pivot.x = 0.5f;
                position.x = 0f;
                size.x = 4096f;

                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = pivot;
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }

            _configuredInstanceId = instanceId;
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnLanguageChange")]
    internal static class MinimapLanguagePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap __instance)
        {
            if (__instance == null ||
                __instance.m_biomeNameSmall == null ||
                Player.m_localPlayer == null)
                return;

            BiomeSector sector =
                Player.m_localPlayer.GetCurrentBiomeData();

            __instance.m_biomeNameSmall.text =
                BiomeSubtypeNames.Get(sector);

            if (__instance.m_mode != Minimap.MapMode.Large &&
                __instance.m_biomeNameLarge != null)
            {
                __instance.m_biomeNameLarge.text =
                    BiomeSubtypeNames.Get(sector);
            }
        }
    }
}
