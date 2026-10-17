namespace WiresAldr
{
    internal static class GlobalMaterialProgress
    {
        internal static GlobalYieldSettings Settings()
        {
            return new GlobalYieldSettings
            {
                EikthyrWood = Plugin.WoodEikthyr.Value,
                ElderWood = Plugin.WoodElder.Value,
                LaterWood = Plugin.WoodLater.Value,
                ElderStone = Plugin.StoneBonus.Value,
                LaterStone = Plugin.StoneLater.Value,
                ElderCoreWood = Plugin.CoreWoodElder.Value,
                ElderFineWood = Plugin.FineWoodElder.Value,
                YagluthFineWood = Plugin.FineWoodYagluth.Value,
                FaderAshwood = Plugin.AshwoodFader.Value
            };
        }

        internal static double Bonus(GlobalResource resource, bool[] defeated)
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value ||
                !Plugin.Materials.Value)
                return 0;
            return GlobalYieldRules.Total(
                GlobalYieldRules.Parts(resource, defeated, Settings()));
        }
    }
}
