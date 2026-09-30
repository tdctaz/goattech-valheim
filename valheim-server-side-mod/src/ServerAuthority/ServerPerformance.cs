using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority
{
    internal static class ServerPerformance
    {
        private const float WindowSeconds = 10f;
        private const float WarningCooldownSeconds = 60f;
        private const float SlowFrameSeconds = 0.1f;

        private static float _lastFrame = -1f;

        private static float _windowStart;
        private static int _windowFrames;
        private static float _windowWorst;
        private static float _lastWarning = float.MinValue;

        private static float _reportStart;
        private static int _reportFrames;
        private static float _reportWorst;
        private static int _reportSlowFrames;
        private static int _reportSlowWindows;
        private static float _reportLowestFps = float.MaxValue;

        internal static void Reset()
        {
            _lastFrame = -1f;
            _windowStart = 0f;
            _windowFrames = 0;
            _windowWorst = 0f;
            _lastWarning = float.MinValue;
            ResetReport(0f);
            NetworkStats.Reset();
        }

        private static void ResetReport(float now)
        {
            _reportStart = now;
            _reportFrames = 0;
            _reportWorst = 0f;
            _reportSlowFrames = 0;
            _reportSlowWindows = 0;
            _reportLowestFps = float.MaxValue;
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            NetworkStats.Tick(now);
            if (_lastFrame < 0f)
            {
                _lastFrame = now;
                _windowStart = now;
                ResetReport(now);
                return;
            }

            float frame = now - _lastFrame;
            _lastFrame = now;

            _windowFrames++;
            _reportFrames++;
            _windowWorst = Mathf.Max(_windowWorst, frame);
            _reportWorst = Mathf.Max(_reportWorst, frame);
            if (frame > SlowFrameSeconds)
            {
                _reportSlowFrames++;
            }

            float windowLength = now - _windowStart;
            if (windowLength >= WindowSeconds)
            {
                CloseWindow(now, windowLength);
            }

            float interval = ModConfig.PerformanceReportSeconds.Value;
            float reportLength = now - _reportStart;
            if (interval > 0f && reportLength >= interval)
            {
                Plugin.Log.LogInfo(
                    $"Server performance over {reportLength:F0}s: {_reportFrames / reportLength:F1} fps average, " +
                    $"lowest 10s average {(_reportLowestFps == float.MaxValue ? 0f : _reportLowestFps):F1} fps, " +
                    $"worst frame {_reportWorst * 1000f:F0} ms, {_reportSlowFrames} frame(s) over " +
                    $"{SlowFrameSeconds * 1000f:F0} ms, {_reportSlowWindows} slow 10s window(s). {Snapshot(false)}");
                string network = NetworkStats.ClosePeriod(reportLength);
                if (network != null)
                {
                    Plugin.Log.LogInfo(network);
                }

                ResetReport(now);
            }
        }

        private static void CloseWindow(float now, float windowLength)
        {
            float fps = _windowFrames / windowLength;
            float worst = _windowWorst;
            _windowStart = now;
            _windowFrames = 0;
            _windowWorst = 0f;
            _reportLowestFps = Mathf.Min(_reportLowestFps, fps);

            int slowFps = ModConfig.SlowFrameRateWarning.Value;
            float hitch = ModConfig.HitchWarningMilliseconds.Value / 1000f;
            bool slow = (slowFps > 0 && fps < slowFps) || (hitch > 0f && worst > hitch);
            if (slow)
            {
                _reportSlowWindows++;
                if (now - _lastWarning >= WarningCooldownSeconds)
                {
                    _lastWarning = now;
                    Plugin.Log.LogWarning(
                        $"Server running slow: {fps:F1} fps over the last {windowLength:F0}s, worst frame " +
                        $"{worst * 1000f:F0} ms. {Snapshot(true)} {NetworkStats.SendWork(windowLength)}");
                }
            }

            NetworkStats.CloseWindow(now, windowLength);
        }

        private static string Snapshot(bool detailed)
        {
            var text = new StringBuilder();

            ZoneSystem zones = ZoneSystem.instance;
            ZNetScene scene = ZNetScene.instance;
            List<Anchor> anchors = SimulationAnchors.Current;
            List<Character> characters = Character.GetAllCharacters();

            int creatures = 0;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] != null && !characters[i].IsPlayer())
                {
                    creatures++;
                }
            }

            text.Append($"{anchors.Count} player(s), {(zones != null ? zones.m_zones.Count : 0)} zone(s) loaded, ");
            text.Append($"{(scene != null ? scene.m_instances.Count : 0)} object(s) instantiated, ");
            text.Append($"{creatures} creature(s), {BaseAI.BaseAIInstances.Count} AI.");

            if (!detailed)
            {
                return text.ToString();
            }

            var perAnchor = new Dictionary<long, Dictionary<string, int>>();
            for (int i = 0; i < characters.Count; i++)
            {
                Character character = characters[i];
                if (character == null || character.IsPlayer())
                {
                    continue;
                }

                long nearest = NearestAnchor(anchors, character.transform.position);
                if (!perAnchor.TryGetValue(nearest, out Dictionary<string, int> counts))
                {
                    counts = new Dictionary<string, int>();
                    perAnchor[nearest] = counts;
                }

                string name = PrefabName(character.gameObject.name);
                counts.TryGetValue(name, out int count);
                counts[name] = count + 1;
            }

            foreach (KeyValuePair<long, Dictionary<string, int>> entry in perAnchor.OrderByDescending(e => e.Value.Values.Sum()))
            {
                text.Append($" Near {AnchorLabel(anchors, entry.Key)}: {entry.Value.Values.Sum()} creature(s), ");
                text.Append(Top(entry.Value, 6));
                text.Append('.');
            }

            if (scene != null)
            {
                var objects = new Dictionary<string, int>();
                foreach (ZNetView view in scene.m_instances.Values)
                {
                    if (view == null)
                    {
                        continue;
                    }

                    string name = PrefabName(view.gameObject.name);
                    objects.TryGetValue(name, out int count);
                    objects[name] = count + 1;
                }

                text.Append(" Most instantiated: ");
                text.Append(Top(objects, 8));
                text.Append('.');
            }

            return text.ToString();
        }

        private static long NearestAnchor(List<Anchor> anchors, Vector3 position)
        {
            long best = 0L;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                float distance = (anchors[i].Position - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = anchors[i].Uid;
                }
            }

            return best;
        }

        private static string AnchorLabel(List<Anchor> anchors, long uid)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].Uid != uid)
                {
                    continue;
                }

                ZNetPeer peer = ZNet.instance?.GetPeer(uid);
                string name = peer != null && !string.IsNullOrEmpty(peer.m_playerName) ? peer.m_playerName : uid.ToString();
                Vector3 p = anchors[i].Position;
                return $"{name} at ({p.x:F0}, {p.z:F0})";
            }

            return "no player";
        }

        private static string Top(Dictionary<string, int> counts, int take)
        {
            return string.Join(", ", counts
                .OrderByDescending(e => e.Value)
                .Take(take)
                .Select(e => $"{e.Value} {e.Key}"));
        }

        private static string PrefabName(string name)
        {
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return clone >= 0 ? name.Substring(0, clone) : name;
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    internal static class ZNet_Update_ServerPerformance_Patch
    {
        private static void Postfix(ZNet __instance)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            try
            {
                ServerPerformance.Tick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Server performance report failed: {e}");
            }
        }
    }
}
