# Wire's Enemy HUD

Overhauled health bars and additional stats.

## Main Features 
(EVERYTHING can be configured; changed or turned off.)

* Generally improved health bar visuals. Double bar width, adjusted colors, additional effects etc. Images on Hexium and Nexus. Everything is made with Valheim's visual language in mind.
* A stagger bar
  * Bar under the in-world health bar, conceptually inspired by Elden Ring's stagger bar mod, visual language taken from vanilla's player HUD stagger bar.
  * Flashes white on initial staggering, parried or just regular stagger. 
  * Shows the stagger time with a yellow bar over the stagger bar's color, fully based on each enemy's actual stagger time when staggered.
* HP numbers for enemies, bosses, and other players
  * Current HP, current/max HP, percentage. Example: 75 (default) or 75/150 or 50%
  * Seperate toggles for enemies, bosses and players. Enemy default is Current HP and boss default is Current/Max.
* Enemy awareness is now represented by with it's name color instead of an icon.
* Seperate friendly and enemy nameplate colors; ex. friendly summoned skeleton health bars are green.
* Improvements to visibility conditions
  * Briefly shows enemy health bars when damaging enemies, no matter how far away
  * Enemy health bars are clamped to the screen, meaning you'll see that troll's nameplate on the top of your screen, even if it's head is way above it. Other visibility conditions still apply, so if nameplate isn't shown in the first place anyway you won't with this.

## Configuration

All settings can be changed through the in-game Configuration Manager. The values listed below are the defaults.

### General

**Preset**

Options: `Author's Choice`, `Vanilla`

Default: `Author's Choice`

Applies a predefined set of settings. Author's Choice enables the recommended HUD improvements, while Vanilla resets it back to complete vanilla so you can configure from a vanilla baseline.

### Enemy Health Bar

**Health display**

Options: `Current HP`, `Current HP/Max HP`, `Percentage`, `None`

Default: `Current HP`

Choose how enemy health is displayed. Show the current HP, current and maximum HP, remaining health percentage, or hide the counter entirely.

**Double size**

Default: `true`

Doubles the thickness of enemy health bars.

**Keep bars on screen**

Default: `true`

Keeps visible enemy nameplates within the screen boundaries. When an enemy's nameplate moves above the screen, it follows the enemy horizontally along the top edge.

**Show on damage**

Default: `true`

Reveals an enemy's health bar for 3 seconds after you successfully damage it. Additional hits refresh the timer. Only damage dealt by you triggers this effect. Killing blows do not reveal previously hidden health bars.

**Critical hit flash**

Default: `true`

Briefly flashes the dark frame surrounding an enemy's health bar white when a backstab or stagger-critical hit lands.

### Boss Health Bar

**Health display**

Options: `Current HP`, `Current HP/Max HP`, `Percentage`, `None`

Default: `Current HP/Max HP`

Choose how boss health is displayed.

**Health counter position**

Options: `Tracked`, `Centered`

Default: `Tracked`

Tracked positions the HP counter at the right edge of the remaining health fill. Centered places the HP and DPS text in the middle of the health bar.

**Boss name color**

Options: `Pastel`, `Vanilla`, `White`

Default: `White`

Changes the color of boss names. Pastel uses soft pink, Vanilla uses the original Valheim color, and White displays boss names in white.

### Player Health Bar

**Health display**

Options: `Current HP`, `Current HP/Max HP`, `Percentage`, `None`

Default: `Current HP/Max HP`

Choose how other players' health is displayed. HP counters require Double size to be enabled.

**Double size**

Default: `true`

Doubles the thickness of multiplayer player health bars without changing their width.

**Fade unfocused player nameplates**

Default: `true`

Fades other players' nameplates to 25% opacity. A nameplate returns to full opacity when you aim your crosshair directly at that player.

### DPS Meter

Each DPS counter has three display modes:

- `On`: Shows the number only, such as `23`.
- `On + DPS`: Shows the number with its label, such as `23 DPS`.
- `Off`: Hides the counter.

**Enemy DPS**

Default: `Off`

Controls the DPS counter on ordinary enemy health bars.

**Boss DPS**

Default: `On + DPS`

Controls the DPS counter on boss health bars.

**Player DPS**

Default: `Off`

Controls the DPS counter on multiplayer player health bars.

### Stagger Bar

**Enabled**

Default: `true`

Displays a stagger meter beneath enemy health bars. The meter fills as the enemy takes stagger damage.

**VerticalOffset**

Default: `8`

Adjusts the vertical distance between an enemy's health bar and stagger bar. Higher values move the stagger bar downward.

**StarVerticalOffset**

Default: `8`

Adjusts the vertical position of enemy star icons when the stagger meter is displayed. Higher values move the icons downward.

**HideForNonStaggerableEnemies**

Default: `true`

Automatically hides the stagger meter on enemies that cannot be staggered.

**Recovery overlay**

Default: `true`

Displays a pale gold layer inside the stagger meter while an enemy is staggered. The layer recedes from right to left as the stagger animation progresses, revealing the orange stagger meter underneath.

### Features

**NameAwarenessColors**

Options: `Pastel`, `Normal`, `None`

Default: `Pastel`

Controls enemy name colors based on their awareness state. Pastel uses softer colors, Normal uses stronger Valheim-style colors, and None keeps normal name colors and awareness icons.

**UppercaseEnemyNames**

Default: `false`

Displays ordinary enemy names in uppercase letters.

### Text

**HP and percent font size**

Default: `1.267`

Range: `0.5` to `2.5`

Adjusts the size of health counters and percentage text. A value of `1.0` uses the original font size.

**DPS font size**

Default: `1.0`

Range: `0.5` to `2.5`

Adjusts the size of DPS counter text independently of HP text. A value of `1.0` uses the original font size.

### Multiplayer

**SyncStaggerPercentage**

Default: `true`

Synchronizes enemy stagger-meter progress between players using Valheim's network data.

**SyncIntervalSeconds**

Default: `0.1`

Sets the minimum time in seconds between stagger synchronization updates for each enemy. Lower values allow more frequent updates.

**SyncMinimumDelta**

Default: `0.01`

Sets the minimum change in stagger-meter progress required to send another update. A value of `0.01` represents 1% of the full meter.

### Colors

All color settings are empty by default, preserving the original colors.

Enter a hexadecimal color using `#RRGGBB` or `#RRGGBBAA`, such as `#E75D16` or `#E75D16FF`. The eight-digit format includes opacity.

Invalid color codes are ignored. Clearing a setting restores the default color. Specific health bar color settings take priority over the general health bar color.

**Health bars**

Default: Empty

Sets the general health-fill color for enemies, bosses, players, and friendly creatures.

**Player health bars**

Default: Empty

Overrides the health-fill color of multiplayer player nameplates.

**Enemy health bars**

Default: Empty

Overrides the health-fill color of hostile enemy nameplates.

**Friendly health bars**

Default: Empty

Overrides the health-fill color of friendly and tamed creature nameplates.

**Stagger bar**

Default: Empty

Overrides the fill color of enemy stagger meters.

**Recovery overlay**

Default: Empty

Overrides the color of the stagger recovery layer.

### Troubleshooting / Debug

**(Debug) Player boss health bar**

Default: `false`

Displays a test boss health bar using your character's current and maximum health.

**See your own nameplate**

Default: `false`

Displays your own ameplate above your character for testing.


## Disclaimer: the code is AI assisted since I barely know C#. Not fully AI nor human.
## https://ko-fi.com/krosshair
