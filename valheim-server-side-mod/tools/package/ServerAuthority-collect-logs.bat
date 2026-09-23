@echo off
REM Gathers every log, config and crash file into one zip on your Desktop,
REM ready to send back for diagnosis. Safe to run while the server is running.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ServerAuthority-collect-logs.ps1"
pause
