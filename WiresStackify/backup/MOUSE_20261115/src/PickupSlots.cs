using HarmonyLib;

namespace WiresStackify
{
    // Items entering without a chosen slot prefer the first empty
    // favorited slot if their type is favorited. Ordinary items avoid
    // favorited slots unless all other slots are full. Existing stacks
    // still merge normally; manual slot placement remains untouched.
    internal static class PickupSlots
    {
        private static ItemDrop.ItemData _adding;

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
        private static class AddItemPatch
        {
            private static void Prefix(ItemDrop.ItemData item, out ItemDrop.ItemData __state)
            {
                __state = _adding;
                _adding = item;
            }

            private static void Finalizer(ItemDrop.ItemData __state)
            {
                _adding = __state;
            }
        }

        [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
        private static class FindEmptySlotPatch
        {
            private static void Postfix(Inventory __instance, bool topFirst, ref Vector2i __result)
            {
                if (__result.x < 0) return;
                Player p = Player.m_localPlayer;
                if (p == null || __instance != p.GetInventory()) return;
                Favorites fav = Favorites.For(p);
                bool favoriteItem = _adding != null && fav.IsItemFavorited(_adding.m_shared);
                // Favorited items prefer empty favorite slots. Other items
                // avoid those slots unless no ordinary slot is available.
                if (!favoriteItem && !fav.IsSlotFavorited(__result)) return;

                int w = __instance.GetWidth();
                int rows = StackifyPlugin.MainRows(__instance, p);
                for (int i = 0; i < rows; i++)
                {
                    int y = topFirst ? i : rows - 1 - i;
                    for (int x = 0; x < w; x++)
                    {
                        Vector2i v = new Vector2i(x, y);
                        if (fav.IsSlotFavorited(v) == favoriteItem && __instance.GetItemAt(x, y) == null)
                        {
                            __result = v;
                            return;
                        }
                    }
                }
                // No preferred slot free: keep the game's original free slot.
            }
        }
    }
}
