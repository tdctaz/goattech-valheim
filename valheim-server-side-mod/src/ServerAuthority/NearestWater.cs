#if DEBUG_TOOLS
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class NearestWater
    {
        private const float MinSailableDepth = 2.5f;
        private const float NearStepMeters = 4f;
        private const float StepMeters = 32f;
        private const float MaxSearchRadius = 6000f;

        private static readonly HashSet<long> Reported = new HashSet<long>();

        internal static void Reset()
        {
            Reported.Clear();
        }

        internal static void Rescan(long uid)
        {
            Reported.Remove(uid);
        }

        internal static void ReportFor(List<Anchor> anchors)
        {
            if (!ModConfig.LogNearestWaterOnJoin.Value)
            {
                return;
            }

            WorldGenerator world = WorldGenerator.instance;
            ZoneSystem zones = ZoneSystem.instance;
            if (world == null || zones == null)
            {
                return;
            }

            for (int i = 0; i < anchors.Count; i++)
            {
                Anchor anchor = anchors[i];

                if (anchor.Position.sqrMagnitude < 1f)
                {
                    continue;
                }

                if (!Reported.Add(anchor.Uid))
                {
                    continue;
                }

                Report(world, zones, anchor);
            }
        }

        private static void Report(WorldGenerator world, ZoneSystem zones, Anchor anchor)
        {
            Vector3 from = anchor.Position;
            float waterLevel = zones.m_waterLevel;

            if (waterLevel - world.GetHeight(from.x, from.z) >= MinSailableDepth)
            {
                Plugin.Log.LogInfo(
                    $"Peer {anchor.Uid} is already on sailable water at " +
                    $"({from.x:0}, {from.z:0}).");
                return;
            }

            for (float radius = NearStepMeters; radius <= MaxSearchRadius; radius += StepFor(radius))
            {
                float step = StepFor(radius);
                int samples = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * radius / step));
                for (int s = 0; s < samples; s++)
                {
                    float angle = (float)s / samples * 2f * Mathf.PI;
                    float x = from.x + (Mathf.Cos(angle) * radius);
                    float z = from.z + (Mathf.Sin(angle) * radius);

                    if (waterLevel - world.GetHeight(x, z) < MinSailableDepth)
                    {
                        continue;
                    }

                    float compass = Mathf.Repeat(Mathf.Atan2(x - from.x, z - from.z) * Mathf.Rad2Deg, 360f);
                    Plugin.Log.LogInfo(
                        $"Nearest sailable water for peer {anchor.Uid} is {radius:0}m away at " +
                        $"({x:0}, {z:0}), bearing {compass:0} degrees ({Compass(compass)}). " +
                        $"Player is at ({from.x:0}, {from.z:0}), biome there is " +
                        $"{world.GetBiome(x, z)}.");
                    return;
                }
            }

            Plugin.Log.LogInfo(
                $"No sailable water found within {MaxSearchRadius:0}m of peer {anchor.Uid} " +
                $"at ({from.x:0}, {from.z:0}).");
        }


        private static float StepFor(float radius)
        {
            return radius < 128f ? NearStepMeters : StepMeters;
        }

        private static string Compass(float degrees)
        {
            string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return points[Mathf.RoundToInt(degrees / 45f) % 8];
        }
    }
}
#endif
