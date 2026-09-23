$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$staging = Join-Path $env:TEMP "ServerAuthority-report-$stamp"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

function Grab($path, $intoName) {
    if (Test-Path $path) {
        $dest = Join-Path $staging $intoName
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        Copy-Item $path $dest -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  collected $intoName"
    }
}

Write-Host "Collecting..."
Grab (Join-Path $here 'ServerAuthority-logs')                          'ServerAuthority-logs'
Grab (Join-Path $here 'BepInEx\LogOutput.log')                         'BepInEx-LogOutput.log'
Grab (Join-Path $here 'BepInEx\config\valheim.server_authority.cfg')   'valheim.server_authority.cfg'
Grab (Join-Path $here 'BepInEx\config\valheim.creatures.cfg')          'valheim.creatures.cfg'
Grab (Join-Path $here 'BepInEx\config\valheim.rebalanced.cfg')         'valheim.rebalanced.cfg'
Grab (Join-Path $here 'GoatTech-VERSION.txt')                          'GoatTech-VERSION.txt'
Grab (Join-Path $here 'BepInEx\config\BepInEx.cfg')                    'BepInEx.cfg'
Grab (Join-Path $here 'ServerAuthority-watchdog.bat')                  'ServerAuthority-watchdog.bat'

# Which build is actually installed, and which game version it is running against.
$info = Join-Path $staging 'environment.txt'
"Collected $(Get-Date)"                                        | Set-Content $info
"Windows: $([Environment]::OSVersion.VersionString)"           | Add-Content $info
"Machine RAM (GB): $([math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1))" | Add-Content $info
"CPU: $((Get-CimInstance Win32_Processor).Name)"               | Add-Content $info
$dll = Join-Path $here 'BepInEx\plugins\ServerAuthority.dll'
if (Test-Path $dll) {
    "ServerAuthority.dll size : $((Get-Item $dll).Length) bytes"       | Add-Content $info
    "ServerAuthority.dll date : $((Get-Item $dll).LastWriteTimeUtc) UTC" | Add-Content $info
}
$server = Join-Path $here 'valheim_server.exe'
if (Test-Path $server) {
    "valheim_server.exe date  : $((Get-Item $server).LastWriteTimeUtc) UTC" | Add-Content $info
}
"Plugins installed:"                                           | Add-Content $info
Get-ChildItem (Join-Path $here 'BepInEx\plugins') -Filter *.dll -ErrorAction SilentlyContinue |
    ForEach-Object { "  $($_.Name)" } | Add-Content $info

$desktop = [Environment]::GetFolderPath('Desktop')
$zip = Join-Path $desktop "ServerAuthority-report-$stamp.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item $staging -Recurse -Force

Write-Host ""
Write-Host "Report written to:" -ForegroundColor Green
Write-Host "  $zip" -ForegroundColor Green
Write-Host ""
Write-Host "Send that file back. It contains logs and config only, no world save."
