using BepInEx.Configuration;

namespace ServerAuthority
{
    internal static class ModConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> RequireDedicatedServer;

        internal static ConfigEntry<OwnershipMode> Mode;

        internal static ConfigEntry<int> MaxObjectsCreatedPerFrame;
        internal static ConfigEntry<int> ZoneEvictionsPerTick;
        internal static ConfigEntry<int> ServerFrameRate;
        internal static ConfigEntry<int> ZdoSendRate;

        internal static ConfigEntry<bool> KeepShipOwnedByDriver;
        internal static ConfigEntry<bool> KeepVehiclesOwnedByUser;
        internal static ConfigEntry<bool> HoldHullsUntilWaterLoads;
        internal static ConfigEntry<bool> RequireLoadedAreaForWaterborneOwner;

        internal static ConfigEntry<float> StatusIntervalSeconds;
        internal static ConfigEntry<bool> LogShipDamage;
        internal static ConfigEntry<float> WaveSyncIntervalSeconds;
#if DEBUG_TOOLS
        internal static ConfigEntry<bool> LogOwnershipChanges;
        internal static ConfigEntry<bool> LogNearestWaterOnJoin;
        internal static ConfigEntry<bool> EnableSpawnRequests;
        internal static ConfigEntry<bool> LogShipState;
        internal static ConfigEntry<string> ForceEnvironment;
#endif

        internal static ConfigEntry<bool> DeterministicWind;
        internal static ConfigEntry<bool> AlignRenderedWaterWithPhysics;
        internal static ConfigEntry<bool> ServerWeatherFollowsPlayers;
        internal static ConfigEntry<bool> ServerWeatherPerPosition;
        internal static ConfigEntry<bool> BlendedWeatherWind;
        internal static ConfigEntry<bool> LevelShipWaterline;
        internal static ConfigEntry<float> ShipFoamSpread;
        internal static ConfigEntry<float> ShipFoamLift;
        internal static ConfigEntry<float> ShipWakeTrailSeconds;
        internal static ConfigEntry<float> ShipWakeSink;
        internal static ConfigEntry<bool> ServerOwnsWaterborne;

        internal static ConfigEntry<bool> ModValidationEnabled;
        internal static ConfigEntry<string> ServerOnlyMods;
        internal static ConfigEntry<string> AllowedClientMods;
        internal static ConfigEntry<string> ForbiddenClientMods;
        internal static ConfigEntry<bool> RequireSameGameVersion;
        internal static ConfigEntry<bool> CompareFileHashes;

        internal static ConfigEntry<bool> CharactersEnabled;
        internal static ConfigEntry<string> CharacterStoragePath;
        internal static ConfigEntry<Integrity.NewCharacterPolicy> NewCharacters;
        internal static ConfigEntry<string> StartingKit;
        internal static ConfigEntry<Integrity.InvalidUploadAction> OnInvalidUpload;
        internal static ConfigEntry<float> CharacterSaveIntervalSeconds;
        internal static ConfigEntry<float> CharacterSyncTimeoutSeconds;
        internal static ConfigEntry<float> CharacterShutdownSaveSeconds;
        internal static ConfigEntry<bool> RejectCheatedItems;

        internal static ConfigEntry<bool> ControlEnabled;
        internal static ConfigEntry<string> ControlDirectory;
        internal static ConfigEntry<bool> RejectCheatedProfiles;
        internal static ConfigEntry<float> MaxSkillLevel;
        internal static ConfigEntry<float> MaxSkillGainPerHour;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Master switch. When false the mod loads but patches nothing.");

            RequireDedicatedServer = config.Bind("General", "RequireDedicatedServer", true,
                "Only activate on a dedicated server. Leave this on unless you are deliberately " +
                "testing the mod on a player-hosted (listen) server, where it cannot help anyway " +
                "because the host is already simulating everything.");

            Mode = config.Bind("Ownership", "Mode", OwnershipMode.Always,
                "Always: the server owns and simulates everything around every player, which is the " +
                "point of the mod. Vanilla: leave ownership exactly as the game assigns it, so the " +
                "mod still loads the world server side but hands simulation to clients as usual. " +
                "Vanilla is for isolating whether a problem comes from this mod at all.");

            MaxObjectsCreatedPerFrame = config.Bind("Performance", "MaxObjectsCreatedPerFrame", 100,
                new ConfigDescription(
                    "Upper bound on GameObjects the server instantiates per frame. Vanilla uses 10 for " +
                    "a single player and 100 while loading. Raise it if players outrun object creation " +
                    "when exploring, lower it if the server stutters.",
                    new AcceptableValueRange<int>(10, 1000)));

            ZoneEvictionsPerTick = config.Bind("Performance", "ZoneEvictionsPerTick", 0,
                new ConfigDescription(
                    "How many expired zones the server may unload per 0.1s tick. Vanilla unloads at most " +
                    "one, which was written for a single player and falls behind when several players are " +
                    "spread out, leaking zones. 0 means scale automatically with the number of players.",
                    new AcceptableValueRange<int>(0, 64)));

            ServerFrameRate = config.Bind("Performance", "ServerFrameRate", 60,
                new ConfigDescription(
                    "Frame rate the server runs at. Valheim hard-codes 30, which was plenty when the " +
                    "server only relayed data but now sets the floor on how fast it answers anything " +
                    "it simulates: an interaction waits up to a whole frame just to be noticed. " +
                    "0 leaves the game's own value alone.",
                    new AcceptableValueRange<int>(0, 360)));

            ZdoSendRate = config.Bind("Performance", "ZdoSendRate", 20,
                new ConfigDescription(
                    "How many times per second the server flushes object updates to each client. " +
                    "Vanilla is 20. Raising it makes server-spawned objects, such as the items a " +
                    "picked bush drops, appear sooner, at the cost of bandwidth.",
                    new AcceptableValueRange<int>(5, 100)));

            KeepShipOwnedByDriver = config.Bind("Vehicles", "KeepShipOwnedByDriver", false,
                "Hand a crewed ship back to whoever is at the rudder, instead of the server " +
                "simulating it.\nThis is a fallback, not a tuning knob. The server owning a ship " +
                "is the intended state: every passenger then gets the same hull physics regardless " +
                "of whose machine is steering, which is the whole point of the mod, and the " +
                "alternative makes the boat feel however the driver's connection feels for " +
                "everybody aboard.\nThe cost is that a server-owned rudder answers a full network " +
                "round trip late. That was judged acceptable, so turn this on only if server " +
                "simulation of a ship goes badly wrong on your server and you need steering back " +
                "while it is investigated.");

            KeepVehiclesOwnedByUser = config.Bind("Vehicles", "KeepVehiclesOwnedByUser", true,
                "Keep a mount owned by its rider and a cart owned by whoever is pulling it. Both hand " +
                "ownership to the user themselves and then never renew it, so without this the server " +
                "reclaims them within a couple of seconds and they answer input a round trip late.");

            HoldHullsUntilWaterLoads = config.Bind("Vehicles", "HoldHullsUntilWaterLoads", true,
                "Hold a server-owned hull still, in place, while any of the five points it measures " +
                "the water at sits over a zone the server has not built. Without this the missing " +
                "water reads as -10000, the hull concludes it is two kilometres above the sea, " +
                "skips all buoyancy, falls, and beats itself to death on the sea floor at 50 blunt " +
                "a hit.\nThe only reason to turn this off is to reproduce that fault deliberately " +
                "and confirm a run actually provoked it, which is what makes it a test switch " +
                "rather than a preference.");

            RequireLoadedAreaForWaterborneOwner = config.Bind(
                "Vehicles", "RequireLoadedAreaForWaterborneOwner", true,
                "Only matters when ServerOwnsWaterborne is off. Give an unmanned hull to a client " +
                "only once that client actually has it inside its active area. Without this the hull " +
                "goes to the nearest player at any distance, so a client can own a hull it has " +
                "instantiated but whose surrounding zones it has not built, and the water under the " +
                "bow then reads -10000 for as long as the player stands there.\nOff restores that " +
                "behaviour, which exists to reproduce it on purpose.");

#if DEBUG_TOOLS
            LogOwnershipChanges = config.Bind("Debug", "LogOwnershipChanges", false,
                "Log every sector ownership transition. Very noisy, for debugging only.");

            LogShipState = config.Bind("Debug", "LogShipState", false,
                "Log a ship's owner, crew count, speed, height, water level and velocity twice a " +
                "second, and immediately on any ownership change. For investigating why a " +
                "server-owned hull misbehaves.");
#endif

            LogShipDamage = config.Bind("Debug", "LogShipDamage", true,
                "Log every hit a boat takes, with the hit type that caused it, which distinguishes a " +
                "creature from a collision with the world, from capsizing, from the Ashlands ocean, " +
                "from structural wear. On by default and cheap, because boats are hit rarely and a " +
                "boat lost while nobody was watching is otherwise unattributable. This exists because " +
                "one was, and the cause was argued from the code for two rounds instead of measured.");

            DeterministicWind = config.Bind("Waves", "DeterministicWind", true,
                "Derive the wind from world time alone, so that every machine computes the same " +
                "sea.\nVanilla picks a wind target from noise seeded by the whole second, which " +
                "every machine agrees on, and then refuses to take a new target while a local " +
                "transition is running. Targets change faster than that transition completes, so " +
                "each machine keeps whichever one its own timer happened to end on and nothing " +
                "pulls them back together. Measured between this server and a client at the same " +
                "second of world time: opposite wind directions and three and a half times the " +
                "intensity, which is three and a half times the wave height.\nThis must be set " +
                "the same on the server and on every client. A machine with it off computes a " +
                "different sea from the rest, which is the fault it exists to remove.");

            AlignRenderedWaterWithPhysics = config.Bind("Waves", "AlignRenderedWaterWithPhysics", true,
                "Draw the water surface at the same moment buoyancy floats boats on it.\nBuoyancy " +
                "reads the world clock directly while the shader reads a smoothed copy, and the " +
                "code that advances that copy adds a whole frame delta on each of the three calls " +
                "it gets per frame while correcting only five percent of the error. It settles " +
                "about twelve frame times ahead of the truth, so the sea is drawn a tenth of a " +
                "second early at 144fps and half a second early at 30, and boats sit visibly wrong " +
                "in it by an amount that depends on the player's frame rate. The smoothing that " +
                "hides a clock correction is kept; only the drift is removed.");

            ServerWeatherFollowsPlayers = config.Bind("Waves", "ServerWeatherFollowsPlayers", true,
                "Resolve the server's weather at a connected player rather than at the world " +
                "origin. Server side only.\nEnvMan picks the environment, and with it the wind " +
                "range that sets wave height, from the main camera's position. Nothing moves that " +
                "camera on a headless server, so without this the server draws its weather for " +
                "wherever the scene happened to leave it.\nWith ServerWeatherPerPosition on, this " +
                "global weather is only the fallback for the few things that still read it rather " +
                "than their own position's (creatures sliding on ice, daylight-only effects), and " +
                "for the WaveSync log. It is taken at the lowest peer id, because that is stable, " +
                "and a raid's weather only reaches it if that player is inside the raid.");

            ServerWeatherPerPosition = config.Bind("Waves", "ServerWeatherPerPosition", true,
                "Give everything the server simulates the weather at its own position, instead of " +
                "one weather for the whole world. Server side only.\nVanilla resolves a single " +
                "weather per machine, at its camera, and every client only simulates what is around " +
                "its own player, so that is always the right weather for it. The server simulates " +
                "every zone around every player at once, and with one weather a raid on one player " +
                "put out every fire in the world, rain in one biome wore down every roofless piece " +
                "everywhere, and the weather-gated spawners (the Neck in rain, Draugr in mist, the " +
                "Serpent in storms, the Ashlands cinder spawners) followed a single player's sky.\n" +
                "This resolves the weather a player standing at each position would see: the " +
                "biome's scheduled weather for the period, and a raid's weather only inside its " +
                "area and biome. Spawners use it for their zone; rain wear, fires, cinders, " +
                "windmills and wisp torches for their own position; ships, floating objects and " +
                "leviathans get their own wind " +
                "and waves from it, and a ship under Moder's power steers its own wind to its " +
                "heading and tells its crew's clients the exact heading it used.\nOff falls back " +
                "to one weather at ServerWeatherFollowsPlayers' viewpoint for everything.");

            BlendedWeatherWind = config.Bind("Waves", "BlendedWeatherWind", true,
                "Draw the wind strength that sets wave height from a blend of the weather that is " +
                "the same on every machine, instead of from each machine's own weather " +
                "transition.\nVanilla blends from one weather to the next over ten seconds, " +
                "starting whenever that machine's camera notices the change, so at every weather " +
                "change and every biome border the server's hull and the clients aboard it drew " +
                "wave height from different numbers until the blends finished. This defines the " +
                "range instead from the position and the world clock alone: a ramp one zone (64m) " +
                "wide across each border between weathers, and a ten second ramp from the old " +
                "weather to the new starting exactly at each weather period boundary. A ship's " +
                "owner also writes the range it used for each wind anchor onto the ship, and " +
                "everyone aboard uses that, so the crew and the hull agree exactly. Only the wind " +
                "changes; fog, rain and light keep vanilla's blend.\nThis must be set the same on " +
                "the server and on every client, like DeterministicWind, which it needs.");

#if DEBUG_TOOLS
            ForceEnvironment = config.Bind("Debug", "ForceEnvironment", "",
                "Pin the weather to one named environment, such as ThunderStorm, instead of letting " +
                "the world's own schedule pick it. Empty leaves the schedule alone.\nThis applies " +
                "on whichever machine sets it, server or client, because weather is not sent over " +
                "the network and each machine derives its own. To test a storm honestly it has to " +
                "be set to the same value everywhere, and setting it in only one place is a way to " +
                "reproduce a wave desync on purpose rather than a way to make weather.\nNames come " +
                "from EnvMan and are listed by the 'weather' spawn request. Leave empty in normal " +
                "play.");
#endif

            LevelShipWaterline = config.Bind("Waves", "LevelShipWaterline", true,
                "Lay a boat's waterline effects on the sea rather than on the hull.\nThe dark " +
                "patch under a boat and the foam ring at its waterline hang off a child transform " +
                "pinned at a fixed height on the hull with the hull's own rotation, and nothing in " +
                "the game ever moves it. So the patch tilts with the boat instead of lying flat on " +
                "the water, and it keeps one height while the hull's real waterline rises and " +
                "falls with the swell.\nThe foam is worse, because its particles simulate in " +
                "world space with no speed and no gravity: each one is frozen for its whole two " +
                "second life at the height the hull had when it was emitted, so the ring traces " +
                "the hull's heave rather than the sea's and reads as foam following a wave that " +
                "is not there. This puts each live particle back on the surface every frame, from " +
                "the same water level buoyancy uses.\nThe speed wake gets the same treatment, but " +
                "only the flat sheets of it. Three of its systems have no start speed and no " +
                "gravity and are foam lying on the water, one of which emits patches that live ten " +
                "seconds and so hang at the height the hull had ten seconds ago; the other three " +
                "are bow spray and rudder churn, which have speed and gravity and are meant to arc " +
                "through the air, and those are left alone.\nThis is vanilla behaviour and is the same " +
                "on every machine, so it is not a sync fault; it only became visible once the hull " +
                "was being placed correctly. Off restores vanilla's version. Client side only, and " +
                "it changes nothing but looks.");

            ShipFoamSpread = config.Bind("Waves", "ShipFoamSpread", 0.75f,
                new ConfigDescription(
                    "How far, in metres, each foam particle reads the sea away from its own " +
                    "position. Applies to the waterline ring and to the flat sheets of the speed " +
                    "wake alike, though the ring only ever reads a height above its own and the " +
                    "wake takes whatever it finds, because the ring may not sink and the wake may.\nLevelShipWaterline on its own puts every particle exactly on the " +
                    "surface, which is correct and looks wrong: the ring becomes one rigid sheet " +
                    "that conforms perfectly to the water, and reads as a decal laid on top of it " +
                    "rather than as foam. Giving each particle a fixed direction and distance to " +
                    "sample from breaks that up, because the short wave components are only a few " +
                    "metres long, so neighbours a metre apart genuinely sit at different heights " +
                    "and rise and fall slightly out of step.\nWave height scales with wind, so " +
                    "this scales itself: near flat in a calm, churned in a storm. A particle is " +
                    "never placed below the real surface at its own position, because the foam " +
                    "material does not write depth and the water would clip or soft-fade it away. " +
                    "0 restores the perfectly conformal ring.",
                    new AcceptableValueRange<float>(0f, 5f)));

            ShipFoamLift = config.Bind("Waves", "ShipFoamLift", 0.07f,
                new ConfigDescription(
                    "How high, in metres, a foam particle may sit above the surface, fixed for its " +
                    "life and different for each one.\nThis is the part that does not depend on " +
                    "the weather, and it applies to the waterline ring only; the wake uses " +
                    "ShipWakeSink instead and goes downward. ShipFoamSpread goes to nothing as the sea flattens, which is " +
                    "right for the swell but leaves a dead calm looking like a painted ring again, " +
                    "so a little thickness is kept regardless. Always upward, never below the " +
                    "surface. 0 turns it off.",
                    new AcceptableValueRange<float>(0f, 1f)));

            ShipWakeTrailSeconds = config.Bind("Waves", "ShipWakeTrailSeconds", 0f,
                new ConfigDescription(
                    "The longest a boat's wake foam may live, in seconds. 0, the default, leaves " +
                    "vanilla's own lifetimes alone.\nThis is a tuning lever rather than a fix, " +
                    "and it is off because the thing it was written for turned out to have a " +
                    "different cause. The wake read as a speedboat's, and the trail's ten second " +
                    "life looked like the reason: it emits a patch eight metres across five times " +
                    "a second, so fifty overlap at once, and its alpha curve holds full opacity " +
                    "from three percent of a particle's life to sixty-five percent. Capping the " +
                    "life shortened the wake and did nothing at all for how solid it was. What " +
                    "was actually wrong was that every particle had been placed at exactly the " +
                    "same height; see ShipWakeSink.\nIt is kept because it does what it says and " +
                    "a shorter wake is a reasonable thing to want. The alpha curve is normalised " +
                    "over the lifetime, so capping the lifetime compresses the whole fade rather " +
                    "than truncating it, and the overlap count falls with it. Only systems " +
                    "authored longer than this are touched, which leaves the one and two second " +
                    "sheets at the waterline and the bow exactly as they are.",
                    new AcceptableValueRange<float>(0f, 10f)));

            ShipWakeSink = config.Bind("Waves", "ShipWakeSink", 0.35f,
                new ConfigDescription(
                    "How deep, in metres, the deepest particle of a boat's wake sits below the " +
                    "surface. 0 keeps the whole wake on the surface.\nLevelShipWaterline puts " +
                    "every particle on the water, which is right for the ring at the waterline and " +
                    "wrong for the wake. Vanilla emitted these at a fixed height on the hull and " +
                    "let the hull's own heave scatter them through the surface, so some were " +
                    "always part submerged; placing them all exactly on the water took that away " +
                    "and left the wake a single opaque mass. Each particle now gets a depth of its " +
                    "own, fixed for its life, and fades toward a quarter of its opacity at the " +
                    "bottom of that range, so the wake reads as foam churned through the water " +
                    "rather than as paint lying on it.\nThe ring at the waterline is deliberately " +
                    "not part of this. It is sitting on the surface because that is where it " +
                    "belongs, and it looks right there.",
                    new AcceptableValueRange<float>(0f, 2f)));

            WaveSyncIntervalSeconds = config.Bind("Debug", "WaveSyncIntervalSeconds", 5f,
                new ConfigDescription(
                    "How often to log the three inputs every machine rebuilds the sea from: world " +
                    "time, the current environment, and the global wind. Runs on a server and on a " +
                    "client alike, and only while a boat is instantiated, so it is silent unless " +
                    "somebody is sailing.\nWaves are never sent over the network. Each machine " +
                    "recomputes them, so a hull that is afloat on one screen and submerged on " +
                    "another means those inputs disagree, and comparing two of these logs by their " +
                    "'second=' key is the only way to say which one. 0 disables.",
                    new AcceptableValueRange<float>(0f, 600f)));

            ServerOwnsWaterborne = config.Bind("Ownership", "ServerOwnsWaterborne", true,
                "Let the server own boats and other floating objects, so that a drifting boat or a " +
                "floating log is simulated consistently rather than by whichever client is closest.\n" +
                "This covers crewed boats too, because KeepShipOwnedByDriver now defaults to off: " +
                "every passenger gets the same hull physics regardless of whose machine is " +
                "steering, at the cost of the driver's input answering a round trip late. Turn " +
                "KeepShipOwnedByDriver on to hand a steered boat back to its driver as a " +
                "fallback.\n" +
                "Server-owned hulls did once sink and tumble apart. That was this mod assigning the " +
                "hull to the driver and to the server on two competing timers, and it is fixed.");

#if DEBUG_TOOLS
            EnableSpawnRequests = config.Bind("Debug", "EnableSpawnRequests", false,
                "Testing aid. Watches BepInEx/config/serverauthority_spawn.txt and spawns whatever is " +
                "listed next to the first connected player, one 'PrefabName Count' per line, then " +
                "empties the file. This is a cheat hook with no authentication beyond filesystem " +
                "access to the server. Leave it off except while testing.");

            LogNearestWaterOnJoin = config.Bind("Debug", "LogNearestWaterOnJoin", false,
                "When a player joins, log where the nearest sailable water is relative to them. " +
                "Useful when testing boats on a fresh world, where the spawn can be a long way inland.");
#endif

            ModValidationEnabled = config.Bind("ModValidation", "Enabled", false,
                "Require every connecting client to run the Server Authority client mod and the same " +
                "mods as the server, at the same versions. Clients without it are kicked, and players " +
                "running the mod are told exactly what is missing or wrong. Server side only.");

            ServerOnlyMods = config.Bind("ModValidation", "ServerOnlyMods", "",
                "Comma separated plugin GUIDs installed on the server that clients do not need. Every " +
                "other server plugin is required on clients. Server Authority itself is always " +
                "required while validation or server characters are on.");

            AllowedClientMods = config.Bind("ModValidation", "AllowedClientMods", "",
                "Comma separated plugin GUIDs clients may run that the server does not have, such as " +
                "a minimap or UI mod. '*' allows any extra mod not listed in ForbiddenClientMods.");

            ForbiddenClientMods = config.Bind("ModValidation", "ForbiddenClientMods", "",
                "Comma separated plugin GUIDs that are always rejected. Only matters when " +
                "AllowedClientMods is '*'.");

            RequireSameGameVersion = config.Bind("ModValidation", "RequireSameGameVersion", true,
                "Reject clients whose Valheim version differs from the server's. Vanilla only compares " +
                "the network protocol version, which stays the same across many game patches.");

            CompareFileHashes = config.Bind("ModValidation", "CompareFileHashes", false,
                "Also require each shared mod's DLL to be byte identical to the server's, not just the " +
                "same version number. Catches locally rebuilt or edited plugins that keep the version.");

            CharactersEnabled = config.Bind("Characters", "Enabled", false,
                "Keep every character's inventory, skills and progress on the server. On login the " +
                "server's copy replaces whatever the client has, so a character edited offline, or " +
                "brought from another world, is reset to what it was when it last left this server. " +
                "Requires the Server Authority client mod on every player.");

            CharacterStoragePath = config.Bind("Characters", "StoragePath", "",
                "Folder the server keeps characters in, one subfolder per world. Empty means " +
                "characters_serverauthority next to the server's worlds_local folder.");

            NewCharacters = config.Bind("Characters", "NewCharacters", Integrity.NewCharacterPolicy.ResetToFresh,
                "What to do with a character the server has never seen. ResetToFresh: keep its name and " +
                "appearance and reset everything else to a brand new character's starting gear, skills " +
                "and progress. Accept: take it as it is, subject to the item and skill checks below. " +
                "RequireNew: refuse characters that have already entered any world.");

            StartingKit = config.Bind("Characters", "StartingKit", "",
                "Extra items every fresh character starts with, comma separated. Only used with " +
                "NewCharacters = ResetToFresh. An item prefab name gives one of it, Name:Count gives " +
                "that many. A buildable piece's prefab name, such as Karve, gives everything needed to " +
                "build it: its materials, the materials of the crafting station it needs, and the tool " +
                "that builds both. Amounts come from the game's own recipes. Empty gives vanilla starting gear.");

            OnInvalidUpload = config.Bind("Characters", "OnInvalidUpload", Integrity.InvalidUploadAction.Kick,
                "What to do when a connected player's save fails validation. Kick: discard it and kick " +
                "them, so they come back as the server's last good copy. LogOnly: discard it, keep the " +
                "last good copy, and only log.");

            CharacterSaveIntervalSeconds = config.Bind("Characters", "SaveIntervalSeconds", 300f,
                new ConfigDescription(
                    "How often the server asks each player to save and upload their character, on top " +
                    "of every world save. This bounds how much progress a crash or a lost connection " +
                    "can roll back. 0 relies on world saves alone.",
                    new AcceptableValueRange<float>(0f, 3600f)));

            CharacterSyncTimeoutSeconds = config.Bind("Characters", "SyncTimeoutSeconds", 90f,
                new ConfigDescription(
                    "How long a joining player may take to exchange their character with the server " +
                    "before they are disconnected.",
                    new AcceptableValueRange<float>(10f, 600f)));

            CharacterShutdownSaveSeconds = config.Bind("Characters", "ShutdownSaveSeconds", 10f,
                new ConfigDescription(
                    "When the server shuts down gracefully, how long it waits for every connected player " +
                    "to upload a final save before disconnecting them. Without the wait every restart " +
                    "rolls everyone back to their last upload. 0 skips the wait.",
                    new AcceptableValueRange<float>(0f, 60f)));

            RejectCheatedItems = config.Bind("Characters", "RejectCheatedItems", true,
                "Reject characters carrying items the game itself marked as spawned with devcommands.");

            RejectCheatedProfiles = config.Bind("Characters", "RejectCheatedProfiles", false,
                "Reject characters the game flagged as having used devcommands anywhere, ever. Off by " +
                "default because the flag is permanent and set by a single command in single player.");

            MaxSkillLevel = config.Bind("Characters", "MaxSkillLevel", 100f,
                new ConfigDescription(
                    "Highest skill level a character may have. Vanilla caps skills at 100. Raise this if " +
                    "a mod raises the cap.",
                    new AcceptableValueRange<float>(1f, 10000f)));

            MaxSkillGainPerHour = config.Bind("Characters", "MaxSkillGainPerHour", 0f,
                new ConfigDescription(
                    "Most levels any one skill may gain per hour of play on this server. 0 disables the " +
                    "check. Low levels rise quickly in vanilla, so start generous (30 or more) and " +
                    "tighten only after watching the log.",
                    new AcceptableValueRange<float>(0f, 1000f)));

            ControlEnabled = config.Bind("Control", "Enabled", true,
                "Accept commands from the server tool through files dropped in the control directory: " +
                "broadcast a message to every player, or save and shut down.");

            ControlDirectory = config.Bind("Control", "Directory", "",
                "Folder the server tool drops command files into. Empty means ServerAuthority-control " +
                "next to the server executable.");

            StatusIntervalSeconds = config.Bind("Debug", "StatusIntervalSeconds", 0f,
                new ConfigDescription(
                    "Periodically log how many sectors and objects the server is simulating. 0 disables.",
                    new AcceptableValueRange<float>(0f, 600f)));
        }
    }
}
