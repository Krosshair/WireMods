# Wire's Helmet Hider

Double-tap the **emote button** to hide or show your equipped headpiece. Hold the emote button to open the normal emote radial. You can also set a custom toggle hotkey to return the emote wheel button to vanilla behaviour.

The headpiece is only hidden visually while remaining equipped, so its armor, set bonuses and other equipment effects continue normally.



## Input

By default, double-tap the emote button to toggle the headpiece and hold it to open the emote radial.

Set **Custom Toggle Key** to any hotkey to toggle directly with that key instead. With a custom hotkey set, the emote button is left completely vanilla.

The double-tap window and emote hold duration only apply when **Custom Toggle Key** is None.

Hidden state is synchronized through the player ZDO so other players running the mod see it as well.



The helmet toggle plays a quick cosmetic equip animation (0.22 seconds by default) before changing visibility. Interrupting it applies the change immediately; the brief animation is synchronized to other players through Valheim's native animation system.

Switching, removing or genuinely equipping another headpiece returns it to visible. Turn this off in config to keep the hidden state while changing headgear. Hidden/Shown state persists throughout logouts and game restarts.



Disclaimer: the code is AI-assisted since I barely know C#. Not fully AI nor human.

https://ko-fi.com/krosshair