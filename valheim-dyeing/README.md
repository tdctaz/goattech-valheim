# Valheim Dyeing

A dyeing tub you can build, and recipes that take a piece of bronze armor and a handful of something
colourful and give the armor back in that colour. Installed on the dedicated server and on every
client.

This one is experimental. It is the first of these mods to mint prefabs of its own rather than
change prefabs the game already ships, so the ways it can go wrong are new.

The server's configuration is the one that counts. Each client receives it when it connects. By
default the server disconnects anyone who joins without the mod, or with a different version of it,
and here that default matters more than it does in the siblings: dyed armor is a set of prefabs this
mod creates at load, so a client without them cannot resolve the prefab hash and the game deletes
the object rather than drawing it.

It works on its own and alongside [Valheim Rebalanced](../valheim-rebalanced),
[Valheim Creatures](../valheim-creatures) and [Server Authority](../valheim-server-side-mod). It
uses its own RPC names and patches methods none of them touch, apart from all of them listening to
`ZNet` for connections.

## The tub

Built with the hammer, under Crafting, for 5 bronze and 10 fine wood, and it needs a workbench in
range to put up. It is a crafting station in its own right, so walking near it is enough to learn
it, and it does not ask for a roof or for a fire.

It is the cooking cauldron's model, renamed and re-costed. That is also why it carries the
cauldron's icon in the build menu.

## Dyeing

Stand at the tub and the craft tab lists each piece of bronze armor in each colour. A recipe takes
the armor itself plus the colouring, and the piece comes back the same in every respect except its
colour.

| Colour | Costs |
| --- | --- |
| Red | 5 raspberries |
| Blue | 5 blueberries |
| Black | 5 coal |
| Green | 5 guck |
| Yellow | 5 dandelions |
| Purple | 5 thistle |
| Orange | 5 carrots |

Three pieces are dyeable for now: the bronze cuirass, the bronze leggings and the bronze helmet.
Almost everything is bronze age, so the tub and the armor and most of the dyes arrive together.
Green is the exception: guck comes out of the swamp, a tier later, which is the price of it being
the only convincingly green thing in the game.

### The armor keeps what it had

Dyeing is not re-crafting. The piece keeps its quality, its durability, the name of whoever forged
it and the slot it was sitting in, and if it was being worn it stays worn. Vanilla crafting cannot
do this: it consumes a resource by name and hands back a brand new item at quality 1 with full
durability, so a recipe alone would quietly destroy an upgraded piece and reset a worn one. The mod
takes the crafting over for its own recipes and changes the colour of the item already in the
inventory instead.

### Only whole armor goes in

A piece has to be at full durability to be dyed. The reason is the same one that makes the point
above necessary: because vanilla hands back a fresh item, a recipe that ate a nearly broken cuirass
and returned a whole one would be a free repair bench that also happens to paint things. Requiring
full durability closes that off, and repairing first is no hardship.

A worn piece is not counted towards the recipe, so the craft button stays dark and the armor's line
in the requirement list flashes red with a note saying why. Turn `Dyeing.RequireFullDurability` off
to allow it.

### Re-dyeing and upgrading

A dyed piece can go back in the tub for a different colour, at the same cost.

Dyed armor upgrades at the forge exactly like plain armor does. That needs saying because it did not
come for free: an upgrade is a recipe naming the item it produces, and the vanilla ones name the
plain prefab, so a dyed piece would have had no upgrade path at all. The mod copies each vanilla
armor recipe once per colour, marked craft-only-upgrade, which is vanilla's own flag for a recipe
that should appear in the upgrade tab and never in the craft tab. So the forge will upgrade a red
cuirass and will not offer to make one.

## Colour

Valheim's shaders already know how to tint, because that is how a starred creature is tinted, and
the mod uses the same knobs. Worn chest and leg armor is drawn by `Custom/Player`, which has
`_ArmorHue` and `_ArmorColor`; helmets are drawn by `Custom/Creature`, which has `_Hue`,
`_Saturation` and `_Value`; the model an item drops on the ground as is plain `Standard`, which has
only `_Color`. Each colour therefore carries a hue rotation, a saturation and value offset, and a
flat multiply colour, and each material gets whichever of those it understands.

The values are in the config as `hue,saturation,value,#RRGGBB` per colour, and they are applied to
the materials in place, so editing one takes effect without a restart.

They are tuned to be muted rather than vivid, because that is what sits right against Valheim's
palette. A dye should read as a stain taken by the metal, not as paint sprayed over it, so every
colour carries a negative value offset and at most a small saturation lift. The first pass was
brighter and looked wrong on every colour but blue, which was the one already pushed dark.

## Configuration

Written to `BepInEx/config/valheim.dyeing.cfg` on first run. Only the server's copy matters while
connected to a server; a client's own copy applies in single player and when hosting.

The file is watched, so editing it takes effect where it stands without a restart: the mod re-reads
it, pushes the new settings to every connected client, and repaints the dyed materials in place, so
armor already being worn changes colour. BepInEx does not do this on its own. Its `ConfigFile` has a
`Reload` method but nothing calls it and there is no file watcher, so without this a change on disk
would sit there unread until the next restart. That matters most for the colours, which are meant to
be tuned by eye and would otherwise cost a server restart per adjustment.

| Setting | Default | Notes |
| --- | --- | --- |
| `General.RequireClientMod` | `true` | Server only. Disconnect players without the mod or on a different version. Leaving it off means their game deletes every dyed piece it sees. |
| `Dyeing.RequireFullDurability` | `true` | Only dye armor at full durability. Off lets a worn piece be dyed. |
| `Tub.Bronze` | `5` | Bronze to build the tub. |
| `Tub.FineWood` | `10` | Fine wood to build the tub. |
| `Tub.RequiresFire` | `false` | Require a fire near the tub, the way the cauldron it is built from requires one. |
| `Dye.<Colour>.Enabled` | `true` | Offer that colour. The prefabs are minted either way, so server and client always agree on what exists; this only removes the recipes. |
| `Dye.<Colour>.Amount` | `5` | How much colouring one piece costs. |
| `Dye.<Colour>.Colour` | per colour | `hue,saturation,value,#RRGGBB`. |

## Building

```
dotnet build src/ValheimDyeing -c Release
```

Set `VALHEIM_MANAGED` if the game is not in one of the usual Steam folders, and `VALHEIM_PLUGINS`
to a BepInEx plugins folder to have each build land there directly.

`-p:DebugTools=true` defines `DEBUG_TOOLS`. There is nothing behind it yet; the flag is wired up so
that anything added later has somewhere to go that a release build leaves out.

After a Valheim update, check that every patch still has something to attach to:

```
dotnet run --project tools/PatchCheck -c Release -- \
  src/ValheimDyeing/bin/Release/ValheimDyeing.dll \
  ~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed
```

## Layout

```text
src/ValheimDyeing/
  Plugin.cs                    BepInEx entry point
  ModConfig.cs                 every setting and its description
  Settings.cs                  the snapshot the server sends to clients
  ConfigSync.cs                the version handshake and the settings broadcast
  EffectiveSettings.cs         writes the settings actually in force to the log at startup
  Dye.cs                       the palette: material, cost, name and colour per dye
  Dyeing.cs                    mints the prefabs, the tub and the recipes, and holds the rules
  Tint.cs                      turns a dye into shader values on a prefab's materials
  Names.cs                     the localization tokens the mod adds
  Patches/
    ConfigSyncPatches.cs       ZNet hooks for the handshake
    SetupPatches.cs            ObjectDB and ZNetScene hooks that build and attach everything
    CraftingPatches.cs         the dyeing itself, the durability gate and the crafting UI
tools/PatchCheck/              checks every Harmony target still exists after a game update
BACKLOG.md                     wanted, designed, not built
```
