copy /y "%~dp0Room.cs" "%~dp0..\..\src\Room.cs"
copy /y "%~dp0Portals.cs" "%~dp0..\..\src\Portals.cs"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
del "%~dp0..\..\src\Listener.cs"
