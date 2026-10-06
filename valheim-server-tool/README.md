# Valheim Server Tool

Runs the Valheim dedicated server on Windows and Linux and looks after it:

- Starts, stops and restarts the server, from its own window or another terminal
- Detects crashes, keeps the logs and configs from each one, and restarts the server
- Restarts once a day (05:00 by default), backs up the world and updates Valheim with SteamCMD
  while the server is down
- Warns every player 15, 10, 5, 2 and 1 minutes before the server stops

Needs the .NET 10 runtime.

## Warnings need Server Authority

A vanilla dedicated server has no way to message players, so the tool goes through Server
Authority. It drops command files into `ServerAuthority-control` next to the server executable
(the mod's `Control.Directory`), and the mod reads them once a second: `say <text>` shows the text
on every player's screen, and `shutdown` saves the world and quits.

Without the mod players get no warnings. If the mod has not taken a `shutdown` within
`ControlAckSeconds` (10 by default), the tool sends an interrupt instead (Ctrl+C on Windows, SIGINT
on Linux), which Valheim also treats as save and quit.

## Setting it up

1. Put the tool in a folder inside the server folder, for example `Valheim dedicated server/servertool`.
2. Run `ValheimServerTool run`. The first run writes `servertool.cfg` next to the tool and stops.
3. Set at least `Name`, `World` and `Password` under `[Server]`. Every setting is described in the
   file, with its default and the values it accepts.
4. Run `ValheimServerTool run` again. It checks the file, starts the server and keeps it running
   until you `quit`. Leave that window open.

A mistake in the file stops the tool before anything starts, naming the line. The tool rewrites the
file at startup, keeping your values and refreshing the notes, so new settings appear by themselves
after an update. To apply a changed setting, `quit` and `run` again.

`--config <path>` picks another config file. Without it the tool uses `servertool.cfg` in the
current folder if there is one, otherwise the one next to the executable.

## Commands

Typed into the tool's window, or run from another terminal, for example
`ValheimServerTool say Boss fight at the swamp in ten minutes`.

| Command | Does |
| --- | --- |
| `start` | Start the server. Also resets the crash loop guard. |
| `stop [--now]` | Warn players, stop the server and back up the world. The tool keeps running. |
| `restart [--now]` | Warn players, stop, back up and start again. |
| `update [--now]` | Like `restart`, and update the server with SteamCMD while it is down. When the server is already stopped, it updates and leaves it stopped. |
| `quit [--now]` | Like `stop`, then exit the tool. |
| `cancel` | Cancel a scheduled stop or restart, and tell the players. |
| `say <message>` | Show a message to every player. |
| `backup` | Back up the world. While the server runs this is the last world save, not the live state. |
| `status` | State, uptime, anything scheduled, the next daily restart and the last crash. |

`--now` skips the warnings. Ctrl+C in the tool's window, and SIGTERM on Linux, is `quit --now`.
Closing the window can take the server down without saving, so use `quit`.

## Configuration

| Section | Settings |
| --- | --- |
| `[Server]` | Everything `valheim_server` accepts: name, world, password, port, public, crossplay, instance id, save interval, Valheim's own backups, save folder, extra arguments. |
| `[World Modifiers]` | The world creation difficulty settings: preset, combat, death penalty, resources, raids, portals, and the no build cost, player based raids, passive enemies, no map and fire hazard checkboxes. |
| `[Schedule]` | Daily restart time, warning times and the text of every message. |
| `[Update]` | Whether the daily restart updates, where SteamCMD is, validation and a timeout. |
| `[Crashes]` | Automatic restart after a crash, and the delay. |
| `[Backups]` | Where backups, logs and crash reports go, and how many are kept. |
| `[Tool]` | The server folder if it is not found by itself, timeouts and environment. |

- **The seed is the world name.** The tool passes the world name as `-seed`, which Server Authority
  applies when it creates a new world. An existing world keeps its seed.
- **World modifiers are stored in the world.** With `ResetModifiers = false` (the default) the
  section adds to what the world already has, so a modifier removed from the file stays. With
  `true` they are cleared on every start and the file decides alone.
- **Password.** On a public server Valheim requires at least 5 characters, not contained in the
  world name. The tool checks this before starting rather than letting the server quit.

## Backups and logs

```
backups/
  servertool.log                      what the tool did and when
  logs/server-<time>.log              the server's output, one file per run
  logs/bepinex-<time>.log             the mods' log for the same run, copied when the server stops
  logs/steamcmd-<time>.log            SteamCMD's output from each update
  crashes/<time>-exit<code>/
    summary.txt                       exceptions, fatal signals and the last 40 lines; read first
    server.log                        the whole run
    BepInEx-LogOutput.log             the mods' log, before the restart overwrites it
    config/                           every BepInEx config as it was
    unity-crash/                      Unity's crash report, when there is one
  worlds/<world>-<time>.zip           the world, plus characters_serverauthority
```

BepInEx empties `BepInEx/LogOutput.log` every time the server starts, so the tool copies it whenever
the server stops.

Any exit the tool did not ask for is a crash. If the server dies within a minute of starting three
times running, the tool stops restarting it: that is a startup fault such as a bad world name, a
short password or a port in use. Fix it and use `start`.

## Daily restart and updates

At 04:45 the countdown starts and players are warned at 15, 10, 5, 2 and 1 minutes. At 05:00 the
tool asks the server to save and quit, zips the world, updates with SteamCMD and starts it again. If
the server is not running when the countdown would begin, or another stop is already under way, that
day's restart is skipped. A countdown that starts late first tells players the time actually left.

Players' games update through Steam, and Server Authority refuses a client whose game version differs
from the server's, so the server has to follow a Valheim patch. The update runs:

```
steamcmd +force_install_dir <server folder> +login anonymous +app_update 896660 +quit
```

SteamCMD only replaces the game's own files; BepInEx, the mods, `servertool` and the worlds are left
alone. The tool looks for `steamcmd` on the PATH and in the usual install folders (`C:\steamcmd` and
`%USERPROFILE%\steamcmd` on Windows; `~/steamcmd`, `~/.steam/steamcmd`, `/usr/games` and
`/usr/lib/games/steam` on Linux), or set `SteamCmdPath`. The tool log records the build change, for
example `Server updated from build 25390671 to build 25412003.` If SteamCMD is missing, fails or
runs past `UpdateTimeoutMinutes`, the error is logged and the server starts on the version it has.

After a Valheim patch the mods may need updating too. Server Authority logs a warning at startup when
the game version differs from the one it was verified against.

## Running it unattended

Windows: run it from a console window, or from Task Scheduler with "Run only when user is logged
on", so it has a console to send Ctrl+C through if the mod is missing. The server shares that
console.

Linux, as a systemd user service in `~/.config/systemd/user/valheim.service`. Set `ServerDirectory`
under `[Tool]` when the config does not sit in or next to the server folder:

```
[Unit]
Description=Valheim server
After=network-online.target

[Service]
WorkingDirectory=%h/valheim-server-tool
ExecStart=%h/valheim-server-tool/publish/ValheimServerTool --config %h/valheim-server-tool/servertool.cfg run
KillMode=mixed
TimeoutStopSec=300
Restart=on-failure

[Install]
WantedBy=default.target
```

`KillMode=mixed` sends SIGTERM to the tool only, which then stops the server properly, so
`systemctl --user stop valheim` is an immediate save and quit.

## Building

```
cd valheim-server-tool/src/ServerTool
dotnet publish -c Release -o ../../publish
```

The version comes from the repository's `VERSION` file. `release/release.sh` builds the tool for
Windows and Linux into the server zip's `servertool` folder. For a Windows server without .NET,
publish self-contained:

```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:DebugType=none -o ../../publish-win
```
