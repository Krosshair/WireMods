@echo off
for %%f in (Filter Travel VanillaShortAmbient) do copy /Y "%~dp0%%f.cs" "%~dp0..\..\src\%%f.cs"
