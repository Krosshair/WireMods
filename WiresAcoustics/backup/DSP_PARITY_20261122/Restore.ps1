$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback.'}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\DSP_PARITY_20261122'
foreach($name in @('SpectralPaths.cs','Filter.cs','FresnelEdge.cs',
                   'Diffraction.cs','AcousticMath.cs','Budget.cs','AcousticTrace.cs')){
    Copy-Item (Join-Path $b $name) (Join-Path $r ('src\'+$name)) -Force
}
Remove-Item (Join-Path $r 'src\AudioMeterHistory.cs') -Force -ErrorAction SilentlyContinue
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
'Restored exact pre-DSP-parity sources and installed DLLs.'
