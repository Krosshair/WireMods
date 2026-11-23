$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before restoring the earlier WA version'}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\MOTION_VS_WORLD_CHANGES_20261122'
$files=@(Get-ChildItem (Join-Path $b 'src') -Filter '*.cs')
if($files.Count -ne 48){throw "Expected 48 source backups, found $($files.Count)"}
foreach($f in $files){Copy-Item $f.FullName (Join-Path $r "src\$($f.Name)") -Force}
Copy-Item (Join-Path $b 'WiresAcoustics.csproj') (Join-Path $r 'src\WiresAcoustics.csproj') -Force
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
if((Get-FileHash $live -Algorithm SHA256).Hash -ne
   (Get-FileHash (Join-Path $b 'installed.dll') -Algorithm SHA256).Hash){
 throw 'Restore DLL hash mismatch'
}
'Restored original installed WA and 48 source files.'
