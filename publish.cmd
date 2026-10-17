@echo off
rem Usage: publish WiresRecycler [-Version 1.0.3] [-DryRun] [-Sites thunderstore,hexium]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Publish.ps1" %*
