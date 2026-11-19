#if DEBUG_TOOLS
using System;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using UnityEngine.Scripting;

namespace ServerAuthority
{
    internal static class GcSwitch
    {
        private const float LongestPause = 600f;

        private static float _resumeAt;

        internal static void Handle(string[] parts)
        {
            string verb = parts.Length > 1 ? parts[1].ToLowerInvariant() : "status";
            float number = parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : 0f;
            switch (verb)
            {
                case "slice":
                    if (number > 0f)
                    {
                        GarbageCollector.incrementalTimeSliceNanoseconds = (ulong)(number * 1000000f);
                    }

                    break;
                case "collect":
                    long started = Stopwatch.GetTimestamp();
                    GC.Collect();
                    Plugin.Log.LogInfo($"GC: a full collection on request took {(Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency:F0} ms.");
                    break;
                case "pause":
                    float seconds = Mathf.Clamp(number > 0f ? number : 60f, 1f, LongestPause);
                    GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                    _resumeAt = Time.realtimeSinceStartup + seconds;
                    Plugin.Log.LogInfo($"GC: collection paused for {seconds:F0} s.");
                    break;
                case "resume":
                    Resume();
                    break;
            }

            Status();
        }

        internal static void Tick()
        {
            if (_resumeAt > 0f && Time.realtimeSinceStartup >= _resumeAt)
            {
                Resume();
                Status();
            }
        }

        private static void Resume()
        {
            _resumeAt = 0f;
            GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
            Plugin.Log.LogInfo("GC: collection enabled again.");
        }

        private static void Status()
        {
            Plugin.Log.LogInfo(
                $"GC: mode {GarbageCollector.GCMode}, {(GarbageCollector.isIncremental ? "incremental" : "not incremental")}, " +
                $"{GarbageCollector.incrementalTimeSliceNanoseconds / 1000000.0:0.##} ms slices, managed heap " +
                $"{GC.GetTotalMemory(false) / (1024 * 1024)} MB, {GC.CollectionCount(0)} collections so far.");
        }
    }
}
#endif
