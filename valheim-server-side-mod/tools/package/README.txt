Server Authority, for Valheim dedicated server 1.0.15
=====================================================

Moves world simulation off the players and onto the server. Normally Valheim
hands each object to whichever player is standing near it, so one person's
machine and connection decide how an area behaves for everyone, and things
hitch when that person dies or walks away. This makes the server own and
simulate everything instead.

Nothing is installed on the players' machines. They connect normally.


READ THIS FIRST
---------------

This is alpha software and it has crashed a server during development. It is
for a test session with people who know that, not for a world anyone minds
losing. Take a copy of your world before you start:

    %USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local

The server it runs on must be Valheim 1.0.15. The mod checks this on startup
and writes a loud warning in the log if the versions do not match.


INSTALLING
----------

1. Stop the Valheim dedicated server if it is running.

2. Copy EVERYTHING inside the "server-files" folder into the folder that
   contains valheim_server.exe. That folder is usually:

       C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server

   You should end up with winhttp.dll, doorstop_config.ini, a BepInEx folder
   and some ServerAuthority-*.bat files sitting next to valheim_server.exe.

3. Right click ServerAuthority-watchdog.bat, choose Edit, and set the server
   name, world name, password and port at the top. Save and close.

4. Double click ServerAuthority-watchdog.bat.

   Windows may warn about an unrecognised app. It is a text file running
   PowerShell, which is part of Windows; allow it if you are comfortable.

5. Wait for the log to say:

       Server Authority active. Ownership mode: Always.

   Players can then connect as usual. Ports 2456 and 2457 need to be open.


RUNNING IT
----------

ServerAuthority-watchdog.bat
    Starts the server and keeps it running. If the server dies it restarts it
    and saves the evidence. Leave the window open; closing it stops the server.
    Ctrl+C in the window stops it properly.

ServerAuthority-tail-log.bat
    Shows the live log in a second window. Read only, close it any time.

ServerAuthority-collect-logs.bat
    Puts every log, crash file and config into one zip on your Desktop, ready
    to send back. Run this after anything goes wrong. It collects logs and
    configuration only, never your world save.


IF SOMETHING GOES WRONG
-----------------------

If the server dies, the watchdog restarts it and writes three files into the
ServerAuthority-logs folder:

    CRASH-<time>-exit<code>.log      the whole server log for that run
    CRASH-<time>-BepInEx.log         the mod's own log
    CRASH-<time>-summary.txt         the interesting lines, read this first

Run ServerAuthority-collect-logs.bat and send the zip from your Desktop.

If the game itself misbehaves rather than crashing, the single most useful
thing you can do is say what you were doing at the time, and roughly when.
The logs are timestamped and that is usually enough to find it.

To check whether the mod is the cause at all, open
BepInEx\config\valheim.server_authority.cfg, set

    Mode = Vanilla

and restart. The mod still loads but stops changing who simulates what. If the
problem persists with Vanilla, it is not this mod.


WHAT TO STRESS TEST
-------------------

The parts most likely to break, roughly in order:

  * Several players in the same area, fighting the same creatures.
  * Opening and using chests, especially while someone else is nearby.
  * Carts, dragged over rough ground and through doorways.
  * Boats, with one person steering and others aboard.
  * Taming, breeding and mounts.
  * Raids and events with a group present.
  * Building, and structures taking damage.
  * A long session with people actually on it. The server has run unattended
    for four hours with no errors and no memory growth, but that was mostly
    idle. Hours of real play is untested.

Two players in one area has never been tested at all. That is the situation
this mod exists for, and the one with the least evidence behind it.


KNOWN LIMITS
------------

  * Boats are deliberately left with the player steering them. A server-owned
    hull answers the rudder a full round trip late.
  * Interactions cost a round trip to the server, because the server is the
    one simulating. On a local or nearby server this is not noticeable; on a
    distant one it may be.
  * The server does real work now. Expect roughly one CPU core per player at
    the default settings, and a few hundred MB of memory per player's area.
  * Four internal patches are known to carry a crash risk that cannot be
    removed without giving up the fixes they provide. This is the failure the
    watchdog exists to capture.


NOTES-diagnostics.txt explains the settings and the diagnostic switches.
source\ contains the full source code of the mod.
