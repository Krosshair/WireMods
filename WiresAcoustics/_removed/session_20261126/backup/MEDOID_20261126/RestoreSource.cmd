@echo off
rem Acoustics source back to the raw SADIE II average table (before the medoid). The smoothed 0.8.0 table is in backup\RAWHUMAN_20261126\src.
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
pause
