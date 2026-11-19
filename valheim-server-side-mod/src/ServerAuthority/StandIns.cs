#if DEBUG_TOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority
{
    internal static class StandIns
    {
        private const float PollSeconds = 2f;
        private const long FirstUid = long.MaxValue;

        internal struct StandIn
        {
            internal string Name;
            internal Vector3 Position;
        }

        private static readonly List<StandIn> Points = new List<StandIn>();
        private static string _path;
        private static DateTime _lastSeen = DateTime.MinValue;
        private static float _nextPoll;

        internal static List<StandIn> Current
        {
            get { Poll(); return Points; }
        }

        internal static void Reset()
        {
            Points.Clear();
            _lastSeen = DateTime.MinValue;
            _nextPoll = 0f;
        }

        internal static bool AnyWithin(Vector3 position, float range)
        {
            List<StandIn> points = Current;
            float limit = range * range;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 offset = points[i].Position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= limit)
                {
                    return true;
                }
            }

            return false;
        }

        internal static long Uid(int index)
        {
            return FirstUid - index;
        }

        internal static string Label(long uid)
        {
            long index = FirstUid - uid;
            return index >= 0 && index < Points.Count ? "stand-in " + Points[(int)index].Name : null;
        }

        private static void Poll()
        {
            if (!ModConfig.EnableSpawnRequests.Value)
            {
                Points.Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll)
            {
                return;
            }

            _nextPoll = now + PollSeconds;
            if (_path == null)
            {
                _path = Path.Combine(Paths.ConfigPath, "serverauthority_standins.txt");
            }

            if (!File.Exists(_path))
            {
                if (Points.Count > 0)
                {
                    Points.Clear();
                    Plugin.Log.LogInfo("Stand-ins: the file is gone, so no point is simulated without a player any more.");
                }

                _lastSeen = DateTime.MinValue;
                return;
            }

            DateTime written = File.GetLastWriteTimeUtc(_path);
            if (written == _lastSeen)
            {
                return;
            }

            _lastSeen = written;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_path);
            }
            catch (IOException)
            {
                _lastSeen = DateTime.MinValue;
                return;
            }

            Points.Clear();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 ||
                    !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    Plugin.Log.LogWarning($"Stand-ins: skipped '{line}', expected 'x z [name]'.");
                    continue;
                }

                float y = WorldGenerator.instance != null ? WorldGenerator.instance.GetHeight(x, z) : 0f;
                string name = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : $"({x:F0}, {z:F0})";
                Points.Add(new StandIn { Name = name, Position = new Vector3(x, y, z) });
            }

            Plugin.Log.LogInfo(Points.Count == 0
                ? "Stand-ins: none listed."
                : $"Stand-ins: simulating {Points.Count} point(s) as if a player stood there: " +
                  string.Join(", ", Points.Select(p => $"{p.Name} at ({p.Position.x:F0}, {p.Position.z:F0})")) + ".");
        }
    }

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateSleep))]
    internal static class MonsterAI_UpdateSleep_StandIns_Patch
    {
        private static bool _failed;

        private static bool Prefix(MonsterAI __instance)
        {
            if (_failed || !Plugin.ServerActive)
            {
                return true;
            }

            try
            {
                float range = Mathf.Max(__instance.m_wakeupRange, __instance.m_fallAsleepDistance);
                if (range <= 0f || !StandIns.AnyWithin(__instance.transform.position, range))
                {
                    return true;
                }

                if (__instance.IsSleeping() && __instance.m_wakeupRange > 0f)
                {
                    __instance.Wakeup();
                }

                return false;
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Stand-ins: keeping monsters awake failed and is off: {e}");
                return true;
            }
        }
    }
}
#endif
