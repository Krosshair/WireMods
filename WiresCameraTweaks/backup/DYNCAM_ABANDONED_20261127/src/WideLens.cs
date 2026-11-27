namespace WiresShakeItUp
{
    // Opt-in wide lens: a wider field of view with barrel distortion, which
    // magnifies the centre a little and keeps the edges where they were.
    // The FOV is applied by DynamicCamera together with its own changes.
    internal static class WideLens
    {
        internal static bool On => ModConfig.WideLensEnabled.Value && LensEffect.Ready;
    }
}
