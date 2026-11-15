@echo off
set "R=C:\Users\kajet\WireMods"
copy /Y "%~dp0MouseControls.cs" "%R%\WiresStackify\src\MouseControls.cs" || exit /b 1
copy /Y "%~dp0SmoothUIBridge.cs" "%R%\WiresStackify\src\SmoothUIBridge.cs" || exit /b 1
copy /Y "%~dp0HalfPickupGuard.cs" "%R%\WiresStackify\src\HalfPickupGuard.cs" || exit /b 1
copy /Y "%~dp0ItemAnimationSystem.cs" "%R%\WiresSmoothUI\src\ItemAnimationSystem.cs" || exit /b 1
copy /Y "%~dp0SmoothUIDriver.cs" "%R%\WiresSmoothUI\src\SmoothUIDriver.cs" || exit /b 1
copy /Y "%~dp0QuickTransferEffects.cs" "%R%\WiresSmoothUI\src\QuickTransferEffects.cs" || exit /b 1
if exist "%R%\WiresStackify\src\MouseDragGuard.cs" del "%R%\WiresStackify\src\MouseDragGuard.cs"
echo Previous source restored; rebuild DLLs separately if needed.
