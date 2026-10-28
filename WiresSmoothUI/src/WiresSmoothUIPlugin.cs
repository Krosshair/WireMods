using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace WiresSmoothUI
{
    internal enum MapRevealScope
    {
        MapOnly,
        FullHud
    }

    internal enum HeldCrosshairMode
    {
        HammerRanged,
        Everything,
        None
    }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("shudnal.MyLittleUI", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Azumatt.AzuExtendedPlayerInventory", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("randyknapp.mods.equipmentandquickslots", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("shudnal.ExtraSlots", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("kajetan.valheim.dynamicreticle", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class WiresSmoothUIPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "wire.valheim.smoothui";
        public const string PluginName = "Wire's Smooth UI";
        public const string PluginVersion = "0.6.5";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> HudEnabled;
        internal static ConfigEntry<bool> HotbarCycle;
        internal static ConfigEntry<bool> DynamicAzuQuickbar;
        internal static ConfigEntry<bool> FoodReplaceable;
        internal static ConfigEntry<bool> ForsakenOnUse;
        internal static ConfigEntry<bool> ResourceFading;
        internal static ConfigEntry<bool> ResourceActivator;
        internal static ConfigEntry<string> HealthThresholdChoice;
        internal static ConfigEntry<ActivatorBehaviorMode> ActivatorBehavior;
        internal static ConfigEntry<float> ReticleFadeIn;
        internal static ConfigEntry<float> ResourceFadeSeconds;
        internal static ConfigEntry<float> HotbarFadeSeconds;
        internal static ConfigEntry<float> HealthFadeSeconds;
        internal static ConfigEntry<float> FoodFadeSeconds;
        internal static ConfigEntry<float> ForsakenFadeSeconds;
        internal static ConfigEntry<float> MinimapMoveSeconds;
        internal static ConfigEntry<float> StatusFadeSeconds;
        internal static ConfigEntry<float> StatusActivatorFadeSeconds;
        internal static ConfigEntry<float> MluiFadeSeconds;

        internal static ConfigEntry<bool> ItemAnimations;
        internal static ConfigEntry<bool> EquipProgressFill;
        internal static ConfigEntry<bool> PanelAnimations;
        internal static ConfigEntry<bool> CraftingAnimations;
        internal static ConfigEntry<bool> HideEmptyMaterialSlots;
        internal static ConfigEntry<bool> SmoothScrolling;
        internal static ConfigEntry<float> ScrollGlide;
        internal static ConfigEntry<bool> SlotAnimations;
        internal static ConfigEntry<float> DropTargetScale;
        internal static ConfigEntry<float> GamepadSelectionScale;
        internal static ConfigEntry<float> HoverScale;
        internal static ConfigEntry<float> HoverResponse;
        internal static ConfigEntry<bool> SlotSway;
        internal static ConfigEntry<float> SwayPixels;
        internal static ConfigEntry<float> SwayDegrees;
        internal static ConfigEntry<float> ItemMoveDuration;
        internal static ConfigEntry<float> ItemAppearDuration;
        internal static ConfigEntry<float> StackPulseScale;
        internal static ConfigEntry<bool> DragPhysics;
        internal static ConfigEntry<float> DragFollow;
        internal static ConfigEntry<float> DragRotation;

        internal static ConfigEntry<bool> ButtonAnimations;
        internal static ConfigEntry<float> ButtonHoverScale;
        internal static ConfigEntry<float> ButtonResponse;

        internal static ConfigEntry<bool> TooltipAnimations;
        internal static ConfigEntry<float> TooltipDuration;
        internal static ConfigEntry<float> TooltipStartScale;

        internal static ConfigEntry<bool> CrosshairAnimations;
        internal static ConfigEntry<bool> CrosshairActivator;
        internal static ConfigEntry<bool> ShowHoverCrosshair;
        internal static ConfigEntry<bool> CrosshairWithTools;
        internal static ConfigEntry<bool> CrosshairWithRanged;
        internal static ConfigEntry<bool> CrosshairWithMelee;
        internal static ConfigEntry<bool> CrosshairYellowHover;
        internal static ConfigEntry<bool> DisableStealthIndicator;
        internal static ConfigEntry<float> CrosshairFade;
        internal static ConfigEntry<bool> ProjectileReticle;
        internal static ConfigEntry<DynamicReticleSystem.EasingMode> ReticleEasing;

        internal static ConfigEntry<bool> BuildMenuAnimations;
        internal static ConfigEntry<float> BuildMenuHoverScale;
        internal static ConfigEntry<float> BuildMenuResponse;
        internal static ConfigEntry<bool> BuildMenuInertia;

        internal static ConfigEntry<bool> MapPinAnimations;
        internal static ConfigEntry<float> MapPinPopScale;
        internal static ConfigEntry<float> MapPinPopDuration;

        internal static ConfigEntry<bool> DynamicHotbar;
        internal static ConfigEntry<bool> HotbarActivator;
        internal static ConfigEntry<bool> HotbarCombat;
        internal static ConfigEntry<bool> HotbarEquipActions;
        internal static ConfigEntry<bool> HotbarHeldItem;

        internal static ConfigEntry<bool> DynamicHealth;
        internal static ConfigEntry<bool> HealthActivator;
        internal static ConfigEntry<bool> HealthCombat;
        internal static ConfigEntry<bool> HealthInventory;
        internal static ConfigEntry<bool> HealthDamaged;

        internal static ConfigEntry<bool> DynamicFood;
        internal static ConfigEntry<bool> FoodActivator;
        internal static ConfigEntry<bool> FoodCombat;
        internal static ConfigEntry<bool> FoodInventory;
        internal static ConfigEntry<bool> FoodStateChanges;
        internal static ConfigEntry<bool> FoodFinalMinute;

        internal static ConfigEntry<bool> DynamicStamina;
        internal static ConfigEntry<bool> StaminaActivator;
        internal static ConfigEntry<bool> StaminaUseState;

        internal static ConfigEntry<bool> DynamicEitr;
        internal static ConfigEntry<bool> EitrActivator;
        internal static ConfigEntry<bool> EitrUseState;

        internal static ConfigEntry<bool> DynamicAdrenaline;
        internal static ConfigEntry<bool> AdrenalineActivator;
        internal static ConfigEntry<bool> AdrenalineUseState;

        internal static ConfigEntry<bool> DynamicForsakenPower;
        internal static ConfigEntry<bool> ForsakenActivator;
        internal static ConfigEntry<bool> ForsakenCombat;
        internal static ConfigEntry<bool> ForsakenInventory;
        internal static ConfigEntry<bool> ForsakenCooldownEnding;

        internal static ConfigEntry<bool> DynamicMluiWeight;
        internal static ConfigEntry<bool> MluiWeightActivator;
        internal static ConfigEntry<bool> MluiWeightInventory;

        internal static ConfigEntry<bool> DynamicMluiSlots;
        internal static ConfigEntry<bool> MluiSlotsActivator;
        internal static ConfigEntry<bool> MluiSlotsInventory;

        internal static ConfigEntry<bool> DynamicMinimap;
        internal static ConfigEntry<bool> MinimapActivator;
        internal static ConfigEntry<bool> MinimapShip;

        internal static ConfigEntry<bool> DynamicMapInfo;
        internal static ConfigEntry<bool> MapInfoActivator;

        internal static ConfigEntry<bool> DynamicStatusEffects;
        internal static ConfigEntry<bool> StatusEffectsActivator;
        internal static ConfigEntry<bool> StatusEffectsNew;
        internal static ConfigEntry<bool> StatusEffectsEnding;
        internal static ConfigEntry<bool> HidePermanentStatusEffects;

        internal static ConfigEntry<KeyboardShortcut> MapInfoHotkey;
        internal static ConfigEntry<KeyCode> MapInfoControllerHotkey;

        private Harmony _harmony;

        private void Awake()
        {
            BindConfig();
            SmoothUIConfigRework.Apply(Config);

            // Dynamic Reticles was merged into Smooth UI in 0.2.0. If the legacy
            // standalone plugin is still installed, BepInEx loads it first because of
            // the soft dependency above; remove its Harmony patches before installing
            // the integrated fixed implementation so users never get double reticles.
            if (Chainloader.PluginInfos.ContainsKey("kajetan.valheim.dynamicreticle"))
            {
                Harmony.UnpatchID("kajetan.valheim.dynamicreticle");
                Logger.LogInfo("Legacy Wire's Dynamic Reticles detected; its patches were disabled because the feature is now built into Smooth UI.");
            }

            Enabled.SettingChanged += (_, __) => SmoothUIDriver.Wake();
            Enabled.SettingChanged += (_, __) => EmptyMaterialSlots.Refresh();
            HideEmptyMaterialSlots.SettingChanged += (_, __) => EmptyMaterialSlots.Refresh();
            SmoothScrolling.SettingChanged += (_, __) => { if (!SmoothScrolling.Value) SmoothScrollSystem.Stop(); };
            // Buttons can rest at their hover size without ticking; put them
            // back when the setting is turned off.
            ButtonAnimations.SettingChanged += (_, __) =>
            {
                if (!ButtonAnimations.Value)
                    ButtonAnimationSystem.RestoreAll();
            };
            SmoothUIDriver.Wake();

            _harmony = new Harmony(PluginGuid);
            // Patch class by class: if a game update breaks one patch, the rest
            // still load instead of the whole mod going dark.
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(WiresSmoothUIPlugin).Assembly))
            {
                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    Logger.LogError($"Couldn't apply {type.Name}: {e.Message}");
                }
            }
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            VisualSystems.RestoreEverything();
            ItemAnimationSystem.Shutdown();
            SlotAnimationSystem.Shutdown();
            ButtonAnimationSystem.Shutdown();
            BuildMenuAnimationSystem.Shutdown();
            MapPinAnimationSystem.Shutdown();
            DynamicReticleSystem.Shutdown();
            EquipFillSystem.RestoreAll();
            UiMotion.RestoreAll();
            HudVisibilityReworkSystem.Shutdown();
            MapStatusVisibilitySystem.Shutdown();
            SmoothScrollSystem.Shutdown();
            _harmony?.UnpatchSelf();
        }

        private void BindConfig()
        {
            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch for all Wire's Smooth UI visuals.");

            SlotAnimations = Config.Bind("Inventory Slots", "Enabled", true,
                "Animate the whole slot and everything inside it on mouse hover, press, valid drag targets, and gamepad selection.");
            HoverScale = BindFloat("Inventory Slots", "Hover scale", 1.08f, 1f, 1.20f,
                "How large a hovered inventory slot becomes.");
            HoverResponse = BindFloat("Inventory Slots", "Response", 18f, 5f, 40f,
                "How quickly slot motion catches up. Higher values feel snappier.");
            SlotSway = Config.Bind("Inventory Slots", "Mouse inertia", true,
                "Let hovered slots lean and sway slightly with mouse movement.");
            SwayPixels = BindFloat("Inventory Slots", "Inertia distance", 2.25f, 0f, 8f,
                "Maximum hovered-slot movement caused by mouse inertia, in UI pixels.");
            SwayDegrees = BindFloat("Inventory Slots", "Inertia rotation", 1.8f, 0f, 8f,
                "Maximum hovered-slot rotation caused by mouse inertia, in degrees.");
            DropTargetScale = BindFloat("Inventory Slots", "Valid drop target scale", 1.11f, 1f, 1.25f,
                "How large a valid destination slot becomes while dragging an item over it.");
            GamepadSelectionScale = BindFloat("Inventory Slots", "Gamepad selection scale", 1.06f, 1f, 1.15f,
                "How large the currently selected inventory slot becomes when using a gamepad.");

            ItemAnimations = Config.Bind("Item Feedback", "Enabled", true,
                "Animate item appearance, moves, landing, stack changes, equip state, and queued actions.");
            ItemMoveDuration = BindFloat("Item Feedback", "Move and landing duration", 0.14f, 0.05f, 0.40f,
                "How long moved or swapped items glide into the destination and settle.");
            ItemAppearDuration = BindFloat("Item Feedback", "Appear and grab duration", 0.16f, 0.05f, 0.40f,
                "How long new items and newly grabbed drag icons take to pop into place.");
            StackPulseScale = BindFloat("Item Feedback", "Stack change pulse", 1.08f, 1f, 1.20f,
                "Peak item scale when a stack amount changes. The number itself receives a slightly stronger pop.");

            DragPhysics = Config.Bind("Dragging", "Physics sway", true,
                "Give dragged items spring-like lag, sway, and rotation when the cursor accelerates or changes direction.");
            DragFollow = BindFloat("Dragging", "Follow speed", 26f, 8f, 50f,
                "How quickly the dragged item follows the cursor. Lower values create more trailing inertia.");
            DragRotation = BindFloat("Dragging", "Physics rotation", 16f, 0f, 16f,
                "Maximum spring-like rotation of a dragged item when the cursor changes direction.");

            ButtonAnimations = Config.Bind("Buttons", "Enabled", true,
                "Give ordinary Valheim UI buttons a subtle hover and press response. Inventory slots use their own animation system.");
            ButtonHoverScale = BindFloat("Buttons", "Hover scale", 1.025f, 1f, 1.10f,
                "Relative scale of hovered non-inventory buttons.");
            ButtonResponse = BindFloat("Buttons", "Response", 20f, 5f, 40f,
                "How quickly button hover and press motion responds.");

            TooltipAnimations = Config.Bind("Tooltips", "Enabled", true,
                "Softly fade and scale tooltips in when they appear.");
            TooltipDuration = BindFloat("Tooltips", "Entrance duration", 0.12f, 0.05f, 0.40f,
                "Tooltip entrance duration in seconds.");
            TooltipStartScale = BindFloat("Tooltips", "Opening scale", 0.96f, 0.80f, 1f,
                "Tooltip scale at the start of its entrance.");

            bool oldCrosshairDynamic =
                TakeLegacyBool(
                    "Crosshair & Reticles",
                    "Enabled",
                    true);

            bool oldCrosshairHover =
                TakeLegacyBool(
                    "Crosshair & Reticles",
                    "Show on hover",
                    true);

            ConfigEntry<HeldCrosshairMode> oldHeldCrosshair =
                Config.Bind(
                    "Crosshair & Reticles",
                    "Held item crosshair",
                    HeldCrosshairMode.HammerRanged,
                    "Legacy Smooth UI setting.");

            HeldCrosshairMode oldHeldMode =
                oldHeldCrosshair.Value;

            Config.Remove(
                oldHeldCrosshair.Definition);

            CrosshairAnimations = Config.Bind(
                "Dynamic - Crosshair",
                "Dynamic",
                oldCrosshairDynamic,
                "Dynamically show and hide the normal crosshair.");

            CrosshairActivator = Config.Bind(
                "Dynamic - Crosshair",
                "Activator key",
                false,
                "Show the normal crosshair while the Activator key is held.");

            ShowHoverCrosshair = Config.Bind(
                "Dynamic - Crosshair",
                "Appear on hover",
                oldCrosshairHover,
                "Show the normal crosshair for an in-range hover target or interactable.");

            CrosshairWithTools = Config.Bind(
                "Dynamic - Crosshair",
                "Appear with tools",
                oldHeldMode != HeldCrosshairMode.None,
                "Show the normal crosshair while holding a tool such as the Hammer.");

            CrosshairWithRanged = Config.Bind(
                "Dynamic - Crosshair",
                "Appear with ranged weapons",
                oldHeldMode != HeldCrosshairMode.None,
                "Show the normal crosshair while holding a ranged or projectile weapon.");

            CrosshairWithMelee = Config.Bind(
                "Dynamic - Crosshair",
                "Appear with melee weapons",
                oldHeldMode == HeldCrosshairMode.Everything,
                "Show the normal crosshair while holding a melee weapon.");

            CrosshairYellowHover = Config.Bind(
                "Dynamic - Crosshair",
                "Yellow hover highlight",
                false,
                "Use Valheim's yellow, full-opacity crosshair highlight on interactable hover.");

            DisableStealthIndicator = Config.Bind(
                "Crosshair & Reticles",
                "Hide stealth indicator",
                true,
                "Hide Valheim's crouch/stealth eye indicator.");

            CrosshairFade = BindFloat(
                "Crosshair & Reticles",
                "Crosshair fade seconds",
                0.00f,
                0.00f,
                1.00f,
                "Fade time for the normal contextual crosshair.");

            ProjectileReticle = Config.Bind(
                "Crosshair & Reticles",
                "Projectile reticle",
                true,
                "Animate Valheim's bow-circle language for bows, crossbows, thrown spears, and projectile magic.");

            ReticleEasing = Config.Bind(
                "Crosshair & Reticles",
                "Reticle easing",
                DynamicReticleSystem.EasingMode.SineInOut,
                "Scale/color easing for the projectile reticle.");
            BuildMenuAnimations = Config.Bind("Build & Dish Menus", "Enabled", true,
                "Give Valheim's build-piece, feast/dish, food and mead selection tiles Smooth UI motion. These menus share the same BuildUi system.");
            BuildMenuHoverScale = BindFloat("Build & Dish Menus", "Hover scale", 1.08f, 1f, 1.20f,
                "How large a hovered build/dish selection tile becomes.");
            BuildMenuResponse = BindFloat("Build & Dish Menus", "Response", 18f, 5f, 40f,
                "How quickly build/dish tile motion responds.");
            BuildMenuInertia = Config.Bind("Build & Dish Menus", "Mouse inertia", true,
                "Let hovered build/dish selection tiles lean slightly with mouse movement.");

            MapPinAnimations = Config.Bind("Map Pins", "Enabled", true,
                "Animate newly placed map pins and other newly-created visible map markers.");
            MapPinPopScale = BindFloat("Map Pins", "Placement pop scale", 1.24f, 1f, 1.60f,
                "Peak scale reached by a newly placed map pin.");
            MapPinPopDuration = BindFloat("Map Pins", "Placement pop duration", 0.20f, 0.05f, 0.50f,
                "Duration of the map-pin placement pop.");

            bool oldHotbar =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Dynamic hotbar and Azu quickslots",
                    true);

            bool oldHealth =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Dynamic health",
                    true);

            bool oldStamina =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Fade stamina",
                    false);

            bool oldEitr =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Fade eitr",
                    false);

            bool oldAdrenaline =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Fade adrenaline",
                    false);

            bool oldFood =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Dynamic food",
                    true);

            bool oldForsaken =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Dynamic Forsaken power",
                    true);

            bool oldMluiWeight =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Dynamic MLUI weight",
                    true);

            bool oldMluiSlots =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Dynamic MLUI slots",
                    true);

            bool oldMap =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Dynamic minimap and map info",
                    true);

            bool oldHidePermanent =
                TakeLegacyBool(
                    "HUD Visibility",
                    "Hide permanent equipment effects",
                    true);

            ConfigEntry<MapRevealScope> oldScope =
                Config.Bind(
                    "HUD Visibility",
                    "Minimap reveal scope",
                    MapRevealScope.FullHud,
                    "Legacy Smooth UI setting.");

            Config.Remove(
                oldScope.Definition);

            ConfigEntry<KeyboardShortcut> oldActivatorKey =
                Config.Bind(
                    "HUD Visibility",
                    "Minimap reveal hotkey",
                    new KeyboardShortcut(
                        KeyCode.BackQuote),
                    "Legacy Smooth UI setting.");

            KeyboardShortcut activatorKeyDefault =
                oldActivatorKey.Value;

            Config.Remove(
                oldActivatorKey.Definition);

            ConfigEntry<KeyCode> oldActivatorController =
                Config.Bind(
                    "HUD Visibility",
                    "Minimap reveal controller button",
                    KeyCode.JoystickButton6,
                    "Legacy Smooth UI setting.");

            KeyCode activatorControllerDefault =
                oldActivatorController.Value;

            Config.Remove(
                oldActivatorController.Definition);

            MapInfoHotkey = Config.Bind(
                "Activator",
                "Activator key",
                activatorKeyDefault,
                "Hold this key to reveal any dynamic element whose Activator key option is enabled.");

            MapInfoControllerHotkey = Config.Bind(
                "Activator",
                "Activator controller button",
                activatorControllerDefault,
                "Controller equivalent of the Activator key.");

            DynamicHotbar = Config.Bind(
                "Dynamic - Hotbar",
                "Dynamic",
                oldHotbar,
                "Dynamically show and hide the vanilla hotbar and Azu quickbar.");

            HotbarActivator = Config.Bind(
                "Dynamic - Hotbar",
                "Activator key",
                true,
                "Show the hotbar while the Activator key is held.");

            HotbarCombat = Config.Bind(
                "Dynamic - Hotbar",
                "Appear in combat",
                true,
                "Show the hotbar while an enemy is targeting or sensing you.");

            HotbarEquipActions = Config.Bind(
                "Dynamic - Hotbar",
                "Appear while equipping or unequipping",
                true,
                "Show the hotbar while an item is actively being equipped or unequipped.");

            HotbarHeldItem = Config.Bind(
                "Dynamic - Hotbar",
                "Appear while holding an item",
                true,
                "Keep the hotbar visible while an item is held in either hand, even if it did not come from the hotbar.");

            DynamicHealth = Config.Bind(
                "Dynamic - Health",
                "Dynamic",
                oldHealth,
                "Dynamically show and hide health.");

            HealthActivator = Config.Bind(
                "Dynamic - Health",
                "Activator key",
                true,
                "Show health while the Activator key is held.");

            HealthCombat = Config.Bind(
                "Dynamic - Health",
                "Appear in combat",
                true,
                "Show health while an enemy is targeting or sensing you.");

            HealthInventory = Config.Bind(
                "Dynamic - Health",
                "Appear with inventory",
                true,
                "Show health while the inventory is open.");

            HealthDamaged = Config.Bind(
                "Dynamic - Health",
                "Appear below 95%",
                true,
                "Show health while current health is below 95% of maximum.");

            DynamicFood = Config.Bind(
                "Dynamic - Food",
                "Dynamic",
                oldFood,
                "Dynamically show and hide the food HUD.");

            FoodActivator = Config.Bind(
                "Dynamic - Food",
                "Activator key",
                true,
                "Show food while the Activator key is held.");

            FoodCombat = Config.Bind(
                "Dynamic - Food",
                "Appear in combat",
                true,
                "Show food while an enemy is targeting or sensing you.");

            FoodInventory = Config.Bind(
                "Dynamic - Food",
                "Appear with inventory",
                true,
                "Show food while the inventory is open.");

            FoodStateChanges = Config.Bind(
                "Dynamic - Food",
                "Appear when state changes",
                true,
                "Briefly show food after eating or when a food first becomes replaceable.");


            FoodFinalMinute = Config.Bind(
                "Dynamic - Food",
                "Appear in final minute",
                true,
                "Show food during Valheim's final flashing minute before a food expires.");

            DynamicStamina = Config.Bind(
                "Dynamic - Stamina",
                "Dynamic",
                oldStamina,
                "Dynamically show and hide stamina.");

            StaminaActivator = Config.Bind(
                "Dynamic - Stamina",
                "Activator key",
                true,
                "Show stamina while the Activator key is held.");

            StaminaUseState = Config.Bind(
                "Dynamic - Stamina",
                "Appear when not full",
                true,
                "Show stamina whenever stamina is below maximum.");

            DynamicEitr = Config.Bind(
                "Dynamic - Eitr",
                "Dynamic",
                oldEitr,
                "Dynamically show and hide eitr.");

            EitrActivator = Config.Bind(
                "Dynamic - Eitr",
                "Activator key",
                true,
                "Show eitr while the Activator key is held.");

            EitrUseState = Config.Bind(
                "Dynamic - Eitr",
                "Appear when not full",
                true,
                "Show eitr whenever eitr is below maximum.");

            DynamicAdrenaline = Config.Bind(
                "Dynamic - Adrenaline",
                "Dynamic",
                oldAdrenaline,
                "Dynamically show and hide adrenaline.");

            AdrenalineActivator = Config.Bind(
                "Dynamic - Adrenaline",
                "Activator key",
                true,
                "Show adrenaline while the Activator key is held.");

            AdrenalineUseState = Config.Bind(
                "Dynamic - Adrenaline",
                "Appear while active",
                true,
                "Show adrenaline whenever the adrenaline meter contains any amount.");

            DynamicForsakenPower = Config.Bind(
                "Dynamic - Forsaken Power",
                "Dynamic",
                oldForsaken,
                "Dynamically show and hide the selected Forsaken power.");

            ForsakenActivator = Config.Bind(
                "Dynamic - Forsaken Power",
                "Activator key",
                true,
                "Show Forsaken power while the Activator key is held.");

            ForsakenCombat = Config.Bind(
                "Dynamic - Forsaken Power",
                "Appear in combat",
                true,
                "Show Forsaken power while an enemy is targeting or sensing you.");

            ForsakenInventory = Config.Bind(
                "Dynamic - Forsaken Power",
                "Appear with inventory",
                true,
                "Show Forsaken power while the inventory is open.");

            ForsakenCooldownEnding = Config.Bind(
                "Dynamic - Forsaken Power",
                "Appear at end of cooldown",
                true,
                "Show Forsaken power during the final five seconds of cooldown.");

            DynamicMluiWeight = Config.Bind(
                "Dynamic - MLUI Weight",
                "Dynamic",
                oldMluiWeight,
                "Dynamically show and hide My Little UI's Weight block.");

            MluiWeightActivator = Config.Bind(
                "Dynamic - MLUI Weight",
                "Activator key",
                true,
                "Show MLUI Weight while the Activator key is held.");

            MluiWeightInventory = Config.Bind(
                "Dynamic - MLUI Weight",
                "Appear with inventory",
                true,
                "Show MLUI Weight while the inventory is open.");

            DynamicMluiSlots = Config.Bind(
                "Dynamic - MLUI Slots",
                "Dynamic",
                oldMluiSlots,
                "Dynamically show and hide My Little UI's Slots block.");

            MluiSlotsActivator = Config.Bind(
                "Dynamic - MLUI Slots",
                "Activator key",
                true,
                "Show MLUI Slots while the Activator key is held.");

            MluiSlotsInventory = Config.Bind(
                "Dynamic - MLUI Slots",
                "Appear with inventory",
                true,
                "Show MLUI Slots while the inventory is open.");

            DynamicMinimap = Config.Bind(
                "Dynamic - Minimap",
                "Dynamic",
                oldMap,
                "Dynamically show and hide the small minimap.");

            MinimapActivator = Config.Bind(
                "Dynamic - Minimap",
                "Activator key",
                true,
                "Show the minimap while the Activator key is held.");

            MinimapShip = Config.Bind(
                "Dynamic - Minimap",
                "Appear while steering ship",
                true,
                "Keep the minimap visible while actively steering a ship.");

            DynamicMapInfo = Config.Bind(
                "Dynamic - Map Info",
                "Dynamic",
                oldMap,
                "Dynamically show and hide My Little UI day/time, forecast and wind information around the minimap.");

            MapInfoActivator = Config.Bind(
                "Dynamic - Map Info",
                "Activator key",
                true,
                "Show map information while the Activator key is held.");

            DynamicStatusEffects = Config.Bind(
                "Dynamic - Status Effects",
                "Dynamic",
                oldMap,
                "Dynamically show and hide temporary status-effect icons.");

            StatusEffectsActivator = Config.Bind(
                "Dynamic - Status Effects",
                "Activator key",
                true,
                "Show status effects while the Activator key is held.");

            StatusEffectsNew = Config.Bind(
                "Dynamic - Status Effects",
                "Appear when added",
                true,
                "Briefly show a status effect when it first appears.");

            StatusEffectsEnding = Config.Bind(
                "Dynamic - Status Effects",
                "Appear near expiry",
                true,
                "Show a temporary status effect during its final five seconds.");

            HidePermanentStatusEffects = Config.Bind(
                "Dynamic - Status Effects",
                "Hide permanent equipment effects",
                oldHidePermanent,
                "Hide persistent status effects supplied by equipped gear and equipment sets.");

            Config.Save();
        }

        private bool TakeLegacyBool(
            string section,
            string key,
            bool fallback)
        {
            ConfigEntry<bool> entry =
                Config.Bind(
                    section,
                    key,
                    fallback,
                    "Legacy Smooth UI setting.");

            bool value =
                entry.Value;

            Config.Remove(
                entry.Definition);

            return value;
        }

        private ConfigEntry<float> BindFloat(
            string section,
            string key,
            float defaultValue,
            float min,
            float max,
            string description)
        {
            return Config.Bind(
                section,
                key,
                defaultValue,
                new ConfigDescription(
                    description,
                    new AcceptableValueRange<float>(min, max)));
        }

        internal static float Dt => Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        internal static float Damp(float current, float target, float response)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0.01f, response) * Dt));
        }

        internal static Vector2 Damp(Vector2 current, Vector2 target, float response)
        {
            return Vector2.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0.01f, response) * Dt));
        }

        internal static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float x = 1f - t;
            return 1f - x * x * x;
        }

        internal static float PopScale(float t, float startScale)
        {
            t = Mathf.Clamp01(t);
            float smooth = EaseOutCubic(t);
            float overshoot = Mathf.Sin(t * Mathf.PI) * 0.055f;
            return Mathf.Lerp(startScale, 1f, smooth) + overshoot;
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    [HarmonyAfter("shudnal.MyLittleUI", "Azumatt.AzuExtendedPlayerInventory",
        "randyknapp.mods.equipmentandquickslots", "shudnal.ExtraSlots")]
    internal static class InventoryGridUpdateGuiPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(InventoryGrid __instance)
        {
            SlotAnimationSystem.RestoreGrid(__instance);
            ItemAnimationSystem.RestoreGrid(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(InventoryGrid __instance)
        {
            ItemAnimationSystem.CaptureGrid(__instance);
            SlotAnimationSystem.CaptureGrid(__instance);
        }
    }
}
