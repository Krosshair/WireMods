using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    internal static class AldrDiagnostics
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> Skills = new Dictionary<string, int>();
        private static StreamWriter output;
        private static ManualLogSource logger;
        internal static string FilePath { get; private set; }
        internal static bool Active => Plugin.Diagnostics != null && Plugin.Diagnostics.Value;

        internal static void Start(ManualLogSource log)
        {
            logger = log;
            if (!Active) return;
            try
            {
                FilePath = Path.Combine(Paths.BepInExRootPath, "Aldr-Diagnostics.log");
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2097152)
                {
                    var previous = FilePath + ".previous.log";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(FilePath, previous);
                }
                output = new StreamWriter(new FileStream(FilePath, FileMode.Append,
                    FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
                // Avoid forcing OS disk writes from the gameplay thread on each event.
                output.AutoFlush = false;
                Log("SESSION", "======= " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    " Aldr " + Plugin.Version + " =======");
                Log("EXPLANATION", "PATCH_OK means installed, not tested. EVENT means actual gameplay trigger.");
                Log("EXPLANATION", "Smelter events are diagnostics ONLY. Aldr does not change smelter output or grant smelting XP.");
                logger.LogInfo("Aldr diagnostics: " + FilePath);
            }
            catch (Exception e)
            {
                logger.LogWarning("Cannot open Aldr diagnostics: " + e.Message);
                try { output?.Dispose(); } catch { }
                output = null;
            }
        }
        internal static void Log(string category, string message)
        {
            if (!Active || output == null) return;
            lock (Sync)
            {
                try { output.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") +
                    " [" + category + "] " + message); }
                catch (Exception e)
                {
                    try { logger?.LogWarning("Aldr diagnostic log write failed: " + e.Message); }
                    catch { }
                    try { output.Dispose(); } catch { }
                    output = null;
                }
            }
        }
        internal static void Sample(string category, string message)
        {
            if (!Active) return;
            int n;
            lock (Sync) { Counts.TryGetValue(category, out n); Counts[category] = ++n; }
            if (n <= 5 || n % 20 == 0) Log(category, "#" + n + " " + message);
        }
        internal static void Skill(Skills.SkillType skill, float original,
            float final, Player player)
        {
            if (!Active || !player) return;
            var name = skill.ToString();
            int n;
            lock (Sync) { Skills.TryGetValue(name, out n); Skills[name] = ++n; }
            if (n <= 3 || n == 10 || n % 50 == 0)
                Log("XP_EVENT", "skill=" + name + " #" + n +
                    " initial=" + original.ToString("0.####") +
                    " modified=" + final.ToString("0.####") +
                    " factor=" + (final / Math.Max(0.00001f, original)).ToString("0.###") +
                    "x level=" + player.GetSkillLevel(skill).ToString("0.##") +
                    " biome=" + player.GetCurrentBiome());
        }
        internal static void World(Player player, string origin)
        {
            if (!Active || !player) return;
            try
            {
                bool[] keys = WorldProgress.ReadKeys();
                int tier = 1;
                string defeated = "";
                for (int i = 0; i < keys.Length; i++)
                    if (keys[i])
                    {
                        tier = Math.Max(tier, i + 2);
                        defeated += (defeated.Length == 0 ? "" : ",") + (i + 1);
                    }
                int local = DynamicExperience.BiomeTier(player.GetCurrentBiome());
                double xp = ExperienceMath.BiomeAndProgress(local, tier,
                    Plugin.XpProgressionStrength.Value);
                Log("WORLD_READY", "source=" + origin +
                    " biome=" + player.GetCurrentBiome() +
                    " biomeTier=" + local + " bossTier=" + tier +
                    " bossKeys=[" + defeated + "]" +
                    " playerBasedRaids=" + WorldProgress.IsPlayerBased() +
                    " baseXp=" + xp.ToString("0.###") + "x");
            }
            catch (Exception e) { Log("WARN", "World snapshot unavailable: " + e.Message); }
        }
        internal static void Settings()
        {
            if (!Active) return;
            Log("FEATURES", "master=" + Plugin.Enabled.Value +
                " creatures=" + Plugin.Stars.Value + " forage=" + Plugin.Forage.Value +
                " materials=" + Plugin.Materials.Value +
                " fishing=" + Plugin.FishYields.Value +
                " xp=" + Plugin.XpEnabled.Value +
                " challenge=" + Plugin.XpChallenge.Value);
            Log("CONFIG", "biomeXp=" + Plugin.XpProgressionStrength.Value +
                "% catchUp=" + Plugin.XpCatchUpBonus.Value +
                "% combat=" + Plugin.XpCombatStrength.Value +
                "% parry=" + Plugin.XpParryStrength.Value +
                "% dodge=" + Plugin.XpDodgeStrength.Value +
                "% fishYield=" + Plugin.FishYieldBonus.Value +
                "% forageVeteran=" + Plugin.VeteranForage.Value +
                "% materialVeteran=" + Plugin.VeteranMaterials.Value +
                "% expeditionPerMinute=" + Plugin.VenturePerKm.Value + "% expeditionCap=" + Plugin.VentureCap.Value +
                "% raid=" + Plugin.RaidBonus.Value + "%");
            try
            {
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    string name = kv.Value.Metadata.Name ?? "";
                    if (name.IndexOf("Fortif", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Notification", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Experience", StringComparison.OrdinalIgnoreCase) >= 0)
                        Log("COMPAT_LOADED", kv.Key + " (" + name + ")");
                }
            }
            catch (Exception e) { Log("WARN", "Cannot enumerate companion plugins: " + e.Message); }
        }
        internal static void Summary(string reason)
        {
            if (!Active) return;
            string events = "";
            lock (Sync)
                foreach (var entry in Counts)
                    events += (events.Length > 0 ? ", " : "") +
                        entry.Key + "=" + entry.Value;
            Log("SUMMARY", reason + " XP_skill_types_seen=" + Skills.Count +
                " events={" + events + "} unobserved mechanics remain UNTESTED");
            Flush();
        }
        internal static void Flush()
        {
            if (!Active) return;
            lock (Sync)
            {
                try { output?.Flush(); }
                catch (Exception e) { logger?.LogWarning("Aldr diagnostic flush failed: " + e.Message); }
            }
        }
        internal static void Close()
        {
            Summary("shutdown");
            lock (Sync)
            {
                try { output?.Flush(); output?.Dispose(); }
                catch { }
                output = null;
            }
        }
    }
}
