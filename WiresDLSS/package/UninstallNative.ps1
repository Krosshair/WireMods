$ErrorActionPreference='Stop'
# Run only after unloading the mod, or when you intentionally want to remove
# the shared NVIDIA native runtime. Never touch ReShade/DXGI or RenoDX.
$game='D:\SteamLibrary\steamapps\common\Valheim'
$archive=Join-Path $env:LOCALAPPDATA 'WiresDLSS-Uninstalled'
$files=@(
 @{Name='NVUnityPlugin.dll';Hash='760F0574F460DE8C22C801F2A64EB4693EA6361F10406356F1837AC31DE64B42'},
 @{Name='nvngx_dlss.dll';Hash='D00B5B0AB29C3CA101B70C3A5ECB3A01B7EF33766A3383A28A18623083E6123E'}
)
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before uninstalling'}
foreach($f in $files){
 $path=Join-Path $game $f.Name
 if(!(Test-Path $path)){throw "Missing native library: $($f.Name)"}
 if((Get-FileHash $path -Algorithm SHA256).Hash -ne $f.Hash){
  throw "Refusing to remove changed/shared NVIDIA DLL: $($f.Name)"
 }
}
if(Test-Path $archive){throw "Archive already exists: $archive"}
New-Item -ItemType Directory -Force $archive|Out-Null
foreach($f in $files){
 Move-Item (Join-Path $game $f.Name) (Join-Path $archive $f.Name) -ErrorAction Stop
}
'Moved only the two known NVIDIA DLLs into '+$archive
'RenoDX, ReShade and dxgi.dll were untouched.'
