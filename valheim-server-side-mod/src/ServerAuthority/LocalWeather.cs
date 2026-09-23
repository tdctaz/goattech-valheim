using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// Why a position has the weather it has, for the log.
    /// </summary>
    internal enum WeatherSource
    {
        None,
        Scheduled,
        Forced,
        EnvZone,
        Debug,
        Raid,
        AltBiome,
        PersistentEvent,
        UnknownOverride,
    }

    /// <summary>
    /// Answers "what weather would a player standing here settle on", for any position, on any
    /// machine.
    ///
    /// Vanilla has no such question. Every machine resolves one weather, at its own camera, and
    /// everything it simulates reads that one answer out of EnvMan's globals. That is right for a
    /// client, which only simulates what is around its own player, and wrong for a server that
    /// simulates every zone around every player at once: before this, the server took the weather
    /// of the lowest peer id and applied it to the whole world, so one player's raid put everyone's
    /// fires out and one player's clear sky let the serpent spawners in someone else's storm idle.
    ///
    /// The answer mirrors EnvMan.UpdateEnvironment and EnvMan.GetEnvironmentOverride step for step,
    /// with the camera replaced by the position:
    ///
    /// - The scheduled pick: the biome sector at the position, switched to the Ashlands or Deep
    ///   North sector over their ocean exactly as EnvMan.GetBiome does, GetAvailableEnvironments of
    ///   it, SelectWeightedEnvironment seeded by the whole second of world time divided by
    ///   m_environmentDuration, then the Ashlands and Deep North override entries. Vanilla's own
    ///   methods are called for the pick, so a retuned weight table or a changed selection comes
    ///   along with a game update instead of silently diverging from it. Vanilla tests Deep North
    ///   with the camera's height where its world z belongs, which puts the boundary outside the
    ///   world; that is copied too, because the point is to agree with the client.
    /// - The overrides, in vanilla's order: a forced environment (the debug pin, or an EnvZone
    ///   with m_force around the position) beats everything, then EnvMan.m_debugEnv, then a raid,
    ///   then the alternate biome's forced environment, then a persistent event, then an unforced
    ///   EnvZone. The intro sequence is a single player's cutscene and is left out.
    /// - A raid only counts where a client standing there would take it: the event carries a
    ///   forced environment, the position is inside its area by vanilla's own test (horizontal
    ///   distance under m_eventRange, height at most 3000), and the biome at the position is in
    ///   the event's biome mask, which is what RandomEvent.InEventBiome checks against the client's
    ///   current biome. Where Valheim Creatures' RaidWaves is installed, it takes a raid over on
    ///   the server before RandEventSystem.m_randomEvent is ever set there, so its raids are read
    ///   through <see cref="CreaturesRaids"/> and tested the same way, alongside vanilla's own.
    ///
    /// The pick is taken at the position itself, as vanilla takes it at the camera, and not at
    /// some representative point of the zone: biome sectors lie on a 12m grid, and a coast runs
    /// through most zones along it, so a zone-wide answer gave a hull on open water the land's
    /// weather while its crew had the ocean's. What the pick depends on is the switched sector,
    /// the two sea override flags and the period, so it is cached on exactly that and dropped
    /// wholesale when the period turns over. Finding the sector is an array lookup. The overrides
    /// are a handful of distance checks and are evaluated on every call, so a raid starting,
    /// moving or ending takes effect on the next call with nothing to invalidate.
    ///
    /// An override naming an environment that does not exist (the surtling raid asks for
    /// "Ashrain", which matches nothing) makes vanilla's QueueEnvironment do nothing, so a client
    /// there keeps whatever it had before. There is no "before" for a position, so the scheduled
    /// weather stands in for it, which is what that client most likely had.
    ///
    /// Nothing here is server only. The server asks it for everything it simulates; every machine
    /// asks it, through <see cref="Baseline"/>, for the weathers <see cref="WindRange"/> blends the
    /// sea's wind range from, which is why it has to give every machine the same answer for the
    /// same position and period.
    /// </summary>
    internal static class LocalWeather
    {
        private struct Place
        {
            internal BiomeSector Raw;
            internal BiomeSector Sector;
            internal bool Ashlands;
            internal bool Deepnorth;
        }

        private sealed class AltWeather
        {
            internal string Name;
            internal EnvSetup Env;
        }

        private sealed class ZoneEntry
        {
            internal EnvZone Zone;
            internal Collider Collider;
            internal Bounds Bounds;
            internal bool HaveBounds;
        }

        private const int MaxCachedPicks = 8192;
        private const float EnvZoneRefreshSeconds = 5f;

        private static readonly Dictionary<(BiomeSector, bool, bool), EnvSetup> Picks =
            new Dictionary<(BiomeSector, bool, bool), EnvSetup>();
        private static readonly Dictionary<(BiomeSector, bool, bool, long), EnvSetup> OtherPeriods =
            new Dictionary<(BiomeSector, bool, bool, long), EnvSetup>();
        private static readonly Dictionary<BiomeSector, AltWeather> AltBiomes = new Dictionary<BiomeSector, AltWeather>();
        private static readonly List<ZoneEntry> EnvZones = new List<ZoneEntry>();
        private static readonly Dictionary<string, int> Summary = new Dictionary<string, int>();

        private static EnvMan _env;
        private static long _period = long.MinValue;
        private static Heightmap _heightmap;
        private static float _envZonesRefreshedAt = float.MinValue;

        private static RandomEvent _loggedEvent;
        private static Vector3 _loggedEventPos;

        private static readonly List<Vector3> _creaturesPositions = new List<Vector3>();
        private static readonly List<float> _creaturesRanges = new List<float>();
        private static readonly List<Heightmap.Biome> _creaturesBiomes = new List<Heightmap.Biome>();
        private static readonly List<string> _creaturesEnvironments = new List<string>();
        private static readonly List<string> _creaturesNames = new List<string>();
        private static readonly List<(string Name, Vector3 Position)> _knownCreaturesRaids =
            new List<(string, Vector3)>();

        internal static void Reset()
        {
            Picks.Clear();
            OtherPeriods.Clear();
            AltBiomes.Clear();
            EnvZones.Clear();
            Summary.Clear();
            _env = null;
            _period = long.MinValue;
            _heightmap = null;
            _envZonesRefreshedAt = float.MinValue;
            _loggedEvent = null;
            _knownCreaturesRaids.Clear();
        }

        internal static void Register(EnvZone zone)
        {
            if (zone == null)
            {
                return;
            }

            for (int i = EnvZones.Count - 1; i >= 0; i--)
            {
                if (EnvZones[i].Zone == zone)
                {
                    return;
                }

                if (EnvZones[i].Zone == null)
                {
                    EnvZones.RemoveAt(i);
                }
            }

            EnvZones.Add(new ZoneEntry { Zone = zone, Collider = zone.GetComponent<Collider>() });
        }

        internal static EnvSetup At(Vector3 position)
        {
            return At(position, out _);
        }

        internal static EnvSetup At(Vector3 position, out WeatherSource source)
        {
            source = WeatherSource.None;

            EnvMan env = EnvMan.instance;
            ZNet znet = ZNet.instance;
            if (env == null || znet == null || WorldGenerator.instance == null)
            {
                return null;
            }

            Sync(env, znet);

            if (!string.IsNullOrEmpty(env.m_forceEnv))
            {
                EnvSetup forced = env.GetEnv(env.m_forceEnv);
                if (forced != null)
                {
                    source = WeatherSource.Forced;
                    return forced;
                }
            }

            bool debugEnv = !string.IsNullOrEmpty(env.m_debugEnv);
            string zoneEnvironment = EnvZoneAt(position, out bool zoneForces);

            if (zoneForces && !debugEnv)
            {
                EnvSetup forced = env.GetEnv(zoneEnvironment);
                if (forced != null)
                {
                    source = WeatherSource.EnvZone;
                    return forced;
                }
            }

            Place place = PlaceAt(position);
            EnvSetup scheduled = Pick(env, place, _period);

            string name;
            WeatherSource kind;

            if (debugEnv)
            {
                name = env.m_debugEnv;
                kind = WeatherSource.Debug;
            }
            else if ((name = RaidEnvironment(position, place.Sector.Biome)) != null)
            {
                kind = WeatherSource.Raid;
            }
            else if ((name = Alt(env, place.Raw).Name) != null)
            {
                kind = WeatherSource.AltBiome;
            }
            else if ((name = PersistentEnvironment(position)) != null)
            {
                kind = WeatherSource.PersistentEvent;
            }
            else if (!zoneForces && !string.IsNullOrEmpty(zoneEnvironment))
            {
                name = zoneEnvironment;
                kind = WeatherSource.EnvZone;
            }
            else
            {
                source = scheduled != null ? WeatherSource.Scheduled : WeatherSource.None;
                return scheduled;
            }

            EnvSetup named = env.GetEnv(name);
            if (named != null)
            {
                source = kind;
                return named;
            }

            source = scheduled != null ? WeatherSource.UnknownOverride : WeatherSource.None;
            return scheduled;
        }

        /// <summary>
        /// The biome a client standing at the position would report as its current one, which is
        /// what a raid's biome mask is tested against: the sector after the Ashlands and Deep North
        /// sea switch, as EnvMan.GetBiome leaves it in m_currentBiome.
        /// </summary>
        internal static Heightmap.Biome BiomeAt(Vector3 position)
        {
            if (EnvMan.instance == null || ZNet.instance == null || WorldGenerator.instance == null)
            {
                return Heightmap.Biome.None;
            }

            Sync(EnvMan.instance, ZNet.instance);
            return PlaceAt(position).Sector.Biome;
        }

        /// <summary>
        /// The weather a position has of its own in a given period, before anything transient: the
        /// alternate biome's forced environment where the sector has one that exists, and otherwise
        /// the scheduled pick for that period. This is what a raid, a persistent event or an
        /// EnvZone would be overriding, and it depends only on the world, the position and the
        /// period, so any machine can evaluate it for any period, past or future. The current
        /// period's picks share <see cref="At"/>'s cache; other periods, which the wind blend asks
        /// for around a period boundary, are cached apart and dropped when the period turns over.
        /// </summary>
        internal static EnvSetup Baseline(Vector3 position, long period)
        {
            EnvMan env = EnvMan.instance;
            ZNet znet = ZNet.instance;
            if (env == null || znet == null || WorldGenerator.instance == null)
            {
                return null;
            }

            Sync(env, znet);

            Place place = PlaceAt(position);
            return Alt(env, place.Raw).Env ?? Pick(env, place, period);
        }

        /// <summary>
        /// Vanilla's two raid tests at one position, as a client standing there would apply them:
        /// RandEventSystem.IsInsideRandomEventArea for whether the event is active for it, and
        /// RandomEvent.InEventBiome for whether the event's weather applies.
        /// </summary>
        internal static bool RaidCovers(float height, float distanceXZ, float range, Heightmap.Biome biomeAt, Heightmap.Biome mask)
        {
            return height <= 3000f && distanceXZ < range && (biomeAt & mask) != 0;
        }

        /// <summary>
        /// Vanilla's own raid, if any, plus whatever Creatures' RaidWaves is running on the server
        /// through <see cref="CreaturesRaids"/>: on a dedicated server with RaidWaves on, Creatures
        /// intercepts a raid before RandEventSystem.m_randomEvent is ever set there, so vanilla's
        /// raid alone would miss it. Both kinds are tested the same way with <see cref="RaidCovers"/>.
        /// A client only ever shows one raid, the one nearest to it (RaidDirector.Show), so where
        /// several cover this position the nearest to it wins here too.
        /// </summary>
        private static string RaidEnvironment(Vector3 position, Heightmap.Biome biome)
        {
            string chosenName = null;
            float chosenDistance = float.MaxValue;

            RandEventSystem events = RandEventSystem.instance;
            RandomEvent raid = events != null ? events.m_randomEvent : null;
            if (raid != null && !string.IsNullOrEmpty(raid.m_forceEnvironment))
            {
                LogRaid(raid);

                float distance = Utils.DistanceXZ(position, raid.m_pos);
                if (RaidCovers(position.y, distance, raid.m_eventRange, biome, raid.m_biome))
                {
                    chosenName = raid.m_forceEnvironment;
                    chosenDistance = distance;
                }
            }

            int count = CreaturesRaids.Get(_creaturesPositions, _creaturesRanges, _creaturesBiomes,
                _creaturesEnvironments, _creaturesNames);
            LogCreaturesRaids(count);

            for (int i = 0; i < count; i++)
            {
                float distance = Utils.DistanceXZ(position, _creaturesPositions[i]);
                if (distance >= chosenDistance ||
                    !RaidCovers(position.y, distance, _creaturesRanges[i], biome, _creaturesBiomes[i]))
                {
                    continue;
                }

                chosenName = _creaturesEnvironments[i];
                chosenDistance = distance;
            }

            return chosenName;
        }

        private static string PersistentEnvironment(Vector3 position)
        {
            PersistentEventSystem system = PersistentEventSystem.instance;
            if (system == null || system.m_activePersistentEvents == null)
            {
                return null;
            }

            List<PersistentEventSystem.ActivePersistentEvent> list = system.m_activePersistentEvents.list;
            if (list == null)
            {
                return null;
            }

            for (int i = 0; i < list.Count; i++)
            {
                PersistentEventSystem.ActivePersistentEvent active = list[i];
                if (active.position.SquaredDistanceTo(position) < active.radius * active.radius)
                {
                    return active.Source.GetEnvironmentOverride(position);
                }
            }

            return null;
        }

        /// <summary>
        /// The EnvZone a player at the position would be standing in. Vanilla learns this from the
        /// player's trigger contact, which a server does not have for anybody; the zones are static
        /// boxes, overwhelmingly the 64 by 500 by 64 volume every dungeon interior is wrapped in, so
        /// a point test against the collider answers the same question. Bounds are cached because
        /// this runs for every piece on the rain wear pass, and refreshed every few seconds because
        /// Location scales an interior's box only after it has been instantiated.
        /// </summary>
        private static string EnvZoneAt(Vector3 position, out bool forces)
        {
            forces = false;
            if (EnvZones.Count == 0)
            {
                return null;
            }

            float now = Time.time;
            bool refresh = now - _envZonesRefreshedAt > EnvZoneRefreshSeconds || now < _envZonesRefreshedAt;
            if (refresh)
            {
                _envZonesRefreshedAt = now;
                for (int i = EnvZones.Count - 1; i >= 0; i--)
                {
                    ZoneEntry entry = EnvZones[i];
                    if (entry.Zone == null || entry.Collider == null)
                    {
                        EnvZones.RemoveAt(i);
                        continue;
                    }

                    entry.HaveBounds = entry.Collider.enabled && entry.Zone.gameObject.activeInHierarchy;
                    if (entry.HaveBounds)
                    {
                        entry.Bounds = entry.Collider.bounds;
                    }
                }
            }

            for (int i = 0; i < EnvZones.Count; i++)
            {
                ZoneEntry entry = EnvZones[i];
                if (!entry.HaveBounds || !entry.Bounds.Contains(position) || entry.Zone == null)
                {
                    continue;
                }

                if ((entry.Collider.ClosestPoint(position) - position).sqrMagnitude > 0.0001f)
                {
                    continue;
                }

                forces = entry.Zone.m_force;
                return entry.Zone.m_environment;
            }

            return null;
        }

        private static void Sync(EnvMan env, ZNet znet)
        {
            if (env != _env)
            {
                _env = env;
                Picks.Clear();
                OtherPeriods.Clear();
                AltBiomes.Clear();
                _period = long.MinValue;
            }

            long duration = env.m_environmentDuration;
            if (duration <= 0)
            {
                return;
            }

            long period = (long)znet.GetTimeSeconds() / duration;
            if (period == _period)
            {
                return;
            }

            if (_period != long.MinValue)
            {
                LogSummary(_period);
            }

            _period = period;
            Picks.Clear();
            OtherPeriods.Clear();
        }

        /// <summary>
        /// EnvMan.GetBiome and the flags UpdateEnvironment takes, at a position rather than at the
        /// camera. Vanilla switches to the Ashlands or Deep North sector only when it finds a
        /// heightmap under the point and the ground there is at or below the ocean check level, and
        /// keeps the raw sector when it finds none; both are copied, and the heightmap is cached as
        /// vanilla caches it, because FindHeightmap walks every loaded one. Vanilla tests Deep North
        /// with the height where the world z belongs, which puts the boundary outside the world at
        /// any ordinary height; that is copied too, because the point is to agree with the client.
        /// </summary>
        private static Place PlaceAt(Vector3 position)
        {
            BiomeSector raw = WorldGenerator.instance.GetBiomeSector(position);
            var place = new Place
            {
                Raw = raw,
                Sector = raw,
                Ashlands = WorldGenerator.IsAshlands(position.x, position.z),
                Deepnorth = WorldGenerator.IsDeepnorth(position.x, position.y),
            };

            if (place.Ashlands | place.Deepnorth)
            {
                if (_heightmap == null || !_heightmap.IsPointInside(position))
                {
                    _heightmap = Heightmap.FindHeightmap(position);
                }

                if (_heightmap != null)
                {
                    _heightmap.GetWorldHeight(position, out float height);
                    if (height <= _env.m_oceanLevelEnvCheckAshlandsDeepnorth)
                    {
                        place.Sector = ZNet.World.m_biomeData
                            .Biomes[place.Ashlands ? Heightmap.Biome.AshLands : Heightmap.Biome.DeepNorth]
                            .Sectors[0];
                    }
                }
            }

            return place;
        }

        /// <summary>
        /// The alternate biome's forced environment, from the unswitched sector at the position, as
        /// EnvMan.GetEnvironmentOverride reads it at the player.
        /// </summary>
        private static AltWeather Alt(EnvMan env, BiomeSector raw)
        {
            if (!AltBiomes.TryGetValue(raw, out AltWeather alt))
            {
                alt = new AltWeather();
                foreach (AltBiome biome in raw.AltBiomes)
                {
                    if (!string.IsNullOrEmpty(biome.m_forceEnvironment))
                    {
                        alt.Name = biome.m_forceEnvironment;
                        alt.Env = env.GetEnv(alt.Name);
                        break;
                    }
                }

                AltBiomes[raw] = alt;
            }

            return alt;
        }

        /// <summary>
        /// The pick half of EnvMan.UpdateEnvironment for a place and a period.
        /// </summary>
        private static EnvSetup Pick(EnvMan env, Place place, long period)
        {
            EnvSetup chosen;
            if (period == _period)
            {
                var key = (place.Sector, place.Ashlands, place.Deepnorth);
                if (!Picks.TryGetValue(key, out chosen))
                {
                    if (Picks.Count >= MaxCachedPicks)
                    {
                        Picks.Clear();
                    }

                    chosen = Resolve(env, place, period);
                    Picks[key] = chosen;

                    string name = $"{place.Sector.Biome}/{(chosen != null ? chosen.m_name : "(none)")}";
                    Summary.TryGetValue(name, out int seen);
                    Summary[name] = seen + 1;
                }

                return chosen;
            }

            var other = (place.Sector, place.Ashlands, place.Deepnorth, period);
            if (!OtherPeriods.TryGetValue(other, out chosen))
            {
                if (OtherPeriods.Count >= MaxCachedPicks)
                {
                    OtherPeriods.Clear();
                }

                chosen = Resolve(env, place, period);
                OtherPeriods[other] = chosen;
            }

            return chosen;
        }

        private static EnvSetup Resolve(EnvMan env, Place place, long period)
        {
            EnvSetup chosen = null;

            UnityEngine.Random.State state = UnityEngine.Random.state;
            UnityEngine.Random.InitState((int)period);

            List<EnvEntry> available = env.GetAvailableEnvironments(place.Sector);
            if (available != null && available.Count > 0)
            {
                chosen = env.SelectWeightedEnvironment(available);
                foreach (EnvEntry entry in available)
                {
                    if (entry.m_ashlandsOverride & place.Ashlands)
                    {
                        chosen = entry.m_env;
                    }

                    if (entry.m_deepnorthOverride & place.Deepnorth)
                    {
                        chosen = entry.m_env;
                    }
                }
            }

            UnityEngine.Random.state = state;
            return chosen;
        }

        /// <summary>
        /// One line per weather period saying which weathers were resolved and for how many biome
        /// sectors. It is the only way to see from a log that two players in different
        /// biomes are getting different weather on the server, which is the whole point of this.
        /// Silent when nobody asked for any weather in that period.
        /// </summary>
        private static void LogSummary(long period)
        {
            if (Summary.Count == 0)
            {
                return;
            }

            var line = new StringBuilder();
            int zones = 0;
            foreach (KeyValuePair<string, int> pair in Summary)
            {
                if (line.Length > 0)
                {
                    line.Append(", ");
                }

                line.Append(pair.Key).Append(" x").Append(pair.Value);
                zones += pair.Value;
            }

            Plugin.Log.LogInfo($"Local weather for period {period}: {zones} biome sector(s) resolved: {line}.");
            Summary.Clear();
        }

        /// <summary>
        /// Says once per raid what weather it forces and where, because from here on that weather
        /// is confined to the raid's own area instead of being the server's weather everywhere.
        /// </summary>
        private static void LogRaid(RandomEvent raid)
        {
            if (raid == _loggedEvent && raid.m_pos == _loggedEventPos)
            {
                return;
            }

            _loggedEvent = raid;
            _loggedEventPos = raid.m_pos;
            LogRaidLine(raid.m_name, raid.m_pos, raid.m_forceEnvironment, raid.m_eventRange, raid.m_biome);
        }

        /// <summary>
        /// The same once-per-raid log as <see cref="LogRaid"/>, for the raids Creatures reports.
        /// There can be several at once, and none of them is a stable object to compare against
        /// like vanilla's single m_randomEvent, so instead this remembers the name and position of
        /// every raid it last saw here and logs whichever of the current ones was not among them.
        /// A raid that ends and is not replaced simply stops appearing in <paramref name="count"/>
        /// worth of entries and is forgotten, so if the same raid starts again later it logs again,
        /// which is what a fresh occurrence should do.
        /// </summary>
        private static void LogCreaturesRaids(int count)
        {
            for (int i = 0; i < count; i++)
            {
                string name = _creaturesNames[i];
                Vector3 position = _creaturesPositions[i];

                bool known = false;
                for (int j = 0; j < _knownCreaturesRaids.Count; j++)
                {
                    if (_knownCreaturesRaids[j].Name == name && _knownCreaturesRaids[j].Position == position)
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    LogRaidLine(name, position, _creaturesEnvironments[i], _creaturesRanges[i], _creaturesBiomes[i]);
                }
            }

            _knownCreaturesRaids.Clear();
            for (int i = 0; i < count; i++)
            {
                _knownCreaturesRaids.Add((_creaturesNames[i], _creaturesPositions[i]));
            }
        }

        private static void LogRaidLine(string name, Vector3 position, string forceEnvironment, float range,
            Heightmap.Biome biome)
        {
            bool known = _env != null && _env.GetEnv(forceEnvironment) != null;
            Plugin.Log.LogInfo(
                $"Raid '{name}' at ({position.x:0}, {position.z:0}) forces weather " +
                $"'{forceEnvironment}' within {range:0}m where the biome is in " +
                $"{biome}. Everywhere else keeps its own weather." +
                (known ? "" : " That name matches no environment, so there the scheduled weather stands in for the one a client would keep."));
        }
    }
}
