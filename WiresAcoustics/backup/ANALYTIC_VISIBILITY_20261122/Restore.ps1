$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue) {
    throw 'Close Valheim before restoring the previous WA build'
}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\ANALYTIC_VISIBILITY_20261122'
$files=@(Get-ChildItem (Join-Path $b 'src') -Filter '*.cs')
if($files.Count -ne 47){throw "Incomplete prior source backup ($($files.Count))"}
Remove-Item (Join-Path $r 'src\AnalyticOcclusion.cs') -Force -ErrorAction SilentlyContinue
foreach($f in $files) {
    Copy-Item $f.FullName (Join-Path $r "src\$($f.Name)") -Force
}
Copy-Item (Join-Path $b 'WiresAcoustics.csproj') (Join-Path $r 'src\WiresAcoustics.csproj') -Force
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
if((Get-FileHash $live -Algorithm SHA256).Hash -ne
   (Get-FileHash (Join-Path $b 'installed.dll') -Algorithm SHA256).Hash) {
    throw 'Installed rollback DLL hash mismatch'
}
'Restored the previous installed WA binary and all 47 production .cs files.'
'NOTE: dev\VerifyReleaseCandidate.ps1 must be returned to the 47-source baseline before reusing that gate.'
