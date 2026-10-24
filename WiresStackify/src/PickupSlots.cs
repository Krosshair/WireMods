using HarmonyLib;

namespace WiresStackify
{
    // Items coming into your inventory without a slot chosen (picking up,
    // auto pickup, crafting) skip favorited slots and take the next free
    // slot instead. Stacking onto a stack that's already in a favorited slot
    // still happens as usual, favorited items may still use favorited
    // slots, and when no other slot is free a favorited one is used anyway.
    // Placing an item into a slot yourself is never affected.
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
                if (!fav.IsSlotFavorited(__result)) return;
                if (_adding != null && fav.IsItemFavorited(_adding.m_shared)) return;

                int w = __instance.GetWidth();
                int rows = StackifyPlugin.MainRows(__instance, p);
                for (int i = 0; i < rows; i++)
                {
                    int y = topFirst ? i : rows - 1 - i;
                    for (int x = 0; x < w; x++)
                    {
                        Vector2i v = new Vector2i(x, y);
                        if (__instance.GetItemAt(x, y) == null && !fav.IsSlotFavorited(v))
                        {
                            __result = v;
                            return;
                        }
                    }
                }
                // Nothing else free: the favorited slot it is.
            }
        }
    }
}
