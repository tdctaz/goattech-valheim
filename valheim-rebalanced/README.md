# Valheim Rebalanced

Balance changes for Valheim. Installed on the dedicated server and on every client.

The server's configuration is the one that counts; each client receives it when it connects. Most
changes run on the player's own machine, so by default the server disconnects anyone without the
mod or with another version of it. A rejected player sees the game's "incompatible version" message
and the server log says why. Works with [Server Authority](../valheim-server-side-mod).

## Changes

### Staff of the Wild: one root per player

Vanilla caps roots at 10 among all those loaded, shared by every player. The staff also writes a
per-root limit of 2 to 6 by skill, but only `Tameable` reads it and a root has none, so it never
applies.

Here each player may have `StaffOfTheWild.MaxRootsPerPlayer` roots, 1 by default. Summoning another
kills that player's oldest root, the same way the game expires one after 23 to 25 seconds. The
caster's machine tells its roots apart by the session in their ZDOID, and claims a root it does not
own before killing it. Skill and staff quality keep their effect, and vanilla's cap of 10 still
applies.

### Tower shields: resistance while blocking

The shields that cannot parry, the wood, bone, iron, serpentscale, black metal, flametal and gold
tower shields, take a quarter off pierce, blunt and slash while blocking (`SlightlyResistant`). The
modifiers are added to the shield's item data, which `Humanoid.BlockAttack` already applies to a
blocked hit before block power, so they work against monsters and players and show in the tooltip.
A shield that already resists a type keeps its own value, so the serpentscale shield keeps its
vanilla half damage from pierce. The unobtainable iron square shield, which also cannot parry, gets
them too.

### Root armor: pierce resistance is the set bonus

| | Vanilla | Rebalanced |
| --- | --- | --- |
| Root harnesk | Pierce resistant, fire weak | +15 Bows ("Improved archery"), fire weak |
| Set bonus, 3 pieces | +15 Bows ("Improved archery") | Pierce resistant ("Root armor") |

The mask's poison resistance and every piece's fire weakness are unchanged. The new set bonus's name
and description are English only.

### Staff of Protection: one target per cast

Vanilla drops a 5 m bubble that shields every friendly inside it, caster included, which makes the
staff very strong in a group. Here each cast shields one character:

| Action | Rebalanced |
| --- | --- |
| Block | Shields the caster as the block starts, unless they are already shielded. |
| Attack | Fires a bolt that shields the first ally it hits, within 20 m. |

The self cast plays vanilla's cast animation and commits the caster like an attack; holding the
button then blocks as normal. The bolt uses the Staff of Embers' cast animation. It stops at the
first thing it hits, so an enemy or a body in the way takes it and nothing happens. Both casts cost
the attack's vanilla eitr and health and apply the game's own `Staff_shield` effect, so duration,
absorption and skill gain are unchanged. The bolt and the cast effect are built from the game's own
prefabs when a world loads.

### Staff of Embers: half the blunt

Vanilla deals 120 blunt and 120 fire at quality 1, and only the fire grows on upgrade (+6 per level).
Almost nothing resists blunt, so half the damage landed whatever the target. The blunt is halved
(`StaffOfEmbers.BluntMultiplier`) and the fire left alone: 60 blunt and 120 fire at quality 1. The
direct hit and the 3 m explosion both use the staff's numbers; the cinders' ground fire is separate
and unchanged.

### Staff of Fracturing: half the fire

A cast throws a bomb that bursts into 12 splinters, and each splinter carries the staff's whole
damage, so a cast that lands fully deals twelve times the tooltip. The fire is halved, base and
upgrades alike (`StaffOfFracturing.FireMultiplier`), and the blunt left alone:

| Per splinter | Blunt | Fire |
| --- | --- | --- |
| Vanilla, quality 1 | 12 | 12 |
| Rebalanced, quality 1 | 12 | 6 |
| Vanilla, quality 4 | 12 | 30 |
| Rebalanced, quality 4 | 12 | 15 |

### Trollstav: one troll, and a cost that can kill

| | Vanilla | Rebalanced |
| --- | --- | --- |
| Summoned trolls at once | 2 | 1 |
| Health a cast costs | 60% of current health | 60, or 60% of current health, whichever is greater |
| When that is more than you have | Leaves you on 1 health | Kills you as the cast goes out |

Blood Magic takes up to a third off, as in vanilla, so a cast costs
`max(60, 0.6 * health) * (1 - 0.33 * skill / 100)`. That is fatal below 60 health at Blood Magic 0
and below 40 at 100, so the staff stops being a cheap summon for a player who is already low.

The limit counts every summoned troll loaded, whoever cast it, with no distance limit. A cast past
it is refused before the swing starts, with "The bond is spoken for. Another troll will not heed
you", and costs neither eitr nor health; vanilla only refuses once both are spent.

The tooltip lists the 60 as a flat cost beside the percentage, and the mod writes the real cost onto
each cast. A fatal cast leaves the caster on 1 health as vanilla would, then kills them with
unblockable self damage the moment the cast leaves the staff. The troll still arrives two and a half
seconds later.

### Burnt wood: a fifth of the coal

Vanilla's spreading fire turns every Wood, Fine wood, Core wood and Blackwood a burning piece or log
would have dropped into one coal, a faster and free charcoal kiln. Here it takes
`BurntWood.CoalDivisor` wood per coal, 5 by default, rounded up:

| Wood burnt | Vanilla | Rebalanced |
| --- | --- | --- |
| 1 | 1 coal | 1 coal |
| 4 | 4 coal | 1 coal |
| 50 | 50 coal | 10 coal |
| 51 | 51 coal | 11 coal |

The mod cuts what `Game.CheckDropConversion` returns when the result is coal. A piece rounds each of
its resources on its own; a log totals all its wood first. The charcoal kiln and the Obliterator are
unchanged. Each conversion is logged by whoever owns the burning object.

## Configuration

Written to `BepInEx/config/valheim.rebalanced.cfg` on first run. While connected, only the server's
copy matters. A changed setting needs a restart.

| Setting | Default | Notes |
| --- | --- | --- |
| `General.RequireClientMod` | `true` | Server only. Disconnect players without the mod or with another version. |
| `StaffOfTheWild.MaxRootsPerPlayer` | `1` | 0 is vanilla. |
| `TowerShields.Pierce` | `SlightlyResistant` | `Normal` is vanilla. |
| `TowerShields.Blunt` | `SlightlyResistant` | `Normal` is vanilla. |
| `TowerShields.Slash` | `SlightlyResistant` | `Normal` is vanilla. |
| `RootArmor.SwapBonuses` | `true` | `false` is vanilla. |
| `StaffOfProtection.SingleTarget` | `true` | `false` is vanilla. |
| `StaffOfEmbers.BluntMultiplier` | `0.5` | 1 is vanilla. |
| `StaffOfFracturing.FireMultiplier` | `0.5` | 1 is vanilla. |
| `Trollstav.MaxSummons` | `1` | 2 is vanilla. |
| `Trollstav.HealthCost` | `60` | The least health a cast costs. 0 is vanilla. |
| `Trollstav.LethalHealthCost` | `true` | Let the cost kill the caster. `false` is vanilla. |
| `BurntWood.CoalDivisor` | `5` | Wood burnt per coal, rounded up. 1 is vanilla. |

## Building

```bash
dotnet build src/ValheimRebalanced -c Release
```

The game's assemblies are found in the usual Steam locations, or set `VALHEIM_MANAGED` to the
`Managed` folder. Set `VALHEIM_PLUGINS` to a BepInEx plugins folder to have each build copied there.
Rebalanced has no debug options, but accepts `-p:DebugTools=true` like the other GoatTech mods.

After every Valheim update, check that every `[HarmonyPatch]` target still resolves:

```bash
dotnet run --project tools/PatchCheck -c Release -- \
  src/ValheimRebalanced/bin/Release/ValheimRebalanced.dll \
  ~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed
```

## Layout

```
src/ValheimRebalanced/
  Plugin.cs              Entry point
  ModConfig.cs           Configuration
  EffectiveSettings.cs   Logs the effective configuration and the build kind at startup
  Balance.cs             The values in effect, and their wire format
  ConfigSync.cs          Server to client sync, and turning away clients without the mod
  Items.cs               Item prefab lookups shared by the changes
  StaffOfTheWild.cs      Per player root limit
  TowerShields.cs        Blocking resistances on shields that cannot parry
  RootArmor.cs           Swaps the harnesk's pierce resistance with the set's archery bonus
  StaffOfProtection.cs   Self shield on block, single target bolt on attack
  StaffOfEmbers.cs       Halves the blunt damage
  StaffOfFracturing.cs   Halves the fire damage
  Trollstav.cs           One troll at a time, and a health cost that can kill
  BurntWood.cs           A fifth of the coal from wood burnt by spreading fire
  Patches/               Harmony hooks
tools/PatchCheck/        Resolves every patch target against the game assembly
BACKLOG.md               Designed changes not built yet
```
