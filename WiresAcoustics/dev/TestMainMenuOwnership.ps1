$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$menu=[IO.File]::ReadAllText("$r\src\Menu.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw "MAIN MENU SOURCE OWNERSHIP: $why"}}
Must ($menu.Contains('private static void Postfix(FejdStartup __instance)') -and
      $menu.Contains('var scene = __instance.gameObject.scene;')) 'missing explicit active menu-scene ownership'
Must ($menu.Contains('src.gameObject.scene != scene ||')) 'persisted AudioMan and prior-scene sources still classified as menu'
Must ($menu.Contains('src.spatialBlend < .5f || src.clip == null')) 'non-spatial or clipless sources still get menu filters'
Must ($menu.Contains('existing.Kind == AcousticFilter.Kinds.Ocean') -and
      $menu.Contains('existing.Kind == AcousticFilter.Kinds.Ambient') -and
      $menu.Contains('existing.Kind == AcousticFilter.Kinds.Outside') -and
      $menu.Contains('existing.Voice ||')) 'existing source owner gets overwritten'
Must ($menu.Contains('src.gameObject.name == "Amb_MainMenu"') -and
      $menu.Contains('playOnAwake " + src.playOnAwake')) 'missing targeted startup diagnosis'
Write-Output 'MAIN MENU SOURCE OWNERSHIP PASS: menu-only, playable spatial sources, original ambient/voice owners preserved'
