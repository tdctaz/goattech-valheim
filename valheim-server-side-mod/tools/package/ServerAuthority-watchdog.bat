@echo off
REM ===========================================================================
REM  Server Authority test server, with crash capture.
REM  Put this file in the Valheim dedicated server folder and double click it.
REM
REM  EDIT THE FOUR SETTINGS BELOW, then save.
REM ===========================================================================

set SA_NAME=ServerAuthority Test
set SA_WORLD=SrvAuthTest
set SA_PASSWORD=changeme123
set SA_PORT=2456

REM  0 = not listed publicly, 1 = listed. Keep 0 for a test server.
set SA_PUBLIC=0

REM  Seconds between world saves. Valheim's default is 1800. A crash loses
REM  everything since the last save, so keep this low while testing.
set SA_SAVEINTERVAL=120

REM ===========================================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ServerAuthority-watchdog.ps1"
echo.
echo The watchdog has stopped. Press any key to close.
pause >nul
