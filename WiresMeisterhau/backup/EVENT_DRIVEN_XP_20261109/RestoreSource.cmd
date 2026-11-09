@echo off
set "SRC=C:\Users\kajet\WireMods\WiresMeisterhau\src\WiresMeisterhauPlugin.cs"
copy /Y "%~dp0WiresMeisterhauPlugin.cs" "%SRC%" || exit /b 1
echo Previous Wire's Meisterhau source restored; rebuild to update binary.
