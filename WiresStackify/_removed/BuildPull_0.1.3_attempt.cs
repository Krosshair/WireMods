using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresStackify
{
    // Shift + right click a piece in the build menu: pulls what it costs
    // from nearby containers, enough to build it one more time than you
    // can now. Never past what you can carry. Runs only on the click.
    internal static class BuildPull
    {
        private static readonly AccessTools.FieldRef<BuildUi, BuildUiPieceButton> Hovered =
            AccessTools.FieldRefAccess<BuildUi, BuildUiPieceButton>("m_currentHoveredPieceButton");

        // The build menu's own input check, while it's open: right click
        // there normally closes it. With shift held over a piece, it pulls
        // instead and the menu stays open (that frame's menu input is
        // skipped). Only a key check each frame; the rest on the click.
        [HarmonyPatch(typeof(BuildUi), "NavigationUpdate")]
        private static class RightClickPatch
        {
            private static bool Prefix(BuildUi __instance)
            {
                if (!StackifyPlugin.BuildMenuPull.Value || !Input.GetMouseButtonDown(1) || !Actions.Shift()) return true;
                BuildUiPieceButton button = Hovered(__instance);
                if (button == null || button.Piece == null) return true;
                try { Pull(button.Piece); button.UpdateRequirements(); }
                catch (Exception e) { StackifyPlugin.Log.LogWarning("Stackify build pull failed: " + e); }
                return false;
            }
        }

        private sealed class Need
        {
            public string Name;
            public int Left;
        }

        private static void Pull(Piece piece)
        {
            Player p = Player.m_localPlayer;
            if (p == null || p.IsTeleporting() || piece.m_resources == null) return;
            Actions.Rustle();

            // How many times you can build it now, then what one more needs.
            Inventory inv = p.GetInventory();
            int builds = int.MaxValue;
            foreach (Piece.Requirement r in piece.m_resources)
                if (r != null && r.m_resItem != null && r.m_amount > 0)
                    builds = Math.Min(builds, inv.CountItems(r.m_resItem.m_itemData.m_shared.m_name) / r.m_amount);
            if (builds == int.MaxValue) return; // costs nothing
            List<Need> needs = new List<Need>();
            foreach (Piece.Requirement r in piece.m_resources)
            {
                if (r == null || r.m_resItem == null || r.m_amount <= 0) continue;
                string name = r.m_resItem.m_itemData.m_shared.m_name;
                int left = (builds + 1) * r.m_amount - inv.CountItems(name);
                if (left > 0) needs.Add(new Need { Name = name, Left = left });
            }
            if (needs.Count == 0) return;

            float room = Math.Max(0f, p.GetMaxCarryWeight() - inv.GetTotalWeight());
            int moved = 0;
            List<StackifyEffects.Hit> hits = new List<StackifyEffects.Hit>();
            foreach (Container c in NearbyChests.ForPouch(p, false, null, StackifyPlugin.RestockRange.Value))
            {
                if (room <= 0f || needs.Count == 0) break;
                int n;
                if (StackifyPlugin.HasMuc) n = FromContainer(needs, inv, c.GetInventory(), ref room);
                else
                {
                    NearbyChests.Claim(c, true);
                    n = FromContainer(needs, inv, c.GetInventory(), ref room);
                    NearbyChests.Claim(c, false);
                }
                if (n > 0) { moved += n; hits.Add(new StackifyEffects.Hit { Chest = c, Count = n }); }
            }
            if (moved == 0) return;
            StackifyPlugin.Changed(inv);
            Actions.Later(p.m_pickupEffects);
            StackifyEffects.Play(StackifyEffects.Kind.Restock, hits, null, p, false);
        }

        // Takes what's still needed from one container; returns how many
        // stacks it took from.
        private static int FromContainer(List<Need> needs, Inventory inv, Inventory from, ref float room)
        {
            int took = 0;
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(from.GetAllItems());
            for (int k = needs.Count - 1; k >= 0; k--)
            {
                Need need = needs[k];
                foreach (ItemDrop.ItemData it in items)
                {
                    if (need.Left <= 0 || room <= 0f) break;
                    if (it.m_stack <= 0 || it.m_shared.m_name != need.Name ||
                        (it.m_customData != null && it.m_customData.Count > 0)) continue;
                    int take = Math.Min(need.Left, it.m_stack);
                    float unit = it.GetWeight(1);
                    if (unit > 0f) take = Math.Min(take, (int)Math.Floor(room / unit + 0.0001f));
                    if (take <= 0) break; // too heavy to carry any more of it
                    ItemDrop.ItemData copy = it.Clone();
                    copy.m_stack = take;
                    if (!inv.CanAddItem(copy)) return took; // no room in your inventory
                    inv.AddItem(copy);
                    from.RemoveItem(it, take);
                    need.Left -= take;
                    room -= unit * take;
                    took++;
                }
                if (need.Left <= 0) needs.RemoveAt(k);
            }
            return took;
        }
    }
}
