# Server Authority

A dedicated-server mod for Valheim that moves world simulation off the players and onto the server.

In vanilla, the server hands each object to whichever client is standing near it, and that client
then simulates it for everyone. So one player's frame rate and connection decide how the area feels
for the whole group, and when that player dies or walks away the objects go ownerless until the
server's next two second pass finds a replacement. That pause is the hitch you feel when the "area
host" changes.

This mod replaces the rule the server uses to hand out ownership, and gives the server the loaded
scene it needs to act on it. Ownership assignment was already a server-side decision, so that half
needs **nothing installed on clients** and vanilla clients can connect normally.

Ownership is that server-side half. The **wave synchronisation** in [Waves](#waves) is not: waves are
never sent over the network, every machine recomputes them, and making two machines agree needs the
same code on both. That part of the mod therefore **does** have to be installed on every player's
game, as do [mod validation and server side characters](#mod-validation-and-server-side-characters).
A vanilla client can still connect and play; it simply keeps vanilla's wave behaviour, and the boats
it renders will not sit in the sea the way the server floats them.

> **Status: early alpha.** Ported to Valheim **1.0.15** and now **running on a live dedicated
> server**. Creature AI, combat, drops and item pickup are confirmed working with the server
> owning simulation. Not yet tested with two players in one area, which is the case the whole
> design exists for. See [Game updates](#game-updates).

## What it does

The server owns and simulates everything around every player, all the time. Creature AI, spawning,
physics, damage and the timers on smelters and crops all run on the server, so no player's machine
or connection decides how an area behaves for anybody else, and nothing hitches when a player dies
or walks away.

There is no partial mode. An earlier design handed a sector back to a lone player after a delay, on
the theory that a solo player should keep vanilla latency. It was dropped, because every serious bug
this mod has produced was an ownership **transfer** rather than a simulation problem, and a design
whose whole purpose is moving ownership to the server should not also be moving it back. The code is
in git history if that reasoning ever needs revisiting.

Some things still belong to clients, and always will:

- **Player characters.** Never reassigned, at all. A body simulated by the server rubber-bands.
- **Mounts and carts.** Continuous input, so a server-owned animal answers a full round trip late.
  The user holds a lease on it; once they let go it reverts to the server.

  **Ships are no longer in this list.** The server owns a boat even while somebody is steering it,
  because that is the only way every passenger gets the same hull physics rather than whatever the
  driver's connection happens to feel like. The rudder does answer a round trip late, and that was
  judged worth it. `Vehicles.KeepShipOwnedByDriver` hands a crewed boat back to its driver and
  exists as a fallback for when server simulation of a ship misbehaves, not as a tuning knob.
- **Anything a player is actively using.** The policy never takes a ZDO whose `InUse` flag is set
  while its owner is still connected. Valheim already uses that flag as its own "somebody is
  interacting with this" convention, so this covers chests, ship cargo and cart inventories at once.

That last rule is load-bearing, not a nicety. `InventoryGui` hides the container panel the moment
`m_currentContainer.IsOwner()` goes false, so a chest whose ownership is reclaimed simply snaps shut
about a second after you open it.

The flag alone is not quite enough. Between the server granting ownership and the client writing
`InUse` back there is roughly a 100ms window where it is not yet set, which against a two second
policy cycle is about a one in twenty chance of the chest closing anyway. So anything a connected
peer has just claimed is left alone for a three second grace before the policy takes it back, which
covers the gap without patching the container RPCs. Those patches used to exist and crashed a live
server; see `ContainerPatches.cs`.

Ship cargo and cart inventories are not separate objects. The container shares its vehicle's ZDO, so
vanilla `Container.RPC_RequestOpen` hands the whole hull or cart to the client that opens it, and its
physics runs on that client until the panel is closed and the policy takes it back. That is accepted
rather than fixed, because the only way to split the two is to patch the RPC that crashed the server.

Boats are simulated by the server throughout, crewed or not, so a hull behaves the same for
everybody aboard and drifts and takes damage on one machine rather than on whichever client happens
to be nearest. Setting `Ownership.ServerOwnsWaterborne` to false leaves an unmanned hull with the
nearest client instead, which is what vanilla does.

A server-owned hull needs the water under it to exist, and on a server that is not a given. See
[the hull water gate](#a-server-owned-hull-needs-its-neighbourhood).

## Trade-offs worth knowing before you run it

- **Melee hit registration moves to the server for everyone in a contested area.** Instead of one
  player having zero latency and everyone else having that player's connection, everybody has the
  server's. On a LAN or a nearby VPS this is a clear win. On a 150ms+ server it can feel worse than
  vanilla for the player who would have been the area host.
- **Steering a boat costs a round trip.** The server simulates a ship even while somebody is at
  the rudder, so the driver's input takes a full round trip to take effect. This was tested and
  judged acceptable, because the alternative gives every passenger the driver's connection instead.
  The test was on a loopback connection, so if it feels wrong on a high latency server that is what
  `Vehicles.KeepShipOwnedByDriver` is for.
- **The server does real work now.** Note that the two costs scale differently. The server loads
  terrain and instantiates objects around **every** player regardless of mode, because a sector can
  become contested at any moment and the takeover has to be instant, so memory cost is roughly
  "one client's working set per player" either way. What `Contested` mode saves is the *simulation*
  cost, physics and pathfinding and AI, which it only pays where players actually group up. Give the
  machine real CPU and RAM.
- **Clients can still grab individual objects** when they interact with them, because
  `ZNetView.ClaimOwnership` is client-side and cannot be prevented without a client mod. The policy
  reclaims those within two seconds and it causes no visible glitch.

## Configuration

Written to `BepInEx/config/valheim.server_authority.cfg` on first run.

| Setting | Default | Notes |
| --- | --- | --- |
| `Ownership.Mode` | `Always` | `Always` simulates server side; `Vanilla` leaves ownership alone, for isolating whether a problem is this mod. |
| `Ownership.ServerOwnsWaterborne` | `true` | Let the server own boats and floating objects. See above. |
| `Performance.MaxObjectsCreatedPerFrame` | `100` | Lower if the server stutters, raise if players outrun object loading. |
| `Performance.ZoneEvictionsPerTick` | `0` (auto) | How many expired zones may unload per tick. |
| `Performance.ServerFrameRate` | `60` | Valheim hardcodes 30. See [Responsiveness](#responsiveness). |
| `Performance.ZdoSendRate` | `20` | How often object updates flush to each client, in Hz. |
| `Vehicles.KeepShipOwnedByDriver` | `false` | Fallback only. On hands a steered boat back to its driver; the server owning it is the intended state. |
| `Vehicles.KeepVehiclesOwnedByUser` | `true` | Same for mounts and carts. Leave this on. |
| `Vehicles.HoldHullsUntilWaterLoads` | `true` | Leave this on. Off reproduces moored boats jumping when their physics activates. |
| `Vehicles.RequireLoadedAreaForWaterborneOwner` | `true` | Only matters when `ServerOwnsWaterborne` is off. Leave this on. |
| `Waves.DeterministicWind` | `true` | Derive wind from world time so every machine computes the same sea. Must match on server and clients. See [Waves](#waves). |
| `Waves.AlignRenderedWaterWithPhysics` | `true` | Draw the sea at the moment boats float on it. See [Waves](#waves). |
| `Waves.ServerWeatherFollowsPlayers` | `true` | Resolve the server's global weather at a player rather than the world origin. Server side only. With per-position weather on, only the few things that still read the global follow it. |
| `Waves.ServerWeatherPerPosition` | `true` | Give everything the server simulates the weather at its own position: spawners, rain wear, fires, cinders, windmills, fish, wisp torches, and the wind and waves under every hull, floating object and leviathan. A raid's weather stays inside the raid. Server side only. See [One weather for the whole world](#one-weather-for-the-whole-world). |
| `Waves.BlendedWeatherWind` | `true` | Draw the wind strength that sets wave height from a blend of the weather that is a function of position and world time, the same on every machine, instead of from each machine's own weather transition. A ship's owner writes the range it used onto the ship so everyone aboard matches the hull exactly. Only the wind changes. Must match on server and clients. See [Wave strength blended in space and time](#wave-strength-blended-in-space-and-time). |
| `Waves.LevelShipWaterline` | `true` | Lay a boat's waterline patch on the sea instead of nailing it to the hull, and put the foam ring and the flat sheets of the speed wake on the surface instead of leaving them frozen at the height the hull had when they were emitted. Bow spray and rudder churn are left alone. Client side, looks only. |
| `Waves.ShipFoamSpread` | `0.75` | How far, in metres, each foam particle reads the sea away from its own position, so the ring and the wake are not rigid sheets. Scales itself with the weather. 0 restores the perfectly conformal version. |
| `Waves.ShipFoamLift` | `0.07` | How high, in metres, a foam particle may sit above the surface, fixed for its life and different for each one. The part that does not depend on the weather. |
| `Waves.ShipWakeTrailSeconds` | `0` | Caps how long a boat's wake foam may live, in seconds. Off by default: it shortens the wake without thinning it, which turned out not to be the fault. A tuning lever, kept because a shorter wake is a reasonable thing to want. |
| `Waves.ShipWakeSink` | `0.35` | How deep, in metres, the deepest particle of a boat's wake sits below the surface, fading toward a quarter opacity with depth. Restores the scatter through the surface that vanilla got from the hull's heave. The waterline ring is not affected. |
| `Debug.LogShipDamage` | `true` | One line per hit a boat takes, with its `HitType`. Leave on; boats are hit rarely. |
| `Debug.WaveSyncIntervalSeconds` | `5` | Logs the three inputs the sea is built from, on server and client alike, only while a boat exists. Compare two logs on the `second=` key. |
| `Debug.ForceEnvironment` | empty | Debug tools build only. Pin the weather to one environment, such as `ThunderStorm`. Must be set identically everywhere or it *creates* a desync. |
| `Debug.LogOwnershipChanges` | `false` | Debug tools build only. Logs every takeover and handback. Noisy. |
| `Debug.StatusIntervalSeconds` | `0` | Periodic "simulating N sectors" line. Start with `60`. |
| `Debug.LogNearestWaterOnJoin` | `false` | Debug tools build only. Logs where the nearest sailable water is, for boat testing. |
| `Debug.EnableSpawnRequests` | `false` | Debug tools build only. Testing aid, see below. Leave off outside testing. |
| `ModValidation.Enabled` | `false` | Require clients to run the same mods as the server. See below. |
| `ModValidation.ServerOnlyMods` | empty | Server plugins clients do not need. |
| `ModValidation.AllowedClientMods` | empty | Extra client mods to allow. `*` allows any. |
| `ModValidation.ForbiddenClientMods` | empty | Always rejected, even with `*`. |
| `ModValidation.RequireSameGameVersion` | `true` | Vanilla only compares the network protocol. |
| `ModValidation.CompareFileHashes` | `false` | Require byte-identical DLLs, not just equal versions. |
| `Control.Enabled` | `true` | Accept commands from the [server tool](../valheim-server-tool/README.md): broadcast a message to every player, or save and quit. |
| `Control.Directory` | empty | Where the tool drops command files. Default is `ServerAuthority-control` next to the server executable. |
| `Characters.Enabled` | `false` | Keep characters on the server. See below. |
| `Characters.StoragePath` | empty | Default is `characters_serverauthority` next to `worlds_local`. |
| `Characters.NewCharacters` | `ResetToFresh` | Unknown characters start over. `Accept` and `RequireNew` are the alternatives. |
| `Characters.StartingKit` | empty | Extra items for a fresh character, comma separated. An item name gives one, `Name:Count` gives that many, and a piece name such as `Karve` gives its materials, its crafting station's materials and the tool that builds both, read from the game's own recipes. The server logs the resolved kit at startup. |
| `Characters.OnInvalidUpload` | `Kick` | `LogOnly` discards a bad save without kicking. |
| `Characters.SaveIntervalSeconds` | `300` | Extra save requests between world saves. Bounds rollback. |
| `Characters.SyncTimeoutSeconds` | `90` | How long the character exchange at login may take. |
| `Characters.ShutdownSaveSeconds` | `10` | How long a graceful shutdown waits for every player's final save. `0` skips the wait and rolls everyone back to their last upload. |
| `Characters.RejectCheatedItems` | `true` | Items the game marked as spawned with devcommands. |
| `Characters.RejectCheatedProfiles` | `false` | The profile-wide devcommands flag. Permanent, so off. |
| `Characters.MaxSkillLevel` | `100` | Raise if a mod raises the skill cap. |
| `Characters.MaxSkillGainPerHour` | `0` (off) | Per skill. Start generous and watch the log. |

### Spawn requests

Only in a [debug tools build](#debug-tools). With `EnableSpawnRequests` on, the server watches `BepInEx/config/serverauthority_spawn.txt` and
spawns what it lists next to the first connected player, one `PrefabName Count` per line, then
empties the file. A third word `tame` spawns creatures already tamed, as in `Lox 1 tame`, so a mount
can be saddled at once. The line `water` instead re-runs the nearest water scan for that player, `diag`
prints what the server can see of the water at that point, `owners` lists what the server actually
owns around the player, by prefab name, which is how you confirm a thing is really being simulated
server side rather than assuming it, and `hulls` prints for every instantiated ship the five points
it measures the water at, which zone each falls in, whether that zone exists, and whether buoyancy
will therefore be on or off. `event army_bonemass` starts that random event on the player, as the
console command does but without devcommands flagging the character, and `event stop` ends it.
See [Reproducing the hull water fault](#reproducing-the-hull-water-fault).

This exists because testing a cart needs bronze nails and testing a boat needs a shoreline, and
neither is reachable quickly on a fresh world. It is also the only route available: on a dedicated
server `Terminal.IsCheatsEnabled()` returns `ZNet.instance.IsServer()`, which is false on every
client, so `devcommands` and `spawn` cannot be used from a connected client no matter who is admin.

It is a cheat hook whose only authentication is filesystem access to the server. Leave it off
except while testing.

### Seed for a new world

The dedicated server always gives a new world a random seed and has no option to choose one. Server
Authority adds one: start the server with `-seed <seed>` and a world created on that start gets it.
An existing world is not touched. The [server tool](../valheim-server-tool/README.md) always passes the
world name, so a world's seed is its name.

## Mod validation and server side characters

Both features are configured on the server only. The client's own copy of these settings does
nothing. Either one makes the client mod mandatory: a client without it is kicked, and vanilla
shows that as a plain "kicked". A client with the mod is shown the actual reason.

### How a connection is checked

On connecting, the client mod sends a manifest: its Server Authority protocol version, Valheim
version, every loaded BepInEx plugin with its version and a SHA-256 of its DLL, and a hash of the
local character file. The server checks it when vanilla admits the peer, at `ZRoutedRpc.AddPeer`,
which is a plain call at the end of `ZNet.RPC_PeerInfo`. Nothing here patches an RPC method, for
the reason given under [Patching RPC methods can kill the server](#patching-rpc-methods-can-kill-the-server).
A failing client is kicked through vanilla's own `InternalKick`.

Every plugin on the server is required on the client at the same version, except those listed in
`ServerOnlyMods`. A client plugin the server does not have is rejected unless it is listed in
`AllowedClientMods`.

**This stops mismatched installs and casual cheating, not a determined cheater.** The manifest is
whatever the client says it is. Anyone willing to edit the client mod can report any mod list they
like. No design can prevent that without a client the server can attest to, and Valheim has none.

### How characters are kept

The server stores one `.fch` per character, per world, at
`characters_serverauthority/<world>/<platform id>/<name>.fch`. It is the game's own character file
format, so an admin can copy one into a client's `characters_local` folder to inspect it. The
previous copy is kept as `.old`. To reset a character, delete its `.fch` together with any `.old`
and `.new` beside it, because a missing `.fch` is otherwise recovered from those.

Each save is written to `<name>.fch.new` and flushed to disk, the current file is copied to `.old`,
and the new file then replaces the `.fch` in a single rename, so a crash at any point leaves a
complete `.fch` behind. If the `.fch` is missing or unreadable anyway, loading falls back to
`.new`, which is always the newer when it exists, and then `.old`, taking the first that passes the
file's own hash check and decodes as a character. It writes that back as the `.fch` and logs a
warning naming the file it used. An unreadable `.fch` is kept
aside as `.fch.unreadable`. Without this, a crash at the wrong moment made the server treat a known
player as new, and `ResetToFresh` then wiped them while the good copy sat next to it.

- **Login with a known character.** The server sends its copy and the client uses it instead of
  its local file. Spawning is held until it arrives. If the local file differed, it is backed up
  first as `characters_local/<name>_backup_serverauthority-<time>.fch`, also for characters kept in
  Steam Cloud, which the game's Manage Saves menu lists, and
  a "Server:" line in chat, shortly after the arrival shout, tells the player their character was
  restored. This is the reset: gear, skills or progress
  edited offline, or earned in another world, are gone on the next login.
- **Login with a character the server has never seen.** By default it starts over. The client sends
  its local copy, and the server builds a fresh character from it that keeps only the name, id and
  appearance: starting gear and health, no skills, recipes, trophies, powers, map or spawn points.
  The client backs up its local file, installs the fresh character, and sends it back, and the
  server stores it only if its player data is byte for byte what the server built. The player then
  enters with the Valkyrie intro. `Accept` instead takes the character as it is, subject to the
  checks below, and `RequireNew` refuses any character that has already entered a world.
- **While playing.** Every save the client makes is uploaded, in 256 KB chunks because Steam caps a
  single message at 512 KB and an explored map easily exceeds that. The server already asks every
  client to save at each world save, and additionally every `SaveIntervalSeconds`. Each upload is
  checked, stored if it passes, and otherwise discarded and the player kicked, so they return as
  the last good copy.

An upload is checked for: the same character name and id as the server's copy, items that exist in
the server's `ObjectDB` with stacks and quality within that item's limits, items the game marked as
spawned with devcommands, skills within `MaxSkillLevel`, and optionally skill gain per hour. The
server reads the player data format itself. The name, id and devcommands flag are always checked.
If a game update moves the player data to a newer version than the mod reads, the items and skills
go **unchecked** with a warning in the log rather than kicking everybody. An older version, or player
data that cannot be read, is rejected: a current client always saves the current version, so only
a stale or hand-edited file arrives that way, and the player is told to load it once in single
player. `PlayerDataSummary` mirrors `Player.Save` at player data version 33, so re-read that method
after a game update.

What it cannot catch: the client still simulates its own character, so a modified client can
report gear it picked up legitimately and gear it invented identically, as long as both are
plausible. What the server copy guarantees is that a character cannot be changed while it is away
from the server, and cannot carry impossible items or skills while it is here.

Logging out or quitting waits for the server to confirm it stored the final save, for up to ten
seconds, before the game is allowed to disconnect. That wait is necessary: Valheim closes its Steam
connection without lingering, which discards anything still queued, and in testing the logout save
never arrived without it, so every logout lost the progress since the last periodic upload.

The wait happens inside a `Game.Shutdown` prefix, which both logging out and quitting pass through,
by pumping the connection by hand until the acknowledgement arrives. It cannot be done
asynchronously: deferring the quit through `Application.wantsToQuit` was tried and left a black
screen, because `Game.OnApplicationQuit` still runs and tears the game down in the same frame, and
nothing is left running to finish the quit afterwards. The game's own save during shutdown is
skipped once the final save is confirmed, so the local file stays identical to the server's copy and
the next login does not see a difference.

A graceful server shutdown does the same from the other side. Vanilla's shutdown saves the world
and then disconnects every player in the same frame, closing each Steam connection without
lingering, and never asks the clients to save first, so every restart rolled every online player
back to their last upload. The server now asks each synced player to save from the same
`Game.Shutdown` prefix, which a dedicated server reaches from `Game.OnApplicationQuit` on Ctrl+C or
`SIGTERM`, before anything is torn down. It pumps the connections by hand until every player's
upload has arrived or `ShutdownSaveSeconds` runs out, and logs per player whether their final save
was stored. The world is saved after the wait, so it matches the characters. Anything that stops
the server must allow for the wait: `docker stop` sends `SIGKILL` after 10 seconds by default, so
give it `-t 30`. A player whose upload was already on its way when the server asked is counted by
that upload, which is at most a round trip older than the request.

A crash, a kill, or a dropped connection still rolls a character back to its last upload, at most
`SaveIntervalSeconds` old, while the world keeps whatever was put into chests since. That can
duplicate items. Vanilla has the same exposure, bounded by its much longer world save interval.

## Building

Needs the .NET SDK and a Valheim install (client or dedicated server, either has the assemblies).

```bash
dotnet build src/ServerAuthority/ServerAuthority.csproj -c Release
```

The game path is found automatically under the usual Steam locations. Override it with
`VALHEIM_MANAGED=/path/to/valheim_server_Data/Managed`. The output is
`src/ServerAuthority/bin/Release/ServerAuthority.dll`.

Set `VALHEIM_PLUGINS` to a BepInEx plugins folder and each build deploys itself there.

### Debug tools

A plain build leaves out every debug option and testing tool, and is the one to distribute:
`Debug.LogOwnershipChanges`, `Debug.LogShipState`, `Debug.LogNearestWaterOnJoin`,
`Debug.ForceEnvironment` and `Debug.EnableSpawnRequests` with its spawn requests. Their settings are
not even written to the config file. The diagnostics meant to be running when something goes wrong,
`Debug.LogShipDamage`, `Debug.WaveSyncIntervalSeconds` and `Debug.StatusIntervalSeconds`, stay in.

For testing, build with them:

```bash
dotnet build src/ServerAuthority/ServerAuthority.csproj -c Release -p:DebugTools=true
```

The startup line listing the effective configuration says which kind of build is running.

## Verifying

```bash
# Every [HarmonyPatch] target still exists and is unambiguous in the game assembly.
dotnet run --project tools/PatchCheck -c Release -- \
  src/ServerAuthority/bin/Release/ServerAuthority.dll \
  ~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed
```

**Run `PatchCheck` after every Valheim update.** The predecessor to this mod broke on nearly every
game patch because it rewrote game methods with IL transpilers that matched silently and then
stopped matching. This mod replaces whole methods instead and `PatchCheck` is the cheap half of
re-verifying it. The expensive half is re-reading the replaced methods against a fresh decompilation;
`RESEARCH.md` lists which ones and why.

## Stability so far

One unattended run of 4 hours 22 minutes on Linux, ending in a clean shutdown: no exceptions, no
assertions, no fatal signals, 132 completed world saves. Object counts returned to zero when the
last player disconnected, so the server unwinds its scene rather than accumulating, which is the
failure the predecessor mod was best known for.

That run was mostly idle, so it demonstrates the absence of a slow leak rather than stability under
load. The one crash observed to date took about an hour of ordinary play to trigger and is described
under [Patching RPC methods can kill the server](#patching-rpc-methods-can-kill-the-server).

## The test rig

`tools/testserver/` holds the two scripts used to exercise this against a real dedicated server.
Copy them into the server directory and run the watchdog.

`start_server_authority_test.sh` launches a private server with `-saveinterval 120` instead of the
default 1800. A crash loses everything since the last save, and this mod has crashed a server, so
two minutes of exposure beats thirty.

It also points `XDG_CONFIG_HOME` at `server_config/` inside the server directory. Without that, a
dedicated server on the same Linux account as a game client shares
`~/.config/unity3d/IronGate/Valheim` with it, and so shares the Unity preferences file the Steam
client keeps its settings in. The server loads that file at startup and writes its copy back on
shutdown, so restarting the server after the client quits silently resets the client's graphics,
key bindings and audio. The world and the admin lists move with it, into
`server_config/unity3d/IronGate/Valheim/`.

`watchdog_authority_test.sh` restarts the server when it dies and keeps the evidence. This matters
more than it sounds: a Mono abort terminates the process rather than raising an exception, so a
crash produces no error in the log, just silence followed by nothing. The watchdog gives each run
its own log, and on an abnormal exit copies it aside as `CRASH-<time>-exit<code>.log` with a
summary of the exceptions, assertions and fatal signal that preceded it.

Watching the log for exceptions is not sufficient monitoring on its own. A clean log meant a healthy
server right up until the run that died, and the thing that killed it never logged an error at all.

## Trying it

Install the Valheim Dedicated Server (Steam app 896660), then install
[BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) into it and
drop `ServerAuthority.dll` into `BepInEx/plugins/`. Launch with `start_server_bepinex.sh`.

Use a recent BepInEx pack. Releases before 5.4.2350 shipped a `start_server_bepinex.sh` that still
set Doorstop 3 variables (`DOORSTOP_ENABLE`, `DOORSTOP_INVOKE_DLL_PATH`) while bundling a Doorstop 4
library that reads `DOORSTOP_ENABLED` and `DOORSTOP_TARGET_ASSEMBLY`. The symptom is a server that
starts perfectly and simply has no BepInEx in its log.

On startup the log should say `Server Authority active`. Set `Debug.StatusIntervalSeconds = 10` for
the first session and watch the simulated sector and object counts track where players actually are.

**Testing with one player** exercises everything: the server owns your surroundings from the moment
you connect, so a solo session is a real test rather than a special case.

What to check first, in rough order of how likely it is to be wrong:

1. Creatures move, attack and path normally with two players in one area.
2. Nothing rubber-bands, especially the players themselves.
3. Sailing still feels responsive, and an abandoned boat still drifts and takes damage. Then run
   [Reproducing the hull water fault](#reproducing-the-hull-water-fault), which is the one test here
   that has a deterministic trigger.
4. Raids actually spawn creatures rather than just playing the horn.
5. Smelters, kilns, fermenters and crops keep progressing.
6. Server memory is flat across a few hours rather than climbing.

### Reproducing the hull water fault

A moored boat destroying itself as somebody walks towards it is a load-order race, so a run that
does not provoke it proves nothing. This makes it deterministic and gives it a read-out.

With a [debug tools build](#debug-tools), set `EnableSpawnRequests`, `LogShipState` and `LogNearestWaterOnJoin` on, and
`StatusIntervalSeconds = 10`.

1. Sail out and moor three or four boats, spread over a couple of hundred metres so they land in
   different zones. Zone boundaries fall at `x = 32 + 64k` and the same in z, and a longship's float
   collider is 8 by 17 metres, so a hull moored across a boundary is guaranteed to sample the zone
   next door. Roughly a third of arbitrarily moored hulls do anyway.
2. **Disconnect every player and wait a minute.** With nobody online the server empties both object
   lists and lets every zone expire, so the whole neighbourhood is torn down. Reconnecting then
   rebuilds it from cold at one zone per 0.1s tick, which is the widest the window ever gets. This is
   the trigger; walking in from a distance is the same thing, only smaller and luckier.
3. Reconnect, and write `hulls` into `BepInEx/config/serverauthority_spawn.txt` a few times over the
   first ten seconds, then once more after a minute.

What the log should show, with the fix on:

- `hulls` reporting at least one hull with a `MISSING` zone and `water -10000.00` at one of its five
  points. **If no run ever shows that, the test has not fired and says nothing about the fix.**
- `Holding ship <id>` for that hull, then `Releasing ship <id>` once its zones arrive.
- `LogShipState` lines keeping `y` at the waterline and `health=100%` throughout.

Then set `Vehicles.HoldHullsUntilWaterLoads = false`, restart, and do the same thing again. The same
mooring on the same world should now show `buoyancy OFF`, `y` falling frame by frame, `upY` going
negative as it rolls, and `health` dropping until the boat is gone. That is the A/B: without it, a
clean run is indistinguishable from a run where nothing happened.

Two things must **not** change either way, since they are what the gate could plausibly break:

- Sailing a crewed boat never logs `Holding ship`. The driver owns the hull, so the gate is not
  supposed to look at it at all.
- A beached hull still settles onto the ground rather than hovering. Out of the water reads -10000
  too, and falling is the right answer there; the gate asks about zones, not about water, exactly so
  that this keeps working.

### Logging what damages a boat

`Debug.LogShipDamage`, on by default, because the investigation above spent a day inferring a damage
source that `HitData` could have named in one line. Two hooks, and the second exists because the
first was not enough:

- `WearNTear.ApplyDamage` reports each hit with its `HitType`, which separates a creature
  (`EnemyHit`) from the hull's own collision self-damage (`Boat`, since `ImpactEffect.m_hitType` is
  17 on a ship) from the capsize timer (`Undefined` blunt) from the Ashlands ocean from structural
  wear (`hitData` null). **Not** `WearNTear.Damage`, which only calls `InvokeRPC("RPC_Damage")` and
  applies nothing: a hook there records nothing on a dedicated server, as was discovered while a hull
  was beaten from 97% to 51% health with an empty log.
- A health watcher on `Ship.CustomFixedUpdate` reports any drop in the ZDO's health value. This
  covers the case the first hook structurally cannot see: `RPC_Damage` runs on the ZDO's **owner**, so
  when a client owns a hull the client subtracts the health and the server only receives the result.
  With `ServerOwnsWaterborne` off that is every boat, which is how a hull sank to the sea floor and
  lost 30 health without one line recorded against it.

Neither can be patched on the client from here, so the watcher reports the surrounding state instead
of a cause: owner, `upY`, height above the seabed, and whether the zones under the hull were complete.

## Waves

Sailing looked wrong for everybody except whoever owned the hull. The boat sat too low, the deck
washed over, and crews described it as an open air submarine. It got worse, not better, when the
server owned the boat, which is the mode this mod exists to make the default.

Nothing about waves is sent over the network. `WaterVolume.GetWaterSurface` builds the height at a
point out of three things: the point, `ZNet.GetWrappedDayTimeSeconds`, and the global wind `EnvMan`
hands it. Two machines therefore agree about the sea exactly as far as those three agree. Three of
them did not.

### Wind was latched local state, not a function of world time

`EnvMan.UpdateWind` derives a target from noise seeded by the whole second, which every machine
agrees on. What it does with that target is where it goes wrong: `SetTargetWind` refuses to take a
new one while `m_windTransitionTimer` is running, and that timer is a purely local ramp started
whenever the previous one ended. Targets change several times per ramp, so **each machine keeps
whichever target its own timer happened to finish on**, and nothing ever pulls two machines back
together.

Measured on the live server against a connected client, at the same second of world time:

| | server | client |
| --- | --- | --- |
| wind direction | `(0.413, 0.911)` | `(-0.318, -0.590)` |
| wind intensity | `0.240` | `0.067` |
| transition state | `-1.00`, settled | `3.28`, mid-ramp |
| wave height at a fixed probe | `-0.1972` | `-0.0549` |

Opposite directions and three and a half times the amplitude. Wave height scales linearly with
intensity and the leading wave runs along the wind, so these are genuinely different seas.

Whether two machines agree is luck. Later in the same session their ramps happened to fall into
step and the numbers matched exactly; a 0.28s offset between the two local timers is all it takes to
put them on different targets for a full transition. That is the "fine for some players, wrong for
others, half the time" report, and it has no fixed point to converge on.

`Waves.DeterministicWind` replaces the latch with a function of world time, keeping vanilla's noise
and vanilla's transition shape. Wind is the cross-fade between the noise evaluated at two fixed
anchors on a grid of `m_windTransitionDuration` seconds of world time. Every machine lands on the
same value with nothing sent.

Keep vanilla's shape here: two anchors and an alpha between them, not one wind vector that turns.
`CreateWave` takes a wave's spatial phase from the wind direction,

```
vector = -(worldPos.z * dir + worldPos.x * tangent)      // = -(worldPos . dir)
phase  = time * waveSpeed + vector.y * waveLength
```

so the phase at a fixed point is proportional to the dot product of the position with the wind
direction. Turning that direction slides the entire field, by an amount that grows with distance
from the world origin. A version of this published a single interpolated wind, and 950m out a
heading turning one radian per ten seconds moved the phase at about 3.8 rad/s against an intended
wave speed of 0.5. The sea raced, and it was reported as the world clock having gone into fast
forward — the clock was in fact correct to within measurement error. Cross-fading two fields that
each hold a fixed direction has no such term, which is why vanilla does it that way.

Each anchor is also evaluated once, when it first comes into use, and then held until it retires,
the way vanilla holds a target for the length of its ramp. The vegetation and grass shaders take
their sway phase as

```
phase = _Time * _SwaySpeed * (wind.w * 0.5 + 0.5)
```

so an anchor whose intensity moves while it is in use shifts that phase by the seconds since the
scene loaded times the rate of change. A version of this re-evaluated both anchors every frame
against the current weather's wind range, and whenever the weather blended into a new range the
anchors slid with it. Ten minutes into a session, a raid's weather handing back to a thunderstorm
moved the range from `0.10-0.30` to `0.80-1.00` over fifteen seconds, and every tree and blade of
grass swayed at about twenty times its speed until the blend finished. Held anchors pick a weather
change up at the next anchor instead, ten to twenty seconds later, exactly as vanilla does. The
noise stays a pure function of world time. With `Waves.BlendedWeatherWind` on, so is the wind range
an anchor is scaled into (see [Wave strength blended in space and time](#wave-strength-blended-in-space-and-time)),
so the only thing left to differ is where each machine evaluates it. With it off, the range is the
current environment's at the instant the anchor is taken, which differs between machines until two
periods after a weather change. If world time ever jumps by more than one anchor in a frame, both
are rebuilt and the log says so with a `Wind anchors rebuilt` line.

The two per-player overrides are kept: the edge of the world turns the wind outward for whoever
sails into it, and Moder's power turns it to the heading of the ship the local player is on. Both are
taken into an anchor when it is evaluated, as vanilla takes them into a target, so they swing round
over the ordinary transition rather than turning a field that is already in use.

Moder is the server's to decide for a hull the server simulates, because the hull floats on the
server's sea. The server works out whether Moder's power is active from the players aboard (their
status effects are in their ZDOs), takes the hull's own heading into that hull's anchor, and writes
that exact heading to the ship's ZDO, one slot per anchor. It writes for every anchor of every hull
it simulates, "not steered" included, whether or not it counts anyone aboard yet: a player who boards
just before an anchor rolls over waits for the server's answer, and if the server wrote nothing
because it had not seen them board, the wait never ended. A client aboard uses the written heading
instead of its own. It has to be the written one, not merely the same idea: the wave phase at a point
is proportional to the point's position dotted with the wind direction, and a client reads the
heading from a transform that trails the server's, which far from the world origin is enough to put
the crew's sea half a wave away from the hull's. The value is written when the anchor is taken and
arrives a round trip later, so a client takes the anchor from its own lagged heading first and swaps
in the server's direction when it lands, keeping the strength it took; at that moment the incoming
anchor carries a percent or two of the blend. A `Moder heading for anchor N settled` line means the
two differed by more than a degree.

### The rendered sea ran ahead of the sea boats float on

Buoyancy reads `s_wrappedDayTimeSeconds`. The shader reads `s_waterTime`. `UpdateWaterTime` adds a
whole frame delta to the second on **every call** while pulling only five percent back toward the
first, and `MonoUpdaters` calls it from `FixedUpdate`, `Update` and `LateUpdate` alike. It settles
about twelve frame times ahead of the truth: a tenth of a second at 144fps, a quarter at 60, half a
second at 30. So the boat floats on one surface and the player looks at another, by an amount that
depends on their frame rate. Measured at `0.184` seconds on both machines at a 20ms frame time.

`Waves.AlignRenderedWaterWithPhysics` ties the two together. What the smoothing was for is kept as a
rate limit rather than a lag: the world clock is set absolutely by the server's `NetTime` message
every two seconds, and a client whose frame rate collapsed can be seconds behind when one arrives, so
a correction is caught up at four times real time. Unlike a lag filter that converges on exactly the
right value and then stays there, giving a steady state error of zero.

That applies only to gaps up to ten seconds. Anything larger snaps, as vanilla's own ten second
reset does. Sleeping fast-forwards the world clock, which a client receives as a jump of ninety to a
hundred seconds every couple of seconds. Rate limited, the water fell hundreds of seconds behind and
then ran at four times speed for minutes after everyone woke up.

### The server picked its weather for the world origin

`EnvMan` resolves the current environment, and with it the `m_windMin`/`m_windMax` range wave height
is drawn from, at the main camera's position. A dedicated server has a camera object but nothing ever
moves it, because `GameCamera.UpdateCamera` returns as soon as it finds no local player. So the
server drew its weather for wherever the scene left that camera. Measured once as a wind range of
`0.10-0.30` on the server against `0.10-0.50` on a client standing in the same biome.

`Waves.ServerWeatherFollowsPlayers` answers `GetBiome` and `UpdateEnvironment` from a connected
player instead. The lowest peer id is used rather than the nearest or the first, because it is stable
and the server's weather should not flip every time the peer list is reordered. With
`Waves.ServerWeatherPerPosition` on, this global is only a fallback: everything the server
simulates asks for the weather at its own position, as described next.

### One weather for the whole world

Vanilla resolves one weather per machine, at its camera, and everything that machine simulates reads
it. That is correct for a client, which only simulates what is around its own player. The server
simulates every zone around every player at once, so with one weather:

- A raid on one player changed the weather for everyone. The server's active event is set whenever
  any player is in a raid, and vanilla then applies the raid's forced environment whenever the
  viewpoint's biome is in the raid's mask, however far away. Five raids force a weather:
  `army_bonemass` and `blobs` (SwampRain), `army_moder` (Snow), `ghosts` (wind 0, so the sea went
  flat and windmills stopped) and `surtlings` (`Ashrain`, which matches no environment).
- The six weather-gated spawners (the Neck in rain, Draugr in mist, the Serpent in storms, three
  Ashlands cinder spawners) followed one player's sky.
- Rain wear, fireplaces, loose fires, cinders and windmills everywhere followed it too, and so did
  the wind and waves under every hull the server floats.

`Waves.ServerWeatherPerPosition` answers "what weather would a player standing here settle on"
instead (`LocalWeather.cs`). It mirrors `EnvMan.UpdateEnvironment` and `GetBiome` with the camera
replaced by the position: the biome sector at the point itself (sectors lie on a 12m grid, so a coast
is resolved where it actually runs rather than per 64m zone), switched to the Ashlands or Deep North
sector over their sea only when a heightmap under the point says so, as vanilla does. It calls
vanilla's own `GetAvailableEnvironments` and `SelectWeightedEnvironment` so a retuned table comes
along with a game update, and applies the overrides in vanilla's order: a forced environment or a
forcing `EnvZone`, the debug environment, a raid, the alternate biome's forced environment (read from
the unswitched sector, as vanilla reads it), a persistent event, an unforced `EnvZone`. A raid only
counts where a client standing there would take it: inside the raid area by vanilla's own test, and
with the switched sector's biome in the raid's mask. The scheduled pick is cached per switched sector
and sea flags per weather period; the overrides are a few distance checks and are evaluated on every
call, so a raid starting or ending takes effect at once.

Where Valheim Creatures is installed with `Raids.Waves` on, it takes a raid over on the server
before vanilla's own event is ever set there, so vanilla's raid alone would miss
it entirely: the weather that clients see for it would never reach the server's fires, spawners or
wind. `CreaturesRaids.cs` finds Creatures by its BepInEx GUID at runtime and binds its raid list by
reflection, with no compile time reference either way, and `LocalWeather.RaidEnvironment` tests each
of its raids the same way as vanilla's. Several raids can cover one position at once, on either side
or both; a client only ever shows the one nearest to it, so the nearest to the position wins here
too. Without Creatures installed, or with `Raids.Waves` off, this is a no-op and raids behave exactly
as described above.

The consumers are not rewritten. Each one's entry point is wrapped in a `WeatherScope`, which loads
the position's weather into exactly the EnvMan state a client standing there would have (wet, cold,
freezing, daylight, the current environment, the wind) and puts the global back afterwards. The
consumer runs vanilla code unchanged, including any weather check a game update adds to it; what can
break is the list of entry points, which `PatchCheck` verifies. Wrapped: `SpawnSystem` (for its own
zone), `WearNTear.UpdateWear` and `UpdateCover`, `Fireplace.CheckWet` and `UpdateIgnite`,
`Fire.UpdateFire`, `Cinder`, `Windmill.GetPowerOutput`, `Fish`, `Leviathan.FixedUpdate`,
`WispSpawner.GetStatus`, `LuredWisp.UpdateTarget`, `Ship.CustomFixedUpdate`, and
`WaterVolume.UpdateFloaters` per floater, which is where floating objects and swimming characters get
their water level. The two called for every piece every second open their scope only on the calls
that read it: `UpdateWear` for a piece owned here and past its settling time, `UpdateCover` on the
call whose timer passes four seconds.

Wind per position keeps `DeterministicWind`'s anchors (`LocalWind.cs`). The noise depends only on the
anchor and is shared. With `BlendedWeatherWind` on, each anchor's range is the blended range at the
position for the anchor's time, so a floater's pair is a pure function of where it is; a server draws
no vegetation, so nothing there needs the pair held. With it off, each weather in use gets its own
constant pair. Each ship gets its own anchors, taken once when they come into use and held, as each
client aboard holds its own, with the range its owner published ahead for the crew and Moder's heading
as described under [Waves](#waves).

The server's global weather is now just this answer at the lowest-id player, and never takes a raid's
weather unless that player is inside the raid. Only what still reads the global follows it: creatures
sliding on ice, a few daylight-only effects, and the `WaveSync field` line.

What it cannot see, the same as vanilla cannot: which weather a client was already in when an
override named an environment that does not exist (the surtling raid), which is approximated by the
scheduled weather there; and the intro cutscene. The ten second blend between weathers, which a
client has and a position does not, is replaced for the wind by the blend described next; for
everything else a position keeps its discrete weather. Each machine still renders the whole sea with
one wind, so a player watching a boat from another biome sees their own biome's waves under it,
exactly as in vanilla.

**Diagnostics**, all on by default: one `Local weather for period N` line per weather period listing
the weathers resolved and for how many biome sectors; a `Raid ... forces weather` line when a raid with
a forced weather starts; a `Server global weather` line when the global changes; `Moder's power
turns ship ...` and `no longer steers` on the server; the `WaveSync hull` line now names the sea it
was measured on (`sea=hull weather=... wind1=... wind2=... moder[N]=...` on the server, `sea=global`
plus the server's published Moder slots on a client aboard). A client line reporting `missing` for
more than a moment while the server's reports a heading is the handoff failing.

### Wave strength blended in space and time

Wave height scales with wind intensity, and each anchor's intensity is vanilla's noise scaled into
the `m_windMin`-`m_windMax` range of a weather. In vanilla that range comes from the current
environment, which is local state: when the weather changes, or the camera crosses into another
biome, `InterpolateEnvironment` blends the old weather's values into the new one's over
`m_transitionDuration` (10 seconds in the shipped `EnvMan`), starting whenever that machine noticed.
A position on the server has no such state, only its weather, so at every weather period boundary
(every 666 seconds) and every border between weathers a hull and its crew drew wave strength from
different numbers until the client's blend finished.

`Waves.BlendedWeatherWind` defines the range instead as a function of a position and a time of world
clock (`WindRange.cs`, arithmetic in `WindBlend.cs`):

- **Transient overrides are applied unblended**: the debug and forced environments, an `EnvZone`
  (dungeon interiors), a raid and a persistent event, at the position itself. Their start and end
  reach each machine at a different moment (a raid's start time and position arrive by the
  `SetEvent` RPC, with latency, and its local timer is resent and overwritten), so no blend of them
  could be made to agree; the anchors' own ten second cross-fade turns their step into a ramp.
- **Everywhere else the world's own weather is blended in space**, bilinearly between the four points
  of a 32m grid around the position. Each grid point holds the weather the world gives it for the
  period (the alternate biome's forced environment or the scheduled pick, evaluated at sea level),
  so across a border the range ramps over one 32m cell instead of stepping. The cell is a little wider
  than the 12m biome grid, so a staircase coast reads as one ramp.
- **And in time**: over the first `m_transitionDuration` seconds of a weather period, from the
  previous period's grid weathers to the new ones, in a straight line, as vanilla's own
  interpolation does, but starting exactly at the period boundary.

Each anchor's range is evaluated for the moment the anchor starts to blend in, so a machine that
takes it a frame late still takes the same number. Fog, rain, light, sound and everything else keep
vanilla's blend at the camera; wet, cold and daylight consumers keep the discrete weather.

**Where it is evaluated.** A client not aboard a ship evaluates it at its local player, the place
vanilla judges the edge of the world from (the camera would do as well; it is a few metres away, and a
player is what the server knows the position of). The server evaluates it per floater at the
floater's position, and per hull at the hull.

**Aboard a ship everyone uses the hull's.** The function agrees to the bit for the same inputs, but
nobody agrees on where a moving ship is: the owner reads its rigidbody, everyone else a transform that
trails it by the interpolation lag, and inside a 32m ramp a few metres is enough to differ. So the
hull's owner, the server or a client that owns the ship it is steering, writes the range it used for
each anchor onto the ship's ZDO (`HullWindRange.cs`, three slots by anchor number), and everyone
aboard builds from that. It is written one anchor early: when anchor N comes into use the owner
evaluates N+2 at the hull and writes it, so it has arrived everywhere long before anyone takes it.
Nobody ever has to take an anchor's range from a guess and correct it later, which would move the
strength of an anchor in use and bring back the vegetation sway fault. The price is that wave
strength follows where the hull was one anchor (10 seconds) earlier and then cross-fades over the
next, 10 to 30 seconds after a crossing in all, which is the same order as vanilla's camera-triggered
blend followed by a latched wind target. A new owner carries on from the previous owner's slots.

What changes for a player compared with vanilla: wave height and wind strength near a biome border
ramp over 32m rather than switching when the camera crosses, a weather change reaches the wind at the
period boundary rather than when each client noticed, and aboard a ship the waves follow the hull's
weather rather than the camera's. Vegetation, cloth and particle wind follow the same range, since
they read the same wind. Nothing else looks different.

**Diagnostics**, on by default. The `WaveSync field` line ends with
`anchorRange[N]=min-max(Source)` for both anchors in use, where `Source` is `ShipPublished` (the
owner's number), `ShipOwn` (this machine owns the ship and evaluated it), `ShipLocal` (aboard, nothing
published, evaluated at this client's view of the hull), `Viewpoint` or `Vanilla`, and, when this
machine evaluated the second anchor itself, `rangeInputs[N]:` with the position, time, the discrete
weather there and why, the period, the time weight `w`, the cell and fractions, and the four grid
weathers (and the previous period's while `w < 1`). The `WaveSync hull` line carries
`range[N]`, `range[N+1]` and `range[N+2]` as published on the ship, on every machine; on the server it
also carries `rangeInputs[N+2]:` for the one it just wrote. A client that had to evaluate a range the
owner had not published logs `No wind range published for anchor N on ship ...` once; a server that
found its own slot empty logs `Hull wind range for ship ... was not published ahead`.

### The waterline effects were nailed to the hull

`ShipEffects.m_shadow` points at a child called `WaterSurface`, which on a karve sits at a fixed
local `(0, 0.55, 0)` with identity rotation and carries both things a crew reads as the boat
touching the water: a mesh called `shadow`, the dark patch under the hull, and a particle system
called `vfx_water_surface`, the foam ring at the waterline. Nothing in the game ever moves or
rotates that transform — `ShipEffects` only calls `SetActive` on it.

Rigidly parented, it inherits the hull's heave *and* its pitch and roll, so it tilts with the boat
rather than lying flat on the sea, and holds one fixed height while the hull's real waterline moves.
Measured in mild swell on the live server, the hull rode between 0.72m and 0.94m into the water. The
patch therefore lifts clear at one end and sinks under at the other.

This is vanilla, and identical on every machine, so it is not a sync fault. It only became
noticeable once the hull itself was being placed correctly. `Waves.LevelShipWaterline` puts it where
its name says it is: on the water surface, level, keeping the hull's heading, with its height from
the same `Floating.GetWaterLevel` that buoyancy uses so the two cannot disagree.

The foam needed a different fix, because moving its emitter does nothing. Read out of the longship
prefab, `vfx_water_surface` emits 40 particles a second over a flat 5m x 20m ellipse, each living 2
seconds, and it simulates in **world space** (`simulationSpace: 1`) with a start speed of zero, no
gravity, and the velocity, force, noise and inherit-velocity modules all disabled. Every particle is
therefore frozen in world space for its whole life at whatever height the emitter had when it was
born, and the emitter's height is the hull's. So the ring traces the hull's heave, which buoyancy
damps and delays, rather than the sea's. In any real swell that reads as foam following a wave that
is not there, which is exactly what it is.

An earlier version moved the shared `WaterSurface` parent and made this worse rather than better: it
drove the emitter up and down the swell while the particles it had already laid down stayed put, so
the foam scattered from below the hull to above head height. The particles were never attached to
the emitter, so the emitter is not where the fix goes.

`Waves.LevelShipWaterline` now rewrites each live particle's height from the same
`Floating.GetWaterLevel` the hull and the shadow mesh use, every `CustomLateUpdate`. Roughly eighty
particles are alive at a time, so that costs about as much as one more buoyancy sample per boat, and
every other property of the effect is left as vanilla authored it. The emitter is levelled too,
which only matters because the emission shape is a flat ellipse: tilted with the hull it
foreshortens where along the boat particles are born.

Wave height is a function of x and z alone — `CreateWave` reads only `worldPos.x` and `worldPos.z`,
and `Depth` reads the point's position across the volume — so the sample uses the emitter's own
height to find the `WaterVolume` rather than the particle's. A particle left above the volume's
collider by the previous frame would otherwise match no collider and never be brought back down.

Only world-space systems are touched. A local-space one already rides its emitter, so levelling the
emitter is the whole fix there and rewriting positions on top of it would apply the correction
twice.

### Perfectly on the water looks fake

Putting every particle exactly on the surface is correct and looks wrong. The ring becomes one rigid
sheet, conforming perfectly and moving in perfect step, which reads as a decal laid on the water
rather than as foam floating in it.

`Waves.ShipFoamSpread` (default `0.75`, metres) gives each particle a fixed direction and distance
to read the sea from, instead of reading it at its own position. The short wave components are only
a few metres long — the shortest five have `waveLength` 1.0 to 1.5, which is about 4m — so
neighbours a metre apart genuinely sit at different heights and rise and fall slightly out of step.
Because `CalcWave` multiplies the whole sum by the wind intensity, this scales itself: near flat in
a calm, churned in a storm.

`Waves.ShipFoamLift` (default `0.07`, metres) is the part that does not depend on the weather, a
fixed small height above the surface, different for each particle. `ShipFoamSpread` goes to nothing
as the sea flattens, which is right for the swell but would leave a dead calm looking like a painted
ring again.

The ring's terms are **upward only**, so it never dips under the surface it is supposed to be lying
on. The wake's are not — see below.

Both are derived from `ParticleSystem.Particle.randomSeed`, which is assigned at birth and never
changes, so the scatter is stable rather than boiling frame to frame. It is the only per-particle
identity available, because the array `GetParticles` returns is not in a stable order and an index
cannot be used. Hashing it locally also keeps this off `UnityEngine.Random`, which `EnvMan`'s wind
octaves seed and read on the same frame.

Set either to `0` to turn that term off; `ShipFoamSpread = 0` also saves the second water sample per
particle.

### The speed wake was the other half of it

With the ring sitting correctly, the wake stood out as a second, solid layer that disagreed with it.
`SpeedWake` is a sibling of `WaterSurface`, not a child, so the first fix never reached it.

It holds two species, and only one of them belongs on the water. From the longship prefab:

| system | shape | start speed | gravity | life | size | verdict |
| --- | --- | --- | --- | --- | --- | --- |
| `aft_particles` | disc, r1.51, flattened | 0 | 0 | 2s | 7 | sheet |
| `front_particles` | sphere, r0.64 | 0 | 0 | 1s | 5 | sheet |
| `Trail` | circle, r0.10 | 0 | 0 | **10s** | **8.05** | sheet |
| `GameObject`, `GameObject (1)` | cone, r1.20 | 1-2 m/s | 0.03 | 2s | 1 | spray |
| `rudder` | cone, r0.50 | 1-2 m/s | 0.03 | 2s | 0.5 | spray |

`Trail` is why it read as solid. At rate 5/s with a ten second life, fifty size-8 patches overlap at
once, each pinned at the height the hull had up to ten seconds earlier. Stacked at their own fixed
height beside a ring that was now correct, they formed a slab.

The three sheets get the same treatment as the ring, scatter included, which is what breaks the slab
up into churn. The three spray systems are left exactly as vanilla wrote them: they emit from cones
at 1 to 2 m/s under gravity with a velocity clamp, they are meant to arc through the air, and
flattening them onto the water would be wrong.

The test is the behaviour rather than the name — **a system that nothing moves after birth is a
sheet; a system with speed or gravity is spray**. Concretely: world simulation space, a start speed
and gravity multiplier of zero, and no velocity, force, inherit-velocity, external-forces or noise
module enabled. That survives Iron Gate retuning the prefabs, which a list of names would not.

### The wake reads as a speedboat's, and why the obvious cause was the wrong one

Placing the wake correctly did not make it look right, because its density is structural rather than
positional. `Trail`'s alpha curve is:

| life fraction | alpha |
| --- | --- |
| 0.00 | 0.0 |
| 0.03 | **1.0** |
| 0.65 | **1.0** |
| 1.00 | 0.0 |

Full opacity from three percent of a particle's life to sixty-five percent. Combined with a ten
second life, a rate of 5/s and a size of 8, that is fifty overlapping patches of which most are at
full alpha — roughly fifty metres of solid white astern before the fade even begins.

`Waves.ShipWakeTrailSeconds` caps how long a wake sheet may live, and it **defaults to `0`, off**,
because that reasoning was wrong. Capping the life to 3.5s shortened the wake and did nothing at all
for how solid it was. The density was never the cause; see the next section for what was.

The setting is kept, because it does what it says and a shorter wake is a reasonable thing to want.
The alpha curve is normalised over the lifetime, so capping the lifetime compresses the whole fade
rather than truncating it, and the overlap count falls in proportion. Only systems authored longer
than the cap are touched, which leaves the one and two second sheets at the waterline and the bow
exactly as they are.

### The wake belongs under the water, not on it

Shortening the trail made it shorter and no less solid, and the reason was a mistake of mine rather
than anything in vanilla. Vanilla emitted the wake at a fixed height on the hull and let the hull's
own heave scatter it through the surface, so some of it was always part submerged and the mass broke
up. Putting every particle exactly on the water — and biasing it upward so none could ever go under
— threw that variation away and left one opaque sheet.

`Waves.ShipWakeSink` (default `0.35`, metres) gives each wake particle a depth of its own, fixed for
its life, from the surface down to that depth. It also fades the particle toward a quarter of its
opacity at the bottom of that range, so deep foam reads as foam seen through water. The fade is done
by writing the particle's `startColor` rather than by relying on the water to dim it, because
whether a submerged particle is actually dimmed depends on how the water surface and the foam
renderer sort against each other, and that is not something to build a look on.

For the same reason the wake takes whatever height `ShipFoamSpread` finds nearby, where the ring only
ever takes one above its own: the ring may not sink and the wake may.

**The ring at the waterline is deliberately not part of this.** It is sitting on the surface because
that is where it belongs, and it looks right there.

### Verifying it yourself

`Debug.WaveSyncIntervalSeconds` logs the three inputs on a server and a client alike, keyed by the
whole second of world time so the two logs can be matched line for line. It is silent unless a boat
is instantiated, which is what lets it default to on. The `probe=` field is wave height at a fixed
point, a fixed depth and a fixed second, computed through `CalcWave` directly, so everything
position and time dependent is identical by construction and the only input left is the wind.

Result on the live server, before and after:

| | before | after |
| --- | --- | --- |
| probe difference, same second | `-0.1972` vs `-0.0549` | **`0.0000`** |
| wind intensity difference | `0.240` vs `0.067` | none |
| `shaderLead` | `0.184` on both | **`0.000`** on both |

To confirm the measurement can still see a real difference, pin `Debug.ForceEnvironment` to
`ThunderStorm` on the server only. The server then reports a wind range of `0.80-1.00` and a probe
swinging +/-2.60m against the client's `0.10-0.60` and +/-0.49m, which is the original fault
reproduced on purpose. Set it identically everywhere to test a storm honestly.

## Boats destroyed by nothing

A boat left in open water, or sailed into it, could be found on the sea floor having beaten itself
apart against it. It had resisted five rounds of investigation: five candidate mechanisms were
proposed and eliminated on a live server, three unrelated faults were found and fixed along the way,
and none of them reproduced the destruction.

The cause is one missing guard in `Floating`:

```csharp
private static int s_waterVolumeMask = 0;       // starts at zero

private void Awake() {                           // needs an INSTANCE of a Floating component
    s_waterVolumeMask = LayerMask.GetMask("WaterVolume");
}

// GetLiquidLevel, line 236
if (s_waterVolumeMask == 0) s_waterVolumeMask = LayerMask.GetMask("WaterVolume");   // guarded

// GetWaterLevel, line 279 -- the one Ship.CustomFixedUpdate uses
Physics.OverlapSphereNonAlloc(p, 0f, s_tempColliderArray, s_waterVolumeMask);       // NOT guarded
```

An overlap query with a layer mask of `0` matches no layers, so it finds no colliders and
`GetWaterLevel` returns its "no water here" value of `-10000`. `Ship.CustomFixedUpdate` computes

```
num2 = centreOfMass.y - averageWaterLevel - m_waterLevelOffset
```

which comes out at about `+10000`, far above `m_disableLevel`, so every line of buoyancy, damping,
sail and rudder force is skipped while the rigidbody keeps its gravity. The hull falls out of the sea
and `ImpactEffect` costs it a full strength self hit each time it bounces on the bottom.

This is why it looked intermittent. The mask is process-wide and, once set, stays set. Near a shore
or a base something with a `Floating` component — a dropped item, a felled log, a corpse — awakes
within seconds and silently repairs it for the rest of the run. Out in open ocean on a freshly
started server, nothing does. Vanilla never meets it either way: a vanilla server has no ship
instantiated to float, and a client has a local player whose swim checks reach the guarded call site
almost immediately. Only a server that owns hulls and has no local player can get there.

`WaterQueries.EnsureLayerMask` sets the mask when the server session starts and logs what it found,
so a recurrence names itself. It is idempotent, because `Floating.Awake` assigns the same value.

Worth noting what did **not** find this. `Vehicles.HoldHullsUntilWaterLoads` exists to stop exactly
this fall, and it waved both hulls straight through, because it asks `ZoneSystem.IsZoneLoaded` rather
than asking whether the water actually reads. The diagnostic said `zonesUnderHull=all loaded` and
`0 of 5 zone(s) missing` while the water under every one of those five points read `-10000`.

The measurement that settled it, from the `diag` spawn request:

```
GetWaterLevel returned -10000.00
25 WaterVolume instance(s) in the scene
1 collider(s) on the WaterVolume layer at that point      <- a collider IS there
GetComponent says live WaterVolume 'WaterVolume'           <- and it is alive
live WaterVolume 'WaterVolume' covers this point, surface 29.87
cache has no entry; cache size 0                           <- the decisive line
```

An empty collider cache after minutes of a ship querying the water five times per physics step can
only mean the loop body never ran, which only happens when the overlap query matches nothing. That
ruled out a first hypothesis, a stale entry in `Floating`'s never-invalidated
`Dictionary<int, WaterVolume>`, and pointed at the mask instead.


## Responsiveness

Once the server owns an object, every interaction with it is a round trip, and two rates Valheim
hardcodes decide how long that trip takes. Neither matters on a vanilla server, because a vanilla
server never simulates anything.

- **The server runs at 30 FPS.** `targetFrameRate` is hardcoded in `GraphicsSettingsManager` and
  applied in `PresentManager`. That sets the floor on how fast the server notices anything at all:
  an interaction waits up to a full 33ms frame before it is even read.
- **Object updates flush at 20Hz.** So objects the server spawns, such as the items a picked bush
  drops, wait up to another 50ms to reach the client.

Together that is roughly 100ms with no network involved, which is enough to feel. Measured on a
loopback server, picking went from noticeably laggy to fine by raising `ServerFrameRate` to 60
alone. The cost is CPU: one player at simulation distance 2 took a Valheim server from 36% to 73%
of a single core. That is nothing on a desktop and may matter on a small VPS, so the knob is there
to turn back down.

`ZdoSendRate` is left at vanilla's 20 by default because raising it costs bandwidth for every
client. Raise it if server-spawned objects appear later than you would like.

None of this removes the round trip, it only shortens it. A round trip to the owner is inherent to
moving ownership, which is exactly why `Contested` mode gives a lone player their surroundings back:
solo, they pay nothing at all. `Always` mode is the worst case for latency by design.

## What the server cannot do

The mod's founding assumption is that the server can simulate anything a client can. That is false,
and every serious bug found so far has been an instance of it. The server is an owner that does not
have the local state vanilla quietly assumes an owner has. Seven kinds, nearly all found by playing:

1. **No local player.** `Player.m_localPlayer` is null, so owner-run code and `Everybody` RPCs that
   touch it throw. See [The local player trap](#the-local-player-trap).
2. **No local component state.** A networked flag says "this is in use" while the mechanism behind
   it is a local object that only the machine really doing the work has.
   - `Vagon.IsAttached()` reads a networked flag, but the joint `m_attachJoin` is local. An owner
     without the joint concludes the cart is attached to nothing and detaches it, ripping the cart
     off whoever is pulling it, within a frame.
   - `ShipControlls.HaveValidUser()` is a networked user id **and** `Ship.IsPlayerInBoat()`, which
     walks `m_players`, a list built by `OnTriggerEnter`. A server whose triggers disagree decides
     nobody is at the helm and takes the boat back mid-voyage. The fix is to ask the replicated
     character positions instead, which is what `Sadle` already does and why mounts were never
     affected.
3. **Nothing. The boat failure was this mod's own bug**, and it is worth recording because the
   first two explanations written here were both wrong. A server-owned hull sank, tumbled and
   exploded, which was blamed first on the server having no water volumes and then on an
   unidentified fault. Instrumenting it settled both: water lookups failed 3 times in 376 samples,
   all in the first frames after spawn, and ownership of the crewed hull was flipping between the
   server and the driver every two seconds. Two machines taking turns simulating one rigidbody is
   what destroyed the boats.

   The cause was an id-space confusion in `FindDriver`. `ShipControlls.GetUser()` returns a
   persistent **playerID**, while `ZDOID.UserID` is a **peer session id**; comparing them never
   matches, so no driver was ever found, no lease was ever granted, and the sector policy reclaimed
   the hull on every tick. `Player.GetPlayer(playerID)` does the lookup in the right space against
   instantiated players whose ids come from their ZDOs, so it works on a server. Mounts had the same
   latent bug and are fixed the same way.

   The 3 failed water lookups in that instrumentation run were written off as noise at the time.
   They were not. See the next item.

4. <a id="a-server-owned-hull-needs-its-neighbourhood"></a>**No loaded neighbourhood.** A client
   never simulates anything until its whole active area exists, because
   `ZNetScene.CreateObjectsSorted` returns early unless `IsActiveAreaLoaded`. The server cannot use
   that gate, since one player still loading would stall object creation for everybody, so it
   creates each object as soon as that object's own zone is ready. Anything that reads the world
   beyond its own zone can therefore run against a half built neighbourhood.

   `Ship` is the case that kills. It samples the water at five points spread across its float
   collider, which on a longship is 8 by 17 metres, and the `WaterVolume` in the zone prefab is a 64
   by 64 box that tiles one zone exactly with no overlap, so bow and stern routinely sit in the
   neighbouring zone. `Floating.GetWaterLevel` answers **-10000** for a point with no water volume
   on it, so one missing neighbour pulls the five point average to about -2000 metres, the hull
   concludes it is two kilometres above the sea, and `Ship.CustomFixedUpdate` skips every line of
   buoyancy, damping and sail force in one `if`. Gravity is untouched, so the hull falls out of the
   ocean.

   **The visible symptom**, reported by a tester before any of this was understood: a moored boat
   **jumps about when you log in near it, or walk in far enough to activate its physics**. That is
   this fault, seen from the outside. The hull activates, reads -10000 for part of itself, falls, and
   is then thrown back up when the missing zone arrives. `Patches/ShipPhysicsPatches.cs` holds a
   server-owned hull still, in place, whenever any of the five zones it is about to measure does not
   exist, and releases it unharmed once they all do. It asks about zones rather than about water on
   purpose: a beached hull also reads -10000, and there vanilla's answer, falling, is the correct one.

   **How bad it gets is a question of duration, and the honest answer is that it varies.** A hull
   that loses buoyancy for the length of a zone-load race, around 0.6s, falls about 1.7m and bobs
   back: measured with the gate disabled, and it cost nothing. But a hull was also observed sitting
   on the sea floor, `y=24.47` against a seabed of `23.9`, with `bow zone MISSING`, having lost 30 of
   its 500 health. So it does reach the bottom and it does cost health. The long windows appear to
   belong to a client-owned hull rather than a server-owned one, and were not pinned down.

   Three corrections to earlier drafts of this section, all found by testing it rather than reading:

   - A draft claimed a *permanent* hole where a hull at the edge of the active area has a sample point
     in a zone that is never built. The geometry does not allow it for a **server-owned** hull:
     `ZNetScene.InActiveArea` caps it at 112m from a player's zone centre, and 8.5m of hull reach
     still leaves every containing zone's centre inside the 160m near radius, because zone centres
     quantise to 64m. The hole does exist beyond ~160m, where the server has the hull but not its
     neighbour zone, and is harmless there because nothing owns it. A client instantiates only after
     `IsActiveAreaLoaded`, so the two bands never overlap on a client either.
   - Holding the hull by zeroing its velocity lets the rigidbody fall **asleep**, and a sleeping body
     gets no gravity. Vanilla only calls `WakeUp` inside the buoyancy block, which is skipped
     whenever the hull rides above `m_disableLevel` — a wave trough is enough. A hull released while
     asleep and above the surface hangs in the air until some later wave runs the block for it.
     Observed doing exactly that by a tester looking at it, while the log showed `vel=0.0` and every
     zone loaded, which is indistinguishable from resting on calm water. `Release` now calls `WakeUp`.
   - The buoyancy fault is **not** what destroyed the boat that started this investigation. Five
     mechanisms were proposed and eliminated: the zone-load race (too short), a permanent geometric
     hole (does not exist), sinking out of the `WaterVolume` box below y=-20 (the seabed there is at
     24.1, only 5.1m of fall), raid creatures (the boat was already destroyed before any spawned),
     and a storm driving it aground (the impacts turned out to be the tester moving it off land).
     The cause is still unidentified. See [Known Bugs](#known-bugs).

5. **Zones that vanish under a player's feet.** Vanilla leaks here and the leak is the one path
   that reaches a moored boat without the mod's help. `CreateLocalZones` resets the time to live of
   each near zone as it walks its list, but returns the moment it spawns one, so everything later in
   that list keeps ageing. A moving player spawns a zone almost every tick, starving the tail
   indefinitely, and `UpdateTTL` then destroys any zone past `m_zoneTTL` whose sector holds no
   instance. `ZNetScene.HaveInstanceInSector` only protects a zone that *contains* something, so an
   open stretch of water beside a moored boat is precisely what gets thrown away — taking its
   `WaterVolume` with it.

   The consequence is the buoyancy fault above, arrived at from the other direction, and it explains
   why it is invisible in calm water: the `-10000` skips the block that holds the only `WakeUp` call
   in `Ship`, so a sleeping hull just sits there. In a storm the waves keep the body awake every
   frame, and an awake body with no buoyancy falls. `KeepNearZonesAlive` in
   `Patches/ZoneSystemPatches.cs` resets every near zone's ttl before anything is built. It touches
   `m_zones` directly rather than calling `PokeLocalZone`, because that method spawns a zone it does
   not find, and calling it across the whole radius would build a neighbourhood in one frame instead
   of one zone per tick. The mod also made this worse than vanilla: `ZoneEvictionsPerTick` defaults
   to one eviction per player per tick against vanilla's one.

6. **No meaningful reference position.** `ZNet.GetReferencePosition()` is the origin on a server,
   and code keyed off it silently does the wrong thing rather than failing. `ZoneSystem` and
   `ZNetScene` are the obvious two and are patched, but `SlowUpdater` is a third: it hands every
   `SlowUpdate` instance the zone of that reference position, and `StaticPhysics` gates its falling
   and settling on whether it is inside the active area around that zone. Everything in the world is
   far from the origin, so every static physics object concluded it was out of range and never ran,
   which would leave felled logs never settling. Found by reading rather than by it failing, so
   unlike the rest of this list it was fixed before anyone saw the symptom. `Plant` is the only
   other `SlowUpdate` and ignores the argument, so crops were never affected.

7. **One weather.** Vanilla resolves weather once per machine, at its camera, and a client only
   simulates what is around that camera. A server simulating every player's surroundings at once
   needs the weather at each position instead, or one player's raid puts out every fire in the
   world. See [One weather for the whole world](#one-weather-for-the-whole-world).

**When hunting the next one, look for vanilla code that reads local component state**, or that keys
off `GetReferencePosition()`, rather than
for ownership bugs. The ownership policy itself has not been the problem.

## Patching RPC methods can kill the server

Valheim dispatches RPCs through `Delegate.DynamicInvoke`, so an RPC method is reached by reflection
rather than by a direct call. Harmony replaces a patched method with a dynamic method, and Mono can
fail to invoke that through reflection, raising
`BadImageFormatException: Method has zero rva`. Worse, Unity then formats the exception, and Mono
aborts inside `StackTrace.ToString` with `Assertion at metadata.c:1381` while reading parameter
metadata off the dynamic method. That is a hard process abort, not an exception: the server dies.

This happened on a live server on `Container.RPC_RequestOpen`, after the same patch had worked for
an hour, so treat it as a latent hazard rather than a deterministic bug. The patch surface on RPC
methods has been cut to the four that prevent guaranteed crashes: `Pickable.RPC_Pick`,
`Trap.RPC_OnStateChanged`, `Leviathan.RPC_Left` and `MusicVolume.RPC_PlayMusic`. Those are still
exposed to the same hazard and there is no way to fix what they fix without patching them, since
the null dereference happens at the call site.

Everything optional was removed. The container and vehicle handover patches only covered the
roughly 100ms gap between the server handing an object to a client and that client writing back the
flag that says so. `OwnershipPolicy` now covers that with a grace period on any object a connected
peer has just claimed, which needs no RPC patching at all and covers every such handover uniformly.

**Do not patch an RPC method unless nothing else will do.**

It is not confined to RPC methods, and it is not confined to the server. Two further sightings:

- **`ZNet.Disconnect`, server side.** `IntegrityServer` had a prefix on it to forget a peer's
  session. The first time a player ever quit to the desktop while the server kept running, Mono
  raised the same exception building the replacement, every frame, forever: `ZNet.UpdatePeers` calls
  `Disconnect` for the closed socket, the call throws, so the peer is never removed from `m_peers`
  and the next frame tries again. One quit produced 5012 exceptions and the slot never freed, so the
  player could not reconnect. `Disconnect` reaches `peer.Dispose()` and so `ISocket.Dispose` and the
  Steam native socket, and a P/Invoke has no IL body to work from. The patch is gone;
  `IntegrityServer.DropClosedSessions` reconciles sessions against `ZNet`'s own peer list from the
  `ZNet.Update` postfix instead, which needs no patch on the hazardous method and cannot be left
  half done by an exception.
- **Client side, during connect.** Once seen on a client while joining, starting in
  `ZRpc::HandlePackage` dispatching an RPC by reflection, with the inner frame inside Mono's own GC
  write barrier icall. From that point *every* Harmony dynamic method in the process failed:
  `WaterVolume::StaticUpdate` 48137 times, `Game::FindSpawnPoint` 8695, `SpawnSystem::UpdateSpawning`
  4009, and so on. The login hung because the character sync RPC could not be delivered and
  `FindSpawnPoint` held the spawn. A relaunch cleared it, and the previous run of the same build had
  only the usual harmless shutdown occurrences.

So treat it as a property of the runtime rather than of any one patch: once it starts, it poisons
every patched method at once, and the method with the highest count is merely the one called most
often. The defence is to keep the patch surface small, which is a real cost to weigh against what a
patch buys.

## The local player trap

The single most common way this mod breaks the game is code that dereferences
`Player.m_localPlayer`, which is always null on a headless server. There are two distinct flavours
and the second one is easy to miss:

1. **Owner-run code.** Anything gated on `IsOwner()` now runs on the server. Vanilla's authors could
   assume the owner was a client with a local player. `SpawnSystem.UpdateSpawning` is the classic.
2. **`Everybody` RPCs.** These are the subtle ones. On a vanilla server they are harmless, because
   the server holds ZDOs but never instantiates the GameObjects, so `HandleRoutedRPC` finds no
   `ZNetView` and the method never runs. This mod gives the server those instances, so every
   `Everybody` RPC that touches the local player starts throwing.

Found and fixed so far, in `Patches/LocalPlayerRpcPatches.cs`: `Pickable.RPC_Pick` (broke picking
entirely: it threw before the drops spawned, so no items and the bush never went picked),
`Leviathan.RPC_Left`, `MusicVolume.RPC_PlayMusic` and `Trap.RPC_OnStateChanged`. In
`Patches/HeadlessPatches.cs`: `Ship.UpdateSailSize`, which reads
`Player.m_localPlayer.GetPlayerID()` with no guard to spawn the sail-change effect and therefore
throws once a frame for the whole of every raise and lower of a sail.

There is a third flavour, and it is quieter than either of these because it throws nothing at all:

3. **Static state that a local player would have initialised.** `Floating.s_waterVolumeMask` starts
   at zero and is filled either by an instance of a `Floating` component awaking or by a call site
   that happens to guard itself. On a client a player's swim checks reach that guard within seconds
   of spawning; on this server nothing does, and `Floating.GetWaterLevel` then silently reports no
   water anywhere and sinks every boat. See [Boats destroyed by nothing](#boats-destroyed-by-nothing).
   When hunting these, a missing null check announces itself in the log and a missing initialisation
   does not, so look for statics whose only writers are `Awake` methods or guarded call sites.

To find more after a game update, scan the decompiled source for `Player.m_localPlayer`
dereferences inside `RPC_*` methods that have no null guard, then check how each RPC is invoked:
ones sent to a specific peer are client-only and safe, while owner-targeted and `Everybody` ones
now execute on the server.

## Game updates

Valheim 1.0 reworked the zone system substantially and the mod was ported to match. What changed,
for whoever has to do this again:

- Zone coordinates went from `Vector2i` to `Vector2s`, and `ZoneSystem.m_activeArea` /
  `m_activeDistantArea` were replaced by `SimulationDistance`, a server-synced value read through
  `ZNet.GetSyncedSimulationDistance()`.
- Whether a point is in an active area is now a continuous distance test
  (`ZNetScene.InActiveArea(position, zone)`) rather than a box of zone indices, so the ownership
  policy measures coverage per object position and resolves one decision per sector from it.
- `SpawnSystem.UpdateSpawning` gained a third spawn source, the alt-biome spawners on
  `Heightmap.m_cornerAltBiomes`, plus `groupSalt` arguments. Missing this silently disables part of
  the world's creature spawning, which is exactly the kind of drift a whole-method replacement is
  meant to make visible.
- Mounts (`Sadle`) and carts (`Vagon`) hand ownership to their user and never renew it, so they now
  take the same lease treatment ships already had.
- `ZoneSystem.Start` is patched to apply the simulation distance, because
  `ZNet.ApplySimulationDistance` skips ZoneSystem when its instance does not exist yet and on a
  server that handshake runs during `ZNet.Awake`.

`ZoneSystem.Update`, `RandEventSystem.FixedUpdate`, `ZNetScene.RemoveObjects` and the ownership pass
in `ZDOMan` were structurally unchanged.

## Layout

```
src/ServerAuthority/
  Plugin.cs               Entry point, session detection, game version check
  ModConfig.cs            Configuration
  OwnershipPolicy.cs      Applies the rules to live ZDOs
  OwnershipLeases.cs      Short-lived pins that keep an object with one client (ships)
  SimulationAnchors.cs    Connected players, which replace the server's unused reference position
  ServerViewpoint.cs      Where a headless server stands when the game asks a question about "here"
  WaveField.cs            Deterministic wind and the water clock, so every machine computes one sea
  WindMath.cs             The anchor arithmetic shared by the client's sea and the server's winds
  LocalWeather.cs         The weather a player standing at any position would settle on
  LocalWind.cs            Wind per position and per hull on the server, with Moder per hull
  WindBlend.cs            The pure arithmetic of the blended wind range, free of game types
  WindRange.cs            The wind range as a function of position and world time
  HullWindRange.cs        The per-anchor wind range a ship's owner writes for its crew
  ModerHeading.cs         The per-anchor Moder heading a server writes to a ship for its crew
  WeatherScope.cs         Lends EnvMan a position's weather around one consumer call
  WaveSync.cs             The wave field diagnostic, keyed so two machines' logs can be compared
  WaterQueries.cs         Initialises the layer mask Floating's water lookups depend on
  Integrity/              Mod manifest, character storage, validation, and both ends of the protocol
  Patches/                One file per subsystem, each explaining what vanilla does and why it changes
tools/PatchCheck/              Resolves every patch target against the game assembly
RESEARCH.md                    How Valheim distributes authority, and why the old mod broke
```

## Credit

The approach was worked out by reading Valheim 0.221.12 directly, and by studying
[ddormer/valheim-serverside](https://github.com/ddormer/valheim-serverside) (MIT), the unmaintained
Serverside Simulations mod that solved this problem first. The set of headless fixes in
`Patches/HeadlessPatches.cs` in particular is its hard-won knowledge.
