@echo off
set S=%~dp0..\..\src
if not exist "%~dp0..\..\_removed" mkdir "%~dp0..\..\_removed"
if exist "%S%\DynamicCamera.cs" move /y "%S%\DynamicCamera.cs" "%~dp0..\..\_removed\"
copy /y "%~dp0src\*" "%S%\"
for %%f in (README.md CHANGELOG.md) do copy /y "%~dp0%%f" "%~dp0..\..\package\"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
pause
