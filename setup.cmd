@echo off
rem One-time setup: installs Git + GitHub CLI, logs in, stores upload tokens, creates the GitHub repo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Setup.ps1" %*
pause
