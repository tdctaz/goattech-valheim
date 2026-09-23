# Valheim Rebalanced

Balance changes for Valheim. Installed on the dedicated server and on every client.

The server's configuration is the one that counts. Each client receives it when it connects, and
a client connected to a server without the mod plays by vanilla rules. By default the server
disconnects anyone who joins without the mod, or with a different version of it, since most of
these changes are applied on the player's own machine and would otherwise simply not happen for
them.

It works on its own and alongside [Server Authority](../valheim-server-side-mod), which moves
world simulation to the server. Neither needs the other.

## Changes

### Staff of the Wild: one root per player

Vanilla caps roots at 10 counted around the caster, shared by every player, and has no per player
limit. The staff does write a limit of 2 to 6 onto each root, scaled by Elemental Magic, but only
`Tameable` reads it and the root has none, so it never applies.

Here each player may have `StaffOfTheWild.MaxRootsPerPlayer` roots at once, 1 by default. Summoning
another kills that player's oldest root the same way the game expires one after 23 to 25 seconds.
The caster's own machine enforces it: a root's ZDOID carries the session of the client that created
it, so each client can tell its own roots apart from everyone else's. If the root is currently owned
by someone else, such as a server running Server Authority, the client claims it first.

Skill and upgrades keep their meaning. Elemental Magic multiplies each root's damage by 1 plus 2%
per level, so up to three times at 100, and staff quality raises the damage of the cast's impact.
The vanilla cap of 10 still applies on top.

### Tower shields: resistance while blocking

The shields that cannot parry get damage resistances while blocking:

| Damage | Default | Effect |
| --- | --- | --- |
| Pierce | `SlightlyResistant` | A quarter off |
| Blunt | `SlightlyResistant` | A quarter off |
| Slash | `SlightlyResistant` | A quarter off |

That covers the wood, bone, iron, serpentscale, black metal, flametal and gold tower shields: every
shield whose parry bonus is at most 1x, which is what stops a block from counting as a parry.

This uses a mechanism vanilla already has. `Humanoid.BlockAttack` applies a shield's own damage
modifiers to a blocked hit before block power, and the serpentscale shield ships with pierce
resistance through it. The mod adds the configured modifiers to each tower shield's item data, so
vanilla applies them on every block, against monsters and players alike, and the item tooltip lists
them. A shield that already resists a type keeps its own value, so the serpentscale shield keeps its
vanilla half damage against pierce while the rest of the tower shields take a quarter off.

### Root armor: pierce resistance is the set bonus

In vanilla the Root harnesk alone makes you resistant to pierce, and wearing all three pieces
adds +15 Bows skill. That puts the best pierce protection of its tier on a single chest piece. The
two are swapped:

| | Vanilla | Rebalanced |
| --- | --- | --- |
| Root harnesk | Pierce resistant, fire weak | +15 Bows skill ("Improved archery"), fire weak |
| Set bonus, 3 pieces | +15 Bows skill ("Improved archery") | Pierce resistant ("Root armor") |

The mask's poison resistance and every piece's fire weakness are unchanged. The harnesk's bonus is
the game's own "Improved archery" effect, moved from the set to the chest as an equip effect, so its
name, icon and wording are vanilla. The set bonus is a copy of it carrying the pierce resistance
instead; its name and description are English only.

### Staff of Protection: one bubble, one target

Vanilla's attack drops a 5m bubble in front of the caster that shields every friendly inside it,
the caster included, which makes the staff very strong in a group and easy to use. Here the shield
goes to one character per cast:

| Action | Rebalanced |
| --- | --- |
| Block | Casts the shield on the caster when the block starts, unless they are already shielded. |
| Attack | Fires a bolt, a small copy of the shield's sphere, that shields the first ally it hits. |

Each action plays the animation that fits it. Blocking without a shield plays vanilla's own cast,
the staff struck into the ground, and the shield lands on the caster at the moment the staff hits.
The caster is committed to the cast like any attack and moves into the ordinary staff block once it
ends if the button is still held. Blocking while already shielded is just a block. The bolt borrows
the cast the Staff of Embers uses for its fireball, the staff thrust out towards where the player is
looking, and leaves the staff tip on that animation's own release.

The bolt flies straight and fast for 20m and stops at the first thing it hits other than the caster.
Only an ally is shielded: a player, a tame, or anything else the vanilla bubble would have shielded.
An enemy or a body in the way takes the bolt and nothing happens, so reaching a friend in melee
takes a clear line. Blocking still blocks as before.

Both casts cost the attack's vanilla eitr and health, wear the staff the same way, and apply the
game's own `Staff_shield` status effect with the staff's quality and the caster's Blood Magic, so
duration, absorption and skill gain on break are unchanged. Refusing to recast on an already
shielded caster keeps ordinary blocking from draining eitr and health every time.

The bolt and a sound-only cast effect are built from the game's own prefabs and registered on every
machine when a world loads, which is one more reason the server turns away clients without the mod.

### Staff of Embers: less of a club

Vanilla splits the staff's damage evenly between blunt and fire, 120 of each at quality 1, with
only the fire half growing on upgrade. Blunt is the type almost nothing in the game resists, so
half of an elemental weapon's damage lands whatever it is aimed at, and the staff stays strong
against the fire immune creatures it should be worst against.

The blunt half is halved and the fire left alone:

| Quality 1 | Blunt | Fire |
| --- | --- | --- |
| Vanilla | 120 | 120 |
| Rebalanced | 60 | 120 |

The upgrade bonus of +6 fire per quality level is untouched, so the staff keeps its whole fire
output at every quality and only loses part of the damage that ignored resistance. The fireball
carries no damage of its own, so both the direct hit and the 3m explosion use the staff's numbers;
the cinders it scatters light a separate ground fire that has always had its own damage and is left
alone.

The multiplier is `StaffOfEmbers.BluntMultiplier`. The numbers go into the staff's item data, so
the tooltip shows them and every hit uses them.

### Staff of Fracturing: half the fire

A cast throws one bomb that deals nothing itself and bursts into 12 splinters, and each splinter
carries the staff's whole damage rather than a twelfth of it. So the 12 blunt and 12 fire on the
item are 144 and 144 across a cast that lands fully, on top of the Elemental Magic multiplier, and
the bouncing splinters keep hitting for 20 seconds.

The fire half is halved and the blunt left alone:

| Per splinter | Blunt | Fire |
| --- | --- | --- |
| Vanilla, quality 1 | 12 | 12 |
| Rebalanced, quality 1 | 12 | 6 |
| Vanilla, quality 4 | 12 | 30 |
| Rebalanced, quality 4 | 12 | 15 |

The multiplier is `StaffOfFracturing.FireMultiplier` and it applies to the upgrade bonus as well as
the base, so the same fraction holds at every quality. Nothing else about the cast changes: the
splinter count, their spread, their bounce, their 0.6m area and their lifetime are all vanilla.

### Trollstav: one troll, and a cost that can kill

Vanilla lets a caster keep two summoned trolls at once and charges 60% of current health for a cast.
It never charges the last point: the deduction is `min(health - 1, cost)`, so the cost shrinks with
the health you have left and the staff can never kill you. Between that and a second troll, a low
player can keep summoning for almost nothing.

| | Vanilla | Rebalanced |
| --- | --- | --- |
| Summoned trolls at once | 2 | 1 |
| Health a cast costs | 60% of current health | 60, or 60% of current health, whichever is greater |
| When that is more than you have | Leaves you on 1 health | Kills you as the cast goes out |

The limit is vanilla's own. `SpawnAbility` counts the summoned trolls already loaded with no distance
limit, so one at a time means one anywhere the caster has the world loaded, not one nearby. It also
counts every summoned troll, whoever cast it, so the cap is shared by everyone nearby. A cast past
the limit is refused before the swing starts, with "The bond is spoken for. Another troll will not
heed you", and costs neither eitr nor health. Vanilla only refuses once the
cast goes out, after both are spent.

A cast costs `max(60, 0.6 * health) * (1 - 0.33 * skill / 100)`, so the percentage rules while the
caster is healthy and the floor takes over below 100 health, where vanilla's share has dwindled to
nothing worth paying. That is fatal below 60 health at Blood Magic 0 and below 40 at Blood Magic
100, which is the point: the staff stops being a cheaper summon the worse off you are, and starts
being a decision.

The game has no way to write "the greater of" into an item, so the floor sits in the staff's item
data as a flat cost, which is what the tooltip lists beside the percentage, and the mod writes the
real cost onto each cast as it starts. Vanilla then charges it, out of the same field it always
uses.

Since vanilla's own deduction cannot kill, the mod finishes the job itself. A cast whose cost is at
least the caster's current health is remembered as a fatal one: vanilla takes what it can, leaving
them on 1 health, and the moment the cast leaves the staff the caster takes the rest as unblockable
self damage, the same kind of damage the game uses for its own self harm, so no armor, resistance or
stagger applies to it. They drop where they stand, and the troll they paid for still climbs out of
the ground two and a half seconds later, at its full strength: the spawn runs on the cast's own
object rather than on the caster, and a dead player's body stays in the world until they respawn, so
the Blood Magic scaling it reads is still there.

A cast refused for the summon limit does not kill, since nothing is summoned. The mod checks that
the same way the game does, by counting the summoned trolls already loaded, before it takes the
caster's life. The eitr and the health vanilla charges are still spent on a refused cast. Only the
caster's own machine does any of this, and only for a player.

## Configuration

Written to `BepInEx/config/valheim.rebalanced.cfg` on first run. Only the server's copy matters
while connected to a server; a client's own copy applies in single player and when hosting.

| Setting | Default | Notes |
| --- | --- | --- |
| `General.RequireClientMod` | `true` | Server only. Disconnect players without the mod or with another version. |
| `StaffOfTheWild.MaxRootsPerPlayer` | `1` | 0 is vanilla. |
| `TowerShields.Pierce` | `SlightlyResistant` | `Normal` is vanilla. |
| `TowerShields.Blunt` | `SlightlyResistant` | `Normal` is vanilla. |
| `TowerShields.Slash` | `SlightlyResistant` | `Normal` is vanilla. |
| `RootArmor.SwapBonuses` | `true` | `false` is vanilla. |
| `StaffOfProtection.SingleTarget` | `true` | `false` is vanilla. |
| `StaffOfEmbers.BluntMultiplier` | `0.5` | Blunt damage multiplier, fire untouched. 1 is vanilla. |
| `StaffOfFracturing.FireMultiplier` | `0.5` | Fire damage multiplier, blunt untouched. 1 is vanilla. |
| `Trollstav.MaxSummons` | `1` | Summoned trolls loaded at once. 2 is vanilla. |
| `Trollstav.HealthCost` | `60` | The least health a cast costs, against vanilla's 60% of current health. 0 is vanilla. |
| `Trollstav.LethalHealthCost` | `true` | Let the cost kill the caster. `false` is vanilla. |

A rejected player sees the game's own "incompatible version" message, and the server log says why.

## Building

```bash
dotnet build src/ValheimRebalanced -c Release
```

The build finds the game's assemblies in the usual Steam locations; set `VALHEIM_MANAGED` to the
`Managed` folder otherwise. Set `VALHEIM_PLUGINS` to a BepInEx plugins folder to have each build
copied there.

Rebalanced has no debug options of its own, but accepts `-p:DebugTools=true` like the other GoatTech
mods, and the startup line listing the effective configuration says which kind of build is running.

```bash
# Every [HarmonyPatch] target still exists and is unambiguous in the game assembly.
dotnet run --project tools/PatchCheck -c Release -- \
  src/ValheimRebalanced/bin/Release/ValheimRebalanced.dll \
  ~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed
```

Run `PatchCheck` after every Valheim update.

## Layout

```
src/ValheimRebalanced/
  Plugin.cs            Entry point
  ModConfig.cs         Configuration
  Balance.cs           The values in effect, and their wire format
  ConfigSync.cs        Server to client sync, and turning away clients without the mod
  StaffOfTheWild.cs    Per player root limit
  TowerShields.cs      Blocking resistances on shields that cannot parry
  RootArmor.cs         Swaps the harnesk's pierce resistance with the set's archery bonus
  StaffOfProtection.cs Self shield on block, single target bolt on attack
  StaffOfEmbers.cs     Halves the staff's blunt damage and leaves its fire alone
  StaffOfFracturing.cs Halves the staff's fire damage and leaves its blunt alone
  Trollstav.cs         One troll at a time, and a health cost that can kill the caster
  Patches/             Harmony hooks
tools/PatchCheck/      Resolves every patch target against the game assembly
BACKLOG.md             Designed changes not built yet
```
