using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;

namespace WiresWeaponMaster
{
    // Which items are shown on the back, kept per character in a separate
    // file (not part of the BepInEx options). Same approach as Helmet Hider:
    // keyed by Valheim's stable player ID, and the file is only written after
    // a successful character save.
    //
    // One line per remembered item: playerId, lane, slot x, slot y, prefab
    // name (tab separated), most recent first within each lane. The slot and
    // prefab together find the same item again after loading.
    internal static class SavedWeaponStates
    {
        private const string FileName = "wire.valheim.weaponmaster.shown.txt";

        private struct Entry
        {
            internal int Lane;
            internal int X;
            internal int Y;
            internal string Prefab;

            internal bool SameAs(Entry other)
            {
                return Lane == other.Lane && X == other.X && Y == other.Y &&
                       string.Equals(Prefab, other.Prefab, StringComparison.Ordinal);
            }
        }

        private static readonly Dictionary<long, List<Entry>> Saved =
            new Dictionary<long, List<Entry>>();
        private static string _path;
        private static bool _loaded;

        private static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;
            _path = Path.Combine(Paths.ConfigPath, FileName);
            Saved.Clear();

            try
            {
                if (!File.Exists(_path))
                    return;

                foreach (string line in File.ReadAllLines(_path))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                        continue;

                    string[] parts = trimmed.Split('\t');
                    if (parts.Length < 5)
                        continue;

                    long id;
                    int lane, x, y;
                    if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id == 0 ||
                        !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out lane) ||
                        !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ||
                        !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out y) ||
                        lane <= (int)HolsterLane.None || lane > (int)HolsterLane.Atgeir ||
                        parts[4].Length == 0)
                        continue;

                    List<Entry> list;
                    if (!Saved.TryGetValue(id, out list))
                    {
                        list = new List<Entry>();
                        Saved.Add(id, list);
                    }

                    list.Add(new Entry { Lane = lane, X = x, Y = y, Prefab = parts[4] });
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("Could not read shown-weapons file: " + ex.Message);
            }
        }

        // Called on the local player's Start, after the history is reset and
        // before the items in hand are added on top.
        internal static void Restore(Player player)
        {
            if (player == null || !WeaponMasterConfig.RememberShown)
                return;

            long id = player.GetPlayerID();
            if (id == 0)
                return;

            Load();
            List<Entry> entries;
            if (!Saved.TryGetValue(id, out entries) || entries.Count == 0)
                return;

            Inventory inventory = player.GetInventory();
            if (inventory == null)
                return;

            Matched.Clear();
            MatchItems(inventory, entries);
            for (int i = 0; i < Matched.Count; i++)
                WeaponMasterController.RestoreToLane(Matched[i].Key, (HolsterLane)Matched[i].Value);
            Matched.Clear();
        }

        // What the main menu character preview should show: per lane, the
        // most recent remembered item that is not in the character's hands.
        internal static HolsterSelection[] BuildPreviewSelections(Player preview, long id)
        {
            if (preview == null || id == 0 || !WeaponMasterConfig.RememberShown)
                return null;

            Load();
            List<Entry> entries;
            if (!Saved.TryGetValue(id, out entries) || entries.Count == 0)
                return null;

            Inventory inventory = preview.GetInventory();
            if (inventory == null)
                return null;

            ItemDrop.ItemData left = WeaponMasterController.GetLeft(preview);
            ItemDrop.ItemData right = WeaponMasterController.GetRight(preview);
            HolsterSelection[] selections = new HolsterSelection[(int)HolsterLane.Atgeir + 1];
            bool any = false;

            Matched.Clear();
            MatchItems(inventory, entries);
            for (int i = 0; i < Matched.Count; i++)
            {
                ItemDrop.ItemData item = Matched[i].Key;
                int lane = Matched[i].Value;
                if (selections[lane].Hash != 0 ||
                    ReferenceEquals(item, left) || ReferenceEquals(item, right) ||
                    !WeaponMasterConfig.IsLaneEnabled((HolsterLane)lane) ||
                    !ItemClassifier.HasHolsterVisual(item))
                    continue;

                selections[lane] = new HolsterSelection(
                    item.m_dropPrefab.name.GetStableHashCode(),
                    item.m_variant,
                    item.m_quality);
                any = true;
            }
            Matched.Clear();

            return any ? selections : null;
        }

        private static readonly List<KeyValuePair<ItemDrop.ItemData, int>> Matched =
            new List<KeyValuePair<ItemDrop.ItemData, int>>();
        private static readonly HashSet<ItemDrop.ItemData> Used =
            new HashSet<ItemDrop.ItemData>();

        // Finds the saved items in an inventory, in saved order.
        private static void MatchItems(Inventory inventory, List<Entry> entries)
        {
            List<ItemDrop.ItemData> all = inventory.GetAllItems();
            HashSet<ItemDrop.ItemData> used = Used;
            used.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                ItemDrop.ItemData item = inventory.GetItemAt(entry.X, entry.Y);
                if (item == null || used.Contains(item) || PrefabName(item) != entry.Prefab)
                {
                    // Slot changed somehow: fall back to the same kind of item.
                    item = null;
                    for (int j = 0; j < all.Count; j++)
                    {
                        ItemDrop.ItemData candidate = all[j];
                        if (candidate != null && !used.Contains(candidate) &&
                            PrefabName(candidate) == entry.Prefab)
                        {
                            item = candidate;
                            break;
                        }
                    }
                }

                if (item == null)
                    continue;

                used.Add(item);
                Matched.Add(new KeyValuePair<ItemDrop.ItemData, int>(item, entry.Lane));
            }
            used.Clear();
        }

        internal static void AfterCharacterSaved(PlayerProfile profile, bool saveSucceeded)
        {
            if (!saveSucceeded || profile == null || !WeaponMasterConfig.RememberShown)
                return;

            // Avoid saving during character-creation menu operations.
            Player player = Player.m_localPlayer;
            if (Game.instance == null || player == null ||
                Game.instance.GetPlayerProfile() != profile ||
                player.GetPlayerID() != profile.GetPlayerID())
                return;

            long id = player.GetPlayerID();
            if (id == 0)
                return;

            Load();
            List<Entry> current = Snapshot(player);
            List<Entry> previous;
            Saved.TryGetValue(id, out previous);
            if (SameList(previous, current))
                return;

            if (current.Count == 0)
                Saved.Remove(id);
            else
                Saved[id] = current;

            Write();
        }

        private static List<Entry> Snapshot(Player player)
        {
            List<Entry> result = new List<Entry>();
            Inventory inventory = player.GetInventory();
            if (inventory == null)
                return result;

            for (int lane = (int)HolsterLane.Shield; lane <= (int)HolsterLane.Atgeir; lane++)
            {
                List<ItemDrop.ItemData> history = WeaponMasterController.GetHistory((HolsterLane)lane);
                if (history == null)
                    continue;

                for (int i = 0; i < history.Count; i++)
                {
                    ItemDrop.ItemData item = history[i];
                    string prefab = PrefabName(item);
                    if (prefab == null || !inventory.ContainsItem(item))
                        continue;

                    result.Add(new Entry
                    {
                        Lane = lane,
                        X = item.m_gridPos.x,
                        Y = item.m_gridPos.y,
                        Prefab = prefab
                    });
                }
            }

            return result;
        }

        private static bool SameList(List<Entry> a, List<Entry> b)
        {
            int countA = a != null ? a.Count : 0;
            int countB = b != null ? b.Count : 0;
            if (countA != countB)
                return false;
            for (int i = 0; i < countA; i++)
            {
                if (!a[i].SameAs(b[i]))
                    return false;
            }
            return true;
        }

        private static void Write()
        {
            try
            {
                List<long> ids = new List<long>(Saved.Keys);
                ids.Sort();
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < ids.Count; i++)
                {
                    List<Entry> list = Saved[ids[i]];
                    for (int j = 0; j < list.Count; j++)
                    {
                        Entry e = list[j];
                        sb.Append(ids[i].ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(e.Lane.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(e.X.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(e.Y.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(e.Prefab).Append('\n');
                    }
                }

                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(_path))
                    File.Replace(tmp, _path, null);
                else
                    File.Move(tmp, _path);
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("Could not save shown-weapons file: " + ex.Message);
            }
        }

        private static string PrefabName(ItemDrop.ItemData item)
        {
            if (item == null || item.m_dropPrefab == null)
                return null;
            string name = item.m_dropPrefab.name;
            return string.IsNullOrEmpty(name) || name.IndexOf('\t') >= 0 || name.IndexOf('\n') >= 0
                ? null
                : name;
        }
    }

    [HarmonyPatch(typeof(FejdStartup), "SetupCharacterPreview")]
    internal static class WeaponStatePreviewPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FejdStartup __instance, PlayerProfile profile)
        {
            Player preview = __instance != null ? __instance.GetPreviewPlayer() : null;
            if (preview == null)
                return;

            WeaponVisuals.SetMenuPreview(
                preview,
                SavedWeaponStates.BuildPreviewSelections(
                    preview, profile != null ? profile.GetPlayerID() : 0L));
        }
    }

    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Save))]
    internal static class WeaponStatePlayerProfileSavePatch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerProfile __instance, bool __result)
        {
            SavedWeaponStates.AfterCharacterSaved(__instance, __result);
        }
    }
}
