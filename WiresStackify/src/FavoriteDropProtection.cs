using HarmonyLib;

namespace WiresStackify
{
    // Both Valheim's drop button and MouseTweaks' quick-drop
    // ultimately call Humanoid.DropItem. Intercept only a real
    // world drop, never an inventory/container transfer.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
    internal static class FavoriteDropProtection
    {
        [HarmonyPrefix]
        private static bool Prefix(Humanoid __instance, Inventory inventory,
            ItemDrop.ItemData item, ref bool __result)
        {
            Player player = Player.m_localPlayer;
            if (player == null || __instance != player || item == null ||
                item.m_shared == null)
                return true;

            Favorites favorites = Favorites.For(player);
            Inventory source = inventory ?? player.GetInventory();
            bool favoriteItem = favorites.IsItemFavorited(item.m_shared);
            bool favoriteSlot = source == player.GetInventory() &&
                favorites.IsSlotFavorited(item.m_gridPos);
            if (!favoriteItem && !favoriteSlot)
                return true;

            StackifyPlugin.Log.LogDebug(
                "Stackify: blocked dropping a favorited item or slot.");
            __result = false;
            return false;
        }
    }
}
