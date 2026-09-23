@echo off
REM Shows the live server log in this window. Read only, safe to close any time.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$d = Join-Path $PSScriptRoot 'ServerAuthority-logs';" ^
  "$f = Get-ChildItem $d -Filter 'server-*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1;" ^
  "if (-not $f) { Write-Host 'No log yet. Start the server first.'; Read-Host 'Enter to close'; exit }" ^
  "Write-Host ('Tailing ' + $f.Name); Get-Content $f.FullName -Wait -Tail 40"
