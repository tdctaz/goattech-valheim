# GoatTech Valheim Mods
This is the collection of GoatTech Valheim mods

## Goals
 - Serverside network host
   - So host do not transfer back and forth between players
   - Consistent experience through out the game regardless of who is in the area
   - Boosted server responce with 60 ups physics vs vanilla's 30 ups
 - Increased difficulty without making all enemies bullit sponges
   - Higher level enemies 0-5 stars (loot and damage scale with stars)
   - New features to enemies
   - New features to bosses
   - Raids that actually have to be dealt with (scales with comfort and players online)
 - Balance non-magic playstyles. Magic is just too good compared to Melee / Archery
   - Give Tower shields a purpose (today they are just useless)
   - Staff of Protection: is just too good and just becomes insane in large groups
   - Staff of Embers: doing large amounts of **Blunt** damage
   - Staff of the Wild: too many roots at the same time
   - Staff of Fracturing: each bomb does full damage to all enemies
   - Trollstaff: To cheap to cast and why are multiple allowed

## Detailed changes

### GoatTech Server Authority

Server and every client. The server does the simulation; the client part checks that every player
runs the same mods at the same version and hands characters to the server.

 - The server owns and simulates everything around every player: AI, spawning, physics, damage, crafting timers
 - No area host, so nothing hitches when a player dies or walks away
 - Server runs 60 ups instead of vanilla's 30, with a configurable update send rate
 - Players keep their own character, their mount or cart, and anything they have open
 - Boats are simulated by the server even while you steer them, so the sea behaves the same for
   everybody aboard rather than however the driver's connection feels
 - Checks clients to run the same mods
 - Server side character saves

##### Fixed Bugs
 - Boat physics is damaging the boat when the boat/ship is sitting on the border of two zones and only one of the zones is loaded. Resulting it dropping to the bottom and taking damage

### GoatTech Rebalanced

Server and every client.

 - Tower shields: a quarter off pierce, blunt and slash while blocking
 - Root armor: pierce resistance moved from the harnesk to the 3 piece set bonus, +15 Bows moved to the harnesk
 - Staff of the Wild: one root per player instead of a shared cap of 10
 - Staff of Protection: one target per cast, block shields you, attack fires a bolt that shields the first ally it hits
 - Staff of Embers: blunt damage halved, fire left as it is, 120/120 becomes 60 blunt and 120 fire
 - Staff of Fracturing: fire damage halved at every quality, blunt left as it is
 - Trollstav: one summoned troll at a time, and a cast costs 60 health or 60% of current health, whichever is greater, which can kill the caster as it goes out, the troll still landing (the Troll will still get summoned)

### GoatTech Creatures

Server and every client.

 - Up to 5 stars, unlocked by killing the faction's boss and hanging its head on the sacrificial stones
 - Loot scales 1x, 2x, 4x, 6x, 8x, 10x; health and damage follow vanilla's own per level rules
 - Colored stars each give a trait: fast, aggressive, regenerating, curious, splitting, armored
   - Fast: +40% movement speed, +50% turning speed
   - Aggressive: +25% attack speed, half the wait between two attacks, half the time spent circling
   - Regenerating: heals every second, scaled to its health, never while burning
   - Curious: twice the sight range, and hears your noise from twice as far
   - Splitting: on death it splits into two of itself with half the stars, each dropping its own loot
   - Armored: 30% less damage taken, 25% slower
 - Every boss gets two colored stars of different colors, traits only, no extra health or damage
   - Fast: +25% movement speed, +50% turning speed
   - Aggressive: +25% attack speed, a quarter off the wait between two attacks
   - Regenerating: heals 0.125% of its maximum health per second, half as much while burning
   - Summoning: calls a wave from its own raid at every 18% of health lost, five waves in all
   - Splitting: drops nothing on death and splits into two full bosses without stars
   - Shielded: half damage from magic, a quarter off damage from arrows and bolts
 - Raids come in waves, scaled by players online and base comfort, and end only when cleared
 - Stars above two get their own size and crature tint

### GoatTech Dyeing

Server and every client. Experimental.

 - A dyeing tub built with the hammer for bronze and fine wood, a crafting station of its own
 - Bronze cuirass, leggings and helmet can be dyed red, blue, black, green, yellow, purple or orange
 - A colour costs the armor itself plus 5 of something that colour: raspberries, blueberries, coal,
   guck, dandelions, thistle or carrots
 - Only armor at full durability goes in, so the tub cannot be used as a free repair bench
 - The piece keeps its quality, its durability, its crafter and its slot, and stays equipped if it
   was being worn
 - Dyed armor upgrades at the forge like plain armor, and can be dyed again for another colour

### Valheim Server Tool

Runs the dedicated server, see [valheim-server-tool](valheim-server-tool/README.md).

 - Start, stop and restart the server, from its window or another terminal
 - Crashes are detected, the logs kept for analysis and the server restarted
 - Daily restart at 05:00 with a world backup
 - Players are warned 15, 10, 5, 2 and 1 minutes before the server stops

## Releasing

The three mods that make up a release (Server Authority, Rebalanced and Creatures) always share one
version number, and the server refuses any client whose mods differ from its own. Dyeing is not part
of the release.

### Where the version lives

The `VERSION` file at the top of this folder is the only place the version is written. Each mod's
`Directory.Build.props` imports `Version.props`, which reads `VERSION` into the assembly version and
generates a `ModVersion.Value` constant that feeds `[BepInPlugin]` and the client handshake. Never
put a version number anywhere else.

Use `MAJOR.MINOR.PATCH`. Any change that touches game behaviour or the network protocol needs a new
version, since that is what forces every player onto the new client package.

### Making a release

```
./release/release.sh 0.8.1
```

With a version argument it writes `VERSION` first; without one it releases what `VERSION` already
says. It then:

1. Downloads BepInExPack_Valheim (pinned version and SHA-256 at the top of the script) into
   `release/cache/`, once.
2. Builds the three mods from scratch in Release with `-p:DebugTools=false`, and fails if a DLL does
   not carry the version or still contains a debug-only type.
3. Writes to `dist/`:
   - `GoatTech-Valheim-client-<version>.zip`: BepInEx, the three mods, and install steps for Windows,
     native Linux and Proton.
   - `GoatTech-Valheim-server-<version>.zip`: BepInEx, the three mods, the Windows watchdog, tail and
     log collection scripts, a Linux watchdog, and a starting `valheim.server_authority.cfg` that
     turns on mod validation and server side characters.
   - `GoatTech-Valheim-<version>.sha256` with checksums for both.

Both zips work on Windows and Linux alike. The Windows files (`winhttp.dll`, `doorstop_config.ini`,
`.bat`, `.ps1`) and the Linux files (`doorstop_libs`, `start_*_bepinex.sh`, `.sh`) sit side by side,
and each platform ignores the other's. Build on Linux so the `.sh` files keep their executable bit
in the zip.

### Checklist

1. Update the "Detailed changes" above for anything players will notice.
2. `./release/release.sh <new version>`.
3. Test on a server: install the server zip, join with the client zip, and check that
   `BepInEx/LogOutput.log` on both sides lists all three mods at the new version and that the server
   log says `Server Authority active. Ownership mode: Always.`
4. Hand out both zips. Existing servers only replace the three DLLs in `BepInEx/plugins` and
   `GoatTech-VERSION.txt`; the server README says the same, so nobody overwrites their own config
   or watchdog settings.

### Files

| Path | Purpose |
| --- | --- |
| `VERSION` | The release version |
| `Version.props` | Reads `VERSION` into every mod build |
| `release/release.sh` | Builds and packages both zips |
| `release/client/README.txt` | Player install guide, `{{VERSION}}` filled in at release |
| `release/server/README.txt` | Server install and update guide |
| `release/server/ServerAuthority-watchdog.sh` | Linux server launcher with crash capture |
| `release/server/valheim.server_authority.cfg` | Starting server config for a first install |
| `valheim-server-side-mod/tools/package/` | Windows server scripts (watchdog, tail, collect logs) |

## Known Bugs
- None outstanding. The boat that was destroyed while travelling towards the area it is moored in
  is fixed: `Floating.GetWaterLevel` ran its overlap query with an uninitialised layer mask, so it
  reported no water anywhere and boats skipped buoyancy and fell to the sea floor. See the Server
  Authority README.
