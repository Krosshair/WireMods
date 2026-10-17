using System;
using System.Collections.Generic;
using HarmonyLib;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class KeywordSync
    {
        private const string RequestRpc =
            "WBCFNC_KeywordRequest";

        private const string ConfigRpc =
            "WBCFNC_Keywords";

        private static readonly Dictionary<string, string[]>
            ServerKeywords =
                new Dictionary<string, string[]>(
                    StringComparer.Ordinal);

        private static bool _registered;

        internal static bool ServerEnforced
        {
            get;
            private set;
        }

        internal static string[] GetKeywords(
            string routeId,
            string[] localValues)
        {
            string[] serverValues;
            if (ServerEnforced &&
                routeId != null &&
                ServerKeywords.TryGetValue(
                    routeId,
                    out serverValues))
                return serverValues;

            return localValues ?? new string[0];
        }

        internal static void Register()
        {
            if (_registered ||
                ZRoutedRpc.instance == null)
                return;

            ZRoutedRpc.instance.Register(
                RequestRpc,
                new Action<long>(
                    OnConfigRequested));

            ZRoutedRpc.instance.Register(
                ConfigRpc,
                new Action<long, ZPackage>(
                    OnConfigReceived));

            _registered = true;
        }

        internal static void RequestFromServer()
        {
            if (ZNet.instance == null ||
                ZNet.instance.IsServer() ||
                ZRoutedRpc.instance == null)
                return;

            ZRoutedRpc.instance.InvokeRoutedRPC(
                RequestRpc);
        }

        internal static void NotifyLocalKeywordChanged()
        {
            ContainerRegistry.Invalidate();

            if (ZNet.instance == null ||
                !ZNet.instance.IsServer() ||
                ZRoutedRpc.instance == null)
                return;

            // Connected clients with the mod can refresh without
            // reconnecting when a host changes a synchronized keyword.
            ZPackage package = BuildPackage();
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.Everybody,
                ConfigRpc,
                package);
        }

        internal static void Reset()
        {
            _registered = false;
            ServerEnforced = false;
            ServerKeywords.Clear();
        }

        private static void OnConfigRequested(
            long sender)
        {
            if (ZNet.instance == null ||
                !ZNet.instance.IsServer() ||
                ZRoutedRpc.instance == null)
                return;

            ZRoutedRpc.instance.InvokeRoutedRPC(
                sender,
                ConfigRpc,
                BuildPackage());
        }

        private static ZPackage BuildPackage()
        {
            var package = new ZPackage();
            var routes =
                new List<RouteDefinition>(
                    RouteRegistry.All);
            var sharedGroups =
                new List<SharedRouteGroup>(
                    SharedRouteRegistry.All);

            package.Write(
                routes.Count + sharedGroups.Count);

            for (int i = 0; i < routes.Count; i++)
            {
                RouteDefinition route = routes[i];
                WriteKeywordSet(
                    package,
                    route.Id,
                    route.KeywordEntries);
            }

            for (int i = 0; i < sharedGroups.Count; i++)
            {
                SharedRouteGroup group =
                    sharedGroups[i];

                WriteKeywordSet(
                    package,
                    group.SyncId,
                    group.KeywordEntries);
            }

            return package;
        }

        private static void WriteKeywordSet(
            ZPackage package,
            string id,
            BepInEx.Configuration.ConfigEntry<string>[] entries)
        {
            package.Write(id ?? "");

            for (int slot = 0; slot < 3; slot++)
            {
                string value = "";
                if (entries != null &&
                    slot < entries.Length &&
                    entries[slot] != null)
                    value = entries[slot].Value ?? "";

                package.Write(value);
            }
        }

        private static void OnConfigReceived(
            long sender,
            ZPackage package)
        {
            if (ZNet.instance == null ||
                ZNet.instance.IsServer() ||
                package == null)
                return;

            ServerKeywords.Clear();

            int count = package.ReadInt();
            for (int i = 0; i < count; i++)
            {
                string id = package.ReadString();
                var keywords = new string[3];

                for (int slot = 0; slot < keywords.Length; slot++)
                    keywords[slot] =
                        package.ReadString() ?? "";

                if (!string.IsNullOrEmpty(id))
                    ServerKeywords[id] = keywords;
            }

            ServerEnforced = true;
            ContainerRegistry.ReindexAll();

            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo(
                    "Using " +
                    ServerKeywords.Count +
                    " station/tool chest keyword(s) from the server.");
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), "Awake")]
    internal static class ZNetAwakeKeywordSyncPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            KeywordSync.Reset();
            KeywordSync.Register();
        }
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class ZNetPeerInfoKeywordSyncPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            KeywordSync.Register();
            KeywordSync.RequestFromServer();
        }
    }
}
