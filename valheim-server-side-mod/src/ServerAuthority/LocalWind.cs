using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// The wind, and so the sea, at a position or under a hull, for a server that simulates many
    /// of them under different weathers at once.
    ///
    /// <see cref="WaveField"/> makes one machine's wind a pure function of world time: two anchors
    /// on a grid of m_windTransitionDuration seconds, each vanilla's noise for its second scaled
    /// into a wind range, cross-faded. A client needs exactly one, for the sea around its player.
    /// The server needs one per position in play, and this keeps them:
    ///
    /// - Per position, for anything that floats or reads wind at a position. With
    ///   BlendedWeatherWind on, each anchor's range is <see cref="WindRange"/> at the position for
    ///   the anchor's time, so the pair is a pure function of the position and the anchor and needs
    ///   no holding: a floater drifting across a border between weathers moves through the blend
    ///   continuously. Nothing on a server draws vegetation, so the sway fault that makes a client
    ///   hold its anchors does not arise here. The noise depends only on the anchor, so it is
    ///   computed once per anchor and shared. With the blend off, each weather in use gets its
    ///   own constant pair instead, as before. The edge of the world bends the wind by position, so
    ///   a position inside the edge band gets its own anchors either way.
    /// - Per hull, for ships, because a ship is what a crew's client actually renders its sea
    ///   around, and because Moder's power belongs to a hull. Each hull's anchors are evaluated
    ///   once, when they come into use, and then held until they retire, exactly as each client
    ///   aboard holds its own. The range comes from <see cref="HullWindRange"/>, written one anchor
    ///   ahead so the crew read the same number; with the blend off it is the discrete weather at
    ///   the hull when the anchor is taken. Under Moder's power the anchor's direction is the
    ///   hull's heading, which is written to the hull's ZDO for the crew through
    ///   <see cref="ModerHeading"/>.
    ///
    /// Nothing here runs unless DeterministicWind is on and the debug wind is off, because both
    /// halves of the agreement have to be computing the same function.
    /// </summary>
    internal static class LocalWind
    {
        private sealed class Pair
        {
            internal long Anchor = long.MinValue;
            internal Vector4 First;
            internal Vector4 Second;
        }

        internal sealed class Hull
        {
            internal long First = long.MinValue;
            internal Vector4 FirstWind;
            internal Vector4 SecondWind;
            internal bool FirstModer;
            internal bool SecondModer;
            internal long Touched;
            internal EnvSetup Weather;
            internal WeatherSource Source;
            internal bool LoggedModer;
            internal long LookaheadAnchor = long.MinValue;
            internal readonly WindTrace Lookahead = new WindTrace();
        }

        private const int NoiseSlots = 4;

        private static readonly long[] NoiseAnchor = new long[NoiseSlots];
        private static readonly bool[] NoiseHave = new bool[NoiseSlots];
        private static readonly Vector3[] NoiseDir = new Vector3[NoiseSlots];
        private static readonly float[] NoiseValue = new float[NoiseSlots];
        private static float _noisePeriod;
        private static EnvMan _noiseEnv;

        private static readonly Dictionary<EnvSetup, Pair> Pairs = new Dictionary<EnvSetup, Pair>();
        private static readonly Dictionary<ZDOID, Hull> Hulls = new Dictionary<ZDOID, Hull>();
        private static readonly List<ZDOID> Expired = new List<ZDOID>();
        private static long _prunedAt = long.MinValue;

        internal static void Reset()
        {
            for (int i = 0; i < NoiseSlots; i++)
            {
                NoiseHave[i] = false;
            }

            _noiseEnv = null;
            Pairs.Clear();
            Hulls.Clear();
            _prunedAt = long.MinValue;
        }

        internal static bool Available(EnvMan env)
        {
            return env != null && ModConfig.DeterministicWind.Value && !env.m_debugWind;
        }

        /// <summary>
        /// The two anchors and the blend between them for a position, given the discrete weather
        /// already resolved there and why.
        /// </summary>
        internal static bool TryAt(EnvSetup weather, WeatherSource source, Vector3 position,
            out Vector4 first, out Vector4 second, out float alpha)
        {
            first = default;
            second = default;
            alpha = 0f;

            EnvMan env = EnvMan.instance;
            if (weather == null || !Available(env) ||
                !WindMath.Clock(env, ZNet.instance, out long anchor, out alpha, out float period))
            {
                return false;
            }

            Vector3 outward = Vector3.zero;
            float edge = 0f;
            bool overridden = position.y <= 3000f &&
                WindMath.EdgeOfWorld(position, env.m_edgeOfWorldWidth, out outward, out edge);

            if (WindRange.Enabled &&
                WindRange.TryAt(position, WindBlend.AnchorTime(anchor, period), weather, source,
                    out float firstMin, out float firstMax, null) &&
                WindRange.TryAt(position, WindBlend.AnchorTime(anchor + 1, period), weather, source,
                    out float secondMin, out float secondMax, null))
            {
                first = Anchor(env, firstMin, firstMax, anchor, period, overridden, outward, edge);
                second = Anchor(env, secondMin, secondMax, anchor + 1, period, overridden, outward, edge);
                return true;
            }

            if (overridden)
            {
                first = Anchor(env, weather.m_windMin, weather.m_windMax, anchor, period, true, outward, edge);
                second = Anchor(env, weather.m_windMin, weather.m_windMax, anchor + 1, period, true, outward, edge);
                return true;
            }

            if (!Pairs.TryGetValue(weather, out Pair pair))
            {
                pair = new Pair();
                Pairs[weather] = pair;
            }

            if (pair.Anchor != anchor)
            {
                pair.First = pair.Anchor == anchor - 1
                    ? pair.Second
                    : Anchor(env, weather.m_windMin, weather.m_windMax, anchor, period, false, Vector3.zero, 0f);
                pair.Second = Anchor(env, weather.m_windMin, weather.m_windMax, anchor + 1, period, false, Vector3.zero, 0f);
                pair.Anchor = anchor;
            }

            first = pair.First;
            second = pair.Second;
            return true;
        }

        /// <summary>
        /// The two anchors and the blend for one hull, held per hull. See the class notes.
        /// </summary>
        internal static bool TryHull(Ship ship, out Vector4 first, out Vector4 second, out float alpha)
        {
            first = default;
            second = default;
            alpha = 0f;

            EnvMan env = EnvMan.instance;
            ZNetView nview = ship != null ? ship.m_nview : null;
            if (nview == null || !nview.IsValid() || !Available(env) ||
                !WindMath.Clock(env, ZNet.instance, out long anchor, out alpha, out float period))
            {
                return false;
            }

            Prune(anchor);

            ZDO zdo = nview.GetZDO();
            if (!Hulls.TryGetValue(zdo.m_uid, out Hull hull))
            {
                hull = new Hull();
                Hulls[zdo.m_uid] = hull;
            }

            hull.Touched = anchor;

            if (hull.First != anchor)
            {
                bool advanced = hull.First != long.MinValue && anchor == hull.First + 1;
                if (advanced)
                {
                    hull.FirstWind = hull.SecondWind;
                    hull.FirstModer = hull.SecondModer;
                }
                else
                {
                    if (hull.First != long.MinValue)
                    {
                        Plugin.Log.LogInfo(
                            $"Hull wind anchors rebuilt for ship {zdo.m_uid}: world time moved from anchor " +
                            $"{hull.First} to {anchor} ({(anchor - hull.First) * (double)period:0.0}s) between two updates.");
                    }

                    if (!Evaluate(env, ship, zdo, hull, anchor, period, out hull.FirstWind, out hull.FirstModer))
                    {
                        hull.First = long.MinValue;
                        return false;
                    }
                }

                if (!Evaluate(env, ship, zdo, hull, anchor + 1, period, out hull.SecondWind, out hull.SecondModer))
                {
                    hull.First = long.MinValue;
                    return false;
                }

                hull.First = anchor;
                Lookahead(ship, zdo, hull, anchor + 2, period);
            }

            first = hull.FirstWind;
            second = hull.SecondWind;
            return true;
        }

        internal static bool TryGetHull(ZDOID id, out Hull hull)
        {
            return Hulls.TryGetValue(id, out hull);
        }

        /// <summary>
        /// One hull anchor. The edge of the world takes precedence over Moder, as it does in
        /// vanilla's UpdateWind, and is judged by the hull's position where a client judges it by
        /// its player's, which is the same place to within a deck's length.
        /// </summary>
        private static bool Evaluate(EnvMan env, Ship ship, ZDO zdo, Hull hull, long anchor, float period,
            out Vector4 wind, out bool moder)
        {
            wind = default;
            moder = false;

            Vector3 position = ship.transform.position;
            EnvSetup weather = LocalWeather.At(position, out WeatherSource source);
            if (weather == null)
            {
                weather = env.GetCurrentEnvironment();
                source = WeatherSource.None;
            }

            if (weather == null)
            {
                return false;
            }

            hull.Weather = weather;
            hull.Source = source;

            bool overridden = false;
            Vector3 dir = Vector3.zero;
            float edge = 0f;

            if (WindMath.EdgeOfWorld(position, env.m_edgeOfWorldWidth, out Vector3 outward, out float ramp))
            {
                overridden = true;
                dir = outward;
                edge = ramp;
            }
            else if (ship.IsWindControllActive())
            {
                overridden = true;
                moder = true;
                dir = ship.transform.forward;
            }

            if (zdo.IsOwner())
            {
                ModerHeading.Publish(zdo, anchor, moder, dir);
            }

            if (moder != hull.LoggedModer)
            {
                hull.LoggedModer = moder;
                Plugin.Log.LogInfo(moder
                    ? $"Moder's power turns ship {zdo.m_uid}'s wind to its heading ({dir.x:0.000}, {dir.z:0.000}) " +
                      $"from anchor {anchor}; the heading is sent to its crew with each anchor."
                    : $"Moder's power no longer steers ship {zdo.m_uid}'s wind, from anchor {anchor}.");
            }

            float min = weather.m_windMin;
            float max = weather.m_windMax;
            if (WindRange.Enabled &&
                HullWindRange.Take(zdo, anchor, period, position, out float blendedMin, out float blendedMax,
                    out bool evaluated, null))
            {
                min = blendedMin;
                max = blendedMax;
                if (evaluated && hull.First != long.MinValue)
                {
                    Plugin.Log.LogInfo(
                        $"Hull wind range for ship {zdo.m_uid} anchor {anchor} was not published ahead, so it " +
                        $"was taken now at the hull ({min:0.0000}-{max:0.0000}); a client aboard may differ for this anchor.");
                }
            }

            wind = Anchor(env, min, max, anchor, period, overridden, dir, edge);
            return true;
        }

        /// <summary>
        /// Publishes the range for the anchor after next, at the hull's position now. See
        /// <see cref="HullWindRange"/>.
        /// </summary>
        private static void Lookahead(Ship ship, ZDO zdo, Hull hull, long anchor, float period)
        {
            if (!WindRange.Enabled || !zdo.IsOwner())
            {
                return;
            }

            if (HullWindRange.Take(zdo, anchor, period, ship.transform.position, out _, out _,
                    out bool evaluated, hull.Lookahead) && evaluated)
            {
                hull.LookaheadAnchor = anchor;
            }
        }

        private static Vector4 Anchor(EnvMan env, float windMin, float windMax, long anchor, float period,
            bool overridden, Vector3 dir, float edge)
        {
            float noise = Noise(env, anchor, period, out Vector3 noiseDir);
            return WindMath.Compose(noiseDir, noise, windMin, windMax, overridden, dir, edge);
        }

        /// <summary>
        /// The noise for an anchor, which is the same for every weather and every hull.
        /// </summary>
        internal static float Noise(EnvMan env, long anchor, float period, out Vector3 dir)
        {
            if (env != _noiseEnv || period != _noisePeriod)
            {
                _noiseEnv = env;
                _noisePeriod = period;
                for (int i = 0; i < NoiseSlots; i++)
                {
                    NoiseHave[i] = false;
                }
            }

            int slot = (int)(anchor & (NoiseSlots - 1));
            if (NoiseHave[slot] && NoiseAnchor[slot] == anchor)
            {
                dir = NoiseDir[slot];
                return NoiseValue[slot];
            }

            float value = WindMath.Noise(env, WindMath.AnchorSecond(anchor, period), out dir);
            NoiseAnchor[slot] = anchor;
            NoiseDir[slot] = dir;
            NoiseValue[slot] = value;
            NoiseHave[slot] = true;
            return value;
        }

        /// <summary>
        /// Forgets hulls nobody has asked about for two anchors, which is every hull that sank,
        /// unloaded or was handed to a client. Run once per anchor.
        /// </summary>
        private static void Prune(long anchor)
        {
            if (anchor == _prunedAt)
            {
                return;
            }

            _prunedAt = anchor;
            Expired.Clear();
            foreach (KeyValuePair<ZDOID, Hull> pair in Hulls)
            {
                if (pair.Value.Touched < anchor - 2)
                {
                    Expired.Add(pair.Key);
                }
            }

            for (int i = 0; i < Expired.Count; i++)
            {
                Hulls.Remove(Expired[i]);
            }
        }
    }
}
