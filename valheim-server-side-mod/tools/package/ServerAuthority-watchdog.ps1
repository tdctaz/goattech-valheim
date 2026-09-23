param(
    [string]$Name,
    [string]$World,
    [string]$Password,
    [string]$Port,
    [string]$Public,
    [string]$SaveInterval
)

# Runs the Valheim dedicated server and keeps the evidence when it dies.
#
# This exists because a Mono abort terminates the process instead of raising an
# exception. When that happens the log simply stops, with no error written, so a
# crash is indistinguishable from someone closing the window unless something is
# watching the exit code. That is what this does.

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

$exe = Join-Path $here 'valheim_server.exe'
if (-not (Test-Path $exe)) {
    Write-Host ""
    Write-Host "ERROR: valheim_server.exe is not in this folder." -ForegroundColor Red
    Write-Host "  This folder: $here"
    Write-Host ""
    Write-Host "Copy everything from server-files into the folder that contains"
    Write-Host "valheim_server.exe, then run it from there."
    Read-Host "Press Enter to close"
    exit 1
}

$logDir = Join-Path $here 'ServerAuthority-logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Setting([string]$name, [string]$fallback) {
    $v = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($v)) { return $fallback }
    return $v
}

# A parameter wins, then the environment variable the .bat sets, then a default.
# That way this runs either from the .bat or straight from a PowerShell prompt:
#   .\ServerAuthority-watchdog.ps1 -World MyWorld -Password secret123
function Pick([string]$given, [string]$envName, [string]$fallback) {
    if (-not [string]::IsNullOrWhiteSpace($given)) { return $given }
    return Setting $envName $fallback
}

$name      = Pick $Name         'SA_NAME'         'ServerAuthority Test'
$world     = Pick $World        'SA_WORLD'        'SrvAuthTest'
$password  = Pick $Password     'SA_PASSWORD'     'changeme123'
$port      = Pick $Port         'SA_PORT'         '2456'
$public    = Pick $Public       'SA_PUBLIC'       '0'
$saveEvery = Pick $SaveInterval 'SA_SAVEINTERVAL' '120'

$env:SteamAppId = '892970'

# Built as an array so PowerShell does the quoting. Building a single command
# string and handing it to cmd.exe is what broke the first version of this.
$serverArgs = @(
    '-name',         $name,
    '-port',         $port,
    '-world',        $world,
    '-password',     $password,
    '-public',       $public,
    '-saveinterval', $saveEvery,
    '-backups',      '8'
)

Write-Host ""
Write-Host "Server Authority watchdog" -ForegroundColor Cyan
Write-Host "  world : $world"
Write-Host "  port  : $port"
Write-Host "  saves : every $saveEvery seconds"
Write-Host "  logs  : $logDir"
Write-Host ""
Write-Host "Leave this window open. Closing it stops the server."
Write-Host "Press Ctrl+C in this window to stop properly."
Write-Host ""

$ErrorActionPreference = 'Continue'
$run = 0
$fastFailures = 0

while ($true) {
    $run++
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $log = Join-Path $logDir "server-$stamp.log"
    $started = Get-Date

    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] starting server (run $run) -> $(Split-Path -Leaf $log)" -ForegroundColor Cyan

    # 2>&1 merges the server's error output in; "$_" forces each record to a
    # plain string so the log stays readable; Tee writes it as it arrives, so
    # the log survives even when the process is killed outright.
    & $exe @serverArgs 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
    $ranFor = [int]((Get-Date) - $started).TotalSeconds

    if ($code -eq 0) {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] server exited cleanly after ${ranFor}s. Stopping." -ForegroundColor Green
        break
    }

    Write-Host ""
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] SERVER DIED, exit code $code, after ${ranFor}s" -ForegroundColor Red

    if (Test-Path $log) {
        $crash = Join-Path $logDir "CRASH-$stamp-exit$code.log"
        Copy-Item $log $crash -Force -ErrorAction SilentlyContinue

        $summary = Join-Path $logDir "CRASH-$stamp-summary.txt"
        "=== Server Authority crash, exit code $code, ran ${ranFor}s, $(Get-Date) ===" | Set-Content $summary
        ""                                                                            | Add-Content $summary
        "Interesting lines from this run:"                                            | Add-Content $summary
        Select-String -Path $log -Pattern 'Exception|Assertion|Caught fatal signal|stack frames|Server Authority' -ErrorAction SilentlyContinue |
            Select-Object -Last 60 |
            ForEach-Object { $_.Line } |
            Add-Content $summary

        Write-Host "                   evidence kept: $(Split-Path -Leaf $crash)" -ForegroundColor Red
    }
    else {
        Write-Host "                   no log was produced, so the server did not start at all." -ForegroundColor Yellow
    }

    $bepLog = Join-Path $here 'BepInEx\LogOutput.log'
    if (Test-Path $bepLog) {
        Copy-Item $bepLog (Join-Path $logDir "CRASH-$stamp-BepInEx.log") -Force -ErrorAction SilentlyContinue
    }

    if ($ranFor -lt 15) {
        $fastFailures++
        if ($fastFailures -ge 3) {
            Write-Host ""
            Write-Host "The server has died immediately three times in a row." -ForegroundColor Yellow
            Write-Host "It is not crashing during play, it is failing to start. Usual causes:" -ForegroundColor Yellow
            Write-Host "  - the world name or password in the .bat file is not valid" -ForegroundColor Yellow
            Write-Host "    (the password must be at least 5 characters and cannot be in the server name)" -ForegroundColor Yellow
            Write-Host "  - port $port is already in use by another server" -ForegroundColor Yellow
            Write-Host "  - these files are in the wrong folder" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Check the newest log in ServerAuthority-logs, then run this again." -ForegroundColor Yellow
            Read-Host "Press Enter to close"
            exit 1
        }
    }
    else {
        $fastFailures = 0
        Write-Host "                   run ServerAuthority-collect-logs.bat before reporting it." -ForegroundColor Yellow
    }

    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] restarting in 5 seconds..."
    Start-Sleep -Seconds 5
}
