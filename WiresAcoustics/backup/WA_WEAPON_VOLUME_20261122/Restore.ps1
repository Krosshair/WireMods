$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\WA_WEAPON_VOLUME_20261122'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Close Valheim before restoring DLL'}
foreach($file in @('Spots.cs','Area.cs','Filter.cs','Follow.cs')){
 Copy-Item (Join-Path $b $file) (Join-Path $r ('src\'+$file)) -Force
}
foreach($file in @('VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1','TestEventSourceExtent.ps1')){
 Copy-Item (Join-Path $b $file) (Join-Path $r ('dev\'+$file)) -Force
}
Remove-Item (Join-Path $r 'dev\TestWeaponVisualEmitter.ps1') -EA SilentlyContinue
# Restore source-extraction test harness altered to track visual-cache clearing.
$runner=Join-Path $r 'dev\AreaInvalidationTests\RunSourceTest.ps1'
$test=[IO.File]::ReadAllText($runner)
$test=$test.Replace('  private static readonly Dictionary<int,int> WeaponShapes=new();'+[Environment]::NewLine,'')
$test=$test.Replace('Shapes[t.GetInstanceID()]=1;WeaponShapes[t.GetInstanceID()]=1;','Shapes[t.GetInstanceID()]=1;')
$test=[regex]::Replace($test,'(?m)^  internal static bool HasWeapon\(Transform t\)=>WeaponShapes.ContainsKey\(t.GetInstanceID\(\)\);\r?\n','')
$test=[regex]::Replace($test,'(?m)^   Assert\(!Area.HasWeapon\(root\).*?\r?\n\s+"equipped weapon visual cache must invalidate with owner geometry"\);\r?\n','')
$test=[regex]::Replace($test,'(?m)^   Assert\(Area.HasWeapon\(sibling\).*?\r?\n\s+"unrelated weapon visual caches must stay stable"\);\r?\n','')
[IO.File]::WriteAllText($runner,$test)
foreach($file in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package_'+$file)) (Join-Path $r ('package\'+$file)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'Restored the prior WA source, source tests, package and Enngard DLL. Rebuild before further installs.'
