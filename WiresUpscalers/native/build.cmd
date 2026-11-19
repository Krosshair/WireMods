@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
cd /d "%~dp0"
set SDK=..\sdk
cl /nologo /O2 /LD /EHsc /MT /std:c++17 /W3 /DNDEBUG ^
  /I "%SDK%\DLSS\include" /I "%SDK%\FidelityFX-SDK\Kits\FidelityFX\api\include" /I "%SDK%\FidelityFX-SDK\Kits\FidelityFX\upscalers\include" /I "%SDK%\xess\inc" ^
  WiresUpscalers.cpp /Fe:WiresUpscalersNative.dll ^
  /link /NOLOGO "%SDK%\DLSS\lib\Windows_x86_64\x64\nvsdk_ngx_s.lib" d3d11.lib d3d12.lib dxgi.lib advapi32.lib user32.lib
