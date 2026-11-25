# GoatTech Valheim Mods

The GoatTech Valheim mods. Server Authority, Rebalanced and Creatures always run together, on the
dedicated server and on every client, all at the same version. Valheim Server Tool runs the server.

| Folder | What it is |
| --- | --- |
| [valheim-server-side-mod](valheim-server-side-mod/README.md) | Server Authority: the server simulates the world |
| [valheim-rebalanced](valheim-rebalanced/README.md) | Rebalanced: weapon, armor and staff balance |
| [valheim-creatures](valheim-creatures/README.md) | Creatures: more stars, colored traits, boss traits, raids in waves |
| [valheim-server-tool](valheim-server-tool/README.md) | Valheim Server Tool: runs, restarts, backs up and updates the server |
| [valheim-danish](valheim-danish/README.md) | Danish: fills in and corrects the Danish translation, not yet in the release |
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
- Fixed: a chest could refuse everyone, with no message, until the player who last opened it
  logged out and back in
- Fixed: a creature killed at a large base could vanish and show its body and loot a second or
  two later. Clients now create a fresh ragdoll and its loot ahead of the base still loading
  around them
- Crossing a zone border at a large base no longer sends the whole base to every player again:
  building pieces and terrain edits stay owned by the server, so creatures near the player skip
  far less
- Every creature death near a player is timed on the server and on each client, so a late ragdoll
  or late loot can be traced in the logs
- Fixed: a character holding an item refined at the Forge of Potential had every save refused, or
  was kicked, because the item was above its normal maximum quality
- Fixed: a thrown spear that hit nothing was deleted after 10 seconds in the air, or as soon as it
  flew out of the area loaded around its thrower. It now drops as an item where it was, so it can
  be found
- Built for Valheim 1.0.30. With 0.9.13 on 1.0.30, the server read the wrong world key, so Hugin's
  warning about the Ashlands ocean would not come
- Items from the Frost Foundry name the player who put the materials in as their crafter, and count
  as their craft, as Valheim 1.0.30 does, rather than the player who took them out
- Fixed: dropped items could vanish, or be duplicated, after a player stood among drops they could
  not carry, as when clearing a demolished build while overweight. The server merged nearby stacks
  from out of date counts; in one test 124 of 200 black marble were lost
- Fixed: the bad luck protection on rare drops such as trophies started over at every server
  restart, so the daily restart threw away the progress towards a trophy. The server now saves it
  with the world
- Fixed: building pieces attached across a zone border could fall when a player crossed into the
  next zone, taking everything they held with them. The server now checks a piece's support only
  once the zones around it have loaded
- Fixed: with several players at large bases the server ran out of navigation links, so creatures
  lost their way across tile borders. Tiles are now joined by wider links, about half as many, with
  paths as good as before, and a vanilla leak that kept some links in use for good is fixed
- The server no longer simulates fire, smoke and mist that nobody sees on it, and makes far less
  garbage in physics, which gives it more room for creatures at a large base
- Creatures, other players and projectiles near you are created before the buildings still loading
  around you, so after a respawn at a large base a monster is no longer invisible while it closes in
- Ice, boats, fish and swimmers in the Deep North float on the sea as it is drawn, rather than
  bobbing on ripples the game does not show. Vanilla has the same mismatch
- Each death logs the hits of the last ten seconds in the player's own log, and the server's
  performance report shows where its frame time goes
- Food taken from a cooking station or an oven still counts as cooked by the player who takes it,
  towards the achievement for cooking every food. Valheim 1.0.30 no longer counts it for anyone
- Fixed: the message that the summon limit was reached did not show when the Spirit Caller sent
  back a spirit of another kind. Since Valheim 1.0.30 its wolf, boar, bjorn and moose spirits share
  one limit

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
  stones; that unlocks the higher stars without making any star more likely
- Every creature placed inside a dungeon can have stars: up to 3, or up to 5 once its faction's boss
  is killed. Spawner structures such as bone piles stay as in vanilla. A setting raises the chance
  of each star in dungeons on its own
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
  - The vanilla raid clock restarts when a raid ends, so a base gets a full interval of peace
  - The server logs every raid roll, and who was in a base when one came up
  - Raiders ignore deer, hares and other creatures that only flee, and otherwise pick targets as in
    vanilla
  - Raiders head for the base and come back when they stray
  - Players are told as each wave falls how much of the raid is still to come
  - Hildir's raids are fought around their boss, who comes once; the rest keep coming at vanilla's
    numbers until it falls
- Creatures hit as hard however many players are near; vanilla adds 4% damage per player, and its
  extra health per player stays
- Stars above two get their own size and tint

### Valheim Server Tool

Runs the dedicated server on Windows and Linux. Needs the .NET 10 runtime.

- Start, stop and restart the server from its window or another terminal
- Restarts the server after a crash and keeps the logs and configs from each one
- Updates Valheim with SteamCMD when it starts, before the server does
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
