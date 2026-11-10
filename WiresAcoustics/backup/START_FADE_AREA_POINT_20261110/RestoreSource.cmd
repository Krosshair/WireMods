@echo off
for %%f in (Filter Travel Area Directions) do copy /Y "%~dp0%%f.cs" "%~dp0..\..\src\%%f.cs"
