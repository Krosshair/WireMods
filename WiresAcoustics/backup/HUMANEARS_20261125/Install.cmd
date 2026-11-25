@echo off
rem Puts the human-ears 0.8.0 into the Gale profile. Close Valheim first.
set G=%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics
copy /y "%~dp0..\..\package\WiresAcoustics.dll" "%G%\WiresAcoustics.dll.new" && move /y "%G%\WiresAcoustics.dll.new" "%G%\WiresAcoustics.dll"
copy /y "%~dp0..\..\package\README.md" "%G%\"
copy /y "%~dp0..\..\package\CHANGELOG.md" "%G%\"
copy /y "%~dp0..\..\package\SADIE_LICENSE.txt" "%G%\"
pause
