using System.Collections.Generic;
using BepInEx.Configuration;

namespace ValheimDyeing
{
    internal static class ModConfig
    {
        internal static ConfigEntry<bool> RequireClientMod;

        internal static ConfigEntry<bool> RequireFullDurability;

        internal static ConfigEntry<int> TubBronze;
        internal static ConfigEntry<int> TubFineWood;
        internal static ConfigEntry<bool> TubRequiresFire;

        internal static readonly Dictionary<string, ConfigEntry<bool>> DyeEnabled =
            new Dictionary<string, ConfigEntry<bool>>();

        internal static readonly Dictionary<string, ConfigEntry<int>> DyeAmount =
            new Dictionary<string, ConfigEntry<int>>();

        internal static readonly Dictionary<string, ConfigEntry<string>> DyeColour =
            new Dictionary<string, ConfigEntry<string>>();

#if DEBUG_TOOLS
        internal static ConfigEntry<bool> EnableTestCommands;
#endif

        internal static void Bind(ConfigFile config)
        {
#if DEBUG_TOOLS
            EnableTestCommands = config.Bind("Debug", "EnableTestCommands", true,
                "Server only. Watch BepInEx/config/valheimdyeing_test.txt and drop the items it asks for " +
                "at a connected player's feet, one command per line, as \"give <prefab> [count]\". It exists " +
                "so a live test can be set up from outside the game rather than typed into the console.");
#endif

            RequireClientMod = config.Bind("General", "RequireClientMod", true,
                "Server only. Disconnect players who join without Valheim Dyeing, or with a different " +
                "version of it. The dyed armor is a set of prefabs this mod mints at load, so a player " +
                "without it has no prefab to resolve and would see nothing where the armor should be.");

            RequireFullDurability = config.Bind("Dyeing", "RequireFullDurability", true,
                "Only let a piece of armor into the tub while it is at full durability. A worn piece is " +
                "not counted towards the recipe, so the craft button stays dark until it has been " +
                "repaired. Vanilla crafting takes whichever copy it finds first and would happily eat a " +
                "nearly broken one and hand back a fresh one, which turns the tub into a free repair " +
                "bench. Off lets any copy be dyed, worn or not.");

            TubBronze = config.Bind("Tub", "Bronze", 5,
                new ConfigDescription(
                    "How much bronze building the dyeing tub costs.",
                    new AcceptableValueRange<int>(0, 100)));

            TubFineWood = config.Bind("Tub", "FineWood", 10,
                new ConfigDescription(
                    "How much fine wood building the dyeing tub costs.",
                    new AcceptableValueRange<int>(0, 100)));

            TubRequiresFire = config.Bind("Tub", "RequiresFire", false,
                "Require a fire burning near the tub before it will dye anything, the way the cauldron " +
                "requires one to cook. The tub is built from the cauldron, so vanilla would ask for fire " +
                "unless this is off.");

            foreach (Dye dye in Dye.All)
            {
                string section = "Dye." + dye.Key;

                DyeEnabled[dye.Key] = config.Bind(section, "Enabled", true,
                    $"Offer {dye.EnglishName.ToLowerInvariant()} at the tub. The dyed armor prefabs are " +
                    "minted whether or not a colour is offered, so that a server and its clients always " +
                    "agree on which prefabs exist; turning a colour off only removes its recipes.");

                DyeAmount[dye.Key] = config.Bind(section, "Amount", dye.Amount,
                    new ConfigDescription(
                        $"How much {dye.Material} one piece of armor costs to dye " +
                        $"{dye.EnglishName.ToLowerInvariant()}.",
                        new AcceptableValueRange<int>(1, 99)));

                DyeColour[dye.Key] = config.Bind(section, "Colour", dye.Describe(),
                    "The colour itself, as hue,saturation,value,#RRGGBB. The first three are offsets fed " +
                    "to the game's own shader knobs, the ones it uses to tint a starred creature: hue " +
                    "rotates the whole texture between 0 and 1, saturation and value shift it between -1 " +
                    "and 1. The hex colour is a plain multiply, used on the older shaders that have no " +
                    "hue knob, such as the model the item drops on the ground as. Both are applied to the " +
                    "materials in place, so a change takes at the next craft without a restart.");
            }
        }
    }
}
