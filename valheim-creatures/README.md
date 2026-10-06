# Valheim Creatures

Harder creatures for Valheim: up to five stars, colored stars that give a creature a trait, colored
stars on bosses, and raids that come in waves. Installed on the dedicated server and on every client.
Loosely based on Creature Level and Loot Control, with a smaller scope and different rules.

The server's configuration is the one that counts; each client receives it when it connects. By
default the server disconnects anyone without the mod or with another version of it. Every trait is
stored on the creature and applied by whichever machine owns it, so it works the same whether a
player or, under [Server Authority](../valheim-server-side-mod), the server owns it. Nothing here
patches an RPC method, and nothing on the server touches the local player.

## Stars

A creature that can have stars in vanilla can now have up to five. One that never has stars in
vanilla still never does.

| Faction's boss | Most stars |
| --- | --- |
| Alive | 3 |
| Killed | 3 |
| Killed, and its trophy hangs on the sacrificial stones | 5 |
| Faction has no boss | 2, as in vanilla |

Each further star is rolled at vanilla's 10%, so 1 in 10 creatures has at least one star and 1 in
100 at least two. World modifiers and world level still apply, and a spawn that sets its own chance
keeps it. `LevelUpChance` scales them all.

A creature's faction decides its boss:

| Boss | Faction | Creatures |
| --- | --- | --- |
| Eikthyr | `AnimalsVeg`, plus overrides | Boars, necks, deer, greylings, hares |
| The Elder | `ForestMonsters` | Greydwarfs, trolls, Black Forest bears |
| Bonemass | `Undead` | Draugr, skeletons, blobs, leeches, wraiths, abominations, ghosts, swamp bats, asksvin hatchlings |
| Moder | `MountainMonsters`, plus overrides | Wolves, drakes, fenrings, cultists, cave bats, stone golems |
| Yagluth | `PlainsMonsters` | Fulings, deathsquitos, lox, Plains bears |
| The Queen | `MistlandsMonsters` | Seekers, gjall, ticks |
| Fader | `Demon` | Charred, morgen, asksvin, fallen valkyries, volture, surtlings |
| Frozen King | `DeepNorth` | Deep North creatures |

The game files the Meadows animals and stone golems under `ForestMonsters`, so `Stars.BossCreatures`
moves boars, necks, deer and greylings to Eikthyr and stone golems to Moder by prefab name. Serpents,
the dvergr and any other unlisted faction have no boss.

A boss counts as killed once its defeat key is set, as vanilla uses it. Its trophy counts while it
hangs on one of the sacrificial stones; the server checks every ten seconds and tells every client.
The Frozen King has no trophy, so killing its final phase unlocks five stars on its own.

| Spawn | Stars | Colors |
| --- | --- | --- |
| Open world | New rules | Yes |
| Raids | New rules | Yes |
| Fixed spawns in locations and dungeons | New rules | Yes |
| Spawner structures: greydwarf nests, bone piles and so on | Vanilla, 0 to 2 | No |
| Offspring of tames | The parent's level, as in vanilla | No |

Vanilla gives raiders no stars. Here a raider can have stars if its creature can have stars anywhere
in the world. Vanilla keeps some open world spawns near the world center at zero stars; that lasts
only until Eikthyr's trophy hangs on the stones (`Stars.CenterProtectionBoss`).

### Loot, health and damage

Vanilla doubles loot with each star. That continues to two stars and then grows by two per star:

| Stars | 0 | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- | --- |
| Loot | 1x | 2x | 4x | 6x | 8x | 10x |

Health and damage follow vanilla's rules, which already extend past two stars: health is the zero
star health times the level, damage grows by half per star. A five star greydwarf has six times the
health and three and a half times the damage.

### Look

Three to five stars continue vanilla's one and two star look. Size grows a further 5% per star, so
five stars is 35% bigger than normal. Each of three, four and five stars gets a hue picked as far
around the color wheel as possible from the creature's own tints, deepened a little per star and
more at five. Inside dungeons, which the game places above a height of 3000, a creature keeps
vanilla's largest size and only takes the tint, so it still fits through doorways. The look settings
are local to each player.

## Raids

On a dedicated server raids come in waves instead of running for a fixed time:

- A raid has one wave per player in its area beyond the first, up to five, plus one per 5 comfort at
  the base, between 1 and 8 in all. A lone player in a fresh base faces one wave; a full group in a
  well furnished base faces eight.
- A wave is one group of every creature in the raid, with the raid's group sizes. It appears about
  40 m from a player in the raid's area, outside any player base and not under cover, and hunts the
  players. Each group gets 40 tries, reaching out to twice the distance; a group with no open ground
  is left out and logged. A wave that finds no open ground at all ends the raid.
- The next wave comes 2 minutes after the last one appeared, even if it still lives, or 20 seconds
  after the raid's last live wave is defeated if that is sooner. Both clocks only run while a player
  is in the area. At most two waves of a raid are alive at once; a wave that is due waits for one to
  be defeated.
- Raiders and wild creatures ignore each other and cannot hurt each other. Raiders still fight
  players and tamed creatures.
- A raider with nothing to fight drifts toward the raid's centre. One that strays more than 60 m from
  the centre, with no player within 30 m and no player target within 60 m, walks back for 30 seconds
  ignoring everything else, until it is within 30 m or a player comes close. One that strays again
  after three trips back leaves the raid and stays as an ordinary monster, as does one that is
  tamed.
- A wave is defeated when every creature in it is dead, has left the raid, or has been beyond every
  player's loaded area for a minute while a player is in the raid. Players in the area are then told,
  in the raid's own words, whether most of it, half of it or only the last of it is still to come.
- The raid ends when all its waves have come and been defeated, with vanilla's end message. After 2
  hours it ends anyway. Whatever is left of a raid that is over, timed out or lost to a restart stops
  hunting.
- Several bases can be under attack at once, but one base only one raid at a time.
- Hildir's three raids are fought around their boss instead: one wave with the boss and one group of
  each of the rest, after which the rest keep coming as in vanilla, every 20 seconds (the strong one
  every 100) up to vanilla's most alive at once, while a player is in the area. Once the boss is
  down nothing more comes, and the raid ends when what is left is defeated.

When and where raids happen is still vanilla's choice. The mod takes each raid over once vanilla has
picked it. Comfort is worked out on the server as the best comfort within 30 m of the raid's start,
counted as sheltered, when the first player enters the area; the raid grows if more players are in
the area at once later, and never shrinks. Music, sky and the start and end messages are vanilla's.

Single player and self hosted games keep vanilla raids. Raids in progress do not survive a restart.

Because vanilla's `RandEventSystem.m_randomEvent` is never set on the server for these raids,
`RaidWeather.Get` exposes them: a public static method that fills caller supplied lists with each
weather-forcing raid's position, range, biome mask, environment and name. Server Authority binds to
it by reflection for its per-position weather, so do not change its signature without checking that
side.

## Colors

A creature with at least one star has a 60% chance of gold stars. Otherwise its stars are one of six
colors, equally likely:

| Color | Trait | Effect |
| --- | --- | --- |
| Magenta | Fast | +40% movement speed, +50% turning speed |
| Red | Aggressive | +25% attack speed, half the wait between attacks, half the time spent circling, +150% time between bouts of circling |
| Green | Regenerating | Heals every second, never while burning |
| Cyan | Curious | Twice the sight range, and hears noise from twice as far |
| White | Splitting | On death, splits into two of itself with half the stars, also white |
| Blue | Armored | 30% less damage taken, 25% slower |

**Green** heals `H * 10 * log10(max(10, H - 1000)) / (H + 1000) * 1.2` per second, where `H` is the
zero star health times 1 plus 0.25 per star: about 0.7 for a two star greydwarf, 6 for a two star
troll and 22 for a three star lox.

**White** halves the stars rounding down, so a four star greydwarf becomes two with two stars, then
four with one, then eight with none, each dropping its own loot. A split raider stays a raider.

**Cyan** matters because most monsters see 30 m and hear noise at any distance, so hearing is limited
by how much noise you make.

## Bosses

Every boss except the Frozen King gets two stars of different colors. They add no health or damage;
each color gives a trait:

| Color | Trait | Effect |
| --- | --- | --- |
| Magenta | Fast | +25% movement speed, +50% turning speed |
| Red | Aggressive | +25% attack speed, a quarter off the wait between attacks |
| Green | Regenerating | Heals 0.125% of its maximum health per second, half while burning |
| Cyan | Summoning | Calls a wave from its raid at every 18% of health lost, 5 waves in all |
| White | Splitting | Drops nothing and splits into two full bosses without stars |
| Blue | Shielded | Half damage from magic, a quarter off from arrows and bolts |

Regeneration is about 6 health a second for Bonemass and 12.5 for Yagluth. Magic is Elemental or
Blood Magic, arrows are Bows or Crossbows.

A summoning boss calls its waves at 82, 64, 46, 28 and 10 percent health, 30 m from the boss,
outside any player base and not under cover. Healing back above a threshold never repeats a wave.

| Boss | Raid | One wave |
| --- | --- | --- |
| Eikthyr | `army_eikthyr` | a neck and a boar |
| The Elder | `army_theelder` | 1-3 greydwarfs, 1-3 greylings, a shaman, a brute |
| Bonemass | `army_bonemass` | a draugr and a skeleton |
| Moder | `army_moder` | a drake |
| Yagluth | `army_goblin` | 1-4 fulings, a berserker, a shaman |
| The Queen | `army_seekers` | a seeker, a brood and a soldier |
| Fader | `army_charred` | a charred warrior, twitcher and archer |

A splitting boss does not count as killed when it splits. Each copy drops the full loot, trophy
included, and the first to die sets the defeat key. The copies lose the boss's other color.

The Frozen King fights in three phases, each its own boss, so stars would change mid fight
(`BossColors.StarlessBosses`).

## Players nearby

Vanilla gives a creature 30% more health and 4% more damage for each player beyond the first within
100 m of whoever is hit, counted on the flat, up to five players. The mod keeps the health and drops the damage, so a
creature hits as hard however many players are near. That also applies to tames, which vanilla
gives the 4% whenever a non-player hits or is hit. `Difficulty.DamagePerPlayer = 0.04` restores it.

## HUD

Stars above two, and every colored star, are drawn by the mod over the health bar where vanilla's
are, with a colored fill. A boss's two stars sit at the ends of its health bar.

## Configuration

Written to `BepInEx/config/valheim.creatures.cfg` on first run. While connected, only the server's
copy matters, except the `Looks` settings, which are local. Editing the file needs a restart; a
setting changed while the server runs, such as with the `config` test command, reaches clients at
once. Creatures already in the world keep their stars and color.

| Setting | Default | Notes |
| --- | --- | --- |
| `General.RequireClientMod` | `true` | Server only. Disconnect players without the mod or with another version. |
| `Stars.MaxStars` | `5` | Once the faction's boss is killed and its trophy placed. |
| `Stars.MaxStarsBeforeTrophy` | `3` | Until then. |
| `Stars.MaxStarsWithoutBoss` | `2` | Factions with no boss. 2 is vanilla. |
| `Stars.LevelUpChance` | `10` | Percent per star. 10 is vanilla. |
| `Stars.BossFactions` | see above | `BossPrefab:Faction` pairs. |
| `Stars.CenterProtectionBoss` | `Eikthyr` | Whose trophy lifts vanilla's zero stars near the world center. Empty keeps it. |
| `Stars.BossCreatures` | Meadows animals to Eikthyr, stone golems to Moder | `CreaturePrefab:BossPrefab` pairs, overriding the faction. |
| `CreatureColors.Normal` | `90` | Relative weight; 90 against 10 per color is 60% gold. |
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
| `BossBlue.ArrowReduction` | `0.25` | |
| `BossCyan.Raids` | see above | `BossPrefab:RaidName` pairs. |
| `BossCyan.Waves` | `5` | |
| `BossCyan.HealthStep` | `18` | Percent of health between waves. |
| `BossCyan.Distance` | `30` | Meters from the boss. |
| `Raids.Waves` | `true` | Dedicated server only. `false` is vanilla raids. |
| `Raids.WaveInterval` | `20` | Seconds from the raid's last live wave being defeated to the next, while a player is in the area. |
| `Raids.NextWaveMinutes` | `2` | Minutes from one wave appearing to the next, while a player is in the area. |
| `Raids.MaxActiveWaves` | `2` | Most waves of one raid alive at once. |
| `Raids.BossRaids` | Hildir's three raids | `RaidName:BossPrefab` pairs for raids fought around one boss. |
| `Raids.MaxWaves` | `8` | |
| `Raids.PlayerWavesMax` | `5` | Most waves from players in the area beyond the first. |
| `Raids.ComfortPerWave` | `5` | Comfort per extra wave; 0 turns it off. |
| `Raids.TimeoutMinutes` | `120` | |
| `Raids.SpawnDistance` | `40` | Meters from a player, reaching out to twice this to clear a base. |
| `Raids.ReturnDistance` | `60` | Meters from the raid's centre at which a raider has strayed. |
| `Raids.ReturnSeconds` | `30` | How long a straying raider walks back; 0 turns it off. |
| `Raids.ReturnTries` | `3` | Trips back before a raider that keeps straying leaves the raid. |
| `Difficulty.DamagePerPlayer` | `0` | Extra creature damage per nearby player beyond the first. 0.04 is vanilla. |
| `Looks.ScalePerStar` | `0.05` | Size added per star past two. |
| `Looks.SaturationPerStar` | `0.1` | |
| `Looks.ValuePerStar` | `-0.05` | Brightness; negative is darker. |
| `Looks.FiveStarSaturation` | `0.25` | Extra for five stars only. |
| `Looks.FiveStarValue` | `-0.3` | Extra for five stars only. |
| `Debug.LogFactions` | `false` | Debug tools build only. Every creature's faction, each boss's key and trophy, and every raid's creatures, on world load. |
| `Debug.LogColors` | `false` | Debug tools build only. The colors this machine reads and what the HUD draws. |
| `Debug.LogRolls` | `false` | Debug tools build only. Every spawn rolled or skipped, with the reason. |
| `Debug.EnableTestCommands` | `false` | Debug tools build only. See [Testing](#testing). |

## Testing

Only in a [debug tools build](#debug-tools). With `Debug.EnableTestCommands` on, the server reads
`BepInEx/config/valheimcreatures_test.txt`, runs each line next to the first connected player (5
seconds after they connect) and empties the file. Results go to the server log. It is a cheat hook
guarded only by access to the server's files.

| Command | Does |
| --- | --- |
| `spawn <Prefab> [count] [stars] [color] [distance]` | Spawns with exactly these stars and color, 10 m away by default. A boss takes two colors as `Red+Green`, or rolls its own. |
| `item <Prefab> [count] [quality]` | Drops items at the player's feet. |
| `roll <Prefab> [samples]` | Rolls stars and colors for that creature where the player stands, 10000 times by default, and logs the spread. |
| `key <globalkey>`, `unkey <globalkey>` | Sets or removes a global key, such as `defeated_gdking`. |
| `raid <name> [x z]` | Starts a raid at the player or a position. A wrong name logs the valid ones. |
| `bases [cell]` | Logs every grid cell, 64 m by default, with 10 or more player built pieces. |
| `raids` | Every raid in progress with its waves, creatures left, comfort and age. |
| `config <Section> <Key> <value>` | Changes a setting live; clients receive it. |
| `clear [radius]` | Removes every untamed creature within 40 m by default. |
| `day` | Skips to the next morning. |
| `status` | Each boss's stage and the trophies on the stones. |
| `nearby [radius]` | Every creature within 60 m by default, with stars and colors. |
| `wait [seconds]` | Holds the following lines until every creature from `spawn` is dead, then 10 seconds by default, logging each death with its drops. |

## Building

```bash
dotnet build src/ValheimCreatures -c Release
```

The game's assemblies are found in the usual Steam locations, or set `VALHEIM_MANAGED` to the
`Managed` folder. Set `VALHEIM_PLUGINS` to a BepInEx plugins folder to have each build copied there.

### Debug tools

A plain build leaves out every `Debug` setting and the test commands, and is the one to distribute.
For testing:

```bash
dotnet build src/ValheimCreatures -c Release -p:DebugTools=true
```

After every Valheim update, check that every `[HarmonyPatch]` target still resolves:

```bash
dotnet run --project tools/PatchCheck -c Release -- \
  src/ValheimCreatures/bin/Release/ValheimCreatures.dll \
  ~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed
```

The loot curve is a transpiler on the `Mathf.Pow` in `CharacterDrop.GenerateDropList`, and logs a
warning at startup if that call is no longer there exactly once.

## Layout

```
src/ValheimCreatures/
  Plugin.cs              Entry point
  ModConfig.cs           Configuration
  EffectiveSettings.cs   Logs the effective configuration and the build kind at startup
  Balance.cs             The values in effect, and their wire format
  ConfigSync.cs          Server to client sync, and turning away clients without the mod
  StarColor.cs           The colors, their tints and names
  BossProgress.cs        Which bosses are killed and whose trophies hang on the stones
  LevelRoll.cs           Stars and color for a new spawn
  CreatureTraits.cs      Applies a creature's color
  Splitting.cs           White creatures and bosses splitting on death
  BossWaves.cs           Cyan bosses calling raid waves
  WaveSpawner.cs         One wave of a raid, shared by raids and summoning bosses
  RaidDirector.cs        Raids as waves, several bases at once
  Raiders.cs             Raid creatures: ignoring wild creatures, heading back, leaving the raid
  RaidMessages.cs        What players are told as a raid's waves are defeated
  RaidWeather.cs         The running raids, for Server Authority's weather
  Loot.cs                The loot curve past two stars
  Looks.cs               Size and tint for three to five stars
  StarHud.cs             Extra and colored stars on the health bars
  StarCapable.cs         Which creatures can have stars anywhere, for raids
  TestCommands.cs        The test hook
  Patches/               Harmony hooks
tools/PatchCheck/        Resolves every patch target against the game assembly
```
