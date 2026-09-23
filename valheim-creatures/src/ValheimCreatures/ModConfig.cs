using BepInEx.Configuration;

namespace ValheimCreatures
{
    internal static class ModConfig
    {
        internal const string DefaultBossFactions =
            "Eikthyr:AnimalsVeg, gd_king:ForestMonsters, Bonemass:Undead, Dragon:MountainMonsters, " +
            "GoblinKing:PlainsMonsters, SeekerQueen:MistlandsMonsters, Fader:Demon, FrozenKing_p3:DeepNorth";

        internal const string DefaultBossCreatures =
            "Boar:Eikthyr, Neck:Eikthyr, Deer:Eikthyr, Deer_White:Eikthyr, Greyling:Eikthyr, StoneGolem:Dragon";

        internal const string DefaultBossWaves =
            "Eikthyr:army_eikthyr, gd_king:army_theelder, Bonemass:army_bonemass, Dragon:army_moder, " +
            "GoblinKing:army_goblin, SeekerQueen:army_seekers, Fader:army_charred";

        internal const string DefaultStarlessBosses = "FrozenKing, FrozenKing_p2, FrozenKing_p3";

        internal static ConfigEntry<bool> RequireClientMod;

        internal static ConfigEntry<int> MaxStars;
        internal static ConfigEntry<int> MaxStarsBeforeTrophy;
        internal static ConfigEntry<int> MaxStarsWithoutBoss;
        internal static ConfigEntry<float> LevelUpChance;
        internal static ConfigEntry<float> BossDefeatedChanceMultiplier;
        internal static ConfigEntry<string> BossFactions;
        internal static ConfigEntry<string> BossCreatures;
        internal static ConfigEntry<string> StarlessBosses;
        internal static ConfigEntry<int> StarsPerBoss;
        internal static ConfigEntry<string> CenterProtectionBoss;

        internal static readonly ConfigEntry<float>[] CreatureWeights = new ConfigEntry<float>[StarColors.Count];
        internal static readonly ConfigEntry<float>[] BossWeights = new ConfigEntry<float>[StarColors.Count];

        internal static ConfigEntry<float> FastMoveSpeed;
        internal static ConfigEntry<float> FastTurnSpeed;
        internal static ConfigEntry<float> AggressiveAttackSpeed;
        internal static ConfigEntry<float> AggressiveAttackInterval;
        internal static ConfigEntry<float> AggressiveCircleDuration;
        internal static ConfigEntry<float> AggressiveCircleInterval;
        internal static ConfigEntry<float> RegeneratingRate;
        internal static ConfigEntry<float> CuriousSenseRange;
        internal static ConfigEntry<float> ArmoredDamageTaken;
        internal static ConfigEntry<float> ArmoredMoveSpeed;

        internal static ConfigEntry<float> BossFastMoveSpeed;
        internal static ConfigEntry<float> BossFastTurnSpeed;
        internal static ConfigEntry<float> BossAggressiveAttackSpeed;
        internal static ConfigEntry<float> BossAggressiveAttackInterval;
        internal static ConfigEntry<float> BossRegeneratingPercent;
        internal static ConfigEntry<float> BossShieldedMagic;
        internal static ConfigEntry<float> BossShieldedArrows;
        internal static ConfigEntry<string> BossWaves;
        internal static ConfigEntry<int> BossWaveCount;
        internal static ConfigEntry<float> BossWaveStep;
        internal static ConfigEntry<float> BossWaveDistance;

        internal static ConfigEntry<float> ScalePerStar;
        internal static ConfigEntry<float> SaturationPerStar;
        internal static ConfigEntry<float> ValuePerStar;
        internal static ConfigEntry<float> TopSaturation;
        internal static ConfigEntry<float> TopValue;

        internal static ConfigEntry<bool> RaidWaves;
        internal static ConfigEntry<float> RaidWaveInterval;
        internal static ConfigEntry<int> RaidMaxWaves;
        internal static ConfigEntry<int> RaidPlayerWavesMax;
        internal static ConfigEntry<int> RaidComfortPerWave;
        internal static ConfigEntry<float> RaidTimeoutMinutes;
        internal static ConfigEntry<float> RaidSpawnDistance;

#if DEBUG_TOOLS
        internal static ConfigEntry<bool> LogFactions;
        internal static ConfigEntry<bool> EnableTestCommands;
        internal static ConfigEntry<bool> LogColors;
        internal static ConfigEntry<bool> LogRolls;
#endif

        internal static void Bind(ConfigFile config)
        {
            RequireClientMod = config.Bind("General", "RequireClientMod", true,
                "Server only. Disconnect players who join without Valheim Creatures, or with a different version " +
                "of it. Whoever owns a creature runs its stars and colors, and in vanilla that is usually the " +
                "nearest player, so a player without the mod would leave the creatures around them unchanged.");

            MaxStars = config.Bind("Stars", "MaxStars", 5,
                new ConfigDescription(
                    "The most stars a creature can have once the boss of its faction has been killed and its head " +
                    "hangs on the sacrificial stones. Vanilla is 2.",
                    new AcceptableValueRange<int>(2, 5)));
            MaxStarsBeforeTrophy = config.Bind("Stars", "MaxStarsBeforeTrophy", 3,
                new ConfigDescription(
                    "The most stars a creature can have until then.",
                    new AcceptableValueRange<int>(2, 5)));
            MaxStarsWithoutBoss = config.Bind("Stars", "MaxStarsWithoutBoss", 2,
                new ConfigDescription(
                    "The most stars for a creature whose faction has no boss in BossFactions, such as serpents " +
                    "and the dvergr. 2 is vanilla.",
                    new AcceptableValueRange<int>(0, 5)));
            LevelUpChance = config.Bind("Stars", "LevelUpChance", 10f,
                new ConfigDescription(
                    "Percent chance of each further star, rolled one star at a time. Vanilla is 10. A spawn that " +
                    "sets its own chance keeps it, and world modifiers and world level still apply.",
                    new AcceptableValueRange<float>(0f, 100f)));
            BossDefeatedChanceMultiplier = config.Bind("Stars", "BossDefeatedChanceMultiplier", 2f,
                new ConfigDescription(
                    "Multiplies the chance of each star once the boss of the creature's faction has been killed.",
                    new AcceptableValueRange<float>(1f, 10f)));
            BossFactions = config.Bind("Stars", "BossFactions", DefaultBossFactions,
                "Which boss rules which faction, as BossPrefab:Faction pairs. A faction that is not listed has " +
                "no boss. The boss's own defeat key tells whether it has been killed, and its trophy on the " +
                "sacrificial stones tells whether its head is placed; a boss without a trophy only needs killing.");
            CenterProtectionBoss = config.Bind("Stars", "CenterProtectionBoss", "Eikthyr",
                "Vanilla keeps some world spawns near the world center at zero stars, so the start stays gentle. " +
                "Once this boss's trophy hangs on the sacrificial stones, that protection is lifted for every " +
                "creature. Empty keeps the vanilla protection for good.");
            BossCreatures = config.Bind("Stars", "BossCreatures", DefaultBossCreatures,
                "Creatures that answer to another boss than their faction's, as CreaturePrefab:BossPrefab pairs. " +
                "The Meadows animals share the ForestMonsters faction with the Black Forest, so they are " +
                "moved to Eikthyr here.");
            StarlessBosses = config.Bind("BossColors", "StarlessBosses", DefaultStarlessBosses,
                "Bosses that never get a star, by prefab name. The Frozen King fights in three phases, each its " +
                "own boss, so a star per phase would change color mid fight and a white one would split.");
            StarsPerBoss = config.Bind("BossColors", "StarsPerBoss", 2,
                new ConfigDescription(
                    "How many stars, each with its own color and effect, every boss gets. Two stars always have two " +
                    "different colors, and are drawn on each side of the boss health bar.",
                    new AcceptableValueRange<int>(1, 2)));

            CreatureWeights[(int)StarColor.None] = config.Bind("CreatureColors", "Normal", 90f,
                "Relative weight of an ordinary starred creature. The defaults give 60% normal and 6.67% for each " +
                "color. Colors only appear on creatures with at least one star, and never on those from spawner " +
                "structures.");
            foreach (StarColor color in StarColors.Creature)
            {
                CreatureWeights[(int)color] = config.Bind("CreatureColors", color.ToString(), 10f,
                    $"Relative weight of {color} ({StarColors.Describe(color, false)}).");
            }

            foreach (StarColor color in StarColors.Boss)
            {
                BossWeights[(int)color] = config.Bind("BossColors", color.ToString(), 1f,
                    $"Relative weight of a {color} ({StarColors.Describe(color, true)}) boss. Every boss gets StarsPerBoss " +
                    "stars of different colors, which leave its health and damage alone and give it these effects instead.");
            }

            FastMoveSpeed = config.Bind("Magenta", "MoveSpeed", 0.4f, "Extra movement speed, 0.4 is +40%.");
            FastTurnSpeed = config.Bind("Magenta", "TurnSpeed", 0.5f,
                "Extra turning speed, 0.5 is +50%, which makes it harder to get behind.");

            AggressiveAttackSpeed = config.Bind("Red", "AttackSpeed", 0.25f,
                "Extra attack animation speed, 0.25 is +25%.");
            AggressiveAttackInterval = config.Bind("Red", "AttackIntervalReduction", 0.5f,
                "Shortens the wait between two attacks, 0.5 is half.");
            AggressiveCircleDuration = config.Bind("Red", "CircleDurationReduction", 0.5f,
                "Shortens the time spent circling the target instead of attacking, 0.5 is half.");
            AggressiveCircleInterval = config.Bind("Red", "CircleIntervalIncrease", 1.5f,
                "Lengthens the time between two bouts of circling, 1.5 is +150%.");

            RegeneratingRate = config.Bind("Green", "RateMultiplier", 1.2f,
                "Scales the healing per second. With H the zero star health times 1 + 0.25 per star, it heals " +
                "H * 10 * log10(max(10, H - 1000)) / (H + 1000) * this per second. Never while burning.");

            CuriousSenseRange = config.Bind("Cyan", "SenseRangeIncrease", 1f,
                "Extra sight and hearing range, 1 is +100%.");

            ArmoredDamageTaken = config.Bind("Blue", "DamageReduction", 0.3f, "Less damage taken, 0.3 is -30%.");
            ArmoredMoveSpeed = config.Bind("Blue", "MoveSpeedReduction", 0.25f,
                "Less movement speed, 0.25 is -25%.");

            BossFastMoveSpeed = config.Bind("BossMagenta", "MoveSpeed", 0.25f, "Extra movement speed.");
            BossFastTurnSpeed = config.Bind("BossMagenta", "TurnSpeed", 0.5f, "Extra turning speed.");
            BossAggressiveAttackSpeed = config.Bind("BossRed", "AttackSpeed", 0.25f,
                "Extra attack animation speed.");
            BossAggressiveAttackInterval = config.Bind("BossRed", "AttackIntervalReduction", 0.25f,
                "Shortens the wait between two attacks.");
            BossRegeneratingPercent = config.Bind("BossGreen", "PercentPerSecond", 0.125f,
                "Percent of maximum health healed per second, halved while burning. 0.125 is about 6 health a " +
                "second for Bonemass and 12.5 for Yagluth.");
            BossShieldedMagic = config.Bind("BossBlue", "MagicReduction", 0.5f,
                "Less damage from staffs and other elemental and blood magic.");
            BossShieldedArrows = config.Bind("BossBlue", "ArrowReduction", 0.25f,
                "Less damage from bows and crossbows.");
            BossWaves = config.Bind("BossCyan", "Raids", DefaultBossWaves,
                "Which raid each boss calls its waves from, as BossPrefab:RaidName pairs. A wave spawns one group " +
                "of every creature in that raid, the way the raid itself spawns them.");
            BossWaveCount = config.Bind("BossCyan", "Waves", 5,
                new ConfigDescription("How many waves a summoning boss calls over the fight.",
                    new AcceptableValueRange<int>(0, 10)));
            BossWaveStep = config.Bind("BossCyan", "HealthStep", 18f,
                new ConfigDescription(
                    "Percent of its maximum health the boss loses between two waves. With 18 and 5 waves they come " +
                    "at 82, 64, 46, 28 and 10 percent. Each wave comes once, however much the boss heals.",
                    new AcceptableValueRange<float>(1f, 100f)));
            BossWaveDistance = config.Bind("BossCyan", "Distance", 30f,
                new ConfigDescription("How far from the boss each group of a wave appears, in meters.",
                    new AcceptableValueRange<float>(5f, 80f)));

            RaidWaves = config.Bind("Raids", "Waves", true,
                "Dedicated server only. Run raids as waves: a raid keeps going until all its waves are dead, never " +
                "runs away on a timer, and several bases can be raided at once, one raid per base. Off is vanilla.");
            RaidWaveInterval = config.Bind("Raids", "WaveInterval", 180f,
                new ConfigDescription(
                    "Seconds between two waves. The clock only runs while a player is inside the raid's area.",
                    new AcceptableValueRange<float>(10f, 1800f)));
            RaidMaxWaves = config.Bind("Raids", "MaxWaves", 8,
                new ConfigDescription(
                    "The most waves a raid can have. A raid has one wave per player online beyond the first, up to " +
                    "PlayerWavesMax, plus one per ComfortPerWave comfort at the base, and at least one.",
                    new AcceptableValueRange<int>(1, 20)));
            RaidPlayerWavesMax = config.Bind("Raids", "PlayerWavesMax", 5,
                new ConfigDescription("The most waves that players online beyond the first can add.",
                    new AcceptableValueRange<int>(0, 20)));
            RaidComfortPerWave = config.Bind("Raids", "ComfortPerWave", 5,
                new ConfigDescription(
                    "Comfort per extra wave. The base's comfort is the best comfort anywhere within 30m of where the " +
                    "raid starts, as if sheltered. 0 turns comfort off.",
                    new AcceptableValueRange<int>(0, 30)));
            RaidTimeoutMinutes = config.Bind("Raids", "TimeoutMinutes", 120f,
                new ConfigDescription(
                    "A raid ends after this long even if creatures are still alive, in case one is stuck somewhere. " +
                    "Its remaining creatures stop hunting and stay as ordinary monsters.",
                    new AcceptableValueRange<float>(10f, 600f)));
            RaidSpawnDistance = config.Bind("Raids", "SpawnDistance", 40f,
                new ConfigDescription("How far from a player inside the raid area each group of a wave appears.",
                    new AcceptableValueRange<float>(10f, 90f)));

            ScalePerStar = config.Bind("Looks", "ScalePerStar", 0.05f,
                new ConfigDescription(
                    "Local. How much bigger each star above two makes a creature, on top of its two star size. " +
                    "Vanilla grows most creatures by 0.1 per star. Read when a world loads.",
                    new AcceptableValueRange<float>(0f, 0.5f)));
            SaturationPerStar = config.Bind("Looks", "SaturationPerStar", 0.1f,
                new ConfigDescription(
                    "Local. Each star above two shifts color saturation by this much from the two star tint. " +
                    "Three to five stars also get their own hue, the one farthest from the creature's zero, one " +
                    "and two star hues.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            ValuePerStar = config.Bind("Looks", "ValuePerStar", -0.05f,
                new ConfigDescription(
                    "Local. Each star above two shifts brightness by this much from the two star tint. Negative " +
                    "is darker.",
                    new AcceptableValueRange<float>(-1f, 1f)));

            TopSaturation = config.Bind("Looks", "FiveStarSaturation", 0.25f,
                new ConfigDescription(
                    "Local. Extra saturation for five stars only, on top of the per star steps, so the top tier " +
                    "stands apart even where its hue lands near the three star one.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            TopValue = config.Bind("Looks", "FiveStarValue", -0.3f,
                new ConfigDescription("Local. Extra brightness for five stars only. Negative is darker.",
                    new AcceptableValueRange<float>(-1f, 1f)));

#if DEBUG_TOOLS
            LogFactions = config.Bind("Debug", "LogFactions", false,
                "Log every creature prefab's faction when a world loads, to help fill in BossFactions.");
            LogColors = config.Bind("Debug", "LogColors", false,
                "Log each creature's stored color when this machine reads it, and what the HUD draws for it.");
            LogRolls = config.Bind("Debug", "LogRolls", false,
                "Log every spawn this machine rolls stars for, and every spawn it skips, with the reason.");
            EnableTestCommands = config.Bind("Debug", "EnableTestCommands", false,
                "Server or host only. Run the commands written to BepInEx/config/valheimcreatures_test.txt next " +
                "to the first connected player, then empty the file. A cheat hook for testing; leave it off " +
                "otherwise. See the README for the commands.");
#endif
        }
    }
}
