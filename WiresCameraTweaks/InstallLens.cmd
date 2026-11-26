@echo off
rem After "Wire > Build Camera Tweaks bundle" in LightRework: embeds the bundle, builds and installs.
tasklist /fi "imagename eq valheim.exe" | find /i "valheim.exe" >nul && (echo Close Valheim first. & pause & exit /b)
copy /y "%~dp0build\wirecameratweaks" "%~dp0src\wirecameratweaks.bundle" || (pause & exit /b)
cd /d "%~dp0src"
dotnet build WiresCameraOverhaul.csproj -c Release || (pause & exit /b)
set G=%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresCameraTweaks
copy /y "bin\Release\net472\WiresCameraTweaks.dll" "%G%\WiresCameraTweaks.dll.new" && move /y "%G%\WiresCameraTweaks.dll.new" "%G%\WiresCameraTweaks.dll"
copy /y "bin\Release\net472\WiresCameraTweaks.dll" "%~dp0package\WiresCameraTweaks.dll.new" && move /y "%~dp0package\WiresCameraTweaks.dll.new" "%~dp0package\WiresCameraTweaks.dll"
echo Installed.
pause
