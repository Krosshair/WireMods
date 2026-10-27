using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Use the existing Compendium entry and text renderer. Every line
    // comes from the same calculations used for spawning and drops.
    [HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
    internal static class AldrCompendium
    {
        private static readonly AccessTools.FieldRef<TextsDialog,
            List<TextsDialog.TextInfo>> TextEntries =
            AccessTools.FieldRefAccess<TextsDialog,
                List<TextsDialog.TextInfo>>("m_texts");

        private static readonly string[] Biomes =
        {
            "Meadows", "Black Forest", "Swamp", "Mountains",
            "Plains", "Mistlands", "Ashlands", "Deep North"
        };

        // These names appear only for already-defeated contributors.
        // The Deep North boss identity is deliberately never revealed.
        private static readonly string[] Contributors =
        {
            "Eikthyr", "Elder", "Bonemass", "Moder",
            "Yagluth", "Queen", "Fader", "Deep North"
        };

        private sealed class Material
        {
            internal string Name;
            internal int Tier;
            internal string Token;
            internal Material(string name, int tier, string token)
            {
                Name = name; Tier = tier; Token = token;
            }
        }

        // Exactly the corresponding regional material prefab groups
        // in BiomeCatalog. Ordinary wood and stone are Worldwide,
        // core/fine wood are Black Forest, ashwood is Ashlands.
        private static readonly Material[][] RegionalMaterials =
        {
            new Material[0],
            new[] { new Material("Copper", 1, "$item_copperore"), new Material("Tin", 1, "$item_tinore") },
            new[] { new Material("Ancient bark", 2, "$item_elderbark"),
                new Material("Iron scrap", 2, "$item_ironscrap"), new Material("Iron ore", 2, "$item_ironore") },
            new[] { new Material("Silver", 3, "$item_silverore"), new Material("Obsidian", 3, "$item_obsidian") },
            new[] { new Material("Tar", 4, "$item_tar") },
            new[] { new Material("Yggdrasil wood", 5, "$item_yggdrasilwood"),
                new Material("Black marble", 5, "$item_blackmarble"), new Material("Soft tissue", 5, "$item_softtissue") },
            new[] { new Material("Grausten", 6, "$item_grausten"),
                new Material("Flametal ore", 6, "$item_flametalore") },
            new[] { new Material(null, 7, "$item_frostwood"),
                new Material(null, 7, "$item_goldore") }
        };

        [HarmonyPostfix]
        private static void Postfix(TextsDialog __instance)
        {
            if (__instance == null || Plugin.Enabled == null)
                return;
            try
            {
                List<TextsDialog.TextInfo> entries = TextEntries(__instance);
                if (entries != null)
                    entries.Insert(0, new TextsDialog.TextInfo(
                        "Aldr", BuildText()));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(
                    "[Aldr] Compendium unavailable: " + e);
            }
        }

        private static string BuildText()
        {
            var result = new StringBuilder(3000);
            bool[] defeated = WorldProgress.ReadKeys();
            bool enabled = Plugin.Enabled.Value;
            bool creatures = enabled && Plugin.Stars.Value;
            bool forage = enabled && Plugin.Forage.Value;
            bool materials = enabled && Plugin.Materials.Value;
            bool fishing = enabled && Plugin.FishYields.Value;
            bool experience = enabled && Plugin.XpEnabled.Value;

            Header(result, "Mod Status");
            Status(result, "Mod", enabled);
            Status(result, "Creature stars", creatures);
            Status(result, "Wild foraging", forage);
            Status(result, "Natural material yields", materials);
            Status(result, "Wild fishing", fishing);
            Status(result, "Dynamic experience", experience);


            if (materials)
            {
                var worldwide = new StringBuilder();
                GlobalYieldSettings settings = GlobalMaterialProgress.Settings();
                Add(worldwide, ItemName("Wood", "$item_wood"), GlobalYieldRules.Parts(
                    GlobalResource.Wood, defeated, settings));
                Add(worldwide, ItemName("Stone", "$item_stone"), GlobalYieldRules.Parts(
                    GlobalResource.Stone, defeated, settings));
                Section(result, "Worldwide", worldwide);
            }

            for (int tier = 0; tier < Biomes.Length; ++tier)
            {
                var section = new StringBuilder();
                if (defeated[tier] && creatures)
                    Add(section, "Creature star chance",
                        BonusBreakdown.ForBiome(tier, defeated,
                            Plugin.StarBonus.Value,
                            Plugin.LaterFactor.Value / 100.0,
                            Plugin.SkippedFactor.Value / 100.0));

                if (forage)
                {
                    foreach (WildResource resource in ForageRules.ResourcesInBiome(tier))
                    {
                        string plant = ItemName(ForageRules.Name(resource), ForageRules.Token(resource));
                        Add(section, plant,
                            ForageRules.Parts(resource, tier, defeated,
                                Plugin.ForageBonus.Value,
                                Plugin.LaterFactor.Value / 100.0,
                                Plugin.SkippedFactor.Value / 100.0,
                                Plugin.MushroomBlackForestPremium.Value,
                                Plugin.ThistleSwampElderPremium.Value,
                                Plugin.ThistleSwampBonemassPremium.Value));
                        Add(section, plant + " (extra yield)",
                            ForageRules.VeteranParts(resource, tier, defeated,
                                Plugin.VeteranForage.Value,
                                Plugin.SkippedFactor.Value / 100.0, Plugin.LaterFactor.Value / 100.0));
                    }
                }

                if (fishing && defeated[tier])
                    Add(section, "Wild fish catch yield",
                        BonusBreakdown.ForBiome(tier, defeated,
                            Plugin.FishYieldBonus.Value,
                            Plugin.LaterFactor.Value / 100.0,
                            Plugin.SkippedFactor.Value / 100.0));

                if (materials)
                {
                    GlobalYieldSettings settings = GlobalMaterialProgress.Settings();
                    if (tier == 1)
                    {
                        Add(section, ItemName("Core wood", "$item_roundlog"), GlobalYieldRules.Parts(
                            GlobalResource.CoreWood, defeated, settings));
                        Add(section, ItemName("Fine wood", "$item_finewood"), GlobalYieldRules.Parts(
                            GlobalResource.FineWood, defeated, settings));
                    }
                    if (tier == 6)
                        Add(section, ItemName("Ashwood", "$item_blackwood"), GlobalYieldRules.Parts(
                            GlobalResource.Ashwood, defeated, settings));

                    // No ore or high-tier resource spoilers until the
                    // player/world has actually conquered its home biome.
                    if (defeated[tier])
                    {
                        List<BonusBreakdown.Portion> native =
                            BonusBreakdown.Veteran(tier, defeated,
                                Plugin.MaterialBonus.Value,
                                Plugin.VeteranMaterials.Value,
                                Plugin.SkippedFactor.Value / 100.0, Plugin.LaterFactor.Value / 100.0);
                        foreach (Material material in RegionalMaterials[tier])
                            Add(section, ItemName(material.Name, material.Token), native);
                    }
                }
                Section(result, KnownBiome(tier, defeated) ? Biomes[tier] : Unknown, section);
            }

            if (experience && Player.m_localPlayer != null)
                Section(result, "Experience", ExperienceInfo(Player.m_localPlayer, defeated));

            return result.ToString();
        }

        // The game's own Compendium style for headings and values (orange);
        // where a value comes from in small gray italics after it.
        private static string Value(string v) => "<color=orange>" + v + "</color>";
        private static string Sub(string v) => "  <color=#99A6AD><size=80%><i>" + v + "</i></size></color>";

        // XP rules that apply to several skills, for the player right now.
        // Everything adds to the location bonus; only raids multiply.
        private static StringBuilder ExperienceInfo(Player p, bool[] defeated)
        {
            var sb = new StringBuilder();
            double location, history;
            DynamicExperience.BiomeXpHere(p, out location, out history);
            Heightmap.Biome here = p.GetCurrentBiome();
            int hereIndex = BiomeCatalog.FromBiome(here);
            string hereName = here == Heightmap.Biome.Ocean ? "Ocean"
                : hereIndex >= 0 ? Biomes[hereIndex] : Unknown;
            sb.Append("Location XP bonus: ").Append(Value("+" + Num((location + history) * 100, 0) + "%"));
            if (location + history > 0.0005)
            {
                string parts = "+" + Num(location * 100, 0) + "% " + hereName;
                if (history > 0.0005) parts += ", +" + Num(history * 100, 0) + "% Forsaken";
                sb.Append(Sub(parts));
            }
            sb.AppendLine();

            // Catch-up and weapon transfer don't stack: the larger applies.
            if (Plugin.XpCatchUpBonus.Value > 0f)
            {
                int tier = DynamicExperience.CatchUpTierOf(p);
                if (tier > 1)
                {
                    int below = (int)Math.Round(ExperienceMath.CatchUpThreshold(tier, Plugin.XpCatchUpBase.Value, Plugin.XpCatchUpPerTier.Value));
                    string cap = Num(Plugin.XpCatchUpBonus.Value, 0);
                    sb.Append("XP bonus for skills below ").Append(below).Append(": ").Append(Value("<" + cap + "%"))
                      .AppendLine(Sub(cap + "% x (" + below + " - skill) / " + below));
                }
            }
            Skills.SkillType best; float level;
            if (Plugin.XpWeaponTransfer.Value > 0f && DynamicExperience.BestWeapon(p, out best, out level))
            {
                string skill = Localization.instance != null
                    ? Localization.instance.Localize("$skill_" + best.ToString().ToLowerInvariant()) : best.ToString();
                string below = Num(Math.Floor(level * Plugin.XpTransferShare.Value / 100f), 0);
                string cap = Num(Plugin.XpWeaponTransfer.Value, 0);
                sb.Append("XP bonus for combat skills below ").Append(below).Append(": ").Append(Value("<" + cap + "%"))
                  .AppendLine(Sub(cap + "% x (" + below + " - skill) / " + below + "; " + Num(Plugin.XpTransferShare.Value, 0) + "% of " + skill + " " +
                      Num(Math.Floor(level), 0) + "; the larger of these two applies"));
            }

            if (Plugin.XpChallenge.Value)
            {
                sb.AppendLine();
                sb.Append("Starred enemy bonus: ").Append(Value("+" + Num(Plugin.XpStarBonus.Value, 1) + "% per star")).AppendLine(Sub("combat XP"));
                if (Plugin.RaidBonus.Value > 0f)
                    sb.Append("Combat XP bonus during raids: ").Append(Value("x" + Num(1 + Plugin.RaidBonus.Value / 100f, 2)))
                      .AppendLine(Sub("against raiders, multiplies all other XP bonuses"));
            }

            if (SleepBonus.Enabled)
            {
                sb.AppendLine();
                sb.Append("Sleep bonus: ").Append(Value("+" + Num(Plugin.SleepXpShare.Value, 1) + "%"))
                  .AppendLine(Sub("of the XP each skill earned in the last day, added when you wake up; older XP counts for less"));
            }

            if (Expedition.Enabled)
            {
                sb.AppendLine();
                bool paused = Expedition.AtBase && Expedition.StoredPercent > 0.05f;
                sb.Append("Expedition bonus: ").Append(Value("+" + Num(Expedition.ActivePercent, 1, true) + "%"));
                if (Expedition.Frozen)
                    sb.Append(Sub(Num(Expedition.StoredPercent, 1, true) + "% after death, frozen until you recover your gravestone"));
                else if (paused)
                    sb.Append(Sub(Num(Expedition.StoredPercent, 1, true) + "% kept while you're at a base; fades slowly at first, then faster the longer you stay"));
                else
                {
                    int minutes = Mathf.FloorToInt(Expedition.Minutes);
                    sb.Append(Sub(minutes + (minutes == 1 ? " minute" : " minutes") +
                        " out, increases with XP gain and time spent adventuring"));
                }
                sb.AppendLine();
            }
            return sb;
        }

        private const string Unknown = "???";
        private static readonly AccessTools.FieldRef<Player, HashSet<string>> KnownBiomes =
            AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownBiome");

        // Anything not yet discovered by this character shows as ???.
        private static bool KnownItem(string token)
        {
            Player p = Player.m_localPlayer;
            return p == null || string.IsNullOrEmpty(token) || p.IsMaterialKnown(token);
        }

        private static string ItemName(string fallback, string token)
        {
            if (!KnownItem(token)) return Unknown;
            if (!string.IsNullOrEmpty(fallback)) return fallback;
            string localized = Localization.instance != null ? Localization.instance.Localize(token) : token;
            return string.IsNullOrEmpty(localized) || localized.StartsWith("[") ? Unknown : localized;
        }

        private static bool KnownBiome(int tier, bool[] defeated)
        {
            Player p = Player.m_localPlayer;
            if (p == null || defeated[tier]) return true;
            if (BiomeCatalog.FromBiome(p.GetCurrentBiome()) == tier) return true;
            HashSet<string> known = KnownBiomes(p);
            if (known == null || Localization.instance == null) return false;
            string name = Localization.instance.Localize(
                BiomeSector.GetBiomeName(BiomeCatalog.TierBiome(tier)));
            foreach (string entry in known)
                if (!string.IsNullOrEmpty(entry) &&
                    entry.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static void Header(StringBuilder sb, string heading)
        {
            sb.Append("<color=orange>")
                .Append(heading).AppendLine("</color>");
        }

        private static void Status(StringBuilder sb, string label, bool on)
        {
            sb.Append(label).Append(": ")
                .AppendLine(Value(on ? "Enabled" : "Disabled"));
        }

        private static void Section(StringBuilder sb, string heading,
            StringBuilder content)
        {
            if (content.Length == 0) return;
            sb.AppendLine();
            Header(sb, heading);
            sb.Append(content);
        }

        private static void Add(StringBuilder sb, string resource,
            List<BonusBreakdown.Portion> parts)
        {
            if (parts == null || parts.Count == 0) return;
            double total = 0;
            foreach (BonusBreakdown.Portion part in parts)
                total += part.Percent;
            if (total <= 0) return;

            int decimals = BonusBreakdown.Precision(total, parts);
            sb.Append(resource).Append(": <color=orange>+")
                .Append(Num(total, decimals)).Append("%</color>");

            // One source is self-explanatory. Show short calculations
            // inline; only wrap longer calculations onto their own row.
            if (parts.Count < 2)
            {
                sb.AppendLine();
                return;
            }
            if (parts.Count <= 2)
                sb.Append("  ");
            else
                sb.AppendLine();
            sb.Append("<color=#99A6AD><size=80%><i>");
            for (int i = 0; i < parts.Count; i++)
            {
                if (i != 0) sb.Append(" + ");
                sb.Append(Num(parts[i].Percent, decimals))
                    .Append("% ")
                    .Append(Contributors[parts[i].BossTier]);
            }
            sb.AppendLine("</i></size></color>");
        }

        // fixed: always show the decimals (3.0%, not 3%).
        private static string Num(double n, int precision, bool fixedDecimals)
        {
            return n.ToString("0." + new string(fixedDecimals ? '0' : '#', precision),
                CultureInfo.InvariantCulture);
        }

        private static string Num(double n, int precision)
        {
            return n.ToString("0." + new string('#', precision),
                CultureInfo.InvariantCulture);
        }
    }
}
