@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
if exist "%~dp0..\..\src\ShadowCache.Tiled.cs" move /y "%~dp0..\..\src\ShadowCache.Tiled.cs" "%~dp0ShadowCache.Tiled.removed.cs"
copy /y "%~dp0shaders\WireToonDeferredShading.shader" "C:\Users\kajet\LightRework\Assets\Shaders\"
copy /y "%~dp0shaders\BuildWireLightBundle.cs" "C:\Users\kajet\LightRework\Assets\Editor\"
echo Restored source. Rebuild the bundle in Unity.
pause
