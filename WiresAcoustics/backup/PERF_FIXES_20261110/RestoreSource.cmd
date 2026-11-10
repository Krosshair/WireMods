@echo off
for %%f in (Room.cs Cost.cs) do copy /Y "%~dp0%%f" "%~dp0..\..\src\%%f"
