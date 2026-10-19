using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace WiresHoodToggle
{
    // A separate character-ID keyed list, not part of the BepInEx options.
    // Disk writes happen ONLY after successful actual character saves.
    internal static class SavedHelmetStates
    {
        private const string FileName = "wire.valheim.helmethider.hidden.txt";
        private static readonly HashSet<long> HiddenIds = new HashSet<long>();
        private static string _path;
        private static bool _loaded;
        private static bool _dirty;

        internal static string PathToFile => _path;

        internal static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;
            _path = Path.Combine(Paths.ConfigPath, FileName);
            HiddenIds.Clear();

            try
            {
                if (!File.Exists(_path))
                {
                    // Create the independent state file once. Nothing is
                    // written in the per-frame UI/gameplay update loop.
                    File.WriteAllText(_path, string.Empty);
                    return;
                }

                foreach (string line in File.ReadAllLines(_path))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                        continue;

                    // Presence in this list means hidden; no flag needed.
                    long id;
                    if (long.TryParse(trimmed, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out id) &&
                        id != 0)
                    {
                        HiddenIds.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning(
                    "Could not read helmet-hidden state file: " + ex.Message);
            }
        }

        internal static bool IsHidden(long playerId)
        {
            Load();
            return playerId != 0 && HiddenIds.Contains(playerId);
        }

        internal static void SetHidden(long playerId, bool hidden)
        {
            if (playerId == 0)
                return;
            Load();

            bool changed = hidden
                ? HiddenIds.Add(playerId)
                : HiddenIds.Remove(playerId);
            if (changed)
                _dirty = true;
        }

        internal static void AfterCharacterSaved(PlayerProfile profile,
            bool saveSucceeded)
        {
            if (!saveSucceeded || !_dirty || profile == null)
                return;

            // Avoid saving during character-creation menu operations.
            if (Game.instance == null ||
                Player.m_localPlayer == null ||
                Game.instance.GetPlayerProfile() != profile ||
                Player.m_localPlayer.GetPlayerID() != profile.GetPlayerID())
                return;

            try
            {
                string[] lines = new string[HiddenIds.Count];
                List<long> sorted = new List<long>(HiddenIds);
                sorted.Sort();
                for (int i = 0; i < sorted.Count; ++i)
                    lines[i] = sorted[i].ToString(
                        CultureInfo.InvariantCulture);

                string tmp = _path + ".tmp";
                File.WriteAllLines(tmp, lines);
                if (File.Exists(_path))
                    File.Replace(tmp, _path, null);
                else
                    File.Move(tmp, _path);
                _dirty = false;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning(
                    "Could not save helmet-hidden state file: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Save))]
    internal static class HelmetStatePlayerProfileSavePatch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerProfile __instance, bool __result)
        {
            SavedHelmetStates.AfterCharacterSaved(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Game), "SpawnPlayer")]
    internal static class HelmetStateSpawnPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __result)
        {
            HoodVisuals.RestoreLocalSavedState(__result);
        }
    }

    [HarmonyPatch(typeof(FejdStartup), "SetupCharacterPreview")]
    internal static class HelmetStatePreviewPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FejdStartup __instance,
            PlayerProfile profile)
        {
            Player preview = __instance != null
                ? __instance.GetPreviewPlayer() : null;
            HoodVisuals.SetMenuPreview(
                preview, profile != null ? profile.GetPlayerID() : 0L);
        }
    }
}
