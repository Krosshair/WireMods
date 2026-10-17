using BepInEx.Configuration;

namespace WiresShakeItUp
{
    internal static class ModConfig
    {
        internal static ConfigEntry<bool> ShakeEnabled;
        internal static ConfigEntry<float> OverallStrength;
        internal static ConfigEntry<float> RumbleStrength;
        internal static ConfigEntry<float> ImpactStrength;
        internal static ConfigEntry<float> ImpactDuration;
        internal static ConfigEntry<float> LongShakeSoftening;
        internal static ConfigEntry<float> MaxRumbleDegrees;
        internal static ConfigEntry<float> FrequencyMultiplier;
        internal static ConfigEntry<float> SecondaryMotion;
        internal static ConfigEntry<float> PositionShake;
        internal static ConfigEntry<float> RollStrength;
        internal static ConfigEntry<bool> DirectionalImpact;
        internal static ConfigEntry<float> ImpactThreshold;

        internal static ConfigEntry<bool> SmoothZoomEnabled;
        internal static ConfigEntry<float> ZoomResponse;
        internal static ConfigEntry<bool> CollisionAutoZoomEnabled;
        internal static ConfigEntry<float> CollisionAutoZoomResponse;

        internal static void Bind(ConfigFile file)
        {
            // Keep the original Shake It Up values when moving into Camera Tweaks.
            ConfigEntry<bool> oldEnabled = file.Bind(
                "General", "Enabled", true, "Legacy migration value.");
            ConfigEntry<float> oldOverall = file.Bind(
                "General", "Overall Strength", 0.5f, "Legacy migration value.");
            ConfigEntry<float> oldRumble = file.Bind(
                "Rumble", "Rumble Strength", 0.85f, "Legacy migration value.");
            ConfigEntry<float> oldLong = file.Bind(
                "Rumble", "Long Shake Softening", 0.8f, "Legacy migration value.");
            ConfigEntry<float> oldMax = file.Bind(
                "Rumble", "Max Rumble Degrees", 1.25f, "Legacy migration value.");
            ConfigEntry<float> oldFreq = file.Bind(
                "Rumble", "Frequency Multiplier", 0.9f, "Legacy migration value.");
            ConfigEntry<float> oldSecondary = file.Bind(
                "Rumble", "Secondary Motion", 0.18f, "Legacy migration value.");
            ConfigEntry<float> oldPosition = file.Bind(
                "Rumble", "Position Shake", 0.018f, "Legacy migration value.");
            ConfigEntry<float> oldImpact = file.Bind(
                "Impact", "Impact Strength", 1f, "Legacy migration value.");
            ConfigEntry<float> oldImpactDuration = file.Bind(
                "Impact", "Impact Duration", 0.18f, "Legacy migration value.");
            ConfigEntry<float> oldImpactThreshold = file.Bind(
                "Impact", "Impact Threshold", 0.12f, "Legacy migration value.");
            ConfigEntry<bool> oldDirectional = file.Bind(
                "Impact", "Directional Impact", true, "Legacy migration value.");
            ConfigEntry<float> oldRoll = file.Bind(
                "Impact", "Roll Strength", 0f, "Legacy migration value.");

            bool migratedEnabled = oldEnabled.Value;
            float migratedOverall = oldOverall.Value;
            float migratedRumble = oldRumble.Value;
            float migratedLong = oldLong.Value;
            float migratedMax = oldMax.Value;
            float migratedFreq = oldFreq.Value;
            float migratedSecondary = oldSecondary.Value;
            float migratedPosition = oldPosition.Value;
            float migratedImpact = oldImpact.Value;
            float migratedImpactDuration = oldImpactDuration.Value;
            float migratedImpactThreshold = oldImpactThreshold.Value;
            bool migratedDirectional = oldDirectional.Value;
            float migratedRoll = oldRoll.Value;

            file.Remove(oldEnabled.Definition);
            file.Remove(oldOverall.Definition);
            file.Remove(oldRumble.Definition);
            file.Remove(oldLong.Definition);
            file.Remove(oldMax.Definition);
            file.Remove(oldFreq.Definition);
            file.Remove(oldSecondary.Definition);
            file.Remove(oldPosition.Definition);
            file.Remove(oldImpact.Definition);
            file.Remove(oldImpactDuration.Definition);
            file.Remove(oldImpactThreshold.Definition);
            file.Remove(oldDirectional.Definition);
            file.Remove(oldRoll.Definition);

            // Remove every abandoned Camera Overhaul experiment.
            Remove(file, "2 - Collision", "Enabled");
            Remove(file, "2 - Collision", "Recovery Response");
            Remove(file, "2 - Collision", "Release Delay");

            Remove(file, "3 - Occlusion", "Enabled");
            Remove(file, "3 - Occlusion", "Fade Occluders");
            Remove(file, "3 - Occlusion", "Occluder Alpha");
            Remove(file, "3 - Occlusion", "Dither Occluders");
            Remove(file, "3 - Occlusion", "Dither Visibility");
            Remove(file, "3 - Occlusion", "Fade Response");
            Remove(file, "3 - Occlusion", "Ignore Small Obstacles");
            Remove(file, "3 - Occlusion", "Small Obstacle Max Volume");
            Remove(file, "3 - Occlusion", "Thin Obstacle Max Thickness");

            Remove(file, "3 - Player Follow", "Enabled");
            Remove(file, "3 - Player Follow", "Maximum Lag Distance");
            Remove(file, "3 - Player Follow", "Response");
            Remove(file, "3 - Player Follow", "Maximum Horizontal Lag");
            Remove(file, "3 - Player Follow", "Vertical Response");
            Remove(file, "3 - Player Follow", "Maximum Vertical Lag");
            Remove(file, "3 - Player Follow", "Horizontal Catch-up Response");
            Remove(file, "3 - Player Follow", "Horizontal Deadzone");
            Remove(file, "3 - Player Follow", "Maximum Horizontal Lead");
            Remove(file, "3 - Player Follow", "Vertical Catch-up Response");
            Remove(file, "3 - Player Follow", "Vertical Deadzone");
            Remove(file, "3 - Player Follow", "Maximum Vertical Lead");

            Remove(file, "4 - Zoom", "Enabled");
            Remove(file, "4 - Zoom", "Response");

            Remove(file, "5 - Motion", "Enabled");
            Remove(file, "5 - Motion", "Strafe Roll");
            Remove(file, "5 - Motion", "Turn Roll");
            Remove(file, "5 - Motion", "Response");
            Remove(file, "5 - Motion", "Roll Response");
            Remove(file, "5 - Motion", "Camera Lag Distance");
            Remove(file, "5 - Motion", "Camera Lag Response");

            ShakeEnabled = file.Bind(
                "1 - Shake", "Enabled", migratedEnabled,
                "Enable the Wire's Shake It Up camera-shake rework.");
            OverallStrength = Float(file, "1 - Shake", "Overall Strength",
                migratedOverall, 0f, 2f, "Master multiplier for camera shake.");
            RumbleStrength = Float(file, "1 - Shake", "Rumble Strength",
                migratedRumble, 0f, 2f, "Strength of sustained shake after the initial impact.");
            LongShakeSoftening = Float(file, "1 - Shake", "Long Shake Softening",
                migratedLong, 0f, 1f, "Prevents long vanilla shakes from becoming absurdly strong.");
            MaxRumbleDegrees = Float(file, "1 - Shake", "Max Rumble Degrees",
                migratedMax, 0.1f, 4f, "Approximate cap for sustained rotational shake.");
            FrequencyMultiplier = Float(file, "1 - Shake", "Frequency Multiplier",
                migratedFreq, 0.25f, 2f, "Speed of sustained shake.");
            SecondaryMotion = Float(file, "1 - Shake", "Secondary Motion",
                migratedSecondary, 0f, 1f, "Subtle second motion layer for less mechanical shake.");
            PositionShake = Float(file, "1 - Shake", "Position Shake",
                migratedPosition, 0f, 0.08f, "Small positional shake in metres.");
            ImpactStrength = Float(file, "1 - Shake", "Impact Strength",
                migratedImpact, 0f, 2f, "Strength of the short initial impact punch.");
            ImpactDuration = Float(file, "1 - Shake", "Impact Duration",
                migratedImpactDuration, 0.05f, 0.5f, "Length of the initial impact kick.");
            ImpactThreshold = Float(file, "1 - Shake", "Impact Threshold",
                migratedImpactThreshold, 0f, 1f, "Tiny shakes below this strength do not receive an extra impact.");
            DirectionalImpact = file.Bind(
                "1 - Shake", "Directional Impact", migratedDirectional,
                "Bias the initial kick away from the shake source.");
            RollStrength = Float(file, "1 - Shake", "Roll Strength",
                migratedRoll, 0f, 1f, "Roll added to impacts. Zero avoids nausea.");

            SmoothZoomEnabled = file.Bind(
                "2 - Smooth Zoom", "Enabled", true,
                "Smooth manual mouse-wheel/controller zoom.");
            ZoomResponse = Float(file, "2 - Smooth Zoom", "Manual Zoom Response",
                18f, 5f, 40f,
                "How quickly manual zoom catches the requested target.");

            CollisionAutoZoomEnabled = file.Bind(
                "2 - Smooth Zoom", "Smooth Collision Auto Zoom", true,
                "Smooth the automatic zoom-out recovery after Valheim collision forces the camera closer. Collision moving inward remains immediate.");
            CollisionAutoZoomResponse = Float(
                file, "2 - Smooth Zoom", "Automatic Zoom Response",
                14f, 4f, 35f,
                "How quickly collision-forced camera distance eases back outward.");

            Hook(ShakeEnabled);
            Hook(OverallStrength);
            Hook(RumbleStrength);
            Hook(ImpactStrength);
            Hook(ImpactDuration);
            Hook(LongShakeSoftening);
            Hook(MaxRumbleDegrees);
            Hook(FrequencyMultiplier);
            Hook(SecondaryMotion);
            Hook(PositionShake);
            Hook(RollStrength);
            Hook(DirectionalImpact);
            Hook(ImpactThreshold);
            Hook(SmoothZoomEnabled);
            Hook(CollisionAutoZoomEnabled);
        }

        private static ConfigEntry<float> Float(
            ConfigFile file,
            string section,
            string key,
            float value,
            float min,
            float max,
            string description)
        {
            return file.Bind(
                section,
                key,
                value,
                new ConfigDescription(
                    description,
                    new AcceptableValueRange<float>(
                        min,
                        max)));
        }

        private static void Remove(
            ConfigFile file,
            string section,
            string key)
        {
            file.Remove(
                new ConfigDefinition(
                    section,
                    key));
        }

        private static void Hook<T>(
            ConfigEntry<T> entry)
        {
            entry.SettingChanged += delegate
            {
                ShakeEngine.ResetTransient();
                ZoomEngine.Reset();
                CollisionAutoZoomEngine.Reset();
            };
        }
    }
}
