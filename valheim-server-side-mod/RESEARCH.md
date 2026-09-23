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

1. **Its reference position is Vector3.zero.** `ZoneSystem.Update`
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
- `ZDO.m_tempSortValue` now doubles as a private `SaveClone` flag when negative. Vanilla's own sort
  path writes non-negative distances into it, so reusing it for distance sorting stays consistent.
- `SpawnSystem.UpdateSpawning` gained alt-biome spawners and `groupSalt` arguments.
- `Ship.UpdateOwner` now runs every 2 seconds rather than 4.

One hazard worth recording: `ZNet.ApplySimulationDistance` only forwards the value to `ZoneSystem`
when `ZoneSystem.instance` already exists, and on a server the handshake runs from `ZNet.Awake`. If
ZoneSystem awakens afterwards its copy stays at the default of zero, and since `CreateLocalZones`,
`CreateGhostZones` and `IsActiveAreaLoaded` all size themselves from that copy, the server would
build only the zone each player stands in. The mod applies it explicitly in a `ZoneSystem.Start`
postfix.
