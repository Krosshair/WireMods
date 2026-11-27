@echo off
set S=%~dp0..\..\src
if not exist "%S%\_removed" mkdir "%S%\_removed"
if exist "%S%\FftBloom.cs" move /y "%S%\FftBloom.cs" "%S%\_removed\"
copy /y "%~dp0src\*" "%S%\"
copy /y "%~dp0editor\BuildWireCameraTweaksBundle.cs" "C:\Users\kajet\LightRework\Assets\Editor\"
for %%f in (README.md CHANGELOG.md manifest.json) do copy /y "%~dp0%%f" "%~dp0..\..\package\"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
echo Source restored. WireBloom.compute in LightRework is unused then.
pause
