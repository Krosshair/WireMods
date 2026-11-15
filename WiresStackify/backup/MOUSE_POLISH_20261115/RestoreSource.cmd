@echo off
set "ROOT=C:\Users\kajet\WireMods"
copy /Y "%~dp0MouseControls.cs" "%ROOT%\WiresStackify\src\MouseControls.cs" || exit /b 1
copy /Y "%~dp0SmoothUIBridge.cs" "%ROOT%\WiresStackify\src\SmoothUIBridge.cs" || exit /b 1
copy /Y "%~dp0ItemAnimationSystem.cs" "%ROOT%\WiresSmoothUI\src\ItemAnimationSystem.cs" || exit /b 1
copy /Y "%~dp0SmoothUIDriver.cs" "%ROOT%\WiresSmoothUI\src\SmoothUIDriver.cs" || exit /b 1
if exist "%ROOT%\WiresStackify\src\HalfPickupGuard.cs" del "%ROOT%\WiresStackify\src\HalfPickupGuard.cs"
if exist "%ROOT%\WiresSmoothUI\src\QuickTransferEffects.cs" del "%ROOT%\WiresSmoothUI\src\QuickTransferEffects.cs"
echo Previous Stackify and Smooth UI source restored; rebuild DLLs separately if needed.
