# Server-side simulation for Valheim: research notes

Analysis of Valheim's decompiled `assembly_valheim.dll`, and of the existing "Serverside
Simulations" mod (ddormer/valheim-serverside, targets 0.220.5, no longer maintained).

Sections 1 to 5 were written against Valheim 0.221.12 and describe the authority model, which 1.0
did not change. Section 6 records what 1.0 moved, and the line numbers and the `m_activeArea` naming
used below are 0.221.12's. Read section 6 alongside them.

To regenerate the decompiled sources this document references:

```
ilspycmd -p -o decomp ~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll
```

## 1. How Valheim distributes simulation authority

All world state lives in ZDOs (Zone Data Objects), managed by `ZDOMan`. Every networked
object (monster, tree, chest, smelter, terrain modification, ship, player) has one ZDO.
The server persists all ZDOs; each client mirrors the ZDOs near its player.

Every ZDO has an **owner** (a peer session id). The owner is the peer that *simulates*
the object: it runs the object's AI, physics and timers, is the only peer allowed to
write the object's data, and broadcasts updates to everyone else. Non-owners only
interpolate (`ZSyncTransform` sets rigidbodies kinematic on non-owners).

### How ownership is assigned

`ZDOMan.ReleaseZDOS` runs **only on the server** (`ZDOMan.cs`, called from the update
loop behind `ZNet.instance.IsServer()`), every 2 seconds. For itself and for each peer it
calls `ReleaseNearbyZDOS(refPos, uid)`:

- If a persistent ZDO near a peer has no owner (or its owner is no longer in range),
  ownership is **given to that peer**.
- If the owner peer walked away, ownership is reset to 0 (unowned).

So the "area host" is simply the first client whose active area (a square of
64x64m zones, `m_activeArea = 2`, so 5x5 zones) covers the object. This is exactly the
mechanism behind the two problems we experience:

- **Laggy area host**: everyone in the area gets updates relayed through that client's
  connection and CPU.
- **Host dies / leaves**: the ZDOs go ownerless until the server's 2 second timer
  reassigns them, and the new owner then has to instantiate and take over thousands of
  objects. Mobs freeze, physics stalls.

Clients can additionally grab ownership directly at any time via
`ZNetView.ClaimOwnership()` when interacting (damaging things, terrain edits, containers,
pickables, fireplaces, cooking stations, signs, fish, turrets, etc.), and some RPCs are
routed to the current owner (`RPC_Damage`, door use, cart `WantOwner`).

### What the owning client simulates (i.e. what is "locally controlled")

- **Monster and animal AI**: `BaseAI`/`MonsterAI`/`AnimalAI` gate `UpdateAI` and most
  logic on `m_nview.IsOwner()`. Aggro, pathfinding, attacks, taming, procreation.
- **Spawning**: `SpawnSystem` (one `_ZoneCtrl` ZDO per zone, owner runs respawns),
  `CreatureSpawner`, `SpawnArea` (bone piles / nests), and raid events
  (`RandEventSystem`).
- **Physics**: falling trees and logs, dropped items, ships, carts. Owner's rigidbody is
  authoritative, everyone else interpolates a kinematic copy.
- **Object logic timers**: smelters, kilns, fireplaces (fuel burn), fermenters, cooking
  stations, plants growing, beehives, torch decay.
- **Damage application**: any damage you deal is sent as an RPC to the object's owner,
  who applies it and syncs new health.
- **Structural integrity**: `WearNTear.UpdateSupport`, rain/water damage.

## 2. Why the dedicated server does not already do this

The dedicated server runs the same Unity code base and *could* simulate everything, but
it deliberately does not:

1. **Its reference position is nowhere near the players.** It was Vector3.zero when this was
   written; in 1.0 the headless build's `Game.FixedUpdate` pins it at (1000000, 0, 1000000),
   outside the world, every physics tick. `ZoneSystem.Update`
   (`ZoneSystem.cs:942-949`) creates real, simulated "local zones" only around the local
   reference position. Around connected peers it creates only **ghost zones**
   (`SpawnMode.Ghost`): terrain and location ZDOs are generated, then the GameObjects are
   immediately destroyed. Data only, nothing simulated.
2. **`ZNetScene.CreateDestroyObjects` (`ZNetScene.cs:358`) instantiates GameObjects only
   around the local reference position.** The server therefore has no live scene around
   players, so even if it kept ZDO ownership, nothing would run the objects.
3. **`Player.m_localPlayer` null checks** early-out many simulation paths on a headless
   server (98 files reference it). Examples that matter: `SpawnSystem.UpdateSpawning`
   (`SpawnSystem.cs:158` refuses to spawn without a local player),
   `RandEventSystem.FixedUpdate`, `Ship.UpdateOwner` (`Ship.cs:692`).

This is a bandwidth/CPU tradeoff Iron Gate made for cheap dedicated hosting. Reversing it
is the whole mod.

## 3. The recipe for server-side ownership

Server-only BepInEx/Harmony plugin. Vanilla clients can join unchanged, because ownership
assignment and object instantiation policy are already server decisions. Core patches
(all validated against the 0.221.12 decompilation, and matching what the old mod did):

1. **`ZoneSystem.Update`**: on the server, call `CreateLocalZones(peer.GetRefPos())` for
   every connected peer instead of only creating ghost zones. Gives the server real
   terrain, heightmaps and colliders around every player. Also patch
   `ZoneSystem.IsActiveAreaLoaded` to consider all peers.
2. **`ZNetScene.CreateDestroyObjects`**: build the near/distant ZDO lists from the union
   of all peers' active areas (deduplicated) and instantiate/remove GameObjects
   accordingly. Also `ZNetScene.OutsideActiveArea(Vector3)` must return "inside" if the
   point is inside any peer's area (used by `SpawnArea` etc.).
3. **`ZDOMan.ReleaseNearbyZDOS`**: never hand ownership to clients. If any player is in
   an object's active area, set owner to the server's uid; if none, release to 0.
   The server then runs AI/physics/timers because it now has instances (patch 1+2) and
   ownership (patch 3).
4. **Remove `Player.m_localPlayer` gates** in `SpawnSystem.UpdateSpawning` and
   `RandEventSystem.FixedUpdate` (replace "is there a local player here" logic with "is
   any player here", using `Player.GetAllPlayers()`, which works server-side since player
   objects are instantiated by patch 2).

### Deliberate exceptions (things that must stay client-owned)

- **Player characters**: always owned by their own client. Never touch.
- **Ships**: steering a server-owned ship means a full round trip per rudder input, which
  feels terrible. Keep the *driver* as owner while someone is at the rudder (patch
  `Ship.UpdateOwner`, and grant ownership when the server routes the `RequestRespons`
  RPC accepting a new driver); revert to server ownership when unmanned. The old mod also
  reset `m_lastWaterImpactTime` on ownership handover to avoid spurious water impact
  damage from desynced wave state.
- **Carts (`Vagon`)**: clients request ownership via the `WantOwner` RPC before pulling;
  the owner grants it in `RPC_RequestOwn`. This should keep working, but needs testing;
  the exclusion policy should probably be configurable per prefab.

### Headless cleanup patches (from the old mod, still relevant)

- Skip `AudioMan.Update` on the server.
- `WearNTear.UpdateSupport`: call `SetupColliders()` when `m_bounds` is null (ghost-created
  objects never ran the normal Awake path).
- `ShieldDomeImageEffect.GetDomeColor`: NREs headless, stub it.
- `Humanoid.UpdateAttack`: clear `m_currentAttack` when its `m_character` is null.

## 4. What went wrong with the existing mod, and what to do better

- **Brittle transpilers.** The `SpawnSystem`/`RandEventSystem` patches are IL pattern
  matches that silently break whenever Iron Gate recompiles those methods. That is the
  main reason each game patch kills the mod. Prefer full method reimplementation as a
  prefix (fails loudly and is easy to re-diff against a new decompilation), plus a startup
  assembly-version check that logs a clear warning.
- **Server resource usage.** The server now runs full physics, pathfinding and AI for
  every player's area. Known issues in the old repo: out-of-memory on long sessions
  (#104), object creation not sorted by distance to any player (#97). Budget per-frame
  instantiation and profile memory from day one.
- **Interaction latency moves to the server.** Every hit you land is now a round trip
  (damage RPC to owner). Everyone gets the same modest latency instead of one player
  getting zero and the rest getting that player's connection. On a LAN or nearby VPS this
  is strictly better; on a 150ms+ server it can feel worse than vanilla for melee.
- **Physics environment differences.** Headless server has no rendering-driven systems;
  water/wave state can desync (hence the ship water-impact workaround).

### Idea worth considering: hybrid ownership policy

Our actual pain is "the host's machine/connection affects everyone" and "host leaves the
area". Both only matter when *multiple* players share an area. A configurable policy in
patch 3 could keep vanilla behavior (client-owned) when exactly one player is in an area,
and pull ownership to the server only when a second player enters. That keeps server load
and melee latency at vanilla levels for solo exploring, and removes host migration
hitches exactly where they hurt. No client mod needed either way.

### Hybrid policy: transitions and hysteresis

The switch-back direction is entirely under our control: vanilla ownership reassignment
happens only in the server's `ReleaseNearbyZDOS` (2 second cadence), which the mod
replaces. There is no engine mechanism that forces ownership back to a client, so a
delayed handback is just a timestamp check in our policy loop.

Transition behavior:

- **1 -> 2 players (escalate)**: server takes ownership immediately. Requires the server
  to already have the area instantiated, so patches 1+2 (zones and objects around all
  peers) should run unconditionally, not only for contested areas.
- **2 -> 1 players (de-escalate)**: no migration is forced; the server keeps simulating
  seamlessly. Handing back to the remaining client is an optimization (round-trip melee
  latency for that player, server load). Apply hysteresis: per zone sector, track the
  last time it was covered by 2+ players' active areas, and only hand back after a
  configurable continuous-solo grace period (default ~5 minutes). This makes the common
  death scenario (player dies, respawns at bed, corpse-runs back) produce zero ownership
  flips.
- **Handback mechanics**: assign the ZDO owner directly server -> client, never via the
  ownerless 0 state, and only to a client standing in the area (its instances already
  exist). Important AI state survives handoffs because it is ZDO-persisted
  (`s_alert`, `s_aggravated`, `s_huntPlayer`, `s_spawnTime`, health, tame status,
  verified in `BaseAI.cs`); only transient component state (current path, attack timers)
  resets, visible as mobs briefly re-evaluating targets.
- **Sticky variant**: keep server ownership until the area is completely empty, then
  release to 0 as vanilla does. No flips at all while occupied, at the cost of server CPU
  and solo-player latency in previously contested areas. Worth shipping as a config
  option alongside the timer.

Caveat: clients still call `ZNetView.ClaimOwnership()` for some interactions and this
cannot be prevented without a client mod. Most such claims are conditional (e.g.
`Fireplace.Interact` only claims when the object has no owner, so server ownership blocks
it) or replaced by RPCs to the owner (containers, doors). Any stray claim is reclaimed by
the next release tick within 2 seconds; the policy loop must tolerate this rather than
assert ownership invariants.

## 4b. Two concrete causes of the old mod's memory growth

Found while writing the prototype's `ZoneSystem.Update` replacement. Both are in the vanilla code
the old mod replaced, and both are addressed in `Patches/ZoneSystemPatches.cs`.

1. **`UpdatePrefabLifetimes()` was dropped.** The old mod's `ZoneSystem.Update` prefix reimplements
   the method but omits the final `UpdatePrefabLifetimes()` call. That method decrements
   `m_iterationLifetime` on every entry in `m_locationPrefabs` and calls `Release()` on the ones
   that reach zero. Without it, loaded location prefab assets are never released for the lifetime
   of the process. This is an unbounded asset leak and is the most likely explanation for issue
   \#104 ("Running out of memory investigation").

2. **`UpdateTTL` unloads at most one zone per call.** Note the `break` in the vanilla loop. That
   budget was written for a single player. The server now creates zones around every player, so
   with several players spread out, zone creation outruns eviction and the surplus is never
   reclaimed. The prototype ages zones once and then makes extra zero-length `UpdateTTL(0f)` calls
   to evict up to one zone per player per tick, which keeps the vanilla logic intact rather than
   reimplementing it against the private `ZoneData` type.

A third, smaller source of churn: the old mod deduplicates the per-peer object lists with
`Distinct().ToList()` inside `CreateDestroyObjects`, which allocates two lists every frame forever.
The prototype merges into reused buffers through a `HashSet` instead.

## 5. Practical notes for building it

- Stack: BepInEx 5.x plugin, HarmonyX, `net472` class library referencing publicized
  `assembly_valheim.dll` + `UnityEngine*.dll` (use BepInEx.AssemblyPublicizer.MSBuild,
  do not commit game DLLs or decompiled sources).
- Server-only: bail out of `Awake` unless `ZNet.IsDedicated()`; nothing is shipped to
  clients, so vanilla and console cross-play clients are unaffected.
- Test setup: Valheim Dedicated Server (Steam app 896660) + a normal client on the same
  machine, plus one remote client for latency testing. `devcommands` + `spawn` for AI
  checks.
- Alternative prior art: PeriodicSeizures/Valhalla (C++ server reimplementation,
  abandoned) and ddormer/valheim-serverside (the mod analyzed here, MIT licensed, so code
  can be reused with attribution).


## 6. Re-verification against Valheim 1.0.15

The 1.0 release reworked the zone system but left the authority model intact, so the premise of this
document still holds: the server still hands each object to whichever client is standing near it,
and there is still no native server-side simulation anywhere in the assembly.

What moved:

- `Vector2i` became `Vector2s` for zone coordinates.
- `ZoneSystem.m_activeArea` and `m_activeDistantArea` were replaced by the `SimulationDistance`
  struct, which the server syncs to clients and caps (`ZNet.GetSyncedSimulationDistance`,
  `RPC_RequestValidSimulationDistance`). Players can now choose a simulation distance, bounded by
  the server's.
- `ZNetScene.InActiveArea` became a Chebyshev distance test around the zone centre rather than a
  box of zone indices, which removes the old `m_activeArea - 1` ownership radius. `ZDOMan`'s
  ownership pass is otherwise line for line what it was.
- `ZDOMan.FindObjects` takes a visited-sector set, and `FindSectorObjects` takes a
  `SimulationDistance`. Sectors still map one to one onto `SectorIndex`, so per-sector iteration is
  unaffected.
- `ZDO.m_tempSortValue` now doubles as a private `SaveClone` flag when negative. The mod's own
  object creation sort writes non-negative squared distances into it, which is safe. Vanilla's send
  sort is not: `ServerSortSendZDOS` writes the distance minus 1.5 times the seconds since the ZDO was
  last sent to that peer, capped at 100, so anything within 150 metres that has never been sent goes
  negative, and `ClientSortSendZDOS` writes zero minus the same term. `ZDO.Reset` skips
  `ZDOExtraData.Release` for a save clone, so a destroyed ZDO whose last sort key was negative keeps
  its extra data for the rest of the session. The typical case is a dropped item that is sent once
  and picked up. `ZDOExtraData.PrepareSave` clones every extra data dictionary on the main thread at
  each save, leaked entries included. Small, but it grows with uptime. Unfixed. An earlier version
  of this note said vanilla only ever wrote non-negative values here, which was wrong.
- `SpawnSystem.UpdateSpawning` gained alt-biome spawners and `groupSalt` arguments.
- `Ship.UpdateOwner` now runs every 2 seconds rather than 4.

One hazard worth recording: `ZNet.ApplySimulationDistance` only forwards the value to `ZoneSystem`
when `ZoneSystem.instance` already exists, and on a server the handshake runs from `ZNet.Awake`. If
ZoneSystem awakens afterwards its copy stays at the default of zero, and since `CreateLocalZones`,
`CreateGhostZones` and `IsActiveAreaLoaded` all size themselves from that copy, the server would
build only the zone each player stands in. The mod applies it explicitly in a `ZoneSystem.Start`
postfix.


## 7. Re-verification against Valheim 1.0.16

A small patch. Diffing the publicized 1.0.15 and 1.0.16 assemblies changes 19 files, and none of the
methods any GoatTech mod patches or replaces is among them. `PatchCheck` resolves every target for
all four mods against both the client and the dedicated server assemblies.

What moved that touches the server's simulation:

- `SpawnSystem.UpdateSpawnList` now counts what it spawned earlier in the same call against
  `m_maxSpawned`, because the nearby-ZDO list it counts from is only gathered once. The mod's
  `UpdateSpawning` replacement calls vanilla's `UpdateSpawnList`, so it gets the fix unchanged.
- `Fireplace.UpdateSnowMelt` and `TreeLog.UpdateSnow` now run only on the owner, and only when every
  heightmap under the terrain op has a terrain compiler
  (`TerrainComp.ValidTCForAllAffectedHeightmaps`). Previously every client placed its own snow
  terrain ops. With the server owning both, only the server places them, which it can because it
  builds heightmaps around every player.
- `TerrainComp.Awake` no longer destroys a duplicate compiler at once. It records both and
  `TerrainComp.Start` keeps the one with the most operations.
- `ItemStand.SetVisualItem` writes the item back into the ZDO when run by the owner, and
  `ArmorStand`'s `RPC_SetVisualItem` now sends the item as a hash rather than a name.
- `Player.UpdateBaseValue` records the max comfort stat on every check rather than only when the
  base value changes.
- `ObjectDB.GetAllFoodItems` takes an exclusion list. No mod calls it.


## 8. The network send path in 1.0.16

Written while researching lag, and the basis for `NetworkStats`, `ZdoSendPatches` and
`SteamSendRate`.

### How a ZDO reaches a player

- `ZDOMan.SendZDOToPeers2` has served one peer per call, round robin, since 0.216.8 (ComfyMods
  ReturnToSender's changelog records the old all-peers `SendZDOToPeers` being removed then). A round
  starts once 0.05 s have passed, so each peer is served every `max(N + 1 frames, ~50 ms)`. Measured
  on the test server at 60 FPS: 19.5 sends a second for one player. Simulated: 20 for two, 15 for
  three, 10 for five, 6.7 for eight, 5.5 for ten. At vanilla's 30 FPS, five players get 5 a second.
- `ZDOMan.SendZDOs` sends nothing while the peer's queue exceeds 10240 bytes, and fills at most
  10240 minus the queue, with a 2048 byte minimum. The queue is `ZSteamSocket.GetSendQueueSize`,
  which adds Steam's pending reliable and unreliable bytes and its **sent but unacknowledged**
  bytes. So it is a 10 KiB window over the round trip: at most 10 KiB per RTT per player.
- Each changed ZDO is sent whole: ZDOID, owner and data revisions, owner, position, then the full
  serialized data. Nothing is delta encoded. Measured from the test world, a creature costs 150 to
  250 bytes per update and a tree, rock or piece 60 to 90, so one 10 KiB send carries about 50 moving
  creatures.
- `ZDO.Deserialize` upserts each key into `ZDOExtraData` and never clears, so a sender could send
  only the keys that changed and an unmodified receiver would stay correct, as long as the sender
  tracks what each peer already has. Not implemented; worth it only if bandwidth stays the limit.
- `ZSteamSocket` sends everything reliable with Nagle (flag 8) and uncompressed. Only
  `ZPlayFabSocket`, the crossplay socket, compresses, with zlib.
- `ZSteamSocket.RegisterGlobalCallbacks` sets `SendRateMin` and `SendRateMax` to the same 153600.
  Valve's GameNetworkingSockets does not estimate bandwidth: `SNP_InitializeConnection` sets the
  rate to about 4380 bytes per RTT and `SNP_ClampSendRate` clamps it to the minimum and maximum,
  and nothing raises it afterwards. The header says min and max "should always be set to the same
  value". Raising only the maximum does nothing over the internet. Config values are looked up
  through the inheritance chain on every read (`ConfigValue::Get`), so a global value set after the
  listen socket exists still reaches every connection.
- Clients report their reference position every 2 s (`ZNet.SendPeriodicData`); on the client it
  is the player's body, written each physics tick by `Tracker`. The server's `CreateSyncList`, the
  mod's anchors and the ownership pass all used that, two seconds stale. The player's character ZDO
  is on the server and current.
- `ZDO.InternalSetPosition` calls `SetSector`, which calls `ZDOMan.ZDOSectorInvalidated`, before it
  stores the new position. The per-peer check (`ZDOPeer.ZDOSectorInvalidated`) tests whether
  `zdo.GetPosition()` left the peer's area, and sees the old one. Anything that jumps, such as a
  player using a portal, is never invalidated for the players it left, who keep a frozen copy.
- Routed RPCs addressed to everybody go to every peer regardless of distance. They are small
  (about 70 bytes), so filtering them by distance would save little.

### What the mod changed

- `SendZDOs` is replaced by a copy that is identical except for the window and statistics, so the
  log can say per player how often sends run, how full they are, what is left over, the Steam
  ping, queue and rate, and which prefabs use the bytes.
- `SendZDOToPeers2` serves every peer at `ZdoSendRate` from a carried-over budget, each peer at
  most once per frame.
- The window is `rate x (1.25 RTT + 2 / ZdoSendRate)`, between 10 KiB and `ZdoSendWindowMaxKiB`,
  from Steam's own per-connection RTT and rate. One send interval of allowance left Steam idle
  between sends: on loopback, sends were full at 279 KiB/s with 384 allowed. Two keep it busy, and
  bound queueing to about two send intervals when a player is saturated.
- The Steam rate is set globally when the mod activates, to `SteamSendRateKiB` for both minimum and
  maximum.
- Anchors and every peer's `m_refPos` follow the character ZDO, restored after each client report.
- `ZDO.InternalSetPosition` gets a postfix that repeats the invalidation once the position is
  stored, when the zone changed.

Measured on the loopback test server, one player, 30 tame wolves hunting deer:

| | Vanilla behaviour | New defaults |
| --- | --- | --- |
| Sends per second | 19.5, 100% full | 19.7 |
| Delivered | 135 KiB/s, Steam out 141 of 150 | 262 KiB/s at 384 |
| Objects per second | about 600 | about 1200 |
| Backlog | up to 2,231 objects | up to 107 |
| Initial area load | 369 objects/s | 781 objects/s |

### Checked and left alone

- Compression would roughly halve the bytes (one mod reports 8.6 MB becoming 3.0 MB with zstd and a
  trained dictionary), but needs framing and a handshake on both ends. Worth adding if the network
  report shows players falling behind at the Steam rate.
- `CreateSyncList` scans every object around the peer on every send. On a 698k object world one mod
  measured 4.1 ms per call, cut to 0.07 ms with per-peer dirty sets. The report's send work shows
  whether that matters here before anything that complex is written.
- Ownership by ping or proximity, relay filtering and motion deadbands, which several mods do,
  either contradict server ownership or trade correctness for little.
- 1.0's incremental save only writes chunks marked dirty. `ZDOMan.RPC_ZDOData` applies a client's
  change without marking its chunk, so a vanilla server can miss client-owned changes. Here the
  server reclaims ownership within two seconds, and `ZDO.SetOwner` marks the chunk, which covers it.
- 1.0 has a `-simulationdistance 0-6` server argument (2 is the classic 5x5 zones, 1 is 21 zones, 0
  is 9). Lowering it is the cheapest way to cut server CPU if that ever becomes the limit.
