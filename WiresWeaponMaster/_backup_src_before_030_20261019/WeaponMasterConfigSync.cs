using System;
using HarmonyLib;

namespace WiresWeaponMaster
{
    internal static class WeaponMasterConfigSync
    {
        private const string RequestRpc =
            "WWM_ConfigRequest";

        private const string ConfigRpc =
            "WWM_Config";

        private static bool _registered;
        private static bool _serverEnforced;

        private static bool _enabled = true;
        private static readonly bool[] Lanes =
            { false, true, true, true, true, true, true };

        internal static bool GetEnabled(bool local)
        {
            return _serverEnforced ? _enabled : local;
        }

        internal static bool GetLaneEnabled(
            HolsterLane lane,
            bool local)
        {
            int index = (int)lane;
            if (!_serverEnforced ||
                index <= 0 ||
                index >= Lanes.Length)
                return local;

            return Lanes[index];
        }
        internal static void Register()
        {
            if (_registered ||
                ZRoutedRpc.instance == null)
                return;

            ZRoutedRpc.instance.Register(
                RequestRpc,
                new Action<long>(
                    OnRequested));

            ZRoutedRpc.instance.Register(
                ConfigRpc,
                new Action<long, ZPackage>(
                    OnReceived));

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

        internal static void NotifyLocalChanged()
        {
            if (ZNet.instance == null ||
                !ZNet.instance.IsServer() ||
                ZRoutedRpc.instance == null)
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.Everybody,
                ConfigRpc,
                BuildPackage());
        }

        private static void OnRequested(long sender)
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

            package.Write(
                WeaponMasterConfig.Enabled == null ||
                WeaponMasterConfig.Enabled.Value);

            for (int lane = 1; lane < 7; lane++)
            {
                package.Write(
                    WeaponMasterConfig.GetLocalLane(
                        (HolsterLane)lane));
            }

            return package;
        }
        private static void OnReceived(
            long sender,
            ZPackage package)
        {
            if (ZNet.instance == null ||
                ZNet.instance.IsServer() ||
                package == null)
                return;

            _enabled = package.ReadBool();

            for (int lane = 1; lane < 7; lane++)
                Lanes[lane] = package.ReadBool();

            _serverEnforced = true;

            if (!_enabled)
                HoldRController.ResetTracking();

            WeaponVisuals.RefreshConfig();

            Plugin.LogInfo(
                "Using Weapon Master settings from the server.");
        }

        internal static void Reset()
        {
            _registered = false;
            _serverEnforced = false;
            _enabled = true;

            for (int lane = 1; lane < 7; lane++)
                Lanes[lane] = true;
        }
    }

    [HarmonyPatch(typeof(ZNet), "Awake")]
    internal static class WeaponMasterConfigSyncAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            WeaponMasterConfigSync.Reset();
            WeaponMasterConfigSync.Register();
        }
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class WeaponMasterConfigSyncPeerPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            WeaponMasterConfigSync.Register();
            WeaponMasterConfigSync.RequestFromServer();
        }
    }

    [HarmonyPatch(typeof(ZNet), "Shutdown")]
    internal static class WeaponMasterConfigSyncShutdownPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            WeaponMasterConfigSync.Reset();
        }
    }
}
