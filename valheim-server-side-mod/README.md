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
- A thrown spear that misses everything lands as an item instead of vanishing in the air. See
  [Thrown spears](#thrown-spears).
- Valheim's bad luck protection on rare drops, such as trophies, carries over server restarts. See
  [Pity counters](#pity-counters).

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
  is opened. See [Handing objects back](#handing-objects-back) for how the server gets it back.

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
  `ZNetView.ClaimOwnership` is client side. The policy asks for it back after the 3 second grace,
  on its next two second pass.

### Handing objects back

When the server wants an object back from a connected player, it asks their client to hand it back
rather than taking it, and the client does so unless one of its containers is open there. Only the
holder of a chest decides who else may open it, so a player using a chest keeps it and everyone else
who asks is told it is in use. As in vanilla, a request that arrives while the chest is changing
hands is dropped without a message, and works when repeated.

Taking it directly broke chests. A player who reopened a chest they still held, at the moment the
server took it back, wrote "in use" while the takeback was in flight. `ZDOMan.RPC_ZDOData` lets
newer data overwrite a newer owner but applies only the owner from older data, so the server ended
up with the player as owner and the player with the server. Every open request then went to a
machine that answered "but im not the owner", and the chest refused everyone, with no message,
until that player relogged. Live servers logged this at least eight times in two weeks, each after
a burst of sorting.

- A client that has not answered within 10 seconds loses the object anyway, with a warning.
- Every 6 seconds the server asks the holder of each object marked in use whether it really holds
  it. A client that does not tells the server, which takes the object and clears the mark, so a
  split that forms some other way repairs itself. Each repair is logged as a warning. A client
  that names another connected player as the owner is left alone, since that is a handover still
  in flight.
- A client clears the in-use mark on a container it no longer owns within a second, which vanilla
  only does on the owner; a stale mark stops the container loading its contents. An owner closes a
  container still marked in use that the inventory no longer shows, which vanilla leaves refusing
  everyone when the inventory switches straight from one chest to another. An owner whose open
  container lost its mark writes it again.
- When the server takes an object from a player who has left, it clears an in-use mark they left.
- Transfers that do not go to the server stay direct: a lease handing a mount, cart or steered ship
  to its user, an unmanned hull given to the nearest player with `ServerOwnsWaterborne` off, and
  releasing to nobody an object that is in no player's active area.
- The performance report adds a line such as `Handbacks over 600s: 41 asked, 41 handed back, 0
  taken after 10 s without an answer, 0 ownership split(s) repaired.`

## Installing

The GoatTech release zips are the normal way; see `release/server/README.txt`. By hand: install
[BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2350 or
later on the dedicated server (Steam app 896660) and on every client, and put `ServerAuthority.dll`
in `BepInEx/plugins/` on both. Older packs shipped a `start_server_bepinex.sh` that set Doorstop 3
variables for a Doorstop 4 library, and the server starts with no BepInEx at all.

The server log should say `Server Authority active. Ownership mode: Always.` The mod warns at startup
when the game version differs from the one it was verified against (1.0.30).

## Configuration

Written to `BepInEx/config/valheim.server_authority.cfg` on first run. Mod validation and characters
are configured on the server only. Settings marked *debug* exist only in a
[debug tools build](#debug-tools).

| Setting | Default | Notes |
| --- | --- | --- |
| `General.Enabled` | `true` | Master switch. |
| `General.RequireDedicatedServer` | `true` | Only activate on a dedicated server. |
| `Ownership.Mode` | `Always` | `Vanilla` leaves ownership alone, to isolate whether a problem is this mod. |
| `Ownership.KeepStaticObjectsOwned` | `true` | Building pieces, trees, small rocks and terrain edits stay server owned instead of being released when players move on, so a step across a zone border does not re-send a whole base. |
| `Ownership.HoldSupportUntilNeighboursLoad` | `true` | Check a building piece's support only once the zones around it have loaded. See [Structural support](#structural-support). Leave on. |
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
| `Performance.CreateFreshDropsFirst` | `true` | Client side. Ragdolls and fresh loot near the player are created ahead of the queue of objects waiting to load. |
| `Performance.CreateCreaturesFirst` | `true` | Client side. Creatures, other players and projectiles within 64 m are created ahead of waiting buildings, so nothing the server is already running stays invisible. |
| `Performance.NavigationLinkMaxWidth` | `3` | Server side. Joins neighbouring navigation tiles with links up to this many metres wide instead of one per metre, which halves the links and keeps the server under Unity's 65535 link cap at large bases. Paths measured as good as vanilla's at 3; at 8, creatures detoured on building floors. `1` is vanilla. See [Server frame time](#server-frame-time). |
| `Performance.ReuseCollisionData` | `true` | Server side. Collision callbacks and creatures' ground contact reads reuse their data instead of allocating it, about 4 MB/s less garbage with 300 creatures. |
| `Performance.PauseServerParticles` | `true` | Server side. Pauses looping particle systems such as fire, smoke and mist, which a dedicated server never draws: about a tenth of its frame at a large base. |
| `Experimental.ServerPhysicsRate` | `0` | Server side. Physics steps per second on the server, at least 20. `0` keeps 50. Not yet played at another rate. |
| `Experimental.CompensatePhysicsRate` | `true` | Scales the game's per step constants, such as lunges, acceleration and ship sail force, when `ServerPhysicsRate` is not 50. No effect at 50. |
| `Experimental.IdleCreatureStep` | `1` | Steps asleep or idle creatures only every this many physics steps. `1` is vanilla. Held back: in one test fight with it on, the fight never started. |
| `Experimental.OwnedSyncRecheckSteps` | `0` | Sorts synced objects by owner every this many steps, skipping the server's own in between. `0` is vanilla. |
| `Experimental.CacheGroundProbes` | `false` | Reuses a creature's ground probes while it stays within a few centimetres. |
| `Experimental.SyncOnlyAfterPhysics` | `false` | Writes positions into network data only on frames with a physics step. |
| `Experimental.FloatPerPhysicsStep` | `false` | Measures the water under floating objects once per physics step instead of every frame. |
| `Vehicles.KeepShipOwnedByDriver` | `false` | Fallback only: hands a steered boat to its driver. |
| `Vehicles.KeepVehiclesOwnedByUser` | `true` | Leases mounts and carts to their user. Leave on. |
| `Vehicles.HoldHullsUntilWaterLoads` | `true` | Holds a server-owned hull still until the zones under it exist. Leave on. |
| `Vehicles.RequireLoadedAreaForWaterborneOwner` | `true` | Only matters with `ServerOwnsWaterborne` off. Leave on. |
| `Waves.DeterministicWind` | `true` | Wind as a function of world time. Must match on server and clients. |
| `Waves.AlignRenderedWaterWithPhysics` | `true` | Draw the sea at the moment boats float on it. |
| `Waves.MatchDeepNorthWaves` | `true` | Float things in the Deep North on the sea as it is drawn. Must match on server and clients. |
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
| `Debug.SlowDeathWarningMilliseconds` | `250` | Log each creature death that took longer than this to reach a nearby player, on the server and on each client. `1` logs every death. See [Reading the death timing lines](#reading-the-death-timing-lines). |
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
| `Loot.KeepPityCounters` | `true` | Server side. Save the bad luck protection on rare drops with the world, so a restart does not start it over. See [Pity counters](#pity-counters). |
| `Control.Enabled` | `true` | Accept commands from the server tool: a message to every player, or save and quit. |
| `Control.Directory` | empty | Default `ServerAuthority-control` next to the server executable. |

The effective configuration is logged at startup on server and client.

The last four `Experimental` settings cut the server's work in fights by about a fifth in a study
without players. In a blind test fight with a player they saved nothing measurable, and running
wolves looked as if they skipped frames, so they stay off until each has been tried on its own.

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
`MaxSkillLevel`, and optionally skill gain per hour and the profile's devcommands flag. An item the
Forge of Potential can refine has no upper quality limit, since the forge sets none. If a game
update moves the player data past version 34, which `PlayerDataSummary` mirrors from `Player.Save`,
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

## Thrown spears

A thrown spear is only a projectile until it hits something, and only the hit turns it back into an
item. Vanilla deletes the projectile, spear and all, when its 10 second flight time runs out, or when
it leaves the area loaded around its thrower, about 130 to 190 m out at simulation distance 2. A
spear thrown off a mountain or over a cliff is lost either way.

Server Authority drops the spear as an item at that point instead. Only weapons that train the
Spears skill are affected. The Abyssal Harpoon is never thrown away, and nothing else thrown or shot
is touched.

- When the flight time runs out, the spear still gets its last physics step, so a hit at the very
  end counts as in vanilla. Otherwise it becomes an item where it is, still moving at the
  projectile's velocity, and carries on and lands. Spear rigidbodies use continuous collision
  detection, so a fast drop does not pass through the ground.
- When it leaves the loaded area, it becomes an item at rest at that point. The item is outside
  everybody's loaded area too, so it waits in the air until somebody comes near enough to load it,
  and falls then.
- The thrower's own client simulates the throw, so that is where this happens, and each landing is
  logged there, for example: `Thrown SpearWolfFang (quality 4, made by Fria EmberHeart) was still
  flying 142 m from Fria EmberHeart when its flight time ran out, so it was dropped as an item at
  (-1874.3, 201.6, 1569.0), still moving 46 m/s, instead of being deleted.`
- From then on it is an ordinary dropped item, which vanilla removes once it is an hour old if it is
  outside a base with nobody within 25 m.

## Pity counters

Valheim protects against bad luck on any drop whose chance, after the star bonus, is 30% or less,
which covers most trophies. Each such drop keeps a count of kills until it is guaranteed. A fresh
count puts the first drop within 2 / chance kills, 20 for a 10% trophy, and after each drop the next
comes within one more than that. The chance is part of the count's key, so a creature at another
star level counts on its own. With the Creatures loot curve a 10% trophy is 20% at one star and 40%
at two, where it is rolled plainly with no protection.

The game keeps the counts only in memory, on whichever machine runs the creature's death. Here that
is the server, so every player's kills share one count, and before this it started over at every
restart, which threw away the progress towards a trophy once a day. A client still saves its own
counts with its character, but they only apply to a creature that dies while that client owns it,
such as a mount under its rider.

With `Loot.KeepPityCounters` on, the server writes the counts to `pity_counters.dat` in the world's
folder under the character storage path, `characters_serverauthority/<world>/` by default, after
every world save, and reads them back when the world loads. The server tool's backups include the
default folder; a `Characters.StoragePath` elsewhere has to be added to its extra backup paths. A
crash loses only the kills since the last world save. The file is hash checked and kept
with a `.old` copy, and a damaged file is read from its `.new` or `.old` copy instead. One line is
logged when the world loads and one per world save:

```
Pity counters: loaded 37 from .../characters_serverauthority/Dedicated/pity_counters.dat, written 2026-11-19 04:00:04 UTC.
Pity counters: saved 38, 4 changed since the last save.
```

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

One more disagreement is vanilla's own and is the same on every machine: in the Deep North the drawn
sea and the physical one are different functions. Across the 200 m border band the water shader fades
all ten waves and keeps the five smallest at no less than a tenth of their height. `CalcWave` fades
only the six largest, to nothing, and keeps the four smallest at full height. Deep in the Deep North
the sea is drawn almost flat while ice, boats, fish and swimmers bob on ripples up to 1.5 m times the
wind strength, two to three seconds apart. `MatchDeepNorthWaves` scales those four waves by the
shader's fade. The fifth smallest still fades to nothing where the shader keeps a tenth, a difference
of under 0.1 m times the wind strength. The WaveSync hull line gives `deepNorthFade` under the hull.

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

A client creates the objects it receives at ten per 1/30 s, or a hundredth of the queue when more
are waiting, and creates nothing until every zone around the player is built. It takes every
building piece, tree and rock before any ragdoll, item or creature, however far away the piece is.
Crossing a zone border at a large base queues hundreds of pieces, so a creature killed just then
vanished at once but showed its ragdoll and loot seconds later. In vanilla the area's owner creates
both itself the moment the creature dies, so only other players waited. `CreateFreshDropsFirst`
creates ragdolls, and items dropped in the last ten seconds, within 48 m ahead of the queue. One
waits until its own zone is built and while a building piece, ship or rock within 16 m of it, or
the terrain it rests on, is still queued, so a ragdoll does not land before its floor. It is
dropped if the player moves more than 48 m away or it is still waiting after ten seconds, and
vanilla then creates it in its usual turn.

Creatures waited in the same queue, and that cost more than a late ragdoll. Every building piece,
tree and rock is `Solid`, a player `Prioritized`, and every creature and projectile `Default`, the
lowest type, so after a respawn or a zone border at a large base a creature next to the player
stayed invisible for seconds. In vanilla nobody simulates a
creature that no client has created, but here the server runs its AI at once, so it closed in
unseen and its arrows flew unseen. `CreateCreaturesFirst` creates characters, other players
included, and projectiles within 64 m, nearest first and ten per 1/30 s, while a `Solid` or terrain
object is still queued or the area around the player is not yet built. It moves forward only objects
another machine owns: a client draws those without gravity at the position their owner sends, so
they need no floor yet. Anything this client owns, such as a mount it rides, waits its usual turn.
Vanilla ignores a hit on the player whose attacker this client has not created, so an attacker
only hurts the player once it exists on their screen.

Every change of an object's owner bumps its owner revision, and the server then sends the whole
object again to every player who has it. The policy released everything outside the players'
active areas, so a step across a zone border at a large base moved thousands of walls and floors in
or out of the active area and re-sent all of them, ahead of the creatures around the player.
`KeepStaticObjectsOwned` keeps the server as owner of a building piece, tree, small rock or terrain
edit once it has owned it, when the prefab has no rigidbody and only passive components. Anything
else is released as before, and each performance report names the prefabs released that way:

```
Ownership over 600s: 312 change(s). Released outside players' active areas because they act on
their own: piece_chest (Container), fire_pit (Fireplace), wood_door (Rigidbody).
```

### Structural support

The owner of a building piece checks its support about once a second, less often in a very large
base, but only inside a player's active area: the inner three by three zones at the default
simulation distance. The zones one ring further out are loaded too, so every piece that is checked
has its neighbours on both sides of a zone border. Most pieces also wait 30 seconds after they are
loaded before their first check.

That margin only exists once the outer ring has finished loading. When a player crosses a zone
border, the active area moves in that frame. The pieces in the zone that has just come within
checking range were loaded long ago, so they are checked straight away, while the zones beyond them
are built one at a time, a tenth of a second apart, and their objects created after that. A piece
resting on something across that border found nothing there, lost its support and fell, and
everything it held fell with it. Vanilla rarely shows this, because a client only gets those pieces
at the server's next ownership pass, up to two seconds later, and until then they read as fully
supported. With `KeepStaticObjectsOwned` the server already owns them.

`HoldSupportUntilNeighboursLoad` keeps track of each zone near a player. A zone counts as loaded once
it is built and none of its objects are still waiting to be created, and stays loaded until it
leaves every player's near area. A piece is checked only when every zone around its own that lies in
some player's near area has been loaded for two seconds. Until then the check is skipped and the
piece keeps the support it had, so wear and weather go on as usual. A structure knocked down next to
a zone that is still loading therefore falls a few seconds late.

Zones outside every near area are left out. The server does not create their objects, apart from
the few large ones meant to be seen from afar, and the active area keeps checked pieces at least
24 m from them at every simulation distance. If a zone's objects are still not all created 30
seconds after it was built, the zone counts as loaded anyway, with a warning naming them. A zone
that is still not built, or still loading a location, after 30 seconds is warned about too, but
checks next to it stay held, since nothing in it exists yet to rest on.

The performance report adds a line when anything fell or was held back:

```
Structural support over 600s: 41 piece(s) fell for lack of support, 38 in zone (42, 98) at (2688,
6272), 3 in zone (43, 98) at (2752, 6272): 20 wood_beam, 12 wood_floor, 9 wood_pole2. Support checks
held back 312 time(s) while 9 zone(s) finished loading, longest load 2.4 s, 0 zone(s) given up on.
```

Knocking out the bottom of a structure to bring it down shows up here like any other loss of
support. A collapse that nobody caused, in a zone a player had just come near, would point back at
this.

### Reading the network report

```
Test Maiden: 19.7 sends/s (19.3 with data), 1205 objects/s (12% new, 3% only a new owner),
262.5 KiB/s, window up to 38 KiB;
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
- **new** is the share of objects the player had never received, and **only a new owner** the
  share sent again just because their owner changed. A large owner-only share while a player falls
  behind means ownership churn rather than new areas loading.
- **send work** is server CPU spent packing updates; it grows with base size and send rate.
- **Most sent** is the share of bytes by prefab.

### Server frame time

Each performance report is followed by two lines that account for the server's main thread.

```
Frame time over 60s, ms per second: physics and collisions 132 (worst 6 ms, heap +0.6 MB/s),
FixedUpdate.ZSyncTransform 102 (worst 3 ms), FixedUpdate.Character 93 (worst 12 ms, heap +0.9 MB/s),
object creation 78 (worst 6 ms), ..., 536 of 1000 measured. Loose items: 912 instantiated, 43 with a
rigidbody awake (14 Fish1, ...). Particle systems: 3159 on the server, 3100 paused (0 newly this
period), 17 left playing because they end or remove themselves. Path queries: 15/s, 5% failed, 3 ms/s.
Navigation: 303 tiles (134 Humanoid, ...), 15253 links in use of 65535, 0 queued for removal, 162 tiles
overdue for a rebuild; 262 tile builds, 114 ms average from start to links, worst 153 ms; main thread
1 ms/s gathering tile sources and 2 ms/s placing links; 0 link allocation failures; edges re-linked
from 41454 edge points (5636 left out with something standing on them, 3159 kept one metre wide
beside something) into 2566 links. Near Test Maiden at (2590, 6425): 58 tiles, 12 path queries/s, 3% failed.
Main thread over 60s by engine step, ms per second: Update.ScriptRunBehaviourUpdate 303,
FixedUpdate.ScriptRunBehaviourFixedUpdate 263, ...; 1000 in all. Worst frame 467 ms:
Update.ScriptRunDelayedDynamicFrameRate 448, ... 3 garbage collection(s), managed heap 1662 MB,
incremental with 3 ms slices. The managed heap grew by 11.2 MB/s between collections, most in ...
```

- **Frame time** is milliseconds per second of wall clock spent in each timed section, so 1000 is
  the whole main thread. Sections inside another, such as path queries inside the AI, are left out
  of the measured total. **heap** is how fast the managed heap grew while the section ran, a lower
  bound on what it allocates.
- **by engine step** covers every millisecond of the frame, split by Unity's own player loop steps.
  `TimeUpdate.WaitForLastPresentationAndUpdateTime` is idle time while the server waits for its
  next frame; when it disappears the server is saturated.
- **Worst frame** names the steps that made the longest frame. A world save shows up as about half
  a second in `ScriptRunDelayedDynamicFrameRate`, next to a `PrepareSave` line in the log. **with a
  garbage collection in it** means the collector ran during that frame.
- **links in use** near 65535 means creatures are about to lose their way across tile borders, and
  every link Unity cannot make logs an error. **overdue** tiles show changes, such as a new wall,
  that the navigation mesh has not caught up with yet.

At startup the server logs the navigation settings the scene uses and how collision callbacks are
set up, in lines starting `Navigation:` and `Physics:`.

### Reading the death timing lines

A creature killed near a player is timed on both sides of the connection, so a death that shows
late can be placed on the server, the network or the client. Each line carries the wall clock time
and the creature's and ragdoll's object ids, which tie a client's line to the server's.

```
Death at 14:03:22.512: Greydwarf 1:4523 at (2766, 34, 6265), ragdoll 1:4530. Last hit, by Fria,
arrived 12 ms before the death. Fria (ping 4 ms): removal queued behind 20.5 KiB (about
53 ms at the send rate), ragdoll sent 31 ms after the death behind 22.0 KiB, first loot sent 6 ms
after its release behind 3.1 KiB. 3 loot object(s) released 2004 ms after the death. Worst server frame 19 ms.
```

- **Last hit ... before the death** is the server's own delay, normally under one frame. The
  attacker is a player or a creature's prefab, such as `Lox`. A creature killed by burning or
  poison shows its last direct hit, which can be seconds earlier.
- **Removal queued behind** is the data already waiting on that player's connection when the
  creature was removed. The removal travels behind it, so the creature keeps standing on the
  player's screen for about that long plus the ping. The time is estimated from the send rate.
- A player who leaves the area or disconnects before the ragdoll or loot reaches them is noted as
  such rather than counted as never sent.
- **Ragdoll sent** is when the ragdoll went into that player's object updates. Sends held back by
  a full queue are counted.
- **Loot released** is the ragdoll's own timer, 2 s for a greydwarf, which is vanilla.

```
Death seen at 14:03:22.640: Greydwarf_ragdoll 1:4530 at (2766, 34, 6265), 6 m away. Ragdoll
shown 128 ms after the death on the server, by the clocks of both machines; it arrived 8 ms before
it was created, with 3 object(s) waiting to be created. Your last hit on Greydwarf 1:4523 was 140 ms
before it vanished, and the ragdoll showed 10 ms after that. Loot shown 2010 ms after the ragdoll,
6 ms after the ragdoll was removed here, arrived 2 ms before it was created. Worst frame 33 ms over
9 frame(s) in 150 ms. Ping 4 ms, 1234 object update(s) received in the last second.
```

- **Shown after the death on the server** compares the two machines' clocks, so it is exact only
  when they agree, and it does not by itself make a line slow. The other times are measured on
  the client alone.
- **Arrived before it was created** is the client's own delay. Vanilla creates ten objects per
  1/30 s, or a hundredth of the queue when more are waiting, and every building piece, tree and
  rock before any ragdoll, item or creature. The waiting count shows how many were queued.
- Loot is timed from its first item, and only items dropped in the last ten seconds count.
- **Your last hit ... before it vanished** covers the whole round trip for the player's own kill.
  A creature counts as the player's kill when they hit it in the last five seconds before it
  vanished.
- **Worst frame** is the longest client frame in that span.

Both sides add a summary to each performance report, with the median and worst of each delay.

Each death of the player is logged on their own client with the hits of the ten seconds before it:

```
You died at 23:19:45.112 at (2637, 33, 6428), with 3533 object(s) waiting to be created. Hits in
the last 10 s, newest first: 0.3 s before, Goblin 1:2452001, melee, 52 raw and 31 taken, from 1.8 m,
attacker created here 0.6 s before and drawn 0.4 m from its last server position, 3533 waiting;
2.1 s before, GoblinArcher 1:2452010, ranged, 40 raw and 0 taken, from 31.0 m, attacker not created
here so vanilla ignored the hit, 3610 waiting.
```

- **Raw** is the damage the attacker sent, **taken** the health the player lost to it, which is
  zero when it was blocked, dodged or ignored.
- A hit whose attacker this client does not have says why: not created here yet, already gone (an
  arrow from a creature that has since died), or absent while the player could not be hit anyway,
  such as when dodging or loading. Only the first counts as ignored in the report.
- **Created here ... before** is how long the attacker had existed on this client, so a short time
  means it was invisible until just before it hit. A creature created before the session's first
  record shows as such.
- **Drawn ... from its last server position** is how far the client drew the attacker from where its
  owner last placed it, which is the visible lag of its movement.
- **Waiting** is the client's queue of objects not yet created at that moment.

Every performance report adds how many hits hurt, how many came from an attacker created less than
a second earlier, how many vanilla ignored because the attacker was not created here, the median and
worst drawn offset, and how many creatures, players and projectiles `CreateCreaturesFirst` created
ahead of waiting buildings.

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
| The pieces a client starts checking reach it at the next ownership pass, after the zones beyond them have loaded | With pieces kept server owned, a piece came within checking range the moment a player crossed a zone border, while the zone beyond was still loading, and anything resting across that border fell | `SupportGate.cs` holds the check until the zones around the piece have loaded |
| A drop's owner knows its current count | A player who held drops they could not carry merged them, and the server took them back and merged them again from counts it read when they appeared: 124 of 200 black marble were lost in one test | `ItemStacks.cs` |

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
| `<Prefab> <count> [tame] [metres]` | Spawns, optionally tamed, next to the player or that many metres away in a random direction, on the highest floor there. |
| `water` | Re-runs the nearest sailable water report for the player. |
| `diag` | What the server can see of the water at the player. |
| `owners` | What the server owns around the player, by prefab. |
| `hulls` | Each ship's five water sample points, their zones, and whether buoyancy is on. |
| `weather [env\|clear]` | Forces or releases the server's environment, or lists them. |
| `event <name>`, `event stop` | Starts or stops a random event at the player without flagging anyone as a cheater. |
| `removeships` | Destroys every instantiated ship. |
| `split [inuse]` | Makes the server hold the nearest server-owned container as the player's, optionally in use, without telling their client: the state that locked chests. |
| `askback` | Asks the player's client to hand back the nearest container they hold, whatever its state. If it is open there, the client must keep it. |
| `profile [seconds]` | Times every `Update`, `LateUpdate` and `FixedUpdate` in the game and its coroutines for that long, then logs the slowest, how fast each grew the heap, and a census of particle systems, lights, animators and audio sources. |
| `scopes` | Logs every timed section of the current report period, not only the top ten. |
| `navtest` | Plans paths across every navigation tile edge near the player, one every 2 m, and logs how many completed and how far they strayed. Compare runs with `NavigationLinkMaxWidth` 1 and above. |
| `gc [slice <ms>\|collect\|pause [seconds]\|resume]` | Sets the incremental collector's time slice, forces a collection, or pauses collection for up to ten minutes. |

`BepInEx/config/serverauthority_standins.txt` lists points the server simulates as if a player stood
there, one `x z [name]` per line, so a busy base can be loaded and measured with nobody online.
Monsters near a stand-in stay awake as they would near a player.

### After a game update

```bash
dotnet run --project tools/PatchCheck -c Release -- \
  src/ServerAuthority/bin/Release/ServerAuthority.dll \
  ~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed
```

`PatchCheck` resolves every `[HarmonyPatch]` target. Then re-read the vanilla methods the mod copies
or replaces against a fresh decompilation: the merge in `ZDOMan.RPC_ZDOData` and `Container.SetInUse`,
`CheckForChanges` and `RPC_RequestOpen`, which [handing objects back](#handing-objects-back) relies on, `ZDOMan.ReleaseZDOS`, `ZoneSystem.Update` and
`IsActiveAreaLoaded`, `ZNetScene.CreateDestroyObjects` and `OutsideActiveArea`,
`SpawnSystem.UpdateSpawning`, `RandEventSystem.FixedUpdate`, `Player.Save` (the player data layout
`ProfileCodec` reads and `FreshCharacter` writes), the cheat flag `Character.OnDeath` reads for `KillCredit`,
`EnvMan.UpdateEnvironment` and `GetBiome`,
`CookingStation.RPC_RemoveDoneItem` and `SpawnItem`, the per-slot record of who put the item in (`RPC_AddItem`, `SetSlot`) and whom `SpawnItem` credits with the craft stat (`RPC_SetCrafterStat`), the stat counting in `TreeBase`, `TreeLog`,
`MineRock`, `MineRock5`, `Character.RPC_Damage` and `Tameable.Tame`, `BaseAI.FindEnemy`,
`ArcheryTarget.OnProjectileHit`, the flight time in `Projectile.FixedUpdate` and `SpawnOnHit` that
`ThrownSpears` mirrors, the counts `ItemDrop.AutoStackItems` and `RemoveOne` read for `ItemStacks`, the removal in `ZNetScene.RemoveObjects`, the active area test in `ZNetScene.PointInsideActiveArea` and `WearNTear.UpdateWear` and `UpdateSupport`, which `SupportGate` relies on, `Tameable.UnsummonMaxInstances` and the names it counts (`m_summonNameBlockers`), `Pet`, the owner-run work in the components `StaticObjects` treats as passive (`TerrainComp.CheckSnow` since 1.0.30), the constants and enum values the mods compile in (`GlobalKeys` shifted in 1.0.30), and the RPC replacements
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
  ThrownSpears.cs         A thrown spear still flying when vanilla would delete it lands as an item
  ItemStacks.cs           Reloads a drop's count before it is merged or eaten from
  SupportGate.cs          Holds support checks until the zones around a piece have loaded
  PityCounters.cs         Saves the server's drop pity counters with the world
  CreaturePriority.cs     Clients create nearby creatures and projectiles before waiting buildings
  HitsOnYou.cs            The hits on the player in the ten seconds before each death
  NavLinks.cs             Wider links between navigation tiles, and the link cap guard
  CollisionData.cs        Reused collision and ground contact data on the server
  ServerParticles.cs      Pauses the server's looping particle systems
  FrameBudget.cs          Frame time by section for the performance report
  FrameLoop.cs            Frame time by engine step
  AllocationMeter.cs      Managed heap growth while a timed section runs
  PhysicsTuning.cs        Experimental physics settings
  CreatureStep.cs         Experimental: idle creatures stepped less often
  SyncStep.cs             Experimental: synced objects sorted by owner every few steps
  GroundProbes.cs         Experimental: ground probes reused while a creature stays put
  FrameSkips.cs           Experimental: sync and water work only on frames with a physics step
  StepCompensation.cs     Experimental: per step constants scaled to the physics rate
  RpcTakeover.cs          Replaces a vanilla RPC handler on the server
  NearestWater.cs         Debug: nearest sailable water
  SpawnRequests.cs        Debug: the spawn request file
  NavTest.cs              Debug: paths across every tile edge near a player
  StandIns.cs             Debug: points simulated as if a player stood there
  UpdateProfiler.cs       Debug: times every update method in the game
  GcSwitch.cs             Debug: garbage collector controls
  PhysicsLab.cs           Debug: physics experiments at runtime
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
