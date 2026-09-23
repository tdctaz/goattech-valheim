# Valheim Server Tool

Runs the Valheim dedicated server and looks after it:

- Starts and stops the server, and restarts it on request
- Detects crashes, keeps the logs for later analysis and restarts the server
- Restarts the server once a day (05:00 by default) and takes a world backup while it is down
- Warns every player 15, 10, 5, 2 and 1 minutes before the server stops

Works on Windows and Linux. Needs the .NET 10 runtime.

## Server Authority is required for warnings

Vanilla Valheim gives a dedicated server no way to talk to players, so the warnings go through
Server Authority. The tool drops command files into `ServerAuthority-control` next to the server
executable, and the mod picks them up once a second: `say <text>` shows the text in the middle of
every player's screen, and `shutdown` saves the world and quits.

Without the mod, or with an older build, the tool still works, but players get no warnings, and
stopping falls back to an interrupt signal (Ctrl+C on Windows, SIGINT on Linux), which Valheim also
treats as save and quit.

## Building

```
cd valheim-server-tool/src/ServerTool
dotnet publish -c Release -o ../../publish
```

For a Windows server without .NET installed, publish self-contained instead:

```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ../../publish-win
```

## Setting it up

```
ValheimServerTool init
```

writes `servertool.json` in the current folder. Edit it, then:

```
ValheimServerTool run
```

`run` starts the server and keeps watching it until you `quit`. Leave that window open. Commands can
be typed into it, or sent from another terminal:

```
ValheimServerTool status
ValheimServerTool say Boss fight at the swamp in ten minutes
ValheimServerTool restart
ValheimServerTool stop --now
```

Pass `--config <path>` to use another config file. Without it the tool looks for `servertool.json`
in the current folder, then next to the executable.

| Command | Does |
| --- | --- |
| `start` | Start the server. Also resets the crash loop guard. |
| `stop [--now]` | Warn players, stop the server and back up the world. The tool keeps running. |
| `restart [--now]` | Warn players, stop, back up and start again. |
| `quit [--now]` | Like `stop`, then exit the tool. |
| `cancel` | Cancel a scheduled stop or restart, and tell the players. |
| `say <message>` | Show a message to every player. |
| `backup` | Back up the world. While the server runs this is the last world save, not the live state. |
| `status` | Server state, uptime, anything scheduled, the next daily restart and the last crash. |

`--now` skips the countdown. Ctrl+C in the tool's window is `quit --now`, and so is SIGTERM on Linux.

## Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| `InstanceName` | `valheim` | Names the control pipe. Give each server its own when running several on one machine. |
| `ServerDirectory` | Steam default | Folder holding `valheim_server.exe` or `valheim_server.x86_64`. Relative paths in this file are relative to the config file. |
| `Executable` | empty | Empty runs the server binary directly. A script works too, as long as it `exec`s the server so signals reach it. |
| `Server.Name`, `World`, `Password`, `Port`, `Public` | | The usual server arguments. An empty password leaves `-password` out. |
| `Server.SaveInterval` | `1800` | Seconds between world saves. A crash loses everything since the last one. |
| `Server.Backups` | `4` | Valheim's own automatic backups, kept inside the save folder. |
| `Server.ExtraArguments` | `[]` | Anything else, for example `["-crossplay"]` or `["-savedir", "D:/valheim-saves"]`. |
| `Environment` | `{}` | Extra environment variables for the server. |
| `SaveDirectory` | empty | Where Valheim keeps `worlds_local`. Empty follows `-savedir`, then Valheim's default (`%USERPROFILE%\AppData\LocalLow\IronGate\Valheim`, or `$XDG_CONFIG_HOME/unity3d/IronGate/Valheim` on Linux, which honours `XDG_CONFIG_HOME` from `Environment`). |
| `ControlDirectory` | empty | Must match `Control.Directory` in the Server Authority config. Empty means `ServerAuthority-control` next to the server. |
| `BackupDirectory` | `backups` | Everything the tool keeps. See below. |
| `ExtraBackupPaths` | `[]` | More files or folders to put in each world backup, such as `adminlist.txt`. |
| `WorldBackupsToKeep` | `14` | Oldest world backups beyond this are deleted. |
| `ServerLogsToKeep` | `30` | Same for the per-run server logs. Crash folders are never deleted. |
| `BackupOnStop` | `true` | Back up after every clean stop. The daily restart always backs up. |
| `DailyRestartTime` | `05:00` | Local time the countdown ends at. Empty turns the daily restart off. |
| `WarningMinutes` | `[15, 10, 5, 2, 1]` | When players are warned. The countdown is as long as the largest. |
| `WarningMessage` | `Server {action} in {time}` | `{action}` is `restarting` or `shutting down`, `{time}` is `5 minutes` or `1 minute`, `{minutes}` the bare number. |
| `FinalMessage` | `Server {action} now` | Sent as the server stops. |
| `AutoRestartOnCrash` | `true` | |
| `CrashRestartDelaySeconds` | `10` | |
| `ControlAckSeconds` | `10` | How long the mod gets to pick up the shutdown before the tool sends an interrupt. |
| `StopTimeoutSeconds` | `180` | After this the server is killed. Saving a large world can take a while, so be generous. |
| `EchoServerOutput` | `false` | Also print the server's own log in the tool's window. |

On Linux, when `Executable` is empty and `doorstop_libs` exists, the tool sets the doorstop
environment BepInEx needs, the same way `start_server_bepinex.sh` does.

## What ends up in the backup folder

```
backups/
  servertool.log                      what the tool did and when
  logs/server-<time>.log              the server's full output, one file per run
  crashes/<time>-exit<code>/
    summary.txt                       exceptions, fatal signals and the last 40 lines; read first
    server.log                        the whole run
    BepInEx-LogOutput.log             the mods' own log, before the restart overwrites it
    config/                           every BepInEx config as it was
    unity-crash/                      Unity's crash report, when there is one (Windows)
  worlds/<world>-<time>.zip           the world, plus characters_serverauthority
```

Any exit the tool did not ask for counts as a crash. If the server dies within a minute of starting
three times running, the tool stops restarting it, since that is a startup failure such as a bad
world name, a short password or a port in use, and restarting will not fix it. Fix the cause and
use `start`.

## How the daily restart runs

At 04:45 the countdown starts and players are warned at 15, 10, 5, 2 and 1 minutes. At 05:00 the
tool asks the server to save and quit, waits for it to exit, zips the world and starts the server
again. If the server is stopped at that time, or another stop is already counting down, the daily
restart is skipped.

A countdown that starts late, for example because the tool was started at 04:52, only sends the
warnings that are still ahead. A stop requested while a countdown runs keeps the earlier time.

## Running it unattended

Windows: run it from a normal console window, or from Task Scheduler with "Run only when user is
logged on", so it has a console to send Ctrl+C through if the mod is missing. The server shares
that console, so closing the window takes the server down with it, possibly without saving. Use
`quit` instead.

Linux, as a systemd user service:

```
[Unit]
Description=Valheim server
After=network-online.target

[Service]
WorkingDirectory=%h/valheim-server-tool
ExecStart=%h/valheim-server-tool/publish/ValheimServerTool run
KillMode=mixed
TimeoutStopSec=300
Restart=on-failure

[Install]
WantedBy=default.target
```

`KillMode=mixed` sends SIGTERM to the tool only, which then stops the server properly.
`systemctl --user stop valheim` is therefore an immediate save and quit.
