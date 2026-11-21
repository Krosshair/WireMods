@echo off
setlocal
set "ROOT=C:\Users\kajet\WireMods\WiresNativeCursorSupport"
copy /Y "%ROOT%\backup\CURSOR_INPUT_RESTORE_20261121\WindowsNativeCursor.cs" "%ROOT%\src\WindowsNativeCursor.cs" >nul || exit /b 1
echo Previous source restored.
