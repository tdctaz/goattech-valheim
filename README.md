# GoatTech Valheim Mods

The GoatTech Valheim mods. Server Authority, Rebalanced and Creatures always run together, on the
dedicated server and on every client, all at the same version. Valheim Server Tool runs the server.

| Folder | What it is |
| --- | --- |
| [valheim-server-side-mod](valheim-server-side-mod/README.md) | Server Authority: the server simulates the world |
| [valheim-rebalanced](valheim-rebalanced/README.md) | Rebalanced: weapon, armor and staff balance |
| [valheim-creatures](valheim-creatures/README.md) | Creatures: more stars, colored traits, boss traits, raids in waves |
| [valheim-server-tool](valheim-server-tool/README.md) | Valheim Server Tool: runs, restarts, backs up and updates the server |
| `release/` | Packaging and the player and server install guides |

## Goals

- The server hosts the world
  - Ownership no longer moves back and forth between players
  - The same experience everywhere, whoever is in the area
  - The server runs at 60 frames per second instead of vanilla's 30
- Harder enemies without making them all damage sponges
  - Up to five stars, with loot and damage scaling
  - New traits for enemies and bosses
  - Raids that have to be dealt with, scaled by comfort and the players present
- Melee and archery that hold up against magic
  - Tower shields that are worth carrying
  - Staff of Protection is too strong, especially in large groups
  - Staff of Embers does a large amount of blunt damage
  - Staff of the Wild allows too many roots at once
  - Staff of Fracturing hits every enemy with every splinter
  - Trollstav is too cheap to cast, and allows more than one troll

## Changes

### Server Authority

- The server owns and simulates everything around every player: AI, spawning, physics, damage,
  crafting timers
- No area host, so nothing hitches when a player dies or walks away
- The server runs at 60 frames per second instead of 30, and sends each player updates at a steady
  rate however many are online
- Players keep their own character, their mount or cart, and anything they have open
- Boats are simulated by the server even while steered, so the sea behaves the same for everyone
  aboard
- Refuses clients without the same mods at the same versions
- Keeps characters on the server
- Achievements count again. BepInEx marks the game as modded, which Valheim treats as cheating.
  Progress from before 0.9.5 was never recorded, so it starts from zero. Cheating is not opened up:
  only admins can run cheat commands on the server, which runs and logs them
- Fixed: boats could lose buoyancy, sink and break apart, in open sea or when moored across the
  border of a zone that had not loaded yet

### Rebalanced

- Tower shields: a quarter off pierce, blunt and slash while blocking
- Root armor: pierce resistance moved from the harnesk to the 3 piece set bonus, and +15 Bows moved
  to the harnesk
- Staff of the Wild: one root per player instead of a shared cap of 10
- Staff of Protection: one target per cast. Block shields yourself, attack fires a bolt that shields
  the first ally it hits
- Staff of Embers: blunt damage halved, fire unchanged, so 120/120 becomes 60 blunt and 120 fire
- Staff of Fracturing: fire damage halved at every quality, blunt unchanged
- Trollstav: one summoned troll at a time, and a cast costs at least 60 health or 60% of current
  health, less with Blood Magic. That can kill the caster, and the troll still arrives
- Burnt wood: spreading fire gives one coal per 5 wood instead of one per wood

### Creatures

- Up to 5 stars, unlocked by killing the faction's boss and hanging its trophy on the sacrificial
  stones
- Loot scales 1x, 2x, 4x, 6x, 8x, 10x; health and damage follow vanilla's per level rules
- Colored stars each give a trait:
  - Fast: +40% movement speed, +50% turning speed
  - Aggressive: +25% attack speed, half the wait between attacks, half the time spent circling
  - Regenerating: heals every second, scaled to its health, never while burning
  - Curious: twice the sight range, and hears noise from twice as far
  - Splitting: on death it splits into two of itself with half the stars, each dropping loot
  - Armored: 30% less damage taken, 25% slower
- Every boss except the Frozen King gets two colored stars of different colors, traits only:
  - Fast: +25% movement speed, +50% turning speed
  - Aggressive: +25% attack speed, a quarter off the wait between attacks
  - Regenerating: heals 0.125% of its maximum health per second, half while burning
  - Summoning: calls a wave from its own raid at every 18% of health lost, five waves in all
  - Splitting: drops nothing on death and splits into two full bosses without stars
  - Shielded: half damage from magic, a quarter off from arrows and bolts
- Raids come in waves of every raider at vanilla's most alive, two alive at a time, scaled by the
  players in the area and the base's comfort, and end only when cleared
- Creatures hit as hard however many players are near; vanilla adds 4% damage per player, and its
  extra health per player stays
- Stars above two get their own size and tint

### Valheim Server Tool

Runs the dedicated server on Windows and Linux. Needs the .NET 10 runtime.

- Start, stop and restart the server from its window or another terminal
- Restarts the server after a crash and keeps the logs and configs from each one
- Restarts daily at 05:00, backs up the world and updates Valheim with SteamCMD while it is down
- Warns every player on screen 15, 10, 5, 2 and 1 minutes before a stop, through Server Authority

## Releasing

A release is the three mods plus Valheim Server Tool, all sharing one version number. The server
refuses any client whose mods differ from its own.

The `VERSION` file is the only place the version is written. Each `Directory.Build.props` imports
`Version.props`, which reads `VERSION` into the assembly version and, for the mods, a
`ModVersion.Value` constant used by `[BepInPlugin]` and the client handshake. Any change to game
behaviour or the network protocol needs a new version, since that is what moves every player to the
new client package.

```
./release/release.sh 0.9.6
```

With a version it writes `VERSION` first; without one it releases what `VERSION` says. It:

1. Downloads BepInExPack_Valheim (version and SHA-256 pinned in the script) into `release/cache/`,
   once.
2. Builds the three mods from scratch with `-p:DebugTools=false`, and fails if a DLL does not carry
   the version or still contains a debug-only type.
3. Publishes Valheim Server Tool for Windows and Linux as single files using the installed .NET 10
   runtime.
4. Writes to `dist/`:
   - `GoatTech-Valheim-client-<version>.zip`: BepInEx, the three mods and the install guide.
   - `GoatTech-Valheim-server-<version>.zip`: BepInEx, the three mods, the server tool in
     `servertool/`, the watchdog scripts, a starting `valheim.server_authority.cfg` that turns on
     mod validation and server side characters, and the install guide.
   - `GoatTech-Valheim-<version>.sha256`.

Both zips hold the Windows and the Linux files side by side. Build on Linux so the `.sh` files keep
their executable bit.

Checklist:

1. Update [Changes](#changes) for anything players will notice.
2. `./release/release.sh <new version>`.
3. Install the server zip on a test server, start it with the server tool and join with the client
   zip. Check that `ValheimServerTool say hello` shows on screen, that `BepInEx/LogOutput.log` on
   both sides lists all three mods at the new version, and that the server log says
   `Server Authority active. Ownership mode: Always.`
4. Hand out both zips. Existing servers replace only the three DLLs, the two server tool
   executables and `GoatTech-VERSION.txt`, as the server guide says. `servertool.cfg` is never
   shipped; the tool adds new settings to it by itself.

| Path | Purpose |
| --- | --- |
| `VERSION` | The release version |
| `Version.props` | Reads `VERSION` into every build |
| `release/release.sh` | Builds and packages both zips |
| `release/client/README.txt` | Player install guide; `{{VERSION}}` is filled in at release |
| `release/server/README.txt` | Server install and update guide |
| `release/server/ServerAuthority-watchdog.sh` | Linux launcher with crash capture |
| `release/server/valheim.server_authority.cfg` | Starting server config for a first install |
| `valheim-server-side-mod/tools/package/` | Windows watchdog, log tail and log collection scripts |
