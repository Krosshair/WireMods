@echo off
for %%f in (Batch.cs Area.cs Follow.cs) do copy /Y "%~dp0%%f" "%~dp0..\..\src\%%f"
