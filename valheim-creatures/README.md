# Valheim Creatures

Harder creatures for Valheim: up to five stars, colored stars that each give a creature a trait,
and a colored star on every boss. Installed on the dedicated server and on every client.

Loosely based on Creature Level and Loot Control, but with a smaller scope and different rules.

The server's configuration is the one that counts. Each client receives it when it connects, and a
client connected to a server without the mod plays by vanilla rules. By default the server
disconnects anyone who joins without the mod, or with a different version of it. In vanilla the
nearest player's machine runs the creatures around them, spawning included, so a player without
the mod would leave every creature near them unchanged.

It works on its own and alongside its two siblings, [Valheim Rebalanced](../valheim-rebalanced) and
[Server Authority](../valheim-server-side-mod). Every trait is stored on the creature itself and
applied by whichever machine owns it, so it makes no difference whether that is a player or, under
Server Authority, the server. Nothing here patches an RPC method, for the reason Server Authority
gives under "Patching RPC methods can kill the server", and nothing on the server touches the local
player. The three mods use their own RPC names and patch different methods, apart from all three
listening to `ZNet` for connections.

## Stars

A creature that can have stars in vanilla can now have up to five. One that never has stars in
vanilla still never does.

| Faction's boss | Most stars |
| --- | --- |
| Alive | 3 |
| Killed | 3, and each star is twice as likely |
| Killed, and its head hangs on the sacrificial stones | 5, each star twice as likely |
| Faction has no boss | 2, as in vanilla |

The chance of each further star is vanilla's: 10%, rolled one star at a time, so 1 in 10 creatures
has at least one star and 1 in 100 at least two. World modifiers and world level still apply, and a
spawn that sets its own chance keeps it. `LevelUpChance` scales them all by the same ratio.

A creature's faction decides which boss it answers to. These are the game's own factions, as the
server logs them with `Debug.LogFactions` in a [debug tools build](#debug-tools):

| Boss | Faction | Creatures |
| --- | --- | --- |
| Eikthyr | `AnimalsVeg`, plus overrides | Boars, necks, deer, greylings; hares |
| The Elder | `ForestMonsters` | Greydwarfs, trolls, Black Forest bears |
| Bonemass | `Undead` | Draugr, skeletons, blobs, leeches, wraiths, abominations, ghosts |
| Moder | `MountainMonsters`, plus overrides | Wolves, drakes, fenrings, cultists, bats; stone golems |
| Yagluth | `PlainsMonsters` | Fulings, deathsquitos, lox, Plains bears |
| The Queen | `MistlandsMonsters` | Seekers, gjall, ticks |
| Fader | `Demon` | Charred, morgen, asksvin, fallen valkyries, volture, surtlings |
| Frozen King | `DeepNorth` | Deep North creatures |

That table is `Stars.BossFactions`. The game puts the Meadows animals in `ForestMonsters` together
with the Black Forest, so `Stars.BossCreatures` moves boars, necks, deer and greylings to Eikthyr by
prefab name, and stone golems, also filed there, to Moder; `AnimalsVeg` itself holds only hares. Serpents, the dvergr and anything else whose
faction is not listed have no boss.

The boss counts as killed once its own defeat key is set, which is what vanilla uses for the same
purpose. Its head counts as placed while its trophy hangs on the matching stone of the sacrificial
stones at the spawn. The server checks the stones every ten seconds and tells every client. Vanilla
never lets a boss trophy be taken down again. A boss without a trophy, the
Frozen King, only needs killing: its final phase's defeat key unlocks five stars on its own.

### Which spawns

| Spawn | Stars | Colors |
| --- | --- | --- |
| Open world | New rules | Yes |
| Raids | New rules | Yes |
| Fixed spawns in locations and dungeons | New rules | Yes |
| Spawner structures: greydwarf nests, bone piles, and so on | Vanilla, 0 to 2 | No |

Vanilla gives raids no stars at all, since every raid's spawns are set to level one. Here a raider
can have stars whenever its creature can have stars anywhere in the world: from the open world, a
location, or a spawner structure.

Vanilla also keeps some open world spawns near the world center at zero stars, so the start stays
gentle. That protection lasts until Eikthyr's trophy hangs on the stones, and then it is gone for
every creature (`Stars.CenterProtectionBoss`).
| Offspring of tames | The parent's level, as in vanilla | No |

### Loot

Vanilla doubles loot with each star. That continues to two stars and then grows by two per star:

| Stars | 0 | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- | --- |
| Loot | 1x | 2x | 4x | 6x | 8x | 10x |

Health and damage follow vanilla's own rules, which already extend past two stars: health is the
zero star health times the level, and damage grows by half per star. A five star greydwarf has six
times the health and three and a half times the damage.

### Look

Vanilla gives each creature its own size and tint for one and two stars, usually 10% and 20% bigger
with a hue shift picked per creature. Three to five stars carry on from the two star look:

- **Size** grows by a further 5% per star, so a five star greydwarf is 35% bigger than a normal one
  where a two star is 20%.
- **Tint** gets a hue of its own for each of three, four and five stars. Each is picked as far
  around the color wheel as possible from the creature's zero, one and two star hues and from the
  hues already picked. Each star past two also deepens it a little: 0.1 more saturation and 0.05
  less brightness. Five stars go further, another 0.25 saturation and 0.3 less brightness, since
  three hues cannot always be far apart once a creature's own tints are avoided, and a greydwarf's
  three and five star hues land only an eighth of the wheel apart.

A greydwarf, which vanilla shifts by -0.06 and -0.5 of the wheel, gets +0.25, -0.28 and +0.12 for
three, four and five stars; a wolf gets +0.45, -0.32 and +0.22. Creatures vanilla never tints, such
as the charred, start on the opposite side of the wheel. The ragdoll keeps the same look.

Inside dungeons and other interiors, a creature above two stars stays at the largest size vanilla
gives it and only takes the new tint, so it still fits through the same doorways and corridors as
vanilla creatures. The game places every interior high above the world, and anything above a
height of 3000 counts, which is the game's own test.

The look is added to the game's own per creature list when a world loads, and the settings for it
are local to each player, since they change nothing but appearance.

## Raids

On a dedicated server, raids come in waves instead of running for a fixed time:

- A raid has one wave for every player online beyond the first, up to five, plus one for every 5
  comfort at the base, and between 1 and 8 waves in all. A lone player in a fresh base faces one
  wave, and two at comfort 10; a full group in a well furnished base faces eight.
- A wave is the raid's own spawn: one group of every creature in it, with the raid's group sizes,
  the same as a summoning boss's wave. They appear about 40m from a player inside the raid's area
  and hunt the players.
- Waves come every 3 minutes. The clock only runs while a player is inside the area, so leaving
  pauses the attack rather than ending it.
- The raid is over when all its waves have come and every creature from them is dead. Nothing runs
  away on a timer. After 2 hours it ends anyway, in case something is stuck, and whatever is left
  stops hunting and stays as ordinary monsters.
- Several bases can be under attack at once, but one base only one raid at a time: a raid picked
  for a base that is already under attack is skipped.

When and where raids happen is still vanilla's choice: its timer, its odds, its base check and its
list of raids for your progress. The mod takes over each raid once vanilla has picked it, which is
also why vanilla never sees one running and keeps picking raids for other bases.

The comfort is worked out on the server, since the game only keeps it on each player's machine: the
best comfort anywhere within 30m of where the raid starts, counted as if sheltered. The server only
has a base's pieces loaded while a player is near, so a raid sizes itself when the first player
enters its area, and that is also when it counts the players online. The music, sky
and start and end messages are vanilla's; every client is told which raids are running and plays
the one it is standing in.

Single player and self hosted games keep vanilla raids, since there the host is both the server and
a player and cannot run two kinds of raid at once. Raids in progress do not survive a server restart.

Because the mod takes a raid over before vanilla's own `RandEventSystem.m_randomEvent` is ever set
on the server, a mod that only reads that field never sees one of these raids running there. `Server
Authority` needs to, for its own per-position weather, so `RaidWeather.cs` exposes the raids running
here through `RaidWeather.Get`, a public static method taking BCL and Unity/game types only: it
fills caller supplied lists with each active raid's position, range, biome mask, forced environment
and name, one entry per raid that forces an environment. Server Authority binds to it by reflection,
with no reference either way, so this stays a contract rather than a dependency; do not change its
signature without checking that side too.

## Colors

A creature with at least one star has a 60% chance of ordinary gold stars. Otherwise its stars are
one of six colors, equally likely:

| Color | Trait | Effect |
| --- | --- | --- |
| Magenta | Fast | +40% movement speed, +50% turning speed |
| Red | Aggressive | +25% attack speed; half the wait between two attacks and half the time spent circling; +150% time between two bouts of circling |
| Green | Regenerating | Heals every second, never while burning; see below |
| Cyan | Curious | Twice the sight range, and hears your noise from twice as far |
| White | Splitting | On death, splits into two of itself with half the stars, also white |
| Blue | Armored | 30% less damage taken, 25% slower |

**Green** heals `H * 10 * log10(max(10, H - 1000)) / (H + 1000) * 1.2` health per second, where `H`
is the zero star health times 1 plus 0.25 per star. That is about 0.7 a second for a two star
greydwarf, 6 for a two star troll and 22 for a three star lox.

**White** halves the stars rounding down, so a four star greydwarf becomes two two star greydwarfs,
then four one star ones, then eight with no stars. Each drops its own loot. A split raid creature
stays a raid creature and leaves when the raid ends.

**Red**'s attack speed speeds up the attack animation, which is where the hit lands. Circling only
affects creatures that circle their target in vanilla.

**Cyan** matters because most monsters see 30m and hear any noise at any distance, so hearing is
really limited by how much noise you make. A cyan greydwarf sees 60m and hears you at twice the
distance your noise normally carries.

## Bosses

Every boss gets two stars, each a different color. They do not change the boss's health or damage;
each color gives it a trait instead, and every pair is equally likely:

| Color | Trait | Effect |
| --- | --- | --- |
| Magenta | Fast | +25% movement speed, +50% turning speed |
| Red | Aggressive | +25% attack speed, and a quarter off the wait between two attacks |
| Green | Regenerating | Heals 0.125% of its maximum health per second, half as much while burning |
| Cyan | Summoning | Calls a wave from its own raid at every 18% of health lost, 5 waves in all |
| White | Splitting | Drops nothing on death and splits into two bosses without stars |
| Blue | Shielded | Half damage from magic, a quarter off damage from arrows and bolts |

Regeneration is about 6 health a second for Bonemass and 12.5 for Yagluth. Bonemass brings rain,
which puts out fire, so a green Bonemass has to be outdamaged; that is part of the challenge.

A summoning boss calls its waves at 82, 64, 46, 28 and 10 percent health. Each wave is one group of
every creature in the boss's raid, as the raid itself spawns them, 30m from the boss, and they hunt
the players. The server remembers how many waves a boss has called, so healing back above a
threshold never calls that wave again. Their stars and colors are rolled as for any other spawn.

| Boss | Raid | One wave |
| --- | --- | --- |
| Eikthyr | `army_eikthyr` | a neck and a boar |
| The Elder | `army_theelder` | 1-3 greydwarfs, 1-3 greylings, a shaman, a brute |
| Bonemass | `army_bonemass` | a draugr and a skeleton |
| Moder | `army_moder` | a drake |
| Yagluth | `army_goblin` | 1-4 fulings, a berserker, a shaman |
| The Queen | `army_seekers` | a seeker, a brood and a soldier |
| Fader | `army_charred` | a charred warrior, twitcher and archer |

A splitting boss does not count as killed when it splits. The two it splits into each drop the full
loot, trophy included, and each is a full boss, so the first of them to die sets the defeat key. If
the splitting boss's other star is, say, red, the two copies are plain bosses without it.

Magic is anything using Elemental or Blood Magic, arrows anything using Bows or Crossbows.

The Frozen King gets no stars. It fights in three phases, each its own boss, so stars per phase
would change mid fight and a white one would split. `BossColors.StarlessBosses` lists such bosses.

## HUD

Stars above two, and every colored star, are drawn by the mod in place of vanilla's stars, centered
over the health bar as vanilla's are. A colored star keeps vanilla's black outline and changes only
its fill. A boss's two stars sit at the two ends of its health bar.

## Configuration

Written to `BepInEx/config/valheim.creatures.cfg` on first run. Only the server's copy matters while
connected to a server; a client's own copy applies in single player and when hosting.

| Setting | Default | Notes |
| --- | --- | --- |
| `General.RequireClientMod` | `true` | Server only. Disconnect players without the mod or with another version. |
| `Stars.MaxStars` | `5` | Once the faction's boss is killed and its head placed. |
| `Stars.MaxStarsBeforeTrophy` | `3` | Until then. |
| `Stars.MaxStarsWithoutBoss` | `2` | Factions with no boss. 2 is vanilla. |
| `Stars.LevelUpChance` | `10` | Percent per star. 10 is vanilla. |
| `Stars.BossDefeatedChanceMultiplier` | `2` | Once the faction's boss is killed. |
| `Stars.BossFactions` | see above | `BossPrefab:Faction` pairs. |
| `Stars.CenterProtectionBoss` | `Eikthyr` | Whose trophy lifts vanilla's zero stars near the world center. Empty keeps it. |
| `Stars.BossCreatures` | Meadows animals to Eikthyr, stone golems to Moder | `CreaturePrefab:BossPrefab` pairs, overriding the faction. |
| `CreatureColors.Normal` | `90` | Relative weights. 90 against 10 for each color is 60% normal. |
| `CreatureColors.<Color>` | `10` | One per color. |
| `BossColors.<Color>` | `1` | One per boss color. |
| `BossColors.StarlessBosses` | the Frozen King's phases | Bosses that never get a star. |
| `BossColors.StarsPerBoss` | `2` | 1 or 2. |
| `Magenta.MoveSpeed` | `0.4` | |
| `Magenta.TurnSpeed` | `0.5` | |
| `Red.AttackSpeed` | `0.25` | |
| `Red.AttackIntervalReduction` | `0.5` | |
| `Red.CircleDurationReduction` | `0.5` | |
| `Red.CircleIntervalIncrease` | `1.5` | |
| `Green.RateMultiplier` | `1.2` | The 1.2 in the formula. |
| `Cyan.SenseRangeIncrease` | `1` | |
| `Blue.DamageReduction` | `0.3` | |
| `Blue.MoveSpeedReduction` | `0.25` | |
| `BossMagenta.MoveSpeed` | `0.25` | |
| `BossMagenta.TurnSpeed` | `0.5` | |
| `BossRed.AttackSpeed` | `0.25` | |
| `BossRed.AttackIntervalReduction` | `0.25` | |
| `BossGreen.PercentPerSecond` | `0.125` | |
| `BossBlue.MagicReduction` | `0.5` | |
| `BossCyan.Raids` | see above | `BossPrefab:RaidName` pairs. |
| `BossCyan.Waves` | `5` | |
| `BossCyan.HealthStep` | `18` | Percent of health between waves. |
| `BossCyan.Distance` | `30` | Meters from the boss. |
| `BossBlue.ArrowReduction` | `0.25` | |
| `Raids.Waves` | `true` | Dedicated server only. `false` is vanilla raids. |
| `Raids.WaveInterval` | `180` | Seconds between waves, counted while a player is in the area. |
| `Raids.MaxWaves` | `8` | |
| `Raids.PlayerWavesMax` | `5` | Most waves from players online beyond the first. |
| `Raids.ComfortPerWave` | `5` | Comfort per extra wave; 0 turns it off. |
| `Raids.TimeoutMinutes` | `120` | |
| `Raids.SpawnDistance` | `40` | Meters from a player in the area. |
| `Looks.ScalePerStar` | `0.05` | Local. Size added per star past two. |
| `Looks.SaturationPerStar` | `0.1` | Local. |
| `Looks.ValuePerStar` | `-0.05` | Local. Brightness; negative is darker. |
| `Looks.FiveStarSaturation` | `0.25` | Local. Extra for five stars only. |
| `Looks.FiveStarValue` | `-0.3` | Local. Extra for five stars only. |
| `Debug.LogFactions` | `false` | Debug tools build only. Local. Lists every creature's faction, each boss's defeat key and trophy, and every raid's creatures, on world load. |
| `Debug.LogColors` | `false` | Debug tools build only. Local. Logs the colors this machine reads and what the HUD draws. |
| `Debug.LogRolls` | `false` | Debug tools build only. Local. Logs every spawn rolled or skipped, with the reason. |
| `Debug.EnableTestCommands` | `false` | Debug tools build only. Server or host. See [Testing](#testing). |

Changing a setting on the server reaches connected clients at once. Creatures already in the world
keep their stars and color; new traits apply to them straight away.

## Testing

The test commands exist only in a [debug tools build](#debug-tools). With `Debug.EnableTestCommands` on, the server watches `BepInEx/config/valheimcreatures_test.txt`,
runs each line next to the first connected player and empties the file. It waits 5 seconds after a
player connects, until the server knows where they are. Results go to the server
log. It is a cheat hook whose only authentication is access to the server's files, so leave it off
outside testing.

| Command | Does |
| --- | --- |
| `spawn <Prefab> [count] [stars] [color] [distance]` | Spawns creatures with exactly these stars and color, 10m away in a random direction by default. A boss takes two colors as `Red+Green`, and rolls its own when given none. |
| `item <Prefab> [count] [quality]` | Drops items at the player's feet, upgraded to a quality. |
| `roll <Prefab> [samples]` | Rolls stars and colors for that creature where the player stands, 10000 times by default, and logs the spread. |
| `key <globalkey>`, `unkey <globalkey>` | Sets or removes a global key, such as `defeated_gdking`. |
| `raid <name> [x z]` | Starts a raid at the player, or at a world position. A wrong name logs the valid ones. |
| `bases` | Logs every spot with 10 or more player built pieces, to find bases. |
| `raids` | Logs every raid in progress with its waves, creatures left, comfort and age. |
| `config <Section> <Key> <value>` | Changes a setting live, and clients receive it. |
| `clear [radius]` | Removes every creature that is not tamed within 40m by default. |
| `day` | Skips to the next morning, as sleeping does. |
| `status` | Logs each boss's stage and the trophies on the stones. |
| `nearby [radius]` | Logs every creature within 60m by default, with its stars and colors. |
| `wait [seconds]` | Holds the following lines until every creature spawned by `spawn` is dead, then this many seconds, 10 by default. Each of their deaths is logged with its drops. |

Server Authority's own spawn requests can be used alongside for anything else.

## Building

```bash
dotnet build src/ValheimCreatures -c Release
```

The build finds the game's assemblies in the usual Steam locations; set `VALHEIM_MANAGED` to the
`Managed` folder otherwise. Set `VALHEIM_PLUGINS` to a BepInEx plugins folder to have each build
copied there.

### Debug tools

A plain build leaves out every `Debug` setting and the test commands, and is the one to distribute.
For testing, build with them:

```bash
dotnet build src/ValheimCreatures -c Release -p:DebugTools=true
```

The startup line listing the effective configuration says which kind of build is running.

```bash
# Every [HarmonyPatch] target still exists and is unambiguous in the game assembly.
dotnet run --project tools/PatchCheck -c Release -- \
  src/ValheimCreatures/bin/Release/ValheimCreatures.dll \
  ~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed
```

Run `PatchCheck` after every Valheim update. Loot is the one transpiler: it swaps the `Mathf.Pow`
in `CharacterDrop.GenerateDropList` for the mod's own curve, and logs a warning at startup if that
call is no longer there exactly once.

## Layout

```
src/ValheimCreatures/
  Plugin.cs            Entry point
  ModConfig.cs         Configuration
  Balance.cs           The values in effect, and their wire format
  ConfigSync.cs        Server to client sync, and turning away clients without the mod
  StarColor.cs         The colors, their tints and names
  BossProgress.cs      Which bosses are killed and whose heads hang on the stones
  LevelRoll.cs         Stars and color for a new spawn
  CreatureTraits.cs    Applies a creature's color: speed, senses, attack timing, damage, healing
  Splitting.cs         White creatures and bosses splitting on death
  BossWaves.cs         Cyan bosses calling raid waves as they lose health
  WaveSpawner.cs       One wave of a raid, shared by raids and summoning bosses
  RaidDirector.cs      Raids as waves, several bases at once
  Loot.cs              The loot curve past two stars
  Looks.cs             Size and tint for three to five stars
  StarHud.cs           Extra and colored stars on the health bars
  StarCapable.cs       Which creatures can have stars anywhere, for raids
  TestCommands.cs      The test hook
  Patches/             Harmony hooks
tools/PatchCheck/      Resolves every patch target against the game assembly
```
