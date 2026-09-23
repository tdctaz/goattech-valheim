using System;

namespace ServerAuthority
{
    /// <summary>
    /// The arithmetic of the blended wind range, with nothing from the game in it, so that it can be
    /// checked on its own and so that no machine can arrive at a different number by a different
    /// route.
    ///
    /// The wind range that feeds the sea is defined as a function of a position and a time of
    /// world clock. Each weather has a range of its own, and the range at a point is blended from
    /// the weathers around it in two ways:
    ///
    /// - Across space, bilinearly between the four points of a 32m grid around the point, each
    ///   holding the weather the world gives that point. Where the four agree, which is almost
    ///   everywhere, nothing changes; where a border between weathers passes between them, the
    ///   step becomes a ramp one cell wide, a few seconds' sailing, which the wind anchors' own ten
    ///   second cross-fade then smooths further. The cell is a little wider than the 12m grid biome
    ///   sectors lie on, so a staircase coast reads as one ramp rather than a string of small
    ///   steps.
    /// - Across time, from the previous weather period's weather to the new one's over the first
    ///   m_transitionDuration seconds of the new period, which is the length and the straight line
    ///   of vanilla's own InterpolateEnvironment. Vanilla starts that blend whenever its camera
    ///   notices the change; this starts it at the period boundary itself, which every machine
    ///   agrees on.
    ///
    /// Every operation is written out here, including the lerp, rather than borrowed from Mathf,
    /// so that the definition is this file and nothing else.
    /// </summary>
    internal static class WindBlend
    {
        internal const float CellSize = 32f;
        internal const float NodeHeight = 30f;

        /// <summary>
        /// The grid points around a point: the one at or below it on each axis, and the fraction of
        /// the way to the next one. Grid point i lies at i times <see cref="CellSize"/>.
        /// </summary>
        internal static void Cell(float x, float z, out int cellX, out int cellZ, out float fx, out float fz)
        {
            double sx = x / (double)CellSize;
            double sz = z / (double)CellSize;
            double floorX = Math.Floor(sx);
            double floorZ = Math.Floor(sz);
            cellX = (int)floorX;
            cellZ = (int)floorZ;
            fx = Clamp01((float)(sx - floorX));
            fz = Clamp01((float)(sz - floorZ));
        }

        /// <summary>
        /// Bilinear interpolation between four node values: v00 at the cell's own corner, v10 one
        /// step along x, v01 one step along z, v11 diagonally opposite.
        /// </summary>
        internal static float Bilinear(float v00, float v10, float v01, float v11, float fx, float fz)
        {
            float near = Lerp(v00, v10, fx);
            float far = Lerp(v01, v11, fx);
            return Lerp(near, far, fz);
        }

        /// <summary>
        /// The weather period a time falls in, computed as vanilla's UpdateEnvironment computes it:
        /// the whole second of world time divided by the period length.
        /// </summary>
        internal static long Period(double time, long duration)
        {
            if (duration <= 0)
            {
                return 0;
            }

            return (long)time / duration;
        }

        /// <summary>
        /// How far a time is through the blend into its own period's weather: 0 at the boundary, 1
        /// once <paramref name="transition"/> seconds have passed, and 1 throughout when there is no
        /// transition. Also returns the period.
        /// </summary>
        internal static float TimeWeight(double time, long duration, float transition, out long period)
        {
            period = Period(time, duration);
            if (duration <= 0 || !(transition > 0f))
            {
                return 1f;
            }

            double into = time - period * (double)duration;
            return Clamp01((float)(into / transition));
        }

        /// <summary>
        /// The time an anchor's range is evaluated for: the moment it starts to blend in, which is
        /// when the anchor before it takes over as the first of the pair.
        /// </summary>
        internal static double AnchorTime(long anchor, float windPeriod)
        {
            return (anchor - 1) * (double)windPeriod;
        }

        internal static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        internal static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
