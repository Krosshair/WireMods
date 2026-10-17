using BepInEx.Configuration;

namespace WiresWeaponMaster
{
    internal static class WeaponMasterConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> BackShield;
        internal static ConfigEntry<bool> BackTool;
        internal static ConfigEntry<bool> BackMelee;
        internal static ConfigEntry<bool> BackTwohandedMelee;
        internal static ConfigEntry<bool> BackAtgeir;
        internal static ConfigEntry<bool> BackBow;

        internal static void Bind(ConfigFile file)
        {
            Enabled = file.Bind(
                "General",
                "Enabled",
                true,
                "Enable Wire's Weapon Master.");

            BackShield = BindCategory(
                file,
                "backShield");
            BackTool = BindCategory(
                file,
                "backTool");
            BackMelee = BindCategory(
                file,
                "backMelee");
            BackTwohandedMelee = BindCategory(
                file,
                "backTwohandedMelee");
            BackAtgeir = BindCategory(
                file,
                "backAtgeir");
            BackBow = BindCategory(
                file,
                "backBow");

            Enabled.SettingChanged +=
                delegate { OnChanged(); };
        }

        private static ConfigEntry<bool> BindCategory(
            ConfigFile file,
            string name)
        {
            ConfigEntry<bool> entry =
                file.Bind(
                    "Permanent display categories",
                    name,
                    true,
                    "Keep this vanilla holster category permanently visible when Weapon Master has a recent item for it. Disabling this does not affect normal Valheim stowing; actually stowed items can still appear.");

            entry.SettingChanged +=
                delegate { OnChanged(); };

            return entry;
        }
        private static void OnChanged()
        {
            WeaponMasterConfigSync.NotifyLocalChanged();

            if (!IsEnabled)
                HoldRController.ResetTracking();

            WeaponVisuals.RefreshConfig();
        }

        internal static bool IsEnabled
        {
            get
            {
                return WeaponMasterConfigSync.GetEnabled(
                    Enabled == null || Enabled.Value);
            }
        }

        internal static bool IsLaneEnabled(
            HolsterLane lane)
        {
            if (!IsEnabled)
                return false;

            bool local;

            switch (lane)
            {
                case HolsterLane.Shield:
                    local = BackShield == null || BackShield.Value;
                    break;
                case HolsterLane.Tool:
                    local = BackTool == null || BackTool.Value;
                    break;
                case HolsterLane.Melee:
                    local = BackMelee == null || BackMelee.Value;
                    break;
                case HolsterLane.TwoHandedMelee:
                    local =
                        BackTwohandedMelee == null ||
                        BackTwohandedMelee.Value;
                    break;
                case HolsterLane.Atgeir:
                    local = BackAtgeir == null || BackAtgeir.Value;
                    break;
                case HolsterLane.Bow:
                    local = BackBow == null || BackBow.Value;
                    break;
                default:
                    return false;
            }

            return WeaponMasterConfigSync.GetLaneEnabled(
                lane,
                local);
        }

        internal static bool GetLocalLane(
            HolsterLane lane)
        {
            switch (lane)
            {
                case HolsterLane.Shield:
                    return BackShield == null || BackShield.Value;
                case HolsterLane.Tool:
                    return BackTool == null || BackTool.Value;
                case HolsterLane.Melee:
                    return BackMelee == null || BackMelee.Value;
                case HolsterLane.TwoHandedMelee:
                    return BackTwohandedMelee == null ||
                           BackTwohandedMelee.Value;
                case HolsterLane.Atgeir:
                    return BackAtgeir == null || BackAtgeir.Value;
                case HolsterLane.Bow:
                    return BackBow == null || BackBow.Value;
                default:
                    return false;
            }
        }
    }
}
