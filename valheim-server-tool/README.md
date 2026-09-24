# Valheim Server Tool

Runs the Valheim dedicated server and looks after it:

- Starts and stops the server, and restarts it on request
- Detects crashes, keeps the logs for later analysis and restarts the server
- Restarts the server once a day (05:00 by default), backs up the world and updates the server with
  SteamCMD while it is down
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

The version comes from the `VERSION` file at the top of the repository, shared with the mods. The
release script at `release/release.sh` builds the tool for both Windows and Linux and puts it in the
server zip's `servertool` folder.

For a Windows server without .NET installed, publish self-contained instead:

```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ../../publish-win
```

## Setting it up

1. Put the tool in a folder next to the server, for example a `servertool` folder inside the
   `Valheim dedicated server` folder.
2. Run `ValheimServerTool run`. The first run writes `servertool.cfg` next to the tool and stops.
3. Open `servertool.cfg`, set at least `Name`, `World` and `Password` under `[Server]`, and change
   anything else you want. Every setting is explained in the file, with its default and the values
   it accepts.
4. Run `ValheimServerTool run` again. It checks the file, starts the server and keeps it running.

To change a setting later, edit the file and restart the tool with `quit`, then `run`. A mistake in
the file stops the tool before anything starts, naming the line and the values that are allowed.
The tool rewrites the file when it starts, keeping your values and refreshing the notes, so new
settings show up by themselves after an update.

`run` keeps running until you `quit`. Leave that window open. Commands can be typed into it, or
sent from another terminal:

```
ValheimServerTool status
ValheimServerTool say Boss fight at the swamp in ten minutes
ValheimServerTool restart
ValheimServerTool stop --now
```

Pass `--config <path>` to use another config file. Without it the tool uses `servertool.cfg` in the
current folder if there is one, otherwise the one next to the executable.

| Command | Does |
| --- | --- |
| `start` | Start the server. Also resets the crash loop guard. |
| `stop [--now]` | Warn players, stop the server and back up the world. The tool keeps running. |
| `restart [--now]` | Warn players, stop, back up and start again. |
| `update [--now]` | Like `restart`, and update the server with SteamCMD while it is down. Use it after a Valheim patch. When the server is already stopped, it updates and leaves it stopped. |
| `quit [--now]` | Like `stop`, then exit the tool. |
| `cancel` | Cancel a scheduled stop or restart, and tell the players. |
| `say <message>` | Show a message to every player. |
| `backup` | Back up the world. While the server runs this is the last world save, not the live state. |
| `status` | Server state, uptime, anything scheduled, the next daily restart and the last crash. |

`--now` skips the countdown. Ctrl+C in the tool's window is `quit --now`, and so is SIGTERM on Linux.

## What the config covers

| Section | Settings |
| --- | --- |
| `[Server]` | Everything `valheim_server` accepts: name, world, password, port, public, crossplay, instance id, save interval, Valheim's own backup count and intervals, save folder, and extra arguments. |
| `[World Modifiers]` | The difficulty settings from the world creation screen: preset, combat, death penalty, resources, raids, portals, and the no build cost, player based raids, passive enemies, no map and fire hazards checkboxes. The descriptions are the game's own. |
| `[Schedule]` | Daily restart time, when players are warned, and the text of every message. |
| `[Update]` | Whether the daily restart updates the server, where SteamCMD is, validation and a timeout. |
| `[Crashes]` | Automatic restart after a crash, and the delay. |
| `[Backups]` | Where backups, logs and crash reports go, and how many are kept. |
| `[Tool]` | Where the server is, if it cannot be found by itself, plus timeouts and environment. |

Two settings behave differently from the rest, because of how Valheim stores worlds:

- **The seed is the world name.** A new world gets its world name as its seed, so the same name
  always gives the same map. An existing world keeps the seed it has. The dedicated server has no
  seed option of its own, so Server Authority hands the seed to the game when it creates the world.
- **World modifiers** are stored in the world. With `ResetModifiers = false` (the default) the
  section adds to whatever the world already has, so a modifier removed from the file stays in the
  world. Set `ResetModifiers = true` to clear them on every start and make the file the only place
  they are decided.

Valheim's password rule applies only to public servers: at least 5 characters, and not part of the
world name. The tool checks it before starting instead of letting the server quit.

## What ends up in the backup folder

```
backups/
  servertool.log                      what the tool did and when
  logs/server-<time>.log              the server's full output, one file per run
  logs/steamcmd-<time>.log            SteamCMD's output from each update
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
tool asks the server to save and quit, waits for it to exit, zips the world, updates the server with
SteamCMD and starts it again. If the server is stopped at that time, or another stop is already
counting down, the daily restart is skipped.

A countdown that starts late, for example because the tool was started at 04:52, first tells
players the time actually left ("8 minutes"), then carries on with the warnings still ahead.

## Updating with SteamCMD

Players' games update themselves through Steam, and Server Authority turns away a client whose game
version differs from the server's. So the server has to follow a Valheim patch quickly, and the
daily restart does that by running:

```
steamcmd +force_install_dir <server folder> +login anonymous +app_update 896660 +quit
```

The server folder is wherever the tool found `valheim_server`, so a server installed to a custom
location with SteamCMD works as long as the tool sits in a `servertool` folder inside it. SteamCMD
only replaces Steam's own files; BepInEx, the mods, `servertool` and the worlds are left alone.

The tool looks for `steamcmd` on the PATH, then in `C:\steamcmd` on Windows or `~/steamcmd`,
`~/.steam/steamcmd` and `/usr/games` on Linux. Set `SteamCmdPath` if it is elsewhere. Its output
goes to `backups/logs/steamcmd-<time>.log`, and the tool log says whether the build changed:

```
[05:00:09] Server updated from build 25390671 to build 25412003.
```

If SteamCMD is missing, fails or runs past `UpdateTimeoutMinutes`, the error is logged and the
server starts on the version it has, so a failed update never keeps the server down.

After a Valheim patch the mods may need updating too. Server Authority logs a warning at startup
when the game version differs from the one it was verified against.

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
ExecStart=%h/valheim-server-tool/publish/ValheimServerTool --config %h/valheim-server-tool/servertool.cfg run
KillMode=mixed
TimeoutStopSec=300
Restart=on-failure

[Install]
WantedBy=default.target
```

`KillMode=mixed` sends SIGTERM to the tool only, which then stops the server properly.
`systemctl --user stop valheim` is therefore an immediate save and quit.
