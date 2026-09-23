using System;
using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// The arithmetic of one wind anchor, shared by the client's single sea and the server's
    /// per-position and per-hull winds so the two cannot drift apart by a rounding difference.
    ///
    /// Everything here is a pure function of its arguments and vanilla's noise, which is what lets
    /// two machines arrive at the same wave field without sending it.
    /// </summary>
    internal static class WindMath
    {
        internal const float WorldEdge = 10500f;

        /// <summary>
        /// The anchor grid: which anchor world time is in, and how far through it.
        /// </summary>
        internal static bool Clock(EnvMan env, ZNet znet, out long anchor, out float alpha, out float period)
        {
            anchor = 0;
            alpha = 0f;
            period = env != null ? env.m_windTransitionDuration : 0f;
            if (znet == null || period <= 0f)
            {
                return false;
            }

            double scaled = znet.GetTimeSeconds() / period;
            anchor = (long)Math.Floor(scaled);
            alpha = (float)(scaled - anchor);
            return true;
        }

        /// <summary>
        /// The whole second an anchor's noise is seeded from.
        /// </summary>
        internal static long AnchorSecond(long anchor, float period)
        {
            return (long)(anchor * (double)period);
        }

        /// <summary>
        /// Vanilla's own target noise, at an arbitrary second rather than at this one: a direction
        /// and a position within the weather's wind range, before the range is applied. The random
        /// state is saved and restored exactly as vanilla does, because AddWindOctave seeds the
        /// shared generator and anything else drawing from it this frame would otherwise see a
        /// different stream.
        /// </summary>
        internal static float Noise(EnvMan env, long timeSec, out Vector3 dir)
        {
            UnityEngine.Random.State state = UnityEngine.Random.state;
            float angle = 0f;
            float intensity = 0.5f;
            env.AddWindOctave(timeSec, 1, ref angle, ref intensity);
            env.AddWindOctave(timeSec, 2, ref angle, ref intensity);
            env.AddWindOctave(timeSec, 4, ref angle, ref intensity);
            env.AddWindOctave(timeSec, 8, ref angle, ref intensity);
            UnityEngine.Random.state = state;

            dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            return intensity;
        }

        /// <summary>
        /// One anchor, in the order vanilla's UpdateWind builds a target: the noise scaled into the
        /// weather's range, then a local override turning the direction, then the edge of the world
        /// pulling the strength toward full, then the clamp SetTargetWind applies.
        /// </summary>
        internal static Vector4 Compose(Vector3 noiseDir, float noise, float windMin, float windMax,
            bool overridden, Vector3 overrideDir, float edge)
        {
            Vector3 dir = noiseDir;
            float intensity = Mathf.Lerp(windMin, windMax, noise);

            if (overridden)
            {
                dir = overrideDir;
                if (edge > 0f)
                {
                    intensity = Mathf.Lerp(intensity, 1f, edge);
                }
            }

            intensity = Mathf.Clamp(intensity, 0.05f, 1f);
            return new Vector4(dir.x, dir.y, dir.z, intensity);
        }

        /// <summary>
        /// Vanilla's edge of the world override at a position: past the edge band the wind turns
        /// outward and ramps toward full strength. <paramref name="edge"/> is vanilla's own eased
        /// ramp. The direction is the position normalised in three dimensions, exactly as vanilla
        /// normalises the player's position.
        /// </summary>
        internal static bool EdgeOfWorld(Vector3 position, float edgeWidth, out Vector3 dir, out float edge)
        {
            dir = Vector3.forward;
            edge = 0f;

            float distance = Utils.LengthXZ(position);
            if (distance <= WorldEdge - edgeWidth)
            {
                return false;
            }

            float step = Utils.LerpStep(WorldEdge - edgeWidth, WorldEdge, distance);
            dir = position.normalized;
            edge = 1f - Mathf.Pow(1f - step, 2f);
            return true;
        }
    }
}
