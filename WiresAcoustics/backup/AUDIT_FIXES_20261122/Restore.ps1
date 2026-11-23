$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue) {
    throw 'Close Valheim before restoring WA'
}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\AUDIT_FIXES_20261122'
$before=Join-Path $b 'src'
$src=Join-Path $r 'src'
$files=@(Get-ChildItem $before -Filter '*.cs')
if($files.Count -ne 47) {throw "Incomplete rollback: $($files.Count) sources"}
foreach($f in $files) {
    Copy-Item $f.FullName (Join-Path $src $f.Name) -Force
}
Copy-Item (Join-Path $b 'WiresAcoustics.csproj') (Join-Path $src 'WiresAcoustics.csproj') -Force
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
$hash=(Get-FileHash $live -Algorithm SHA256).Hash
$original=(Get-FileHash (Join-Path $b 'installed.dll') -Algorithm SHA256).Hash
if($hash -ne $original) {throw 'Rollback installed DLL hash mismatch'}
"Restored WA source and Enngard binaries. SHA256=$hash"
