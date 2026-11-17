@echo off
rem Chains.cs is new: moved aside, not deleted
if not exist "%~dp0..\..\_removed" mkdir "%~dp0..\..\_removed"
move /y "%~dp0..\..\src\Chains.cs" "%~dp0..\..\_removed\Chains.cs"
