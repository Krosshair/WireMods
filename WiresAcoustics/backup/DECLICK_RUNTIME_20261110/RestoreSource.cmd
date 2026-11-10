@echo off
copy /Y "%~dp0Travel.cs" "%~dp0..\..\src\Travel.cs"
copy /Y "%~dp0WiresAcoustics.csproj.before" "%~dp0..\..\src\WiresAcoustics.csproj"
move /Y "%~dp0..\..\src\Declick.cs" "%~dp0Declick.removed.cs"
move /Y "%~dp0..\..\src\DeclickMath.cs" "%~dp0DeclickMath.removed.cs"
