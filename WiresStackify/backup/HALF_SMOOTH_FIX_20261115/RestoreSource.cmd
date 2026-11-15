@echo off
copy /Y "%~dp0MouseControls.cs" "C:\Users\kajet\WireMods\WiresStackify\src\MouseControls.cs" || exit /b 1
copy /Y "%~dp0SmoothUIBridge.cs" "C:\Users\kajet\WireMods\WiresStackify\src\SmoothUIBridge.cs" || exit /b 1
copy /Y "C:\Users\kajet\WireMods\WiresSmoothUI\backup\STACKIFY_FAST_TRANSFER_20261115\ItemAnimationSystem.cs" "C:\Users\kajet\WireMods\WiresSmoothUI\src\ItemAnimationSystem.cs" || exit /b 1
echo Previous source restored; rebuild DLLs separately if needed.
