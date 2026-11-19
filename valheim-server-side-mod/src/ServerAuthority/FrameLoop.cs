using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine.LowLevel;
using UnityEngine.Scripting;

namespace ServerAuthority
{
    internal static class FrameLoop
    {
        private const int ShownSteps = 10;
        private const int ShownWorstSteps = 3;

        private struct Marker
        {
        }

        private static readonly List<string> Steps = new List<string>();
        private static long[] _ticks = new long[0];
        private static long[] _frame = new long[0];
        private static long[] _bytes = new long[0];
        private static long _lastAllocated;
        private static long _last;
        private static int _lastStep = -1;
        private static bool _installed;
        private static long _frameStart;
        private static int _frameCollections;
        private static long _worstFrame;
        private static string _worstSteps;
        private static bool _worstCollected;
        private static int _periodCollections = -1;

        internal static void Install()
        {
            if (_installed)
            {
                return;
            }

            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            if (root.subSystemList == null)
            {
                return;
            }

            Steps.Clear();
            PlayerLoopSystem[] phases = root.subSystemList;
            for (int p = 0; p < phases.Length; p++)
            {
                PlayerLoopSystem phase = phases[p];
                PlayerLoopSystem[] children = phase.subSystemList ?? new PlayerLoopSystem[0];
                var marked = new List<PlayerLoopSystem>(children.Length * 2 + 1);
                string phaseName = phase.type != null ? phase.type.Name : "Phase" + p;
                for (int c = 0; c < children.Length; c++)
                {
                    string childName = children[c].type != null ? children[c].type.Name : "Step" + c;
                    marked.Add(MarkerFor(phaseName + "." + childName));
                    marked.Add(children[c]);
                }

                marked.Add(MarkerFor("between steps"));
                phase.subSystemList = marked.ToArray();
                phases[p] = phase;
            }

            root.subSystemList = phases;
            _ticks = new long[Steps.Count];
            _frame = new long[Steps.Count];
            _bytes = new long[Steps.Count];
            _lastAllocated = AllocationMeter.Now();
            _last = 0;
            _lastStep = -1;
            _frameStart = 0;
            _worstFrame = 0;
            _worstSteps = null;
            _periodCollections = GC.CollectionCount(0);
            PlayerLoop.SetPlayerLoop(root);
            _installed = true;
        }

        internal static void Uninstall()
        {
            if (!_installed)
            {
                return;
            }

            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            if (root.subSystemList != null)
            {
                PlayerLoopSystem[] phases = root.subSystemList;
                for (int p = 0; p < phases.Length; p++)
                {
                    PlayerLoopSystem phase = phases[p];
                    if (phase.subSystemList != null)
                    {
                        phase.subSystemList = phase.subSystemList.Where(s => s.type != typeof(Marker)).ToArray();
                        phases[p] = phase;
                    }
                }

                root.subSystemList = phases;
                PlayerLoop.SetPlayerLoop(root);
            }

            _installed = false;
            _last = 0;
            _lastStep = -1;
        }

        internal static string ClosePeriod(float seconds)
        {
            if (!_installed || _ticks.Length == 0)
            {
                return null;
            }

            float span = Math.Max(seconds, 0.001f);
            var totals = new Dictionary<string, long>();
            long all = 0;
            for (int i = 0; i < _ticks.Length; i++)
            {
                totals.TryGetValue(Steps[i], out long sum);
                totals[Steps[i]] = sum + _ticks[i];
                all += _ticks[i];
                _ticks[i] = 0;
            }

            string top = string.Join(", ", totals
                .Where(e => e.Value > 0)
                .OrderByDescending(e => e.Value)
                .Take(ShownSteps)
                .Select(e => $"{e.Key} {Ms(e.Value) / span:F0}"));
            int collections = GC.CollectionCount(0);
            string worst = _worstSteps == null
                ? ""
                : $" Worst frame {Ms(_worstFrame):F0} ms: {_worstSteps}{(_worstCollected ? ", with a garbage collection in it" : "")}.";
            string memory = $" {collections - _periodCollections} garbage collection(s), managed heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB, " +
                            (GarbageCollector.isIncremental
                                ? $"incremental with {GarbageCollector.incrementalTimeSliceNanoseconds / 1000000.0:0.#} ms slices."
                                : "not incremental.");
            string allocated = Allocated(span);
            _periodCollections = collections;
            _worstFrame = 0;
            _worstSteps = null;
            _worstCollected = false;
            return $"Main thread over {seconds:F0}s by engine step, ms per second: {top}; {Ms(all) / span:F0} in all.{worst}{memory}{allocated}";
        }

        private static string Allocated(float span)
        {
            var totals = new Dictionary<string, long>();
            long all = 0;
            for (int i = 0; i < _bytes.Length; i++)
            {
                totals.TryGetValue(Steps[i], out long sum);
                totals[Steps[i]] = sum + _bytes[i];
                all += _bytes[i];
                _bytes[i] = 0;
            }

            string top = string.Join(", ", totals
                .Where(e => e.Value > 0)
                .OrderByDescending(e => e.Value)
                .Take(ShownWorstSteps)
                .Select(e => $"{e.Key} {Mb(e.Value) / span:0.0}"));
            return $" The managed heap grew by {Mb(all) / span:0.0} MB/s between collections, most in {top}.";
        }

        private static double Mb(long bytes)
        {
            return bytes / (1024.0 * 1024.0);
        }

        private static PlayerLoopSystem MarkerFor(string step)
        {
            int index = Steps.Count;
            Steps.Add(step);
            return new PlayerLoopSystem
            {
                type = typeof(Marker),
                updateDelegate = () => Mark(index)
            };
        }

        private static void Mark(int step)
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastStep >= 0 && _lastStep < _ticks.Length)
            {
                long spent = now - _last;
                _ticks[_lastStep] += spent;
                _frame[_lastStep] += spent;
                long allocated = AllocationMeter.Now();
                if (allocated > _lastAllocated)
                {
                    _bytes[_lastStep] += allocated - _lastAllocated;
                }

                _lastAllocated = allocated;
            }
            else
            {
                _lastAllocated = AllocationMeter.Now();
            }

            if (step == 0)
            {
                FrameStarted(now);
            }

            _last = now;
            _lastStep = step;
        }

        private static void FrameStarted(long now)
        {
            int collections = GC.CollectionCount(0);
            FrameBudget.FrameStarted();
            if (_frameStart != 0)
            {
                long length = now - _frameStart;
                if (length > _worstFrame)
                {
                    _worstFrame = length;
                    _worstCollected = collections != _frameCollections;
                    _worstSteps = string.Join(", ", Enumerable.Range(0, _frame.Length)
                        .Where(i => _frame[i] > 0)
                        .OrderByDescending(i => _frame[i])
                        .Take(ShownWorstSteps)
                        .Select(i => $"{Steps[i]} {Ms(_frame[i]):F0}"));
                }
            }

            Array.Clear(_frame, 0, _frame.Length);
            _frameStart = now;
            _frameCollections = collections;
        }

        private static double Ms(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
