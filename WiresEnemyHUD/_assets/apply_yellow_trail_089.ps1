$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresEnemyHUD'
$package=Join-Path $root 'package'
$src=Join-Path $root 'src'
$compiled=Join-Path $src 'bin\Release\net472\EnemyHudRework.dll'
$backup=Join-Path $root '_assets\before_yellow_trail_fix_20261026'
$installed=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Default\BepInEx\plugins\Wire-WiresEnemyHUD'
$zip='C:\Users\kajet\Downloads\WiresEnemyHUD-0.8.9-YellowDamageTrail-Fix.zip'
$stage=Join-Path $root '_assets\staging_yellow_trail_089'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'VALHEIM_RUNNING'}
foreach($f in @($compiled,"$package\README.md","$package\CHANGELOG.md","$package\manifest.json","$package\icon.png","$backup\installed\EnemyHudRework.dll","$backup\package\README.md","$backup\package\manifest.json")){
 if(!(Test-Path -LiteralPath $f)){throw "MISSING_FILE: $f"}
}
if(Test-Path $zip){throw 'RELEASE_ZIP_EXISTS'}
if(Test-Path $stage){throw 'STAGING_DIRECTORY_EXISTS'}
$before=Get-Content "$backup\package\manifest.json" -Raw |ConvertFrom-Json
$present=Get-Content "$package\manifest.json" -Raw |ConvertFrom-Json
$installedManifest=Get-Content "$installed\manifest.json" -Raw |ConvertFrom-Json
if($present.version_number -ne '0.8.9' -or $installedManifest.version_number -ne '0.8.9'){throw 'UNEXPECTED_VERSION'}
if($present.description -cne $before.description){throw 'MANIFEST_DESCRIPTION_CHANGED'}
if((Get-FileHash "$package\README.md").Hash -ne (Get-FileHash "$backup\package\README.md").Hash){throw 'README_CHANGED'}
if((Get-FileHash "$installed\EnemyHudRework.dll").Hash -ne (Get-FileHash "$backup\installed\EnemyHudRework.dll").Hash){throw 'INSTALLED_MOD_CHANGED_AFTER_BACKUP'}
if(!(Test-Path "$src\EnemyHudDamageTrail.cs")){throw 'MISSING_PRODUCTION_TRAIL_PATCH'}
if(!(Select-String -Path "$src\EnemyHudPlayerFocus.cs" -SimpleMatch -Pattern 'ForceShowOnDamage(state.Character)')){throw 'REVEAL_CONDITION_NOT_UPDATED'}
dotnet build "$src\EnemyHudRework.csproj" -c Release --no-restore --nologo -v:q
if($LASTEXITCODE -ne 0){throw 'BUILD_FAILED'}
dotnet run --project "$root\_assets\trail_089_tests\Tests.csproj" -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'REGRESSION_TESTS_FAILED'}
Copy-Item -LiteralPath $compiled -Destination "$package\EnemyHudRework.dll" -Force
New-Item -ItemType Directory -Path $stage |Out-Null
$names=@('EnemyHudRework.dll','README.md','CHANGELOG.md','manifest.json','icon.png')
foreach($name in $names){Copy-Item "$package\$name" "$stage\$name" -Force}
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 if($archive.Entries.Count -ne 5){throw 'INVALID_PACKAGE_ENTRY_COUNT'}
 foreach($name in $names){if(!$archive.GetEntry($name)){throw "PACKAGE_MISSING $name"}}
}finally{$archive.Dispose()}
Copy-Item $compiled "$installed\EnemyHudRework.dll" -Force
Copy-Item "$package\CHANGELOG.md" "$installed\CHANGELOG.md" -Force
$hash=(Get-FileHash $compiled).Hash
foreach($f in @("$installed\EnemyHudRework.dll","$package\EnemyHudRework.dll")){
 if((Get-FileHash $f).Hash -ne $hash){throw "DLL_MISMATCH: $f"}
}
if((Get-FileHash "$installed\README.md").Hash -ne (Get-FileHash "$backup\installed\README.md").Hash){throw 'INSTALLED_README_CHANGED'}
'INSTALLED_WIRES_ENEMY_HUD_0.8.9_YELLOW_TRAIL_FIX'
'DLL_SHA256='+$hash
'ZIP='+$zip
'ZIP_BYTES='+(Get-Item $zip).Length
'README_UNTOUCHED'
'MANIFEST_UNTOUCHED'
'CONFIG_UNTOUCHED'
'NOT_PUBLISHED'
