GoatTech Valheim mods {{VERSION}}, CLIENT package
=================================================

Needed by every player joining a GoatTech server. Contains BepInEx and the
three GoatTech mods, Server Authority, Rebalanced and Creatures, all at
version {{VERSION}}. The server refuses anyone without exactly this version
of all three.

Your Valheim must be the current Steam version, the same as the server's.


INSTALLING ON WINDOWS
---------------------

1. In Steam, right click Valheim, Manage, Browse local files. That opens the
   folder with valheim.exe in it.

2. Copy EVERYTHING inside the "client-files" folder into that folder. You
   should end up with winhttp.dll, doorstop_config.ini and a BepInEx folder
   next to valheim.exe.

3. Start Valheim as usual.


INSTALLING ON LINUX
-------------------

Native Linux, which is what Steam installs unless you forced Proton:

1. In Steam, right click Valheim, Manage, Browse local files. That opens the
   folder with valheim.x86_64 in it.

2. Copy EVERYTHING inside the "client-files" folder into that folder. You
   should end up with start_game_bepinex.sh, a doorstop_libs folder and a
   BepInEx folder next to valheim.x86_64. The Windows files come along too
   and do no harm.

3. In a terminal in that folder:

       chmod +x start_game_bepinex.sh

4. In Steam, right click Valheim, Properties, and set the launch options to:

       ./start_game_bepinex.sh %command%

Proton: do the same as for Windows, then set the launch options to:

    WINEDLLOVERRIDES="winhttp=n,b" %command%


UPDATING FROM AN EARLIER VERSION
--------------------------------

Copy the three .dll files from client-files/BepInEx/plugins over the ones in
your BepInEx/plugins folder, and GoatTech-VERSION.txt over the old one.


OTHER MODS
----------

If you already use BepInEx, copy only the three .dll files into your
BepInEx/plugins folder. The server refuses any mod it does not run itself,
so remove other mods before joining.


YOUR CHARACTER
--------------

The server keeps every character itself. When you join, its copy replaces
your local one. A character it has never seen starts over as a fresh one
with the same name and looks. Your local file is backed up first either
way, but it is simplest to make a new character for the server.


CHECKING WHICH VERSION YOU HAVE
-------------------------------

After the game has started once, BepInEx/LogOutput.log in the Valheim folder
lists every loaded plugin with its version. All three GoatTech mods should
say {{VERSION}}.


REMOVING IT AGAIN
-----------------

Delete winhttp.dll, doorstop_config.ini, .doorstop_version,
GoatTech-VERSION.txt and the BepInEx folder from the Valheim folder, and on
Linux also start_game_bepinex.sh and the doorstop_libs folder. Clear the
launch options if you set any.
