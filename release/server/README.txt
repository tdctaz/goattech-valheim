GoatTech Valheim mods {{VERSION}}, SERVER package
=================================================

For the Valheim dedicated server. Contains BepInEx, the three GoatTech mods
(Server Authority, Rebalanced and Creatures) and Valheim Server Tool, all at
version {{VERSION}}.

Every player needs the matching CLIENT package, GoatTech-Valheim-client-
{{VERSION}}.zip. The server refuses clients without it, or with a different
version.

Take a copy of your saves before installing or updating: the worlds_local
folder, and characters_serverauthority next to it once the server has run.
On Windows they are in

    %USERPROFILE%\AppData\LocalLow\IronGate\Valheim

and on Linux in

    ~/.config/unity3d/IronGate/Valheim


WHAT IS IN HERE
---------------

server-files/ holds everything that goes into the server folder:

  BepInEx/, winhttp.dll, doorstop_*    the mod loader and the three mods
  servertool/                          Valheim Server Tool, which runs the
                                       server for you (recommended)
  ServerAuthority-watchdog.*           a simpler launcher, if you would
                                       rather not use the tool

The server tool starts the server, restarts it after a crash and keeps the
logs, restarts it daily at 05:00 with a world backup and a Valheim update
through SteamCMD, and warns every player on screen 15, 10, 5, 2 and 1
minutes before any stop. It needs the .NET 10 runtime ("Runtime" for x64 on
Windows, dotnet-runtime-10.0 on Linux):

    https://dotnet.microsoft.com/download/dotnet/10.0

For the updates it also needs SteamCMD:

    https://developer.valvesoftware.com/wiki/SteamCMD

The tool finds steamcmd on the PATH and in the usual install folders, such
as C:\steamcmd on Windows or ~/steamcmd on Linux. Otherwise set
SteamCmdPath under [Update] in servertool.cfg. Without SteamCMD the update
is skipped and logged, and everything else works.


INSTALLING
----------

1. Stop the Valheim dedicated server if it is running.

2. Copy EVERYTHING inside the "server-files" folder into the folder that
   contains valheim_server.exe (Windows) or valheim_server.x86_64 (Linux),
   usually

       C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server

   You should end up with a BepInEx folder and a servertool folder next to
   the server executable. The other platform's files do no harm.

3. On Linux, in a terminal in that folder:

       chmod +x servertool/ValheimServerTool *.sh

4. Start the tool once from a terminal in the servertool folder:

       Windows:  ValheimServerTool.exe run
       Linux:    ./ValheimServerTool run

   The first run writes servertool.cfg next to the tool and stops.

5. Set at least Name, World and Password under [Server] in
   servertool/servertool.cfg. Every setting is explained in the file.

6. Run the same command again. The tool starts the server and keeps it
   running until you type quit. Leave that window open.

7. Wait for the log to say

       Server Authority active. Ownership mode: Always.

   Players can then connect. Ports 2456 and 2457 need to be open.


USING THE SERVER TOOL
---------------------

Type commands into the tool's window, or run them from another terminal in
the servertool folder, for example "ValheimServerTool status":

    status              state, uptime, the next daily restart, last crash
    say <message>       show a message to every player
    restart [--now]     warn players, stop, back up and start again
    update [--now]      like restart, and update Valheim with SteamCMD while
                        it is down; use it after a Valheim patch
    stop [--now]        warn players, stop and back up; the tool keeps running
    start               start the server again
    cancel              cancel a stop or restart that is counting down
    backup              back up the world
    quit [--now]        stop the server and exit the tool

--now skips the warnings. Ctrl+C in the tool's window is quit --now. Closing
the window instead can take the server down without saving.

Everything the tool keeps goes into servertool/backups:

    servertool.log                  what the tool did and when
    logs/server-<time>.log          the server's output, one file per run
    logs/bepinex-<time>.log         the mods' log for the same run, with
                                    the performance reports
    logs/steamcmd-<time>.log        SteamCMD's output from each update
    crashes/<time>-exit<code>/      logs and configs from each crash;
                                    summary.txt is the one to read first
    worlds/<world>-<time>.zip       the world and the characters

Players' games update themselves through Steam, and the server refuses
anyone whose Valheim version differs from its own. The daily update keeps
the server in step; after a Valheim patch during the day, type update.
SteamCMD only replaces the game's own files, so BepInEx, the mods,
servertool and the worlds are left alone.

To run it as a Linux service, see the systemd example in the server tool's
README in the source.


WITHOUT THE SERVER TOOL
-----------------------

ServerAuthority-watchdog.bat (Windows) and ServerAuthority-watchdog.sh
(Linux) start the server, restart it after a crash and keep the logs in a
ServerAuthority-logs folder. They have no warnings, daily restart or
backups. Set the server name, world, password and port at the top of the
file and run it. On Linux these can also be environment variables:

    SA_WORLD=MyWorld SA_PASSWORD=secret123 ./ServerAuthority-watchdog.sh

On Windows, ServerAuthority-tail-log.bat shows the live log and
ServerAuthority-collect-logs.bat zips every log and config onto the Desktop
for reporting a problem.


UPDATING FROM AN EARLIER VERSION
--------------------------------

1. Stop the server.

2. Copy these over the ones in the server folder:

       BepInEx/plugins/*.dll
       servertool/ValheimServerTool.exe and servertool/ValheimServerTool
       GoatTech-VERSION.txt

3. Do NOT copy anything in BepInEx/config or the watchdog scripts over your
   own, or you lose your settings. servertool.cfg is not in the package;
   the tool adds new settings to it by itself.

4. Start the server, and tell every player to install the new client
   package. Players on the old version are refused until they do.


SETTINGS
--------

BepInEx/config holds one file per mod. The server sends its Rebalanced and
Creatures settings to every client, so they only need changing on the
server; the exception is the Creatures [Looks] settings, which each player
keeps for themselves. A changed setting needs a server restart.

The shipped valheim.server_authority.cfg turns on two things that are off by
default in the mod:

  [ModValidation] Enabled = true
      Refuse clients that do not run exactly the same mods, at the same
      versions, as the server.

  [Characters] Enabled = true
      The server keeps every character. A character it has never seen is
      reset to a fresh one with the same name and looks. Set this to false
      to let players bring their own characters.

Every other setting is written with its default and a description the first
time the server runs.

Check one default against the server's upload. Server Authority lets Steam
send each player up to 384 KiB/s, where Valheim alone allows 150:

  [Performance] SteamSendRateKiB = 384

It only reaches that in bursts, such as a player arriving through a portal,
but eight players at once is about 25 Mbit/s. If the upload is smaller,
lower it, or set 0 to keep Valheim's 150.

Every ten minutes the log reports how each player's object updates are
keeping up. A warning starting "Object updates could not keep up" means a
player saw creatures stutter. If its Steam queue is 100 ms or more, the
send rate above is what limited them.


IF SOMETHING GOES WRONG
-----------------------

If the server dies, the tool (or the watchdog) restarts it and keeps the
whole log, the mods' log and a summary of the interesting lines. A crash
may write no error at all: a Mono abort ends the process without an
exception, so the log just stops. The exit code is the reliable signal.

If the server dies within a minute of starting three times running, the
tool stops restarting it. That is a startup problem, such as a bad world
name, a short password or a port in use. Fix it, then type start.

To check whether Server Authority is the cause, set

    Mode = Vanilla

under [Ownership] in BepInEx/config/valheim.server_authority.cfg and restart.
The mod still loads but stops changing who simulates what.
