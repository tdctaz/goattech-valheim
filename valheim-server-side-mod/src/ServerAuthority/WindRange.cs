using System.Text;
using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// What went into one evaluation of <see cref="WindRange"/>, kept for the WaveSync log so that
    /// a server line and a client line that disagree say which input differed.
    /// </summary>
    internal sealed class WindTrace
    {
        internal Vector3 Position;
        internal double Time;
        internal float Min;
        internal float Max;
        internal bool Blended;
        internal EnvSetup Here;
        internal WeatherSource Source;
        internal long Period;
        internal float TimeWeight;
        internal int CellX;
        internal int CellZ;
        internal float Fx;
        internal float Fz;
        internal readonly EnvSetup[] Nodes = new EnvSetup[4];
        internal readonly EnvSetup[] Previous = new EnvSetup[4];

        internal string Describe()
        {
            var line = new StringBuilder();
            line.Append($"range={Min:0.0000}-{Max:0.0000} at=({Position.x:0.0},{Position.z:0.0}) t={Time:0.0} ");
            line.Append($"here={Name(Here)}({Source}) ");
            if (!Blended)
            {
                line.Append("override");
                return line.ToString();
            }

            line.Append($"period={Period} w={TimeWeight:0.000} cell=({CellX},{CellZ}) f=({Fx:0.000},{Fz:0.000}) ");
            line.Append("nodes=").Append(Names(Nodes));
            if (TimeWeight < 1f)
            {
                line.Append(" prev=").Append(Names(Previous));
            }

            return line.ToString();
        }

        private static string Names(EnvSetup[] nodes)
        {
            return $"{Name(nodes[0])}/{Name(nodes[1])}/{Name(nodes[2])}/{Name(nodes[3])}";
        }

        private static string Name(EnvSetup env)
        {
            return env != null ? env.m_name : "(none)";
        }
    }

    /// <summary>
    /// The wind range that feeds the sea, as a function of a position and a time of world clock,
    /// the same on every machine.
    ///
    /// Vanilla draws each wind target's strength from the current environment's m_windMin and
    /// m_windMax, and the current environment is local state: an interpolation between the last
    /// weather and the next, started whenever that machine's camera noticed the change and run over
    /// m_transitionDuration seconds of its own frame time. The server has no such state for a
    /// position at all, only the weather there, so at every weather change and every biome border
    /// a hull on the server and its crew's clients were drawing wave strength from different
    /// numbers for as long as the blend ran.
    ///
    /// Defined instead, in this order:
    ///
    /// - Where something overrides the weather at the position itself, the overriding weather's
    ///   range, unblended: the debug and forced environments, an EnvZone (dungeon interiors), a
    ///   raid, a persistent event. These are the transient cases; their start and end reach each
    ///   machine at a different moment, so no blend could make them agree, and the wind anchors'
    ///   own ten second cross-fade already turns the step into a ramp.
    /// - Everywhere else, the weathers the world itself gives the points of a fixed grid around
    ///   the position (<see cref="LocalWeather.Baseline"/>), blended in space and in time by
    ///   <see cref="WindBlend"/>: bilinearly between the four grid points around the position, and
    ///   from the previous period's weathers to the new ones over the first m_transitionDuration
    ///   seconds of a period. Those depend only on the world, the grid point and the period, so
    ///   two machines evaluating the same position and time agree to the bit.
    ///
    /// Only the wind range is defined this way. Fog, rain, light, sounds and everything else that
    /// a client draws from its environment keep vanilla's own blend at its camera, and the
    /// consumers that ask whether it is wet or freezing keep the discrete weather at their
    /// position.
    /// </summary>
    internal static class WindRange
    {
        /// <summary>
        /// The four node weathers of a recently used cell and period, which saves the lookups for a
        /// floater that asks for both of its anchors, and for its neighbours in the same cell, every
        /// frame. They depend on nothing else, except in the Ashlands and the Deep North, where the
        /// sea switch depends on whether a heightmap under the node has loaded yet; a cell touching
        /// either is looked up afresh every time, so that it follows the heightmap as a client's
        /// does.
        /// </summary>
        private sealed class Memo
        {
            internal bool Valid;
            internal EnvMan Env;
            internal int CellX;
            internal int CellZ;
            internal long Period = long.MinValue;
            internal readonly EnvSetup[] Nodes = new EnvSetup[4];
        }

        private const int MemoSlots = 64;

        private static readonly Memo[] Memos = CreateMemos();
        private static readonly EnvSetup[] Current = new EnvSetup[4];
        private static readonly EnvSetup[] Earlier = new EnvSetup[4];

        private static Memo[] CreateMemos()
        {
            var memos = new Memo[MemoSlots];
            for (int i = 0; i < MemoSlots; i++)
            {
                memos[i] = new Memo();
            }

            return memos;
        }

        internal static bool Enabled => ModConfig.BlendedWeatherWind.Value;

        internal static bool TryAt(Vector3 position, double time, out float min, out float max, WindTrace trace = null)
        {
            EnvSetup here = LocalWeather.At(position, out WeatherSource source);
            return TryAt(position, time, here, source, out min, out max, trace);
        }

        /// <summary>
        /// The range at a position and time, given the discrete weather already resolved there.
        /// False when there is no world to evaluate it against.
        /// </summary>
        internal static bool TryAt(Vector3 position, double time, EnvSetup here, WeatherSource source,
            out float min, out float max, WindTrace trace)
        {
            min = 0f;
            max = 0f;

            EnvMan env = EnvMan.instance;
            if (env == null)
            {
                return false;
            }

            if (trace != null)
            {
                trace.Position = position;
                trace.Time = time;
                trace.Here = here;
                trace.Source = source;
                trace.Blended = false;
            }

            if (here != null && Overrides(source))
            {
                return Unblended(here, out min, out max, trace);
            }

            float weight = WindBlend.TimeWeight(time, env.m_environmentDuration, env.m_transitionDuration, out long period);
            WindBlend.Cell(position.x, position.z, out int cellX, out int cellZ, out float fx, out float fz);

            if (!Nodes(cellX, cellZ, period, Current) ||
                (weight < 1f && !Nodes(cellX, cellZ, period - 1, Earlier)))
            {
                return here != null && Unblended(here, out min, out max, trace);
            }

            min = Blend(Current, fx, fz, true);
            max = Blend(Current, fx, fz, false);

            if (weight < 1f)
            {
                min = WindBlend.Lerp(Blend(Earlier, fx, fz, true), min, weight);
                max = WindBlend.Lerp(Blend(Earlier, fx, fz, false), max, weight);
            }

            if (trace != null)
            {
                trace.Min = min;
                trace.Max = max;
                trace.Blended = true;
                trace.Period = period;
                trace.TimeWeight = weight;
                trace.CellX = cellX;
                trace.CellZ = cellZ;
                trace.Fx = fx;
                trace.Fz = fz;
                for (int i = 0; i < 4; i++)
                {
                    trace.Nodes[i] = Current[i];
                    trace.Previous[i] = weight < 1f ? Earlier[i] : null;
                }
            }

            return true;
        }

        private static bool Overrides(WeatherSource source)
        {
            switch (source)
            {
                case WeatherSource.Forced:
                case WeatherSource.EnvZone:
                case WeatherSource.Debug:
                case WeatherSource.Raid:
                case WeatherSource.PersistentEvent:
                    return true;
                default:
                    return false;
            }
        }

        private static bool Unblended(EnvSetup here, out float min, out float max, WindTrace trace)
        {
            min = here.m_windMin;
            max = here.m_windMax;
            if (trace != null)
            {
                trace.Min = min;
                trace.Max = max;
            }

            return true;
        }

        /// <summary>
        /// The four grid points around a cell for one period, in <see cref="WindBlend.Bilinear"/>'s
        /// order. Each is evaluated at sea level rather than at the height of whatever asked, so
        /// that a deck and a keel, or a server and a client that disagree about a hull's heave,
        /// read the same nodes.
        /// </summary>
        private static bool Nodes(int cellX, int cellZ, long period, EnvSetup[] nodes)
        {
            EnvMan env = EnvMan.instance;
            int slot = (int)(((uint)cellX * 73856093u ^ (uint)cellZ * 19349663u ^ (uint)period) & (MemoSlots - 1));
            Memo memo = Memos[slot];
            if (!memo.Valid || memo.Env != env || memo.CellX != cellX || memo.CellZ != cellZ || memo.Period != period)
            {
                memo.Valid = !Switchable(cellX, cellZ) && !Switchable(cellX + 1, cellZ + 1) &&
                             !Switchable(cellX + 1, cellZ) && !Switchable(cellX, cellZ + 1);
                memo.Env = env;
                memo.CellX = cellX;
                memo.CellZ = cellZ;
                memo.Period = period;
                memo.Nodes[0] = LocalWeather.Baseline(Node(cellX, cellZ), period);
                memo.Nodes[1] = LocalWeather.Baseline(Node(cellX + 1, cellZ), period);
                memo.Nodes[2] = LocalWeather.Baseline(Node(cellX, cellZ + 1), period);
                memo.Nodes[3] = LocalWeather.Baseline(Node(cellX + 1, cellZ + 1), period);
            }

            for (int i = 0; i < 4; i++)
            {
                nodes[i] = memo.Nodes[i];
            }

            return nodes[0] != null && nodes[1] != null && nodes[2] != null && nodes[3] != null;
        }

        private static bool Switchable(int cellX, int cellZ)
        {
            Vector3 node = Node(cellX, cellZ);
            return WorldGenerator.IsAshlands(node.x, node.z) || WorldGenerator.IsDeepnorth(node.x, node.y);
        }

        private static Vector3 Node(int cellX, int cellZ)
        {
            return new Vector3(cellX * WindBlend.CellSize, WindBlend.NodeHeight, cellZ * WindBlend.CellSize);
        }

        private static float Blend(EnvSetup[] nodes, float fx, float fz, bool min)
        {
            return min
                ? WindBlend.Bilinear(nodes[0].m_windMin, nodes[1].m_windMin, nodes[2].m_windMin, nodes[3].m_windMin, fx, fz)
                : WindBlend.Bilinear(nodes[0].m_windMax, nodes[1].m_windMax, nodes[2].m_windMax, nodes[3].m_windMax, fx, fz);
        }
    }
}
