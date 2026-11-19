#if DEBUG_TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority
{
    internal static class UpdateProfiler
    {
        private const int Shown = 20;
        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly string[] Messages = { "Update", "LateUpdate", "FixedUpdate" };

        private sealed class Bucket
        {
            internal long Ticks;
            internal long Bytes;
            internal int Calls;
        }

        private struct Probe
        {
            internal long Started;
            internal long Heap;
        }

        private static readonly Dictionary<MethodBase, Bucket> Buckets = new Dictionary<MethodBase, Bucket>();
        private static readonly List<MethodInfo> Patched = new List<MethodInfo>();
        private static HarmonyMethod _prefix;
        private static HarmonyMethod _postfix;
        private static float _until;
        private static float _started;
        private static int _startFrame;

        internal static bool Running => Patched.Count > 0;

        internal static void Start(float seconds)
        {
            if (Running)
            {
                Plugin.Log.LogInfo("Update profile: already running.");
                return;
            }

            _prefix = new HarmonyMethod(typeof(UpdateProfiler).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));
            _postfix = new HarmonyMethod(typeof(UpdateProfiler).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
            Buckets.Clear();

            int failed = 0;
            int coroutines = 0;
            foreach (Type type in typeof(ZNet).Assembly.GetTypes())
            {
                if (IsCoroutine(type))
                {
                    MethodInfo moveNext = type.GetMethod("MoveNext", Declared, null, Type.EmptyTypes, null);
                    if (moveNext != null)
                    {
                        try
                        {
                            Plugin.Harmony.Patch(moveNext, _prefix, _postfix);
                            Patched.Add(moveNext);
                            coroutines++;
                        }
                        catch (Exception)
                        {
                            failed++;
                        }
                    }

                    continue;
                }

                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                foreach (string message in Messages)
                {
                    MethodInfo method = type.GetMethod(message, Declared, null, Type.EmptyTypes, null);
                    if (method == null || method.IsAbstract || method.ReturnType != typeof(void))
                    {
                        continue;
                    }

                    try
                    {
                        Plugin.Harmony.Patch(method, _prefix, _postfix);
                        Patched.Add(method);
                    }
                    catch (Exception)
                    {
                        failed++;
                    }
                }
            }

            foreach (MethodInfo extra in Extras())
            {
                try
                {
                    Plugin.Harmony.Patch(extra, _prefix, _postfix);
                    Patched.Add(extra);
                }
                catch (Exception)
                {
                    failed++;
                }
            }

            _started = Time.realtimeSinceStartup;
            _until = _started + Mathf.Max(seconds, 5f);
            _startFrame = Time.frameCount;
            Plugin.Log.LogInfo(
                $"Update profile: timing {Patched.Count - coroutines} Update, LateUpdate and FixedUpdate methods and {coroutines} coroutines for {_until - _started:F0} s" +
                (failed > 0 ? $", {failed} could not be patched." : "."));
        }

        internal static void Tick()
        {
            if (!Running || Time.realtimeSinceStartup < _until)
            {
                return;
            }

            float seconds = Mathf.Max(Time.realtimeSinceStartup - _started, 0.001f);
            int frames = Time.frameCount - _startFrame;
            foreach (MethodInfo method in Patched)
            {
                try
                {
                    Plugin.Harmony.Unpatch(method, _prefix.method);
                    Plugin.Harmony.Unpatch(method, _postfix.method);
                }
                catch (Exception)
                {
                }
            }

            Patched.Clear();
            double total = Buckets.Values.Sum(b => Ms(b.Ticks));
            string top = string.Join("; ", Buckets
                .OrderByDescending(e => e.Value.Ticks)
                .Take(Shown)
                .Select(e => $"{Label(e.Key)} {Ms(e.Value.Ticks) / seconds:F1} ms/s, " +
                             $"{e.Value.Calls / Math.Max(frames, 1)} calls per frame"));
            string heap = string.Join("; ", Buckets
                .Where(e => e.Value.Bytes > 0)
                .OrderByDescending(e => e.Value.Bytes)
                .Take(Shown)
                .Select(e => $"{Label(e.Key)} {e.Value.Bytes / (1024.0 * 1024.0) / seconds:0.00} MB/s"));
            Plugin.Log.LogInfo(
                $"Update profile over {seconds:F0} s and {frames} frames: {total / seconds:F0} ms/s in all timed methods. {top}. " +
                Census());
            Plugin.Log.LogInfo($"Update profile, managed heap growth by method: {(heap.Length == 0 ? "none measured" : heap)}.");
            Buckets.Clear();
        }

        private static string Census()
        {
            ParticleSystem[] systems = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            int playing = 0;
            int alive = 0;
            foreach (ParticleSystem system in systems)
            {
                if (system.isPlaying)
                {
                    playing++;
                }

                if (system.particleCount > 0)
                {
                    alive++;
                }
            }

            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            Animator[] animators = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            int animating = animators.Count(a => a.isActiveAndEnabled);
            AudioSource[] audio = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            return $"Census: {systems.Length} particle systems ({playing} playing, {alive} with particles), {lights.Length} lights, " +
                   $"{animating} of {animators.Length} animators enabled, {audio.Length} audio sources.";
        }

        private static void Prefix(out Probe __state)
        {
            __state.Heap = GC.GetTotalMemory(false);
            __state.Started = Stopwatch.GetTimestamp();
        }

        private static void Postfix(MethodBase __originalMethod, Probe __state)
        {
            long spent = Stopwatch.GetTimestamp() - __state.Started;
            long grew = GC.GetTotalMemory(false) - __state.Heap;
            if (!Buckets.TryGetValue(__originalMethod, out Bucket bucket))
            {
                bucket = new Bucket();
                Buckets[__originalMethod] = bucket;
            }

            bucket.Ticks += spent;
            bucket.Calls++;
            if (grew > 0)
            {
                bucket.Bytes += grew;
            }
        }

        private static IEnumerable<MethodInfo> Extras()
        {
            string[] names = { nameof(WaterVolume.UpdateFloaters), nameof(WaterVolume.UpdateMaterials) };
            foreach (string name in names)
            {
                MethodInfo method = AccessTools.Method(typeof(WaterVolume), name);
                if (method != null)
                {
                    yield return method;
                }
            }

            MethodInfo staticUpdate = AccessTools.Method(typeof(WaterVolume), nameof(WaterVolume.StaticUpdate));
            if (staticUpdate != null)
            {
                yield return staticUpdate;
            }
        }

        private static bool IsCoroutine(Type type)
        {
            return type.IsNested && !type.IsGenericTypeDefinition && type.Name.StartsWith("<", StringComparison.Ordinal) &&
                   typeof(System.Collections.IEnumerator).IsAssignableFrom(type) &&
                   type.DeclaringType != null && typeof(MonoBehaviour).IsAssignableFrom(type.DeclaringType);
        }

        private static string Label(MethodBase method)
        {
            Type type = method.DeclaringType;
            if (type == null)
            {
                return method.Name;
            }

            if (type.IsNested && type.Name.StartsWith("<", StringComparison.Ordinal) && type.DeclaringType != null)
            {
                int end = type.Name.IndexOf('>');
                string name = end > 1 ? type.Name.Substring(1, end - 1) : type.Name;
                return $"{type.DeclaringType.Name}.{name} (coroutine)";
            }

            return $"{type.Name}.{method.Name}";
        }

        private static double Ms(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
#endif
