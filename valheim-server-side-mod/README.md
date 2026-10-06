# Server Authority

A Valheim mod that moves world simulation off the players and onto the dedicated server. Installed
on the server and on every client.

In vanilla the server hands each object to whichever client is near it, and that client simulates
it for everyone. One player's frame rate and connection decide how an area behaves for the whole
group, and when that player dies or walks away the objects go ownerless until the server's next two
second pass finds a new owner. That pause is the hitch felt when the "area host" changes.

## What it does

- The server owns and simulates everything around every player: creature AI, spawning, raids,
  physics, damage, and the timers on smelters, fermenters and crops. Nothing is handed back to a
  client when they are alone.
- The server runs at 60 frames per second instead of the 30 Valheim hardcodes, and sends each player
  object updates at a steady rate however many are online.
- Boats are simulated by the server even while somebody steers, so everyone aboard gets the same
  hull physics.
- Wind, waves and the water clock are made the same on every machine, so a boat sits in the sea the
  way the server floats it.
- Vanilla code that assumes the owner of an object is a player's machine is fixed where it broke.
  See [Vanilla code that assumes a client owner](#vanilla-code-that-assumes-a-client-owner).
- Optional: refuse clients that do not run the same mods, keep characters on the server, and
  accept commands from the [server tool](../valheim-server-tool/README.md).
- Achievements count on a Server Authority server, which BepInEx otherwise prevents.

The client half is required. It publishes state only a player's own machine has, receives the
credits, launches and keys the server earns on a player's behalf, and computes the same sea as the
server. The GoatTech release turns on mod validation, so a client without it is refused.

### What stays with clients

- **Player characters.** Never taken. A body simulated elsewhere rubber-bands.
- **Mounts and carts** while in use. The user holds a lease; when they let go it returns to the
  server.
- **A fish on the line**, leased to the fisher while their float names it as the catch.
- **A loaded catapult**, leased to the loader for 6 seconds so the shot fires.
- **Anything a player is using.** The policy never takes a ZDO whose `InUse` flag is set while its
  owner is connected, and leaves anything a connected client has just claimed alone for 3 seconds,
  covering the gap before the flag arrives. Otherwise a chest snaps shut about a second after it
  is opened.

Ship cargo and cart inventories share their vehicle's ZDO, so opening one hands the whole boat or
cart to that client until the panel closes. Splitting the two would mean patching
`Container.RPC_RequestOpen`, which once crashed a live server (see
[Patching RPC methods](#patching-rpc-methods)).

`Vehicles.KeepShipOwnedByDriver` hands a steered boat back to its driver. It is a fallback for when
server simulation of a ship misbehaves, not a tuning knob.

### Trade-offs

- **Everyone gets the server's latency.** Melee, interactions and steering a boat cost a round trip
  to the server. On a nearby server this is a clear win; on a distant one the player who would have
  been the area host feels it.
- **The server does real work.** It loads terrain and instantiates objects around every player, and
  pays the physics, pathfinding and AI for all of it. Give it real CPU and memory. Raising the frame
  rate from 30 to 60 took one player at simulation distance 2 from 36% to 73% of a core.
- **The server sends more.** See [Object updates](#object-updates) for the upload it needs.
- **Clients can still grab an object** when they interact with it, since
  `ZNetView.ClaimOwnership` is client side. The policy takes it back after the 3 second grace, on
  its next two second pass.

## Installing

The GoatTech release zips are the normal way; see `release/server/README.txt`. By hand: install
[BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2350 or
later on the dedicated server (Steam app 896660) and on every client, and put `ServerAuthority.dll`
in `BepInEx/plugins/` on both. Older packs shipped a `start_server_bepinex.sh` that set Doorstop 3
variables for a Doorstop 4 library, and the server starts with no BepInEx at all.

The server log should say `Server Authority active. Ownership mode: Always.` The mod warns at startup
when the game version differs from the one it was verified against (1.0.17).

## Configuration

Written to `BepInEx/config/valheim.server_authority.cfg` on first run. Mod validation and characters
are configured on the server only. Settings marked *debug* exist only in a
[debug tools build](#debug-tools).

| Setting | Default | Notes |
| --- | --- | --- |
| `General.Enabled` | `true` | Master switch. |
| `General.RequireDedicatedServer` | `true` | Only activate on a dedicated server. |
| `Ownership.Mode` | `Always` | `Vanilla` leaves ownership alone, to isolate whether a problem is this mod. |
| `Ownership.ServerOwnsWaterborne` | `true` | Let the server own boats and floating objects. Off gives an unmanned hull to the nearest client, as vanilla. |
| `Performance.MaxObjectsCreatedPerFrame` | `100` | Lower if the server stutters, raise if players outrun object loading. |
| `Performance.ZoneEvictionsPerTick` | `0` | Expired zones unloaded per 0.1s tick. `0` is one per player; vanilla is one. |
| `Performance.ServerFrameRate` | `60` | Valheim's dedicated server hardcodes 30. `0` leaves it alone. |
| `Performance.ZdoSendRate` | `20` | Object update sends per second to each player. |
| `Performance.FairZdoSending` | `true` | Serve every player at `ZdoSendRate`. Off is vanilla's one player per frame. |
| `Performance.FollowLivePlayerPosition` | `true` | Load, simulate and send around where each character is now, not where its client last reported it. |
| `Performance.ZdoSendWindowMaxKiB` | `64` | Cap on object data in flight to one player. Vanilla is 10. |
| `Performance.SteamSendRateKiB` | `384` | Steam's send rate per player. `0` keeps vanilla's 150. Needs upload for every player at once. |
| `Performance.ClearTeleportGhosts` | `true` | Fixes players seeing a frozen copy of someone at a portal they used. |
| `Vehicles.KeepShipOwnedByDriver` | `false` | Fallback only: hands a steered boat to its driver. |
| `Vehicles.KeepVehiclesOwnedByUser` | `true` | Leases mounts and carts to their user. Leave on. |
| `Vehicles.HoldHullsUntilWaterLoads` | `true` | Holds a server-owned hull still until the zones under it exist. Leave on. |
| `Vehicles.RequireLoadedAreaForWaterborneOwner` | `true` | Only matters with `ServerOwnsWaterborne` off. Leave on. |
| `Waves.DeterministicWind` | `true` | Wind as a function of world time. Must match on server and clients. |
| `Waves.AlignRenderedWaterWithPhysics` | `true` | Draw the sea at the moment boats float on it. |
| `Waves.ServerWeatherFollowsPlayers` | `true` | Resolve the server's global weather at a player, not the world origin. |
| `Waves.ServerWeatherPerPosition` | `true` | Everything the server simulates gets the weather at its own position. |
| `Waves.BlendedWeatherWind` | `true` | Wave strength from weather blended over position and world time. Must match on server and clients. |
| `Waves.LevelShipWaterline` | `true` | Lay a boat's waterline patch, foam ring and wake on the sea. Client side, looks only. |
| `Waves.ShipFoamSpread` | `0.75` | Metres each foam particle reads the sea away from itself, so the foam is not a rigid sheet. |
| `Waves.ShipFoamLift` | `0.07` | Most a foam particle sits above the surface, fixed per particle. |
| `Waves.ShipWakeTrailSeconds` | `0` | Caps the life of wake foam. Off. |
| `Waves.ShipWakeSink` | `0.35` | Deepest a wake particle sits below the surface, fading with depth. |
| `Debug.LogShipDamage` | `true` | One line per hit a boat takes, with its `HitType`. |
| `Debug.WaveSyncIntervalSeconds` | `5` | Logs the inputs the sea is built from, while a boat exists. Compare logs on `second=`. |
| `Debug.StatusIntervalSeconds` | `0` | Periodic "simulating N sectors" line. |
| `Debug.PerformanceReportSeconds` | `600` | Frame rate, zones, objects and creatures, and a [network report](#reading-the-network-report) per player. |
| `Debug.SlowFrameRateWarning` | `20` | Warn when the server averages fewer FPS than this over ten seconds, listing what is near each player. At most once a minute. |
| `Debug.HitchWarningMilliseconds` | `500` | The same warning for one frame longer than this. |
| `Debug.NetworkSaturationWarning` | `50` | Warn when this percentage of sends to a player fell behind or were held back over ten seconds. |
| `Debug.ForceEnvironment` | empty | *Debug.* Pin the weather, such as `ThunderStorm`. Set it identically everywhere or it causes a desync. |
| `Debug.LogOwnershipChanges` | `false` | *Debug.* Every takeover and handback. Noisy. |
| `Debug.LogShipState` | `false` | *Debug.* Hull height, buoyancy, zones and health. |
| `Debug.LogNearestWaterOnJoin` | `false` | *Debug.* Where the nearest sailable water is. |
| `Debug.EnableSpawnRequests` | `false` | *Debug.* See [Spawn requests](#spawn-requests). |
| `ModValidation.Enabled` | `false` | Refuse clients that do not run the same mods. The release turns it on. |
| `ModValidation.ServerOnlyMods` | empty | Server plugins clients do not need. |
| `ModValidation.AllowedClientMods` | empty | Extra client mods to allow. `*` allows any. |
| `ModValidation.ForbiddenClientMods` | empty | Always refused, even with `*`. |
| `ModValidation.RequireSameGameVersion` | `true` | Vanilla only compares the network protocol. |
| `ModValidation.CompareFileHashes` | `false` | Require byte-identical DLLs, not just equal versions. |
| `Characters.Enabled` | `false` | Keep characters on the server. The release turns it on. |
| `Characters.StoragePath` | empty | Default `characters_serverauthority` next to `worlds_local`. |
| `Characters.NewCharacters` | `ResetToFresh` | Or `Accept`, or `RequireNew`. |
| `Characters.StartingKit` | empty | Extra items for a fresh character: `Name`, `Name:Count`, or a piece such as `Karve` for its materials, its station's materials and the tool. |
| `Characters.OnInvalidUpload` | `Kick` | `LogOnly` discards a bad save without kicking. |
| `Characters.SaveIntervalSeconds` | `300` | Extra save requests between world saves. Bounds rollback. |
| `Characters.SyncTimeoutSeconds` | `90` | How long the character exchange at login may take. |
| `Characters.ShutdownSaveSeconds` | `10` | How long a graceful shutdown waits for every player's final save. |
| `Characters.RejectCheatedItems` | `true` | Items the game marked as spawned with devcommands. |
| `Characters.RejectCheatedProfiles` | `false` | The profile's devcommands flag, which is permanent. |
| `Characters.MaxSkillLevel` | `100` | Raise if a mod raises the skill cap. |
| `Characters.MaxSkillGainPerHour` | `0` | Per skill. Off. |
| `Control.Enabled` | `true` | Accept commands from the server tool: a message to every player, or save and quit. |
| `Control.Directory` | empty | Default `ServerAuthority-control` next to the server executable. |

The effective configuration is logged at startup on server and client.

## Mod validation

On connecting, the client sends a manifest: its protocol version, Valheim version, every BepInEx
plugin with its version and the SHA-256 of its DLL, and a hash of the local character file. The
server checks it when vanilla admits the peer (`ZRoutedRpc.AddPeer`, the last call of
`ZNet.RPC_PeerInfo`) and kicks a failing client through `ZNet.InternalKick`. With validation or
characters on, a client without the mod is kicked; vanilla shows that as a plain "kicked", while a
client with the mod sees the reason.

Every server plugin is required on the client at the same version, except `ServerOnlyMods`. A client
plugin the server lacks is refused unless listed in `AllowedClientMods`. This stops mismatched
installs and casual cheating, not a determined cheater: the manifest is whatever the client says.

## Server side characters

The server stores one character per world at
`characters_serverauthority/<world>/<platform id>/<name>.fch`, the name lowercased, in the game's own
format. Each save goes to `.fch.new` and is renamed over the `.fch` after the old one is copied to
`.old`. If the `.fch` is missing or unreadable, loading falls back to `.new`, then `.old`, and keeps
an unreadable file as `.fch.unreadable`. To reset a character, delete the `.fch` and any `.old` and
`.new` beside it.

- **Known character.** The server's copy replaces the local one, which is first backed up as
  `characters_local/<file>_backup_serverauthority-<yyyyMMdd-HHmmss>.fch`. A "Server:" chat line says
  so. Anything changed offline or earned in another world is gone.
- **Unknown character.** With `ResetToFresh` the server builds a fresh character keeping only the
  name, id and appearance, the client backs up its file and installs it, and the player enters with
  the Valkyrie intro. `Accept` takes the character as it is, subject to the checks below.
  `RequireNew` refuses one that has entered a world, trained a skill or earned trophies or powers.
- **While playing.** Every save is uploaded in 256 KB chunks. The server asks for a save at each
  world save and every `SaveIntervalSeconds`. A failing upload is discarded and, with `Kick`, the
  player returns as the last good copy.

An upload is checked for the name, the id against the stored copy, items that exist in `ObjectDB`
with stacks and quality within limits, items marked as spawned with devcommands, skills within
`MaxSkillLevel`, and optionally skill gain per hour and the profile's devcommands flag. If a game
update moves the player data past version 33, which `PlayerDataSummary` mirrors from `Player.Save`,
items and skills go unchecked with a warning. The client still simulates its own character, so a
modified client can report invented gear as long as it is plausible.

Logging out or quitting waits up to ten seconds, inside a `Game.Shutdown` prefix, for the server to
confirm the final save, because Valheim closes its Steam connection without flushing. A graceful
server shutdown (Ctrl+C or SIGTERM) likewise asks every player to save and waits up to
`ShutdownSaveSeconds` before saving the world. Anything that stops the server must allow for this,
for example `docker stop -t 30`. A crash or a dropped connection rolls a character back to its last
upload, while chests keep what was put in since, which can duplicate items; vanilla has the same
exposure over its longer save interval.

## Achievements

BepInExPack sets `Game.isModded`, and `Achievements.IsCheatedAtAll` counts that as cheating, so a
modded game earns no achievements and records none of the stats they read. Once the server has
answered its manifest, a client on a Server Authority server lowers the flag only while
`IsCheatedAtAll` runs. Every other reason still applies: a character that used devcommands, items
spawned with them, and world modifiers the game counts as cheats. Single player and hosted games
are untouched.

Cheating is not opened up. On a dedicated server a client cannot run cheat commands at all
(`Terminal.IsCheatsEnabled` needs `ZNet.IsServer`); an admin's command runs on the server, which
logs `Remote admin '<id>' executed command`. Progress from before this was never recorded, so it
starts from zero. Each player's log says at spawn whether achievements are allowed, and if not, why.

## Waves and weather

Waves are never sent over the network: every machine computes the sea from position, world time and
its own wind. Three of those inputs disagreed between machines, which made boats look sunk for
everyone but the hull's owner.

- **Wind was latched local state.** Each machine kept whichever noise target its own transition
  timer finished on. `DeterministicWind` makes it a cross-fade between two anchors on a grid of world
  time, with vanilla's noise and shape, each anchor held while in use so vegetation sway stays
  steady. Moder's power is decided by the server for a hull it simulates and written to the ship
  (`ModerHeading.cs`).
- **The rendered sea ran ahead of the physical one** by about twelve frames.
  `AlignRenderedWaterWithPhysics` ties the shader's water time to the world clock, catching up at four
  times real time and snapping gaps over ten seconds.
- **The server picked its weather at the world origin**, where its unused camera sits.
  `ServerWeatherFollowsPlayers` resolves it at the lowest-id player.

`ServerWeatherPerPosition` goes further: anything the server simulates gets the weather a player
standing at its position would have (`LocalWeather.cs`), loaded around each call by a `WeatherScope`.
That covers spawning, rain wear and cover, fires and fireplaces, cinders, windmills, fish, the
leviathan, wisps, ships and every floater. Without it one player's raid forced its weather on the
whole world. Raids run by Valheim Creatures are found through reflection (`CreaturesRaids.cs`).

`BlendedWeatherWind` makes wave strength a function of position and world time: the weather is
blended over a 32 m grid and over the first seconds of each weather period, so a border or a weather
change ramps the same way on every machine. A ship's owner writes the range for upcoming wind
anchors onto the ship (`HullWindRange.cs`), and everyone aboard uses it.

`LevelShipWaterline` puts a boat's waterline patch, foam ring and wake sheets on the water instead of
at the hull's height when they were emitted. `ShipFoamSpread`, `ShipFoamLift` and `ShipWakeSink`
give each particle a stable offset of its own so the foam does not read as a decal; spray is left
alone.

`Debug.WaveSyncIntervalSeconds` logs wind, water time and a fixed probe on every machine, keyed by
the world second, so a server and a client log can be compared line for line.

## Object updates

Two rates Valheim hardcodes decide how long a round trip to the server takes: the dedicated server's
30 frames per second, and object updates at most 20 times a second. Together that is roughly 100 ms
before any network. Vanilla also limits the update stream in three ways, all written for a server
that only relays:

- **One player per frame.** `ZDOMan.SendZDOToPeers2` serves one player per frame in turn, so at 60
  FPS each player gets about 15 sends a second with up to three players online, 10 with five and
  5.5 with ten. `FairZdoSending` serves everyone at `ZdoSendRate`.
- **10 KiB in flight.** `ZDOMan.SendZDOs` sends nothing while more than 10 KiB is unacknowledged.
  `ZdoSendWindowMaxKiB` sizes the window per player from ping and Steam rate.
- **150 KiB/s per connection.** `ZSteamSocket` fixes Steam's minimum and maximum send rate at
  153600 bytes, and Steam does not estimate bandwidth, so raising only the maximum does nothing.
  `SteamSendRateKiB` sets both. At 384 KiB/s, eight players at once is about 25 Mbit/s of upload.

`FollowLivePlayerPosition` uses each player's character on the server rather than the position its
client reports every two seconds, which trails a sailing player by 20 metres. `ClearTeleportGhosts`
repeats the zone departure check that `ZDO.InternalSetPosition` runs against the old position, so
players at a portal are told the traveller has left.

### Reading the network report

```
Test Maiden: 19.7 sends/s (19.3 with data), 1205 objects/s, 262.5 KiB/s, window up to 38 KiB;
51% of sends full, 3% fell behind, 2% held back by a full queue, backlog up to 107 object(s);
ping 42 ms (worst 95), delivery 100.0%, Steam queue up to 48 ms, Steam rate 384 KiB/s,
Steam out 245.3 KiB/s; send work 0.53 ms per send (worst 3.5 ms).
Most sent: 43% Deer, 34% Wolf, 6% Neck, ...
```

- **sends/s** near `ZdoSendRate`; much lower means the server's frame rate is the limit.
- **full** sends left changes for the next send, which is harmless alone. **fell behind** means
  changes are piling up and creatures stutter for that player. **held back** means the window was
  still full.
- **Steam queue** staying near 100 ms or more while the player falls behind means the Steam rate is
  the limit.
- **send work** is server CPU spent packing updates; it grows with base size and send rate.
- **Most sent** is the share of bytes by prefab.

## Vanilla code that assumes a client owner

A server that owns everything lacks local state vanilla assumes an owner has: a local player, its
profile, its status effects, the joints and triggers on its own machine. Each fix below is logged
when it acts.

| Vanilla assumption | What broke | Fix |
| --- | --- | --- |
| `Everybody` RPCs run only where the object is instantiated | `Pickable.RPC_Pick`, `Leviathan.RPC_Left`, `MusicVolume.RPC_PlayMusic` and `Trap.RPC_OnStateChanged` threw on `Player.m_localPlayer`; picking dropped nothing | `LocalPlayerRpcPatches.cs` |
| A ship's owner has a local player | `Ship.UpdateSailSize` threw every frame of raising or lowering a sail | `HeadlessPatches.cs` |
| `Floating.s_waterVolumeMask` is set by some `Floating.Awake` | Unset on a fresh server, so `GetWaterLevel` found no water and boats in open sea fell to the bottom | `WaterQueries.cs` sets it at startup |
| A client simulates only once its whole area is loaded | A hull measuring water in a zone not yet built lost buoyancy | `ShipPhysicsPatches.cs` holds it until the zones exist |
| `CreateLocalZones` keeps near zones alive | It returns after spawning one zone, so the rest aged out and took their water with them | `ZoneSystemPatches.cs` refreshes every near zone |
| `ZNet.GetReferencePosition()` is the player | The server build pins it outside the world, so `StaticPhysics` never ran and felled logs would never settle | `SlowUpdatePatches.cs` |
| A cart's attach joint exists on its owner | The server would detach a cart being pulled | `VehiclePatches.cs` leases it to the puller |
| A hooked fish's float is local | The policy took the fish mid-fight and it stopped fighting | `HookedFish.cs` |
| The loading client owns the catapult when it fires | The ammo count lived only on the loader, and a reclaim fired nothing | `CatapultLoad.cs` |
| `ApplyPushback` runs on the creature's owner | Knockback from a client's AoE never moved server-owned creatures | `RemotePush.cs` |
| One weather per machine, at its camera | One player's raid set the weather for every fire, spawner and hull | `LocalWeather.cs`, `WeatherScope.cs` |
| Kill rewards go to `Player.m_localPlayer` | No player got defeat keys such as `KilledBat`, or last-hit stats | `KillCredit.cs` |
| `Aoe.Setup` runs where the AoE is | Breath attacks reached clients with prefab values and did nothing | `TriggerAoeSetup.cs` |
| The cooking station's owner is the collector | The Frost Foundry threw mid-collect and duplicated items; no cooking or craft stats counted | `CookingCollect.cs`, `StatCredit.cs` |
| A summon follows its summoner on the summoner's machine | Blood Magic from summons trained the server's copy of the player | `SummonSkill.cs` |
| Chopping, mining, hits and taming are counted on the owner for the local player | No player earned those stats, so GrindTrees could not unlock | `EarnedStats.cs` |
| `ForceJump` runs on the character's owner | The Fader's arrival smash threw only the server's copy of each player | `PlayerLaunch.cs` |
| Status effects are on the owner | The Anti-Sting Concoction and the troll love potion did nothing | `PlayerEffects.cs`, `Pheromones.cs` |
| A destroyed bed's owner is its sleeper | The player kept a spawn point at a bed that was gone | `BedSpawnPoint.cs` |
| `PrivateArea.HaveLocalAccess` has a local player | Lava blob and Writhan explosions in a ward threw on the server | `WardAccess.cs` |
| Only a client's ship sets the Ashlands key | Hugin's Ashlands ocean warning never triggered | `AshlandsHintPatches.cs` |
| An archery target's hit has a local player to message | Server-owned shots threw and passed through the target | `ArcheryTargetPatches.cs` |
| Ghost mode, debug flying and cinematics are local | Server creatures hunted admins in ghost mode and players watching a boss dream | `PlayerFlags.cs`, `CreatureSenses.cs` |
| Summon limit and pet rock react to the owner's player | The "max summons" message was lost; the pet rock ignored everyone | `SummonCapPatches.cs`, `PetFaces.cs` |

To find more after a game update, look in owner-run code and owner-targeted or `Everybody` RPCs for
unguarded `Player.m_localPlayer`, for writes to `Game.instance.GetPlayerProfile()` or other `Game`
methods that edit the local profile, for statics only an `Awake` or a guarded call site initialises,
for code that keys off `GetReferencePosition()`, and for owner-run code that writes another
character's state, which on the server lands in a copy of the player that is never saved.

## Development

### Building

```bash
dotnet build src/ServerAuthority/ServerAuthority.csproj -c Release
```

The game's assemblies are found under the usual Steam locations, or set
`VALHEIM_MANAGED=/path/to/valheim_server_Data/Managed`. Set `VALHEIM_PLUGINS` to a BepInEx plugins
folder to have each build copied there.

### Debug tools

A plain build leaves out every *debug* setting and testing tool, and is the one to distribute. For
testing:

```bash
dotnet build src/ServerAuthority/ServerAuthority.csproj -c Release -p:DebugTools=true
```

The startup line listing the effective configuration says which kind of build is running.

### Spawn requests

With `Debug.EnableSpawnRequests` on, the server reads `BepInEx/config/serverauthority_spawn.txt`,
acts on each line next to the first connected player, and empties the file. It is a cheat hook
guarded only by access to the server's files, and the only route to spawning on a dedicated server,
where `devcommands` cannot run from a client.

| Line | Does |
| --- | --- |
| `<Prefab> <count> [tame]` | Spawns, optionally tamed. |
| `water` | Re-runs the nearest sailable water report for the player. |
| `diag` | What the server can see of the water at the player. |
| `owners` | What the server owns around the player, by prefab. |
| `hulls` | Each ship's five water sample points, their zones, and whether buoyancy is on. |
| `weather [env\|clear]` | Forces or releases the server's environment, or lists them. |
| `event <name>`, `event stop` | Starts or stops a random event at the player without flagging anyone as a cheater. |
| `removeships` | Destroys every instantiated ship. |

### After a game update

```bash
dotnet run --project tools/PatchCheck -c Release -- \
  src/ServerAuthority/bin/Release/ServerAuthority.dll \
  ~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed
```

`PatchCheck` resolves every `[HarmonyPatch]` target. Then re-read the vanilla methods the mod copies
or replaces against a fresh decompilation: `ZDOMan.ReleaseZDOS`, `ZoneSystem.Update` and
`IsActiveAreaLoaded`, `ZNetScene.CreateDestroyObjects` and `OutsideActiveArea`,
`SpawnSystem.UpdateSpawning`, `RandEventSystem.FixedUpdate`, `Player.Save` (player data version),
`EnvMan.UpdateEnvironment` and `GetBiome`,
`CookingStation.RPC_RemoveDoneItem` and `SpawnItem`, the stat counting in `TreeBase`, `TreeLog`,
`MineRock`, `MineRock5`, `Character.RPC_Damage` and `Tameable.Tame`, `BaseAI.FindEnemy`,
`ArcheryTarget.OnProjectileHit`, `Tameable.UnsummonMaxInstances`, `Pet`, and the RPC replacements
`Pickable.RPC_Pick` and `Trap.RPC_OnStateChanged`. `RESEARCH.md` records
how Valheim distributes authority and what earlier game versions changed.

### Patching RPC methods

Valheim invokes RPC methods through `Delegate.DynamicInvoke`. Mono can fail to invoke a Harmony
replacement that way (`BadImageFormatException: Method has zero rva`) and then abort the process
while formatting the exception, with no error logged. This killed a live server on
`Container.RPC_RequestOpen`, and a patch on `ZNet.Disconnect` once looped the same exception every
frame. Once it starts it can poison every patched method in the process.

So do not patch an RPC method unless nothing else will do. Five remain: `Pickable.RPC_Pick`,
`Trap.RPC_OnStateChanged`, `Leviathan.RPC_Left` and `MusicVolume.RPC_PlayMusic`, which throw at the
call site otherwise, and a postfix on `ZNet.RPC_ServerSyncedPlayerData`. Replacing a registered
handler with an ordinary method through `RpcTakeover.cs` is safe.

### Test rig

`tools/testserver/` holds a launcher and a watchdog for a test server. The launcher saves every two
minutes and points `XDG_CONFIG_HOME` at `server_config/`, so a server and a game client on one Linux
account do not share and overwrite each other's Unity preferences. The watchdog restarts the server
and keeps each crashed run's log with a summary, since a Mono abort leaves no error in the log.

## Layout

```
src/ServerAuthority/
  Plugin.cs               Entry point, session detection, game version check
  ModConfig.cs            Configuration
  EffectiveSettings.cs    Logs the effective configuration at startup
  OwnershipMode.cs        Always or Vanilla
  OwnershipPolicy.cs      Applies the ownership rules to live ZDOs
  OwnershipLeases.cs      Leases keeping an object with one client
  HookedFish.cs           Leases a hooked fish to its fisher
  CatapultLoad.cs         Leases a loaded catapult to its loader
  SimulationAnchors.cs    Connected players, in place of the server's reference position
  ServerViewpoint.cs      Where a headless server stands when the game asks about "here"
  Waterborne.cs           Which prefabs float, for ServerOwnsWaterborne
  HullWater.cs            A ship's water sample points and whether their zones exist
  WaterQueries.cs         Initialises the layer mask Floating's water lookups need
  WaveField.cs            Deterministic wind and the water clock
  WindMath.cs             Wind anchor arithmetic shared by client and server
  LocalWeather.cs         The weather a player standing at a position would have
  LocalWind.cs            Wind per position and per hull on the server
  WindBlend.cs            Blended wind range arithmetic
  WindRange.cs            The wind range as a function of position and world time
  HullWindRange.cs        The wind range a ship's owner writes for its crew
  ModerHeading.cs         Moder's heading the server writes to a ship
  WeatherScope.cs         Lends EnvMan a position's weather around one call
  CreaturesRaids.cs       Valheim Creatures' raids, bound by reflection
  WaveSync.cs             The wave diagnostic
  ServerPerformance.cs    Performance report and slow frame warnings
  NetworkStats.cs         Per player send counters and the network report
  SteamSendRate.cs        Applies SteamSendRateKiB
  ServerControl.cs        Reads the server tool's command files
  WorldSeed.cs            -seed for a new world
  KillCredit.cs           Defeat keys and last hits for the players who earned them
  TriggerAoeSetup.cs      A creature's trigger AoE setup carried to every client
  CookingCollect.cs       Cooking station collects on the server
  StatCredit.cs           A stat, craft or skill gain sent to one player
  SummonSkill.cs          A summon's skill gain sent to its summoner
  EarnedStats.cs          Chopping, mining, hit and taming stats
  PlayerLaunch.cs         A launch aimed at a player sent to their client
  RemotePush.cs           A client's push on a server-owned creature
  PlayerEffects.cs        Each player's pheromone effects on their ZDO
  Pheromones.cs           Pheromone meads on server-owned creatures
  PlayerFlags.cs          Ghost mode and cinematic playback on each player's ZDO
  CreatureSenses.cs       Hides ghost, flying and cinematic-watching players from server AI
  PetFaces.cs             The pet rock's face picked on each client
  BedSpawnPoint.cs        A destroyed bed's spawn point cleared for its owner
  WardAccess.cs           Ward access answered on the server
  AchievementGate.cs      Achievements on a Server Authority server
  RpcTakeover.cs          Replaces a vanilla RPC handler on the server
  NearestWater.cs         Debug: nearest sailable water
  SpawnRequests.cs        Debug: the spawn request file
  Integrity/              Mod manifest, validation, character storage and both ends of the protocol
  Patches/                Harmony patches, one file per subsystem
tools/PatchCheck/         Resolves every patch target against the game assembly
tools/testserver/         Test server launcher and watchdog
tools/package/            Windows watchdog, log tail and log collection scripts for the server zip
RESEARCH.md               How Valheim distributes authority
```

## Credit

The approach came from reading Valheim directly and from
[ddormer/valheim-serverside](https://github.com/ddormer/valheim-serverside) (MIT), the unmaintained
Serverside Simulations mod that solved this problem first. The headless fixes in
`Patches/HeadlessPatches.cs` in particular are its hard-won knowledge.
