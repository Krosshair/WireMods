$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics'
$bk=Join-Path $root 'backup\SMELTER_BOUNDS_20261116'
$src=Join-Path $root 'src'
$pkg=Join-Path $root 'package'
$inst='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics'
if (Get-Process valheim -ErrorAction SilentlyContinue) {
  throw 'Close Valheim before restoring 0.5.11'
}
foreach ($name in @('Spots.cs','Area.cs','Filter.cs','Occlusion.cs','Batch.cs','Plugin.cs','Follow.cs')) {
  Copy-Item (Join-Path $bk $name) (Join-Path $src $name) -Force
}
$file = Join-Path $src 'InteractionOrigin.cs'
if (Test-Path $file) { Remove-Item $file -Force }
foreach ($name in @('README.md','CHANGELOG.md','manifest.json')) {
  Copy-Item (Join-Path $bk ($name+'.before')) (Join-Path $pkg $name) -Force
}
Copy-Item (Join-Path $bk 'WiresAcoustics.package.before.dll') (Join-Path $pkg 'WiresAcoustics.dll') -Force
Copy-Item (Join-Path $bk 'WiresAcoustics.installed.before.dll') (Join-Path $inst 'WiresAcoustics.dll') -Force
Copy-Item (Join-Path $bk 'README.installed.before.md') (Join-Path $inst 'README.md') -Force
Copy-Item (Join-Path $bk 'CHANGELOG.installed.before.md') (Join-Path $inst 'CHANGELOG.md') -Force
Copy-Item (Join-Path $bk 'manifest.installed.before.json') (Join-Path $inst 'manifest.json') -Force
dotnet build (Join-Path $src 'WiresAcoustics.csproj') -c Release --no-restore -v:q
if ($LASTEXITCODE -ne 0) { throw '0.5.11 source build failed' }
'Restored Wire''s Acoustics 0.5.11 sources, package and Enngard installation.'
