using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ServerAuthority
{
    internal static class FrameBudget
    {
        internal const string PathQueryScope = "path queries";
        internal const string PathfindingScope = "pathfinding";
        internal const string NavSourcesScope = "navigation tile sources";
        internal const string NavLinksScope = "navigation links";
        internal const string CreationScope = "object creation";
        internal const string ZoneScope = "zone loading";
        internal const string EnemySearchScope = "enemy search";
        internal const string StaticTargetScope = "structure target search";
        internal const string SightScope = "line of sight";
        internal const string PhysicsScope = "physics and collisions";
        internal const string SendScope = "object sends";
        internal const string MonoUpdateScope = "MonoUpdaters.Update in all";
        internal const string WaterFloatScope = "floating on water";
        internal const string WaterMaterialScope = "water materials";
        internal const string WaterClockScope = "water clock and wind";
        private const string ScopePrefix = "MonoUpdaters.";
        private const int LinkCap = 65535;
        private const int ShownScopes = 10;
        private const int ShownItems = 5;

        internal static bool Active;

        private sealed class Bucket
        {
            internal long Ticks;
            internal long WindowTicks;
            internal long Worst;
            internal long WindowWorst;
            internal int Calls;
            internal long Bytes;
        }

        private sealed class Share
        {
            internal int Queries;
            internal int Failed;
            internal int Tiles;
        }

        private static readonly Dictionary<string, Bucket> Buckets = new Dictionary<string, Bucket>();
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        private static readonly Dictionary<long, Share> Shares = new Dictionary<long, Share>();
        private static readonly Dictionary<int, int> TilesByAgent = new Dictionary<int, int>();

        private static int _queries;
        private static int _failedQueries;
        private static int _windowQueries;
        private static int _builds;
        private static int _windowBuilds;
        private static long _buildStarted;
        private static long _buildWorst;
        private static long _buildTotal;
        private static int _buildsFinished;
        private static int _linkFailures;
        private static int _windowLinkFailures;
        private static bool _listening;
        private static long _fixedScriptsDone;
        private static long _fixedScriptsHeap;
        private const int MaxDepth = 32;
        private static readonly long[] HeapAtBegin = new long[MaxDepth];
        private static int _depth;
        private static readonly Dictionary<string, int> AwakeItems = new Dictionary<string, int>();
        private static readonly HashSet<string> Nested = new HashSet<string>
        {
            PathQueryScope, EnemySearchScope, StaticTargetScope, SightScope, NavSourcesScope, NavLinksScope, MonoUpdateScope
        };

        internal static void Reset(bool server)
        {
            Active = server;
            Buckets.Clear();
            Shares.Clear();
            _queries = 0;
            _failedQueries = 0;
            _windowQueries = 0;
            _builds = 0;
            _windowBuilds = 0;
            _buildStarted = 0;
            _buildWorst = 0;
            _buildTotal = 0;
            _buildsFinished = 0;
            _linkFailures = 0;
            _windowLinkFailures = 0;
            if (server)
            {
                FrameLoop.Install();
                Plugin.Log.LogInfo(
                    $"Physics: fixed step {Time.fixedDeltaTime * 1000f:0.#} ms, at most {Time.maximumDeltaTime * 1000f:0} ms of game time per frame, " +
                    $"collision callbacks {(Physics.reuseCollisionCallbacks ? "reuse one object" : "allocate a new object each call")}, " +
                    $"creature contact reads {(CollisionData.Active ? "reuse an array" : "allocate as in vanilla")}.");
            }
            else
            {
                FrameLoop.Uninstall();
            }

            if (server && !_listening)
            {
                Application.logMessageReceived += OnLog;
                _listening = true;
            }
            else if (!server && _listening)
            {
                Application.logMessageReceived -= OnLog;
                _listening = false;
            }
        }

        internal static void Fail(Exception e)
        {
            if (!Active)
            {
                return;
            }

            Active = false;
            Plugin.Log.LogError($"Frame time accounting stopped after an error and stays off until the next session: {e}");
        }

        internal static long Begin()
        {
            if (!Active)
            {
                return 0L;
            }

            if (_depth < MaxDepth)
            {
                HeapAtBegin[_depth] = AllocationMeter.Now();
            }

            _depth++;
            return Stopwatch.GetTimestamp();
        }

        internal static void FrameStarted()
        {
            _depth = 0;
        }

        internal static void End(string scope, long started)
        {
            if (started == 0L || !Active)
            {
                return;
            }

            long bytes = 0;
            if (_depth > 0)
            {
                _depth--;
                if (_depth < MaxDepth)
                {
                    bytes = AllocationMeter.Growth(HeapAtBegin[_depth]);
                }
            }

            Add(scope, Stopwatch.GetTimestamp() - started, bytes);
        }

        private static void Add(string scope, long spent, long bytes)
        {
            if (!Buckets.TryGetValue(scope, out Bucket bucket))
            {
                bucket = new Bucket();
                Buckets[scope] = bucket;
            }

            bucket.Ticks += spent;
            bucket.WindowTicks += spent;
            bucket.Calls++;
            bucket.Bytes += bytes;
            if (spent > bucket.Worst)
            {
                bucket.Worst = spent;
            }

            if (spent > bucket.WindowWorst)
            {
                bucket.WindowWorst = spent;
            }
        }

        internal static void FixedScriptsDone()
        {
            if (!Active)
            {
                _fixedScriptsDone = 0L;
                return;
            }

            _fixedScriptsHeap = AllocationMeter.Now();
            _fixedScriptsDone = Stopwatch.GetTimestamp();
        }

        internal static void PhysicsDone()
        {
            long started = _fixedScriptsDone;
            _fixedScriptsDone = 0L;
            if (started == 0L || !Active)
            {
                return;
            }

            Add(PhysicsScope, Stopwatch.GetTimestamp() - started, AllocationMeter.Growth(_fixedScriptsHeap));
        }

        internal static IEnumerator PhysicsProbe()
        {
            var wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait;
                try
                {
                    PhysicsDone();
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        internal static string Scope(string profileScope)
        {
            if (profileScope == null)
            {
                return "other updates";
            }

            if (Names.TryGetValue(profileScope, out string name))
            {
                return name;
            }

            name = profileScope.StartsWith(ScopePrefix, StringComparison.Ordinal)
                ? profileScope.Substring(ScopePrefix.Length)
                : profileScope;
            Names[profileScope] = name;
            return name;
        }

        internal static void PathQuery(Vector3 from, bool found)
        {
            if (!Active)
            {
                return;
            }

            _queries++;
            _windowQueries++;
            Share share = ShareFor(NearestAnchor(from));
            share.Queries++;
            if (!found)
            {
                _failedQueries++;
                share.Failed++;
            }
        }

        internal static void TileBuildStarted()
        {
            if (!Active)
            {
                return;
            }

            _builds++;
            _windowBuilds++;
            _buildStarted = Stopwatch.GetTimestamp();
        }

        internal static void TileBuildHarvested()
        {
            if (!Active || _buildStarted == 0L)
            {
                return;
            }

            long took = Stopwatch.GetTimestamp() - _buildStarted;
            _buildStarted = 0L;
            _buildsFinished++;
            _buildTotal += took;
            if (took > _buildWorst)
            {
                _buildWorst = took;
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (Active && type == LogType.Error && condition != null &&
                condition.StartsWith("Failed to allocate NavMeshLink", StringComparison.Ordinal))
            {
                _linkFailures++;
                _windowLinkFailures++;
            }
        }

        internal static string Window(float seconds)
        {
            if (!Active)
            {
                return "";
            }

            var text = new StringBuilder();
            text.Append("Frame time, ms per second: ");
            text.Append(Top(seconds, true));
            Navigation(out int tiles, out int links, out int queued, out int overdue);
            text.Append($". Navigation: {tiles} tiles, {links} links in use of {LinkCap}, {queued} queued for removal, ");
            text.Append($"{overdue} tiles overdue for a rebuild, {_windowBuilds} built, {_windowQueries / Math.Max(seconds, 0.001f):F0} path queries/s");
            if (_windowLinkFailures > 0)
            {
                text.Append($", {_windowLinkFailures} link allocation failures");
            }

            text.Append('.');
            ClearWindow();
            return text.ToString();
        }

        internal static void ClearWindow()
        {
            foreach (Bucket bucket in Buckets.Values)
            {
                bucket.WindowTicks = 0;
                bucket.WindowWorst = 0;
            }

            _windowQueries = 0;
            _windowBuilds = 0;
            _windowLinkFailures = 0;
        }

        internal static string ClosePeriod(float seconds)
        {
            if (!Active)
            {
                return null;
            }

            Navigation(out int tiles, out int links, out int queued, out int overdue);
            var text = new StringBuilder();
            text.Append($"Frame time over {seconds:F0}s, ms per second: ");
            text.Append(Top(seconds, false));
            text.Append($", {Measured(seconds)} of 1000 measured. ");
            text.Append(Items());
            string particles = ServerParticles.ClosePeriod();
            if (particles != null)
            {
                text.Append(' ');
                text.Append(particles);
            }

            text.Append($" Path queries: {_queries / Math.Max(seconds, 0.001f):F0}/s, ");
            text.Append($"{Percent(_failedQueries, _queries)} failed, {PerSecond(PathQueryScope, seconds):F0} ms/s. ");
            text.Append($"Navigation: {tiles} tiles");
            if (TilesByAgent.Count > 0)
            {
                text.Append(" (");
                text.Append(string.Join(", ", TilesByAgent
                    .OrderByDescending(e => e.Value)
                    .Select(e => $"{e.Value} {AgentName(e.Key)}")));
                text.Append(')');
            }

            text.Append($", {links} links in use of {LinkCap}, {queued} queued for removal, {overdue} tiles overdue ");
            text.Append($"for a rebuild; {_builds} tile builds, ");
            text.Append(_buildsFinished > 0
                ? $"{Ms(_buildTotal) / _buildsFinished:F0} ms average from start to links, worst {Ms(_buildWorst):F0} ms"
                : "none finished");
            text.Append($"; main thread {PerSecond(NavSourcesScope, seconds):F0} ms/s gathering tile sources and ");
            text.Append($"{PerSecond(NavLinksScope, seconds):F0} ms/s placing links");
            text.Append($"; {_linkFailures} link allocation failures");
            if (NavLinks.Discarded > 0)
            {
                text.Append($"; {NavLinks.Discarded} finished tile builds dropped because their tile had timed out meanwhile");
            }

            if (NavLinks.Points > 0)
            {
                text.Append($"; edges re-linked from {NavLinks.Points} edge points ({NavLinks.Blocked} left out with something standing on them, " +
                            $"{NavLinks.Beside} kept one metre wide beside something) into {NavLinks.Links} links");
                if (NavLinks.Skipped > 0)
                {
                    text.Append($", {NavLinks.Skipped} left out to stay under the cap");
                }

                if (NavLinks.Drained > 0)
                {
                    text.Append($", {NavLinks.Drained} queued removals done early to make room");
                }
            }

            text.Append('.');
            NavLinks.ClearCounts();

            List<Anchor> anchors = SimulationAnchors.Current;
            foreach (KeyValuePair<long, Share> entry in Shares.OrderByDescending(e => e.Value.Queries + e.Value.Tiles))
            {
                if (entry.Value.Queries == 0 && entry.Value.Tiles == 0)
                {
                    continue;
                }

                text.Append($" Near {ServerPerformance.AnchorLabel(anchors, entry.Key)}: {entry.Value.Tiles} tiles, ");
                text.Append($"{entry.Value.Queries / Math.Max(seconds, 0.001f):F0} path queries/s, ");
                text.Append($"{Percent(entry.Value.Failed, entry.Value.Queries)} failed.");
            }

            foreach (Bucket bucket in Buckets.Values)
            {
                bucket.Ticks = 0;
                bucket.Worst = 0;
                bucket.Calls = 0;
                bucket.Bytes = 0;
            }

            Shares.Clear();
            _queries = 0;
            _failedQueries = 0;
            _builds = 0;
            _buildsFinished = 0;
            _buildTotal = 0;
            _buildWorst = 0;
            _linkFailures = 0;
            return text.ToString();
        }

        private static string Top(float seconds, bool window)
        {
            float span = Math.Max(seconds, 0.001f);
            var parts = Buckets
                .Select(e => new { Name = e.Key, Ticks = window ? e.Value.WindowTicks : e.Value.Ticks, Worst = window ? e.Value.WindowWorst : e.Value.Worst, Bytes = window ? 0L : e.Value.Bytes })
                .Where(e => e.Ticks > 0)
                .OrderByDescending(e => e.Ticks)
                .Take(ShownScopes)
                .Select(e => $"{e.Name} {Ms(e.Ticks) / span:F0} (worst {Ms(e.Worst):F0} ms{HeapGrowth(e.Bytes, span)})")
                .ToList();
            return parts.Count == 0 ? "nothing measured" : string.Join(", ", parts);
        }

        internal static string AllScopes()
        {
            return string.Join(", ", Buckets
                .Where(e => e.Value.Ticks > 0)
                .OrderByDescending(e => e.Value.Ticks)
                .Select(e => $"{e.Key} {Ms(e.Value.Ticks):F0} ms over {e.Value.Calls} calls"));
        }

        private static string HeapGrowth(long bytes, float span)
        {
            double perSecond = bytes / (1024.0 * 1024.0) / span;
            return perSecond >= 0.05 ? $", heap +{perSecond:0.0} MB/s" : "";
        }

        private static double PerSecond(string scope, float seconds)
        {
            return Buckets.TryGetValue(scope, out Bucket bucket) ? Ms(bucket.Ticks) / Math.Max(seconds, 0.001f) : 0.0;
        }

        private static string Measured(float seconds)
        {
            long ticks = 0;
            foreach (KeyValuePair<string, Bucket> entry in Buckets)
            {
                if (!Nested.Contains(entry.Key))
                {
                    ticks += entry.Value.Ticks;
                }
            }

            return $"{Ms(ticks) / Math.Max(seconds, 0.001f):F0}";
        }

        private static string Items()
        {
            AwakeItems.Clear();
            int total = 0;
            int awake = 0;
            List<ItemDrop> items = ItemDrop.s_instances;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop item = items[i];
                if (item == null)
                {
                    continue;
                }

                total++;
                Rigidbody body = item.m_body;
                if (body == null || body.isKinematic || body.IsSleeping())
                {
                    continue;
                }

                awake++;
                string name = Utils.GetPrefabName(item.gameObject);
                AwakeItems.TryGetValue(name, out int count);
                AwakeItems[name] = count + 1;
            }

            string top = AwakeItems.Count == 0
                ? ""
                : " (" + string.Join(", ", AwakeItems.OrderByDescending(e => e.Value).Take(ShownItems).Select(e => $"{e.Value} {e.Key}")) + ")";
            return $"Loose items: {total} instantiated, {awake} with a rigidbody awake{top}.";
        }

        private static void Navigation(out int tiles, out int links, out int queued, out int overdue)
        {
            tiles = 0;
            links = 0;
            queued = 0;
            overdue = 0;
            TilesByAgent.Clear();
            global::Pathfinding pathfinding = global::Pathfinding.instance;
            if (pathfinding == null)
            {
                return;
            }

            List<Anchor> anchors = SimulationAnchors.Current;
            foreach (Share share in Shares.Values)
            {
                share.Tiles = 0;
            }

            float time = Time.time;
            foreach (KeyValuePair<Vector3Int, global::Pathfinding.NavMeshTile> entry in pathfinding.m_tiles)
            {
                global::Pathfinding.NavMeshTile tile = entry.Value;
                tiles++;
                links += tile.m_links1.Count + tile.m_links2.Count;
                TilesByAgent.TryGetValue(entry.Key.z, out int count);
                TilesByAgent[entry.Key.z] = count + 1;
                if (tile.m_pokeTime - tile.m_buildTime > pathfinding.m_updateInterval * 2f &&
                    time - tile.m_pokeTime <= pathfinding.m_tileTimeout)
                {
                    overdue++;
                }

                if (anchors.Count > 0)
                {
                    ShareFor(NearestAnchor(anchors, tile.m_center)).Tiles++;
                }
            }

            queued = pathfinding.m_linkRemoveQueue.Count;
            links += queued;
        }

        private static Share ShareFor(long anchor)
        {
            if (!Shares.TryGetValue(anchor, out Share share))
            {
                share = new Share();
                Shares[anchor] = share;
            }

            return share;
        }

        private static long NearestAnchor(Vector3 position)
        {
            return NearestAnchor(SimulationAnchors.Current, position);
        }

        private static long NearestAnchor(List<Anchor> anchors, Vector3 position)
        {
            long best = 0L;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                Vector3 offset = anchors[i].Position - position;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = anchors[i].Uid;
                }
            }

            return best;
        }

        private static string AgentName(int agent)
        {
            return Enum.IsDefined(typeof(global::Pathfinding.AgentType), agent)
                ? ((global::Pathfinding.AgentType)agent).ToString()
                : agent.ToString();
        }

        private static string Percent(int part, int whole)
        {
            return whole == 0 ? "0%" : $"{part * 100 / whole}%";
        }

        private static double Ms(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
