using System;
using System.Reflection;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace WiresSmoothUI
{
    internal enum ActivatorBehaviorMode { Hold, Toggle }

    // BepInEx ConfigurationManager reads this metadata by reflection. There is no dependency on it.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public string DispName = null;
        public int? Order;
        public string Category = null;
    }

    internal static class SmoothUIConfigRework
    {
        private static bool _firstMigration;
        private static ConfigEntry<bool> _migrationMarker;
        private static ConfigEntry<bool> _migrationMarker52;
        private static bool _migrating52;

        internal static void Apply(ConfigFile config)
        {
            _migrationMarker = config.Bind("Internal", "Migrated settings to 0.5.1", false,
                new ConfigDescription("Internal configuration migration state. Do not edit.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            _firstMigration = !_migrationMarker.Value;
            _migrationMarker52 = config.Bind("Internal", "Migrated settings to 0.5.2", false,
                new ConfigDescription("Internal migration state. Do not edit.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            _migrating52 = !_migrationMarker52.Value;

            Move(config, ref WiresSmoothUIPlugin.MapInfoHotkey, "Keybinds", "activator key", "Keyboard activator key for revealing selected dynamic HUD elements; works with Hold and Toggle modes.", 30);
            Move(config, ref WiresSmoothUIPlugin.MapInfoControllerHotkey, "Keybinds", "activator controller button", "Controller equivalent of the activator key.", 20);
            WiresSmoothUIPlugin.ActivatorBehavior = config.Bind("Keybinds", "Keybind behaviour", ActivatorBehaviorMode.Hold, new ConfigDescription("Choose whether the keyboard and controller activator must be held or pressed to toggle the reveal state.", null, new ConfigurationManagerAttributes { Order = 10 }));
            Move(config, ref WiresSmoothUIPlugin.Enabled, "Global Toggles", "Global toggle - animations", "Master switch for UI animations. Disabling overrides the individual animation switches without disabling dynamic HUD hiding.", 20);
            NewBool(config, ref WiresSmoothUIPlugin.HudEnabled, "Global Toggles", "Global toggle - Dynamic HUD", true, "Master switch for contextual HUD hiding. Disabling restores dynamic HUD elements to normal visibility without disabling menu animations.", 10);
            Move(config, ref WiresSmoothUIPlugin.CrosshairAnimations, "Crosshair (Dynamic HUD)", "Dynamic Crosshair", "Dynamically show and hide the + crosshair.", 90);
            Move(config, ref WiresSmoothUIPlugin.CrosshairFade, "Crosshair (Dynamic HUD)", "Crosshair fade seconds", "Fade time for the + crosshair.", 80, 0f, 1f);
            Move(config, ref WiresSmoothUIPlugin.CrosshairActivator, "Crosshair (Dynamic HUD)", "Affected by activator key", "Show the normal crosshair when the activator key is active.", 70);
            Move(config, ref WiresSmoothUIPlugin.ShowHoverCrosshair, "Crosshair (Dynamic HUD)", "Appear on hover", "Show the normal crosshair for an in-range hover target or interactable.", 60);
            Move(config, ref WiresSmoothUIPlugin.CrosshairWithTools, "Crosshair (Dynamic HUD)", "Appear with tools", "Show the normal crosshair while holding a tool such as the Hammer.", 50);
            Move(config, ref WiresSmoothUIPlugin.CrosshairWithRanged, "Crosshair (Dynamic HUD)", "Appear with ranged weapons", "Show the normal crosshair while holding a ranged or projectile weapon.", 40);
            Move(config, ref WiresSmoothUIPlugin.CrosshairWithMelee, "Crosshair (Dynamic HUD)", "Appear with melee weapons", "Show the normal crosshair while holding a melee weapon.", 30);
            Move(config, ref WiresSmoothUIPlugin.CrosshairYellowHover, "Crosshair (Dynamic HUD)", "Yellow hover highlight", "Use vanilla's yellow, full-opacity crosshair highlight on interactable hover.", 20);
            Move(config, ref WiresSmoothUIPlugin.DisableStealthIndicator, "Crosshair (Dynamic HUD)", "Hide stealth indicator", "Hide Valheim's crouch/stealth eye indicator.", 10);
            Move(config, ref WiresSmoothUIPlugin.ProjectileReticle, "Yellow reticles (Animations)", "Universal projectile reticle", "Add vanilla's bow yellow reticle to all projectile weapons.", 30);
            Move(config, ref WiresSmoothUIPlugin.ReticleEasing, "Yellow reticles (Animations)", "Reticle easing", "Scale/color easing for the projectile reticle.", 20);
            NewFloat(config, ref WiresSmoothUIPlugin.ReticleFadeIn, "Yellow reticles (Animations)", "Reticle fade-in seconds", .1f, "Time for the projectile ring to fade in when it first appears; it still disappears instantly.", 10);
            NewBool(config, ref WiresSmoothUIPlugin.ResourceActivator, "Stamina/Eitr/Adrenaline (Animations)", "Affected by activator key", true, "Show stamina, eitr and adrenaline while the activator key is active (bars with zero maximum remain hidden).", 30);
            NewBool(config, ref WiresSmoothUIPlugin.ResourceFading, "Stamina/Eitr/Adrenaline (Animations)", "Alpha fading", true, "Fade stamina, eitr and adrenaline bars as they appear or disappear, instead of snapping.", 20);
            NewFloat(config, ref WiresSmoothUIPlugin.ResourceFadeSeconds, "Stamina/Eitr/Adrenaline (Animations)", "Fade duration", .2f, "Time in seconds for stamina, eitr and adrenaline bars to appear or disappear. Zero is instant.", 10);
            Move(config, ref WiresSmoothUIPlugin.DynamicFood, "Food stack (Dynamic HUD)", "Dynamic Food Stack", "Dynamically show and hide the food HUD.", 70);
            Move(config, ref WiresSmoothUIPlugin.FoodActivator, "Food stack (Dynamic HUD)", "Affected by activator key", "Show food when the activator key is active.", 60);
            Move(config, ref WiresSmoothUIPlugin.FoodCombat, "Food stack (Dynamic HUD)", "Appear in combat", "Show food while an enemy is targeting or sensing you.", 50);
            Move(config, ref WiresSmoothUIPlugin.FoodInventory, "Food stack (Dynamic HUD)", "Appear with inventory", "Show food while the inventory is open.", 40);
            Move(config, ref WiresSmoothUIPlugin.FoodStateChanges, "Food stack (Dynamic HUD)", "Appear when state changes", "Briefly show food after eating or when a food first becomes replaceable.", 30);
            Move(config, ref WiresSmoothUIPlugin.FoodFinalMinute, "Food stack (Dynamic HUD)", "Appear in final minute", "Show food during the last minute before a food expires.", 20);
            NewBool(config, ref WiresSmoothUIPlugin.FoodReplaceable, "Food stack (Dynamic HUD)", "Appear while replaceable", false, "Keep the food stack visible while any food is replaceable.", 10);
            NewFloat(config, ref WiresSmoothUIPlugin.FoodFadeSeconds, "Food stack (Dynamic HUD)", "Fade duration", .2f, "Fade duration in seconds for food stack. Zero is instant.", 1);
            Move(config, ref WiresSmoothUIPlugin.DynamicForsakenPower, "Forsaken power (Dynamic HUD)", "Dynamic", "Dynamically show and hide the selected Forsaken power.", 60);
            Move(config, ref WiresSmoothUIPlugin.ForsakenActivator, "Forsaken power (Dynamic HUD)", "Affected by activator key", "Show Forsaken power when the activator key is active.", 50);
            Move(config, ref WiresSmoothUIPlugin.ForsakenCombat, "Forsaken power (Dynamic HUD)", "Appear in combat", "Show Forsaken power while an enemy is targeting or sensing you.", 40);
            Move(config, ref WiresSmoothUIPlugin.ForsakenInventory, "Forsaken power (Dynamic HUD)", "Appear with inventory", "Show Forsaken power while the inventory is open.", 30);
            NewBool(config, ref WiresSmoothUIPlugin.ForsakenOnUse, "Forsaken power (Dynamic HUD)", "Appear on use", true, "Show Forsaken power briefly immediately after activation.", 20);
            Move(config, ref WiresSmoothUIPlugin.ForsakenCooldownEnding, "Forsaken power (Dynamic HUD)", "Appear at end of cooldown", "Show Forsaken power during the final five seconds of cooldown.", 10);
            NewFloat(config, ref WiresSmoothUIPlugin.ForsakenFadeSeconds, "Forsaken power (Dynamic HUD)", "Fade duration", .2f, "Fade duration in seconds for forsaken power. Zero is instant.", 1);
            Move(config, ref WiresSmoothUIPlugin.DynamicHealth, "Health (Dynamic HUD)", "Dynamic", "Dynamically show and hide health.", 50);
            Move(config, ref WiresSmoothUIPlugin.HealthActivator, "Health (Dynamic HUD)", "Affected by activator key", "Show health when the activator key is active.", 40);
            Move(config, ref WiresSmoothUIPlugin.HealthCombat, "Health (Dynamic HUD)", "Appear in combat", "Show health while an enemy is targeting or sensing you.", 30);
            Move(config, ref WiresSmoothUIPlugin.HealthInventory, "Health (Dynamic HUD)", "Appear with inventory", "Show health while the inventory is open.", 20);
            WiresSmoothUIPlugin.HealthThresholdChoice = config.Bind("Health (Dynamic HUD)", "Appear below 95%", "95.1%", new ConfigDescription("Show health when current health drops to 95% or below, or whenever it is below 100%.", new AcceptableValueList<string>("95.1%", "100.0%"), new ConfigurationManagerAttributes { Order = 10 }));
            NewFloat(config, ref WiresSmoothUIPlugin.HealthFadeSeconds, "Health (Dynamic HUD)", "Fade duration", .2f, "Fade duration in seconds for health. Zero is instant.", 1);
            Move(config, ref WiresSmoothUIPlugin.DynamicHotbar, "Hotbar (Dynamic HUD)", "Dynamic", "Dynamically show and hide the vanilla hotbar and Azu quickbar.", 60);
            Move(config, ref WiresSmoothUIPlugin.HotbarActivator, "Hotbar (Dynamic HUD)", "Affected by activator key", "Show the hotbar when the activator key is active.", 50);
            Move(config, ref WiresSmoothUIPlugin.HotbarCombat, "Hotbar (Dynamic HUD)", "Appear in combat", "Show the hotbar while an enemy is targeting or sensing you.", 40);
            Move(config, ref WiresSmoothUIPlugin.HotbarEquipActions, "Hotbar (Dynamic HUD)", "Appear while equipping or unequipping", "Show the hotbar while an item is actively being equipped or unequipped.", 30);
            Move(config, ref WiresSmoothUIPlugin.HotbarHeldItem, "Hotbar (Dynamic HUD)", "Appear while holding an item", "Keep the hotbar visible while an item is held in either hand, even if it did not come from the hotbar.", 20);
            NewBool(config, ref WiresSmoothUIPlugin.HotbarCycle, "Hotbar (Dynamic HUD)", "Appear when cycling with controller", true, "Show the hotbar while cycling its selected slot with a controller. It stays visible for one second after the last step.", 10);
            NewFloat(config, ref WiresSmoothUIPlugin.HotbarFadeSeconds, "Hotbar (Dynamic HUD)", "Fade duration", .2f, "Fade duration in seconds for hotbar. Zero is instant.", 1);
            Move(config, ref WiresSmoothUIPlugin.DynamicMinimap, "Minimap (Dynamic HUD)", "Dynamic", "Dynamically show and hide the small minimap.", 30);
            Move(config, ref WiresSmoothUIPlugin.MinimapActivator, "Minimap (Dynamic HUD)", "Affected by activator key", "Reveal the minimap while the activator key is active. When disabled, the activator never moves the minimap.", 20);
            Move(config, ref WiresSmoothUIPlugin.MinimapShip, "Minimap (Dynamic HUD)", "Appear while steering ship", "Keep the minimap visible while actively steering a ship.", 10);
            NewFloat(config, ref WiresSmoothUIPlugin.MinimapMoveSeconds, "Minimap (Dynamic HUD)", "Move duration", .1f, "The time in seconds for minimap movement. Zero moves immediately.", 1);
            Move(config, ref WiresSmoothUIPlugin.DynamicStatusEffects, "Status Effects (Dynamic HUD)", "Dynamic status effects", "Dynamically show and hide temporary status-effect icons.", 50);
            Move(config, ref WiresSmoothUIPlugin.StatusEffectsActivator, "Status Effects (Dynamic HUD)", "Affected by activator key", "Show status effects when the activator key is active.", 40);
            Move(config, ref WiresSmoothUIPlugin.StatusEffectsNew, "Status Effects (Dynamic HUD)", "Appear when new", "Briefly show a status effect when it first appears.", 30);
            Move(config, ref WiresSmoothUIPlugin.StatusEffectsEnding, "Status Effects (Dynamic HUD)", "Appear near expiry", "Show a temporary status effect during its final five seconds.", 20);
            Move(config, ref WiresSmoothUIPlugin.HidePermanentStatusEffects, "Status Effects (Dynamic HUD)", "Hide permanent equipment effects", "Hide persistent status effects supplied by equipped gear and equipment sets.", 10);
            NewFloat(config, ref WiresSmoothUIPlugin.StatusFadeSeconds, "Status Effects (Dynamic HUD)", "Fade duration", .2f, "Duration for status effects appearing independently of the minimap. Zero is instant.", 1);
            NewFloat(config, ref WiresSmoothUIPlugin.StatusActivatorFadeSeconds, "Status Effects (Dynamic HUD)", "Activator fade duration", .1f, "Fade duration for the status-effect and minimap-information blocks when revealing or hiding them with the Activator key.", 1);
            Move(config, ref WiresSmoothUIPlugin.SlotAnimations, "Inventory Slots (Animations)", "Enabled", "Animate the whole slot and everything inside it on mouse hover, press, valid drag targets, and gamepad selection.", 80);
            Move(config, ref WiresSmoothUIPlugin.HoverScale, "Inventory Slots (Animations)", "Hover scale", "How large a hovered inventory slot becomes.", 70);
            Move(config, ref WiresSmoothUIPlugin.HoverResponse, "Inventory Slots (Animations)", "Response", "How quickly slot motion catches up. Higher values feel snappier.", 60);
            Move(config, ref WiresSmoothUIPlugin.SlotSway, "Inventory Slots (Animations)", "Mouse inertia", "Let hovered slots lean and sway slightly with mouse movement.", 50);
            Move(config, ref WiresSmoothUIPlugin.SwayPixels, "Inventory Slots (Animations)", "Inertia distance", "Maximum hovered-slot movement caused by mouse inertia, in UI pixels.", 40);
            Move(config, ref WiresSmoothUIPlugin.SwayDegrees, "Inventory Slots (Animations)", "Inertia rotation", "Maximum hovered-slot rotation caused by mouse inertia, in degrees.", 30);
            Move(config, ref WiresSmoothUIPlugin.DropTargetScale, "Inventory Slots (Animations)", "Valid drop target scale", "How large a valid destination slot becomes while dragging an item over it.", 20);
            Move(config, ref WiresSmoothUIPlugin.GamepadSelectionScale, "Inventory Slots (Animations)", "Gamepad selection scale", "How large the currently selected inventory slot becomes when using a gamepad.", 10);
            Move(config, ref WiresSmoothUIPlugin.ItemAnimations, "Item Feedback (Animations)", "Enabled", "Animate item appearance, moves, landing, stack changes, equip state, and queued actions, and pop the armor number when armor goes on or off.", 40);
            Move(config, ref WiresSmoothUIPlugin.ItemMoveDuration, "Item Feedback (Animations)", "Move and landing duration", "How long moved or swapped items glide into the destination and settle.", 30);
            Move(config, ref WiresSmoothUIPlugin.ItemAppearDuration, "Item Feedback (Animations)", "Appear and grab duration", "How long new items and newly grabbed drag icons take to pop into place.", 20);
            Move(config, ref WiresSmoothUIPlugin.StackPulseScale, "Item Feedback (Animations)", "Stack change pulse", "Peak item scale when a stack amount changes. The number itself receives a slightly stronger pop.", 10);
            Move(config, ref WiresSmoothUIPlugin.MapPinAnimations, "Map Pins (Animations)", "Enabled", "Animate newly placed map pins and other newly-created visible map markers.", 30);
            Move(config, ref WiresSmoothUIPlugin.MapPinPopScale, "Map Pins (Animations)", "Placement pop scale", "Peak scale reached by a newly placed map pin.", 20);
            Move(config, ref WiresSmoothUIPlugin.MapPinPopDuration, "Map Pins (Animations)", "Placement pop duration", "Duration of the map-pin placement pop.", 10);
            Move(config, ref WiresSmoothUIPlugin.TooltipAnimations, "Tooltips (Animations)", "Enabled", "Softly fade and scale tooltips in when they appear.", 30);
            Move(config, ref WiresSmoothUIPlugin.TooltipDuration, "Tooltips (Animations)", "Entrance duration", "Tooltip entrance duration in seconds.", 20);
            Move(config, ref WiresSmoothUIPlugin.TooltipStartScale, "Tooltips (Animations)", "Opening scale", "Tooltip scale at the start of its entrance.", 10);
            Move(config, ref WiresSmoothUIPlugin.BuildMenuAnimations, "Build & Dish Menus (Animations)", "Enabled", "Give Valheim's build-piece, feast/dish, food and mead selection tiles Smooth UI motion. These menus share the same BuildUi system.", 40);
            Move(config, ref WiresSmoothUIPlugin.BuildMenuHoverScale, "Build & Dish Menus (Animations)", "Hover scale", "How large a hovered build/dish selection tile becomes.", 30);
            Move(config, ref WiresSmoothUIPlugin.BuildMenuResponse, "Build & Dish Menus (Animations)", "Response", "How quickly build/dish tile motion responds.", 20);
            Move(config, ref WiresSmoothUIPlugin.BuildMenuInertia, "Build & Dish Menus (Animations)", "Mouse inertia", "Let hovered build/dish selection tiles lean slightly with mouse movement.", 10);
            NewBool(config, ref WiresSmoothUIPlugin.EquipProgressFill, "Item Feedback (Animations)", "Equip progress fill", true, "Fill an item's slot background from the bottom up while it's being equipped, and drain it while it's being unequipped. Works in the inventory and on the hotbar.", 15);
            NewBool(config, ref WiresSmoothUIPlugin.PanelAnimations, "Menus (Animations)", "Panels", true, "Fade and scale menus in when they open: texts, the split, variant and sign dialogs, the big map and pin name box, the craft progress bar, the pause menu, and settings and its tabs. The build menu scales in, and the selected piece panel fades in when you take out the hammer. Skills, trophies and achievements scale in without a fade, and the skill rows you can see fade and rise in.", 20);
            NewBool(config, ref WiresSmoothUIPlugin.CraftingAnimations, "Menus (Animations)", "Crafting panel", true, "Slide the visible recipe rows in when the panel opens or you switch Craft/Upgrade, and pop the tab label. When you pick a recipe, pop its icon, fade the selection in and slide in the details and requirements that differ from the last recipe; the required station level pops when it changes. After an upgrade, slide in what it changed.", 10);
            NewBool(config, ref WiresSmoothUIPlugin.HideEmptyMaterialSlots, "Menus", "Hide empty material slots", true, "In the crafting panel, material slots a recipe doesn't use aren't shown (a recipe with 3 materials shows 3 slots), and neither is the station level box when no level is needed or nothing is selected. Buttons other mods put in those slots, like Forge of Goodtential's, still show.", 0);
            // Entries from test builds of 0.6.0 that didn't make it.
            foreach (string stale in new[] { "Crafting list", "Hotbar slots" })
                config.Remove(config.Bind("Menus (Animations)", stale, true, "Removed.").Definition);
            config.Remove(config.Bind("Item Feedback (Animations)", "Hotbar, armor and food pops", true, "Removed.").Definition);
            Move(config, ref WiresSmoothUIPlugin.ButtonAnimations, "Buttons (Animations)", "Enabled", "Give ordinary Valheim UI buttons a subtle hover and press response. Inventory slots use their own animation system.", 30);
            Move(config, ref WiresSmoothUIPlugin.ButtonHoverScale, "Buttons (Animations)", "Hover scale", "Relative scale of hovered non-inventory buttons.", 20);
            Move(config, ref WiresSmoothUIPlugin.ButtonResponse, "Buttons (Animations)", "Response", "How quickly button hover and press motion responds.", 10);
            Move(config, ref WiresSmoothUIPlugin.DragPhysics, "Dragging (Animations)", "Physics sway", "Give dragged items spring-like lag, sway, and rotation when the cursor accelerates or changes direction.", 30);
            Move(config, ref WiresSmoothUIPlugin.DragFollow, "Dragging (Animations)", "Follow speed", "How quickly the dragged item follows the cursor. Lower values create more trailing inertia.", 20);
            Move(config, ref WiresSmoothUIPlugin.DragRotation, "Dragging (Animations)", "Physics rotation", "Maximum spring-like rotation of a dragged item when the cursor changes direction.", 10);
            Move(config, ref WiresSmoothUIPlugin.DynamicMluiSlots, "Mod support", "Dynamic MLUI slots and weight", "Dynamically show and hide My Little UI's Slots block.", 40);
            Move(config, ref WiresSmoothUIPlugin.MluiSlotsActivator, "Mod support", "Affected by activator key (MLUI slots and weight)", "Show MLUI Slots and Weight when the activator key is active.", 30);
            Move(config, ref WiresSmoothUIPlugin.MluiSlotsInventory, "Mod support", "Appear with inventory (MLUI slots and weight)", "Show MLUI Slots and Weight while the inventory is open.", 20);
            float oldMluiFade = .5f;
            if (_migrating52)
            {
                var previousMlui = config.Bind("Mod support", "MLUI inventory fade duration",
                    .5f, "Old MLUI fade setting, removed during migration.");
                oldMluiFade = previousMlui.Value;
                config.Remove(previousMlui.Definition);
            }
            NewFloat(config, ref WiresSmoothUIPlugin.MluiFadeSeconds, "Mod support", "MLUI Weight/Slots Fade Duration", .2f, "Fade duration for My Little UI weight and slots blocks. Zero is instant.", 10);
            if (_migrating52 && Math.Abs(oldMluiFade - .5f) > .0001f)
                WiresSmoothUIPlugin.MluiFadeSeconds.Value = oldMluiFade;
            NewBool(config, ref WiresSmoothUIPlugin.DynamicAzuQuickbar, "Mod support", "Dynamic Azu Extended Inventory quickslots", true, "Dynamically show AzuExtendedInventory quickslots. Uses hotbar's specific options when on, static unaffected when off.", 1);
            config.Remove(WiresSmoothUIPlugin.DynamicStamina.Definition);
            config.Remove(WiresSmoothUIPlugin.DynamicEitr.Definition);
            config.Remove(WiresSmoothUIPlugin.DynamicAdrenaline.Definition);
            config.Remove(WiresSmoothUIPlugin.StaminaActivator.Definition);
            config.Remove(WiresSmoothUIPlugin.EitrActivator.Definition);
            config.Remove(WiresSmoothUIPlugin.AdrenalineActivator.Definition);
            config.Remove(WiresSmoothUIPlugin.DynamicMluiWeight.Definition);
            config.Remove(WiresSmoothUIPlugin.MluiWeightActivator.Definition);
            config.Remove(WiresSmoothUIPlugin.MluiWeightInventory.Definition);
            config.Remove(WiresSmoothUIPlugin.DynamicMapInfo.Definition);
            config.Remove(WiresSmoothUIPlugin.MapInfoActivator.Definition);
            WiresSmoothUIPlugin.DynamicStamina = WiresSmoothUIPlugin.ResourceFading;
            WiresSmoothUIPlugin.DynamicEitr = WiresSmoothUIPlugin.ResourceFading;
            WiresSmoothUIPlugin.DynamicAdrenaline = WiresSmoothUIPlugin.ResourceFading;
            WiresSmoothUIPlugin.StaminaActivator = WiresSmoothUIPlugin.ResourceActivator;
            WiresSmoothUIPlugin.EitrActivator = WiresSmoothUIPlugin.ResourceActivator;
            WiresSmoothUIPlugin.AdrenalineActivator = WiresSmoothUIPlugin.ResourceActivator;
            WiresSmoothUIPlugin.DynamicMluiWeight = WiresSmoothUIPlugin.DynamicMluiSlots;
            WiresSmoothUIPlugin.MluiWeightActivator = WiresSmoothUIPlugin.MluiSlotsActivator;
            WiresSmoothUIPlugin.MluiWeightInventory = WiresSmoothUIPlugin.MluiSlotsInventory;
            WiresSmoothUIPlugin.DynamicMapInfo = WiresSmoothUIPlugin.DynamicMinimap;
            WiresSmoothUIPlugin.MapInfoActivator = WiresSmoothUIPlugin.MinimapActivator;
            HideLegacy(config, ref WiresSmoothUIPlugin.StaminaUseState, true);
            HideLegacy(config, ref WiresSmoothUIPlugin.EitrUseState, true);
            HideLegacy(config, ref WiresSmoothUIPlugin.AdrenalineUseState, true);
            HideLegacy(config, ref WiresSmoothUIPlugin.HealthDamaged, true);
            if (_firstMigration && !WiresSmoothUIPlugin.Enabled.Value)
                WiresSmoothUIPlugin.HudEnabled.Value = false;
            if (_migrating52)
            {
                // Preserve intentional adjustments. Only move untouched old defaults.
                MigrateFadeDefault(WiresSmoothUIPlugin.ResourceFadeSeconds);
                MigrateFadeDefault(WiresSmoothUIPlugin.HealthFadeSeconds);
                MigrateFadeDefault(WiresSmoothUIPlugin.FoodFadeSeconds);
                MigrateFadeDefault(WiresSmoothUIPlugin.ForsakenFadeSeconds);
                MigrateFadeDefault(WiresSmoothUIPlugin.HotbarFadeSeconds);
                MigrateFadeDefault(WiresSmoothUIPlugin.StatusFadeSeconds);
                if (Math.Abs(WiresSmoothUIPlugin.CrosshairFade.Value - .1f) < .0001f)
                    WiresSmoothUIPlugin.CrosshairFade.Value = 0f;
                if (Math.Abs(WiresSmoothUIPlugin.DragRotation.Value - 8f) < .0001f)
                    WiresSmoothUIPlugin.DragRotation.Value = 16f;
                _migrationMarker52.Value = true;
            }

            _migrationMarker.Value = true;
            config.Save();
            RestoreCategoryOrder(config);
        }

        private static void MigrateFadeDefault(ConfigEntry<float> entry)
        {
            if (entry != null && Math.Abs(entry.Value - .5f) < .0001f)
                entry.Value = .2f;
        }

        // BepInEx reuses removed dictionary slots during BindConfig migrations.
        // Re-establish the authored category order explicitly, without changing
        // any user-facing category names or setting values.
        private static void RestoreCategoryOrder(ConfigFile config)
        {
            var property = typeof(ConfigFile).GetProperty("Entries",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var entries = property?.GetValue(config, null)
                as Dictionary<ConfigDefinition, ConfigEntryBase>;
            if (entries == null)
                return;

            string[] order = {
                "Keybinds", "Global Toggles", "Crosshair (Dynamic HUD)",
                "Yellow reticles (Animations)", "Stamina/Eitr/Adrenaline (Animations)",
                "Food stack (Dynamic HUD)", "Forsaken power (Dynamic HUD)",
                "Health (Dynamic HUD)", "Hotbar (Dynamic HUD)",
                "Minimap (Dynamic HUD)", "Status Effects (Dynamic HUD)",
                "Inventory Slots (Animations)", "Item Feedback (Animations)",
                "Map Pins (Animations)", "Tooltips (Animations)",
                "Build & Dish Menus (Animations)", "Menus (Animations)", "Buttons (Animations)",
                "Dragging (Animations)", "Mod support", "Internal"
            };

            var existing = new List<KeyValuePair<ConfigDefinition, ConfigEntryBase>>(entries);
            var reordered = new List<KeyValuePair<ConfigDefinition, ConfigEntryBase>>(existing.Count);
            foreach (string category in order)
            {
                foreach (var entry in existing)
                    if (entry.Key.Section == category)
                        reordered.Add(entry);
            }
            foreach (var entry in existing)
                if (Array.IndexOf(order, entry.Key.Section) < 0)
                    reordered.Add(entry);

            entries.Clear();
            foreach (var entry in reordered)
                entries.Add(entry.Key, entry.Value);
        }

        private static ConfigDescription Describe(string tooltip, AcceptableValueBase range, int order)
        {
            return new ConfigDescription(tooltip, range, new ConfigurationManagerAttributes { Order = order });
        }

        private static void Move<T>(ConfigFile config, ref ConfigEntry<T> original,
            string section, string name, string tooltip, int order, float min = -1f, float max = -1f)
        {
            var old = original;
            T value = old.Value;
            T originalDefault = (T)old.DefaultValue;

            // The 0.5.1 labels used a capitalized "Activator key".
            // Transfer those saved values when the labels change in 0.5.2.
            string previousName = null;
            if (section == "Keybinds" && name == "activator key")
                previousName = "Affected by Activator key";
            else if (section == "Keybinds" && name == "activator controller button")
                previousName = "Activator controller button";
            else if (name == "Affected by activator key")
                previousName = "Affected by Activator key";
            else if (name == "Affected by activator key (MLUI slots and weight)")
                previousName = "Affected by Activator key (MLUI slots and weight)";

            if (_migrating52 && previousName != null)
            {
                var previous = config.Bind(section, previousName, value,
                    "Old setting migrated into the renamed 0.5.2 setting.");
                value = previous.Value;
                config.Remove(previous.Definition);
            }
            bool sameKey = old.Definition.Section == section && old.Definition.Key == name;
            AcceptableValueBase range = old.Description.AcceptableValues;
            if (typeof(T) == typeof(float) && min >= 0f && max >= min)
                range = new AcceptableValueRange<float>(min, max);
            config.Remove(old.Definition);
            var result = config.Bind(section, name, originalDefault, Describe(tooltip, range, order));
            if (_firstMigration || sameKey || (_migrating52 && previousName != null))
                result.Value = value;
            original = result;
        }

        private static void NewBool(ConfigFile config, ref ConfigEntry<bool> dest,
            string section, string key, bool fallback, string description, int order)
        {
            bool previousValue = fallback;
            if (_migrating52 && section == "Stamina/Eitr/Adrenaline (Animations)" &&
                key == "Affected by activator key")
            {
                var previous = config.Bind(section, "Affected by Activator key", fallback,
                    "Old resource activator setting.");
                previousValue = previous.Value;
                config.Remove(previous.Definition);
            }

            dest = config.Bind(section, key, fallback, Describe(description, null, order));
            if (_migrating52 && section == "Stamina/Eitr/Adrenaline (Animations)" &&
                key == "Affected by activator key")
                dest.Value = previousValue;
        }

        private static void NewFloat(ConfigFile config, ref ConfigEntry<float> dest,
            string section, string key, float fallback, string description, int order)
        {
            dest = config.Bind(section, key, fallback,
                Describe(description, new AcceptableValueRange<float>(0f, 1f), order));
        }

        private static void HideLegacy(ConfigFile config, ref ConfigEntry<bool> original, bool value)
        {
            config.Remove(original.Definition);
            original = null;
        }
    }
}
