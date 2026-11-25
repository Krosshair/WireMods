@echo off
rem Wire's AI source back to before the body sizes. Files the update added are moved aside, not deleted.
if not exist "%~dp0..\..\_removed\RAGDOLL_20261125" mkdir "%~dp0..\..\_removed\RAGDOLL_20261125"
for %%f in (RagdollSize) do if exist "%~dp0..\..\src\%%f.cs" move /y "%~dp0..\..\src\%%f.cs" "%~dp0..\..\_removed\RAGDOLL_20261125\"
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
pause
