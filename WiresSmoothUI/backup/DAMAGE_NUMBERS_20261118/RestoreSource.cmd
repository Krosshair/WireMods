@echo off
for %%f in (WiresSmoothUIPlugin.cs SmoothUIConfigRework.cs) do copy /y "%~dp0%%f" "%~dp0..\..\src\%%f"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
copy /y "%~dp0README.md" "%~dp0..\..\package\README.md"
if exist "%~dp0..\..\src\DamageNumbers.cs" (if not exist "%~dp0..\..\_removed" mkdir "%~dp0..\..\_removed") & move /y "%~dp0..\..\src\DamageNumbers.cs" "%~dp0..\..\_removed\DamageNumbers.cs"
