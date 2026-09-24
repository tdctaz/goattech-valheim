using BepInEx.Configuration;

namespace ValheimRebalanced
{
    internal static class ModConfig
    {
        internal static ConfigEntry<bool> RequireClientMod;

        internal static ConfigEntry<int> MaxStaffOfTheWildRoots;

        internal static ConfigEntry<HitData.DamageModifier> TowerShieldPierce;
        internal static ConfigEntry<HitData.DamageModifier> TowerShieldBlunt;
        internal static ConfigEntry<HitData.DamageModifier> TowerShieldSlash;

        internal static ConfigEntry<bool> SwapRootArmorBonuses;

        internal static ConfigEntry<bool> StaffOfProtectionSingleTarget;

        internal static ConfigEntry<float> StaffOfEmbersBlunt;

        internal static ConfigEntry<float> StaffOfFracturingFire;

        internal static ConfigEntry<int> TrollstavMaxSummons;
        internal static ConfigEntry<float> TrollstavHealthCost;
        internal static ConfigEntry<bool> TrollstavLethalHealthCost;

        internal static ConfigEntry<int> BurntWoodCoalDivisor;

        internal static void Bind(ConfigFile config)
        {
            RequireClientMod = config.Bind("General", "RequireClientMod", true,
                "Server only. Disconnect players who join without Valheim Rebalanced, or with a different " +
                "version of it. Without the mod on their machine a player plays by vanilla rules, since " +
                "most of these changes are applied by the client.");

            MaxStaffOfTheWildRoots = config.Bind("StaffOfTheWild", "MaxRootsPerPlayer", 1,
                new ConfigDescription(
                    "How many Staff of the Wild roots each player may have at once. Summoning one more " +
                    "kills that player's oldest root, as if it had expired. Vanilla has no per player " +
                    "limit, only a cap of 10 roots around the caster shared by everyone. 0 is vanilla.",
                    new AcceptableValueRange<int>(0, 10)));

            TowerShieldPierce = config.Bind("TowerShields", "Pierce", HitData.DamageModifier.SlightlyResistant,
                Describe("pierce"));
            TowerShieldBlunt = config.Bind("TowerShields", "Blunt", HitData.DamageModifier.SlightlyResistant,
                Describe("blunt"));
            TowerShieldSlash = config.Bind("TowerShields", "Slash", HitData.DamageModifier.SlightlyResistant,
                Describe("slash"));

            SwapRootArmorBonuses = config.Bind("RootArmor", "SwapBonuses", true,
                "Move the Root harnesk's pierce resistance to the three piece set bonus, and the set " +
                "bonus's +15 Bows skill to the harnesk. Vanilla has it the other way round, which makes " +
                "the harnesk alone the best pierce protection of its tier. Off is vanilla.");

            StaffOfProtectionSingleTarget = config.Bind("StaffOfProtection", "SingleTarget", true,
                "Blocking with the staff shields the caster, if not already shielded, for the attack's eitr " +
                "and health cost. Attacking fires a bolt that stops at the first character it hits and shields " +
                "it if it is an ally. Vanilla drops a " +
                "bubble that shields every friendly within 5m, the caster included. Off is vanilla.");

            StaffOfEmbersBlunt = config.Bind("StaffOfEmbers", "BluntMultiplier", 0.5f,
                new ConfigDescription(
                    "What the Staff of Embers' blunt damage is multiplied by, at every quality level. Its " +
                    "fire damage is left alone. Vanilla splits the staff's damage evenly between blunt and " +
                    "fire, which lets it hit hard through fire resistance, so 120 blunt and 120 fire become " +
                    "60 blunt and 120 fire. 1 is vanilla.",
                    new AcceptableValueRange<float>(0f, 1f)));

            StaffOfFracturingFire = config.Bind("StaffOfFracturing", "FireMultiplier", 0.5f,
                new ConfigDescription(
                    "What the Staff of Fracturing's fire damage is multiplied by, at every quality level. " +
                    "Its blunt damage is left alone. Each of the 12 splinters a cast throws carries the " +
                    "staff's full damage, so 12 blunt and 12 fire at quality 1 becomes 12 blunt and 6 fire, " +
                    "and the +6 fire per level becomes +3. 1 is vanilla.",
                    new AcceptableValueRange<float>(0f, 1f)));
            TrollstavMaxSummons = config.Bind("Trollstav", "MaxSummons", 1,
                new ConfigDescription(
                    "How many summoned trolls may be loaded at once, counted across the whole area the " +
                    "caster has loaded rather than around the caster. A cast past the limit is refused " +
                    "with the game's own message, and its eitr and health are spent anyway, as vanilla " +
                    "takes those when the cast starts. Vanilla is 2.",
                    new AcceptableValueRange<int>(1, 5)));

            TrollstavHealthCost = config.Bind("Trollstav", "HealthCost", 60f,
                new ConfigDescription(
                    "The least health a cast costs. A cast costs this or the 60% of current health vanilla " +
                    "takes, whichever is greater, so the staff keeps a price once the caster is hurt. Blood " +
                    "Magic takes up to a third off either of them, as it does in vanilla. 0 is vanilla.",
                    new AcceptableValueRange<float>(0f, 200f)));

            TrollstavLethalHealthCost = config.Bind("Trollstav", "LethalHealthCost", true,
                "Let the health cost kill the caster. Vanilla always leaves them on 1 health, which makes " +
                "the cost free once they are low enough. A cast that cannot be paid for kills the caster " +
                "as it leaves the staff, and the troll it paid for still lands. Off is vanilla.");

            BurntWoodCoalDivisor = config.Bind("BurntWood", "CoalDivisor", 5,
                new ConfigDescription(
                    "How many wood burnt by spreading fire make one coal, rounded up. Vanilla turns every " +
                    "Wood, Fine wood, Core wood and Blackwood that a burning piece or log would have dropped " +
                    "into one coal, so a piece built from 50 wood leaves 50 coal, and with this at 5 leaves 10. " +
                    "Each resource of a piece and each whole log is rounded on its own. The charcoal kiln " +
                    "is not affected. 1 is vanilla.",
                    new AcceptableValueRange<int>(1, 50)));
        }

        private static string Describe(string type)
        {
            return $"Resistance to {type} damage while blocking with a tower shield, a shield that cannot " +
                   "parry. It becomes part of the shield's item data, so vanilla applies it on a block and " +
                   "the tooltip shows it. Resistant halves the damage, SlightlyResistant takes a quarter off, " +
                   "Normal is vanilla. A shield that already resists the type keeps its own value.";
        }
    }
}
