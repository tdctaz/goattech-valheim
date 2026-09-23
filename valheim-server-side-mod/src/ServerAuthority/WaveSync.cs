using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// Reads the three inputs every machine recomputes the sea from, so that a server log and a
    /// client log can be laid side by side and the disagreement named rather than guessed at.
    ///
    /// Nothing about waves is sent over the network. WaterVolume.GetWaterSurface builds the height
    /// at a point out of the point itself, ZNet.GetWrappedDayTimeSeconds, and the global wind
    /// EnvMan hands WaterVolume.StaticUpdate. Two machines therefore agree about the sea exactly as
    /// far as those three agree, and a hull that looks submerged on one screen and afloat on
    /// another is one of them differing.
    ///
    /// Four ways they are known to differ, which is what these lines are shaped to tell apart:
    ///
    /// - Wind is latched, not computed. EnvMan.UpdateWind derives a target from time seeded noise,
    ///   but SetTargetWind refuses to take a new one while m_windTransitionTimer is running, and
    ///   that timer is a local five second ramp. Targets change several times per ramp, so each
    ///   machine keeps whichever target was current when its own ramp happened to end. Wave height
    ///   scales linearly with wind intensity and the leading wave direction is the wind direction,
    ///   so two machines holding different wind have visibly different seas.
    /// - The server resolves weather for a camera it does not have. EnvMan.UpdateEnvironment
    ///   returns early when Utils.GetMainCamera is null and GetBiome falls back to EmptyMeadows, so
    ///   a headless server's current environment never follows any ship, and the wind range it
    ///   draws intensity from is the wrong one.
    /// - The rendered surface leads the physical one. Buoyancy reads s_wrappedDayTimeSeconds while
    ///   the shader reads s_waterTime, and UpdateWaterTime adds a whole frame delta on every call
    ///   while MonoUpdaters calls it from FixedUpdate, Update and LateUpdate alike, pulling only
    ///   five percent back toward the truth each time. The lead that settles out of that is
    ///   proportional to the frame time, so it differs per machine.
    /// - Only the owner floats the hull at all. Ship.CustomFixedUpdate returns before every line of
    ///   buoyancy unless IsOwner, so on everyone else the hull is a lagged copy of a position that
    ///   knows nothing about the wave under it.
    ///
    /// On a server with ServerWeatherPerPosition on, there is no longer one sea. Each hull the
    /// server simulates floats on its own, from the weather at its position and anchors it holds
    /// itself, so the field line describes only the server's global weather at its viewpoint, and
    /// each hull line names the sea it was measured on and the wind that built it. Compare a
    /// client's field line with the server's line for the hull that client is aboard.
    ///
    /// With BlendedWeatherWind on, both lines also carry the wind ranges the anchors were built
    /// from. The field line gives each anchor's range and its source, and for a range this machine
    /// evaluated itself, every input: the position and time, the discrete weather there, the
    /// period, the time weight, the cell and the four zone weathers it was blended from. The hull
    /// line gives the ranges published on the ship for the anchors in use and the one ahead, which
    /// are what everyone aboard builds from, and on the owner the inputs of the one it just wrote.
    ///
    /// The canonical probe is what makes the comparison possible. Height is sampled at a fixed
    /// world point, for a fixed depth and a whole second of network time, through CalcWave
    /// directly. Everything position and time dependent is then identical by construction, so two
    /// machines reporting different numbers for the same second differ in wind and nothing else.
    /// </summary>
    internal static class WaveSync
    {
        private static readonly Vector3 ProbePoint = new Vector3(0f, 0f, 0f);

        private static readonly Vector3[] Points = new Vector3[HullWater.PointCount];

        private static long _lastBucket = long.MinValue;
        private static bool _loggedConstants;

        internal static void Reset()
        {
            _lastBucket = long.MinValue;
            _loggedConstants = false;
        }

        /// <summary>
        /// True once per interval, however many hulls ask within it.
        ///
        /// The interval is counted in world time rather than in local wall time, and deliberately:
        /// every machine then logs at the same instants, so the lines from a server log and a
        /// client log can be matched on their second= key and compared field by field. Counted
        /// locally the two drift into opposite phases and no two lines ever describe the same
        /// moment, which is exactly the mistake the first version of this made.
        ///
        /// Driven from the ship update rather than from a timer of its own, so the log stays silent
        /// on a server where nobody is sailing. That is what lets it default to on.
        /// </summary>
        internal static bool Due()
        {
            float interval = ModConfig.WaveSyncIntervalSeconds.Value;
            ZNet znet = ZNet.instance;
            if (interval <= 0f || znet == null)
            {
                return false;
            }

            long bucket = (long)(znet.GetTimeSeconds() / interval);
            if (bucket == _lastBucket)
            {
                return false;
            }

            _lastBucket = bucket;
            return true;
        }

        /// <summary>
        /// The wave field this machine believes in, keyed by the whole second of network time that
        /// the canonical probe was evaluated for. Match that key across two logs to compare.
        /// </summary>
        internal static void LogField()
        {
            ZNet znet = ZNet.instance;
            EnvMan env = EnvMan.instance;
            if (znet == null || env == null)
            {
                return;
            }

            if (!_loggedConstants)
            {
                _loggedConstants = true;
                Plugin.Log.LogInfo(
                    $"WaveSync constants: DeterministicWind={ModConfig.DeterministicWind.Value} " +
                    $"BlendedWeatherWind={ModConfig.BlendedWeatherWind.Value} " +
                    $"environmentDuration={env.m_environmentDuration}s transitionDuration={env.m_transitionDuration}s " +
                    $"windTransitionDuration={env.m_windTransitionDuration}s windPeriodDuration={env.m_windPeriodDuration}s " +
                    $"blendCell={WindBlend.CellSize}m. These must match between machines for their seas to match.");
            }

            double netTime = znet.GetTimeSeconds();
            float wrapped = znet.GetWrappedDayTimeSeconds();
            long second = (long)netTime;

            env.GetWindData(out Vector4 wind1, out Vector4 wind2, out float alpha);
            Vector3 wind = env.GetWindDir();

            EnvSetup current = env.GetCurrentEnvironment();
            string envName = current != null ? current.m_name : "(none)";
            string range = current != null
                ? $"{current.m_windMin:0.00}-{current.m_windMax:0.00}"
                : "?";

            string global = "";
            if (Plugin.ServerActive && ServerViewpoint.TryPosition(out Vector3 viewpoint))
            {
                EnvSetup there = LocalWeather.At(viewpoint, out WeatherSource source);
                global = $"viewpoint=({viewpoint.x:0},{viewpoint.z:0}) viewpointWeather=" +
                         $"{(there != null ? there.m_name : "(none)")}({source}) ";
            }

            Plugin.Log.LogInfo(
                $"WaveSync field: second={second} netTime={netTime:0.000} wrapped={wrapped:0.000} " +
                $"shaderTime={WaterVolume.s_waterTime:0.000} shaderLead={WaterVolume.s_waterTime - wrapped:0.000} " +
                $"env={envName} windRange={range} {global}camera={(Utils.GetMainCamera() != null ? "yes" : "NONE")} " +
                $"wind=({wind.x:0.000},{wind.z:0.000}) intensity={env.GetWindIntensity():0.000} " +
                $"wind1=({wind1.x:0.000},{wind1.z:0.000},w={wind1.w:0.000}) " +
                $"wind2=({wind2.x:0.000},{wind2.z:0.000},w={wind2.w:0.000}) alpha={alpha:0.000} " +
                $"transition={env.m_windTransitionTimer:0.00} " +
                $"probe={Probe(second):0.0000} probeNow={Probe(wrapped):0.0000} " +
                $"dt={Time.deltaTime * 1000f:0.0}ms fixedDt={Time.fixedDeltaTime * 1000f:0.0}ms" +
                AnchorRanges());
        }

        /// <summary>
        /// The wind range each of this machine's two anchors was built from and where it came
        /// from, and for one this machine evaluated itself, every input to the evaluation. A
        /// client aboard a ship should report ShipPublished for both, with the same numbers as the
        /// server's range[N] for that hull; ShipLocal means it had to guess.
        /// </summary>
        private static string AnchorRanges()
        {
            WaveField.AnchorRange first = WaveField.CurrentFirstRange;
            WaveField.AnchorRange second = WaveField.CurrentSecondRange;
            if (first == null || second == null)
            {
                return "";
            }

            string line =
                $" anchorRange[{first.Anchor}]={first.Min:0.0000}-{first.Max:0.0000}({first.Source})" +
                $" anchorRange[{second.Anchor}]={second.Min:0.0000}-{second.Max:0.0000}({second.Source})";

            if (second.Source == WaveField.RangeSource.Viewpoint || second.Source == WaveField.RangeSource.ShipOwn ||
                second.Source == WaveField.RangeSource.ShipLocal)
            {
                line += $" rangeInputs[{second.Anchor}]: {second.Trace.Describe()}";
            }

            return line;
        }

        /// <summary>
        /// Wave height at a fixed point and depth for a given wave time, with none of the fading
        /// GetWaterSurface applies, so the only machine-dependent input left is the wind.
        /// </summary>
        private static float Probe(float waterTime)
        {
            if (WaterVolume.Instances.Count == 0)
            {
                return float.NaN;
            }

            return WaterVolume.Instances[0].CalcWave(ProbePoint, 1f, waterTime, 1f, 1f);
        }

        /// <summary>
        /// How many fish this machine is simulating, and how many of them have no WaterVolume.
        ///
        /// Fish.GetWaterLevel has a fallback that costs nothing to hit and is invisible in a log:
        ///
        ///     private float GetWaterLevel(Vector3 point)
        ///     {
        ///         if (!(m_waterVolume != null)) return 30f;
        ///         return m_waterVolume.GetWaterSurface(point);
        ///     }
        ///
        /// A fish with no volume swims to a dead flat surface at the global water level, so it is
        /// in the air in every trough and under the sea on every crest whatever the waves are
        /// doing. The volume is only ever assigned by SetLiquidLevel, which WaterVolume.UpdateFloaters
        /// calls for the objects inside its trigger, so a fish that never entered one keeps the
        /// fallback for its whole life. This mod makes the server own fish, so the server is where
        /// it would happen.
        ///
        /// Counted rather than logged per fish, because a shoal is many and the number is the
        /// question: any non-zero count on the machine that owns them explains fish swimming in air.
        /// </summary>
        internal static void LogFish()
        {
            System.Collections.Generic.List<IMonoUpdater> instances = Fish.Instances;
            int owned = 0;
            int ownedWithoutVolume = 0;
            int total = 0;

            for (int i = 0; i < instances.Count; i++)
            {
                if (!(instances[i] is Fish fish) || fish.m_nview == null || !fish.m_nview.IsValid())
                {
                    continue;
                }

                total++;
                if (!fish.m_nview.IsOwner())
                {
                    continue;
                }

                owned++;
                if (fish.m_waterVolume == null)
                {
                    ownedWithoutVolume++;
                }
            }

            if (total == 0)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"WaveSync fish: {total} instantiated, {owned} simulated here, " +
                $"{ownedWithoutVolume} of those with NO WaterVolume so swimming to a flat {30f:0} " +
                "instead of to the waves.");
        }

        /// <summary>
        /// How one hull sits in the water this machine believes in. The reported depth is the
        /// number Ship.CustomFixedUpdate itself steers on: the centre of mass above the average of
        /// the five sampled water levels, less the hull's own offset. The owner holds it near zero
        /// because it is actively doing so; anyone else reports whatever the lagged position
        /// happens to land on, and the spread between the two is the fault the user sees.
        /// </summary>
        internal static void LogHull(Ship ship)
        {
            ZNetView nview = ship.m_nview;
            if (nview == null || !nview.IsValid() || ship.m_body == null)
            {
                return;
            }

            if (!HullWater.TryPoints(ship, Points))
            {
                return;
            }

            float total = 0f;
            float lowest = float.MaxValue;
            float highest = float.MinValue;
            string sea;

            WeatherScope scope = WeatherScope.Enabled && nview.IsOwner() ? WeatherScope.Hull(ship) : default;
            try
            {
                for (int i = 0; i < HullWater.PointCount; i++)
                {
                    WaterVolume probe = null;
                    float level = Floating.GetWaterLevel(Points[i], ref probe);
                    total += level;
                    lowest = Mathf.Min(lowest, level);
                    highest = Mathf.Max(highest, level);
                }

                sea = HullSea(ship, scope.Active);
            }
            finally
            {
                scope.Exit();
            }

            float average = total / HullWater.PointCount;
            Vector3 com = ship.m_body.worldCenterOfMass;
            float depth = com.y - average - ship.m_waterLevelOffset;

            ZDO zdo = nview.GetZDO();
            long owner = zdo.GetOwner();
            string who = owner == ZDOMan.GetSessionID()
                ? (ZNet.instance != null && ZNet.instance.IsServer() ? "US(server)" : "US")
                : (owner == 0L ? "nobody" : owner.ToString());

            Plugin.Log.LogInfo(
                $"WaveSync hull {zdo.m_uid}: owner={who} isOwner={zdo.IsOwner()} " +
                $"pos=({com.x:0.0},{com.y:0.00},{com.z:0.0}) water={average:0.00} " +
                $"spread={highest - lowest:0.00} depth={depth:0.00} " +
                $"disableLevel={ship.m_disableLevel:0.00} " +
                $"floating={(depth > ship.m_disableLevel ? "NO" : "yes")} " +
                $"crew={ship.m_players.Count} vel={ship.m_body.linearVelocity.y:0.00} {sea}");
        }

        /// <summary>
        /// Which sea the hull line above was measured on, and what its crew is told about Moder.
        ///
        /// On a server simulating the hull that is the hull's own: the weather at its position,
        /// the two anchors it holds, and the Moder heading it published for each. Anywhere else it
        /// is the machine's single global sea, and a client aboard also reports what the server has
        /// published for the two anchors in use, which is what its own anchors should match: a
        /// client line saying "missing" for more than a moment while the server line says "moder"
        /// is the handoff failing.
        /// </summary>
        private static string HullSea(Ship ship, bool own)
        {
            EnvMan env = EnvMan.instance;
            ZNetView nview = ship.m_nview;
            if (env == null || nview == null || !nview.IsValid())
            {
                return "sea=?";
            }

            env.GetWindData(out Vector4 wind1, out Vector4 wind2, out float alpha);
            string winds =
                $"wind1=({wind1.x:0.000},{wind1.z:0.000},w={wind1.w:0.000}) " +
                $"wind2=({wind2.x:0.000},{wind2.z:0.000},w={wind2.w:0.000}) alpha={alpha:0.000}";

            if (!WindMath.Clock(env, ZNet.instance, out long anchor, out _, out _))
            {
                return $"sea={(own ? "hull" : "global")} {winds}";
            }

            ZDO zdo = nview.GetZDO();
            string moder = $"moder[{anchor}]={Moder(zdo, anchor)} moder[{anchor + 1}]={Moder(zdo, anchor + 1)} " +
                           $"range[{anchor}]={Range(zdo, anchor)} range[{anchor + 1}]={Range(zdo, anchor + 1)} " +
                           $"range[{anchor + 2}]={Range(zdo, anchor + 2)}";

            if (own && LocalWind.TryGetHull(zdo.m_uid, out LocalWind.Hull hull))
            {
                string lookahead = hull.LookaheadAnchor == anchor + 2
                    ? $" rangeInputs[{anchor + 2}]: {hull.Lookahead.Describe()}"
                    : "";
                return $"sea=hull weather={(hull.Weather != null ? hull.Weather.m_name : "(none)")}({hull.Source}) " +
                       $"{winds} {moder}{lookahead}";
            }

            return $"sea=global {winds}" + (Ship.GetLocalShip() == ship ? " " + moder : "");
        }

        private static string Range(ZDO zdo, long anchor)
        {
            return HullWindRange.TryRead(zdo, anchor, out float min, out float max)
                ? $"{min:0.0000}-{max:0.0000}"
                : "missing";
        }

        private static string Moder(ZDO zdo, long anchor)
        {
            if (!ModerHeading.TryRead(zdo, anchor, out bool active, out Vector3 heading))
            {
                return "missing";
            }

            return active ? $"({heading.x:0.0000},{heading.z:0.0000})" : "none";
        }
    }
}
