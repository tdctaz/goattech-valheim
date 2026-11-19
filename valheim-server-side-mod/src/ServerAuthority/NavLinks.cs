using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;

namespace ServerAuthority
{
    internal static class NavLinks
    {
        internal const int LinkCap = 65535;
        private const int CapMargin = 1024;
        private const float MaxSpread = 0.3f;
        private const float OnPointHalfWidth = 0.05f;
        private const float NearHalfWidth = 0.25f;

        private sealed class Run
        {
            internal int First;
            internal int Last;
            internal float Min;
            internal float Max;
            internal float LastY;
            internal bool Single;
            internal bool Extended;
        }

        private static readonly ConditionalWeakTable<List<KeyValuePair<Vector3, NavMeshLinkInstance>>, List<float>> Widths =
            new ConditionalWeakTable<List<KeyValuePair<Vector3, NavMeshLinkInstance>>, List<float>>();

        private static readonly List<Vector3> Hits = new List<Vector3>();
        private static readonly List<Run> Open = new List<Run>();
        private static readonly Stack<Run> Spare = new Stack<Run>();
        private static readonly List<Vector3> Centers = new List<Vector3>();
        private static readonly List<float> CenterWidths = new List<float>();

        private static bool _failed;
        private static bool _described;
        private static int _terrainMask = -1;

        internal static int Points;
        internal static int Blocked;
        internal static int Beside;
        internal static int Links;
        internal static int Skipped;
        internal static int Drained;
        internal static int Discarded;

        internal static bool Active => Plugin.ServerActive && !_failed && ModConfig.NavigationLinkMaxWidth.Value > 1f;

        internal static void Reset()
        {
            _failed = false;
            _described = false;
            ClearCounts();
        }

        internal static void ClearCounts()
        {
            Points = 0;
            Blocked = 0;
            Beside = 0;
            Links = 0;
            Skipped = 0;
            Drained = 0;
            Discarded = 0;
        }

        internal static bool DiscardStaleBuild(global::Pathfinding pathfinding)
        {
            if (!Plugin.ServerActive || pathfinding.m_buildOperation == null || !pathfinding.m_buildOperation.isDone ||
                pathfinding.m_buildTile == null)
            {
                return false;
            }

            global::Pathfinding.NavMeshTile tile = pathfinding.m_buildTile;
            if (pathfinding.m_tiles.TryGetValue(tile.m_tile, out global::Pathfinding.NavMeshTile current) && ReferenceEquals(current, tile))
            {
                return false;
            }

            pathfinding.m_buildOperation = null;
            pathfinding.m_buildTile = null;
            Discarded++;
            return true;
        }

        internal static bool TryWidths(List<KeyValuePair<Vector3, NavMeshLinkInstance>> links, out List<float> widths)
        {
            return Widths.TryGetValue(links, out widths) && widths.Count == links.Count;
        }

        internal static void DescribeOnce()
        {
            if (_described || !Plugin.ServerActive || global::Pathfinding.instance == null)
            {
                return;
            }

            _described = true;
            Describe();
        }

        internal static void Fail(Exception e)
        {
            if (_failed)
            {
                return;
            }

            _failed = true;
            Plugin.Log.LogError($"Navigation links: merging failed and is off until the next session, vanilla links are used: {e}");
        }

        private static void Describe()
        {
            global::Pathfinding pathfinding = global::Pathfinding.instance;
            if (pathfinding == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"Navigation: tiles of {pathfinding.m_tileSize:0} m built from layers {LayerNames(pathfinding.m_layers.value)}, " +
                $"water from {LayerNames(pathfinding.m_waterLayers.value)}; vanilla places one link per {pathfinding.m_linkWidth:0.#} m of " +
                $"tile edge, rebuilds a tile in use every {pathfinding.m_updateInterval:0.#} s and drops it {pathfinding.m_tileTimeout:0} s " +
                $"after its last use. " +
                (Active
                    ? $"Links along a level stretch of edge are joined into one, up to {ModConfig.NavigationLinkMaxWidth.Value:0.#} m wide."
                    : "Links are placed as in vanilla."));
        }

        internal static void Connect(global::Pathfinding pathfinding, List<KeyValuePair<Vector3, NavMeshLinkInstance>> links,
            Vector3 p0, Vector3 p1, float step, global::Pathfinding.AgentSettings settings)
        {
            Vector3 along = (p1 - p0).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, along);
            float length = Vector3.Distance(p0, p1);
            int longest = Mathf.Max(1, Mathf.FloorToInt(ModConfig.NavigationLinkMaxWidth.Value / step + 0.001f));
            float spread = Mathf.Min(MaxSpread, settings.m_build.agentClimb);
            float height = settings.m_build.agentHeight;
            float lift = Mathf.Min(settings.m_build.agentClimb, height * 0.5f);
            float probeHalfHeight = Mathf.Max(height - lift, 0.05f) * 0.5f;
            var onPoint = new Vector3(OnPointHalfWidth, probeHalfHeight, OnPointHalfWidth);
            float nearHalfWidth = Mathf.Max(NearHalfWidth, settings.m_build.agentRadius);
            var near = new Vector3(nearHalfWidth, probeHalfHeight, nearHalfWidth);
            if (_terrainMask < 0)
            {
                _terrainMask = LayerMask.GetMask("terrain");
            }

            int standing = pathfinding.m_layers.value & ~_terrainMask;
            int points = 0;
            int blocked = 0;
            int beside = 0;

            Centers.Clear();
            CenterWidths.Clear();
            ReleaseAll();

            int sample = 0;
            for (float distance = step / 2f; distance <= length; distance += step, sample++)
            {
                Vector3 point = p0 + along * distance;
                Hits.Clear();
                pathfinding.FindGround(point, settings.m_canSwim, Hits, settings);
                points += Hits.Count;

                for (int r = 0; r < Open.Count; r++)
                {
                    Open[r].Extended = false;
                }

                for (int h = 0; h < Hits.Count; h++)
                {
                    Vector3 hit = Hits[h];
                    Vector3 probeCenter = hit + Vector3.up * (lift + probeHalfHeight);
                    if (Physics.CheckBox(probeCenter, onPoint, Quaternion.identity, standing, QueryTriggerInteraction.Ignore))
                    {
                        blocked++;
                        continue;
                    }

                    if (Physics.CheckBox(probeCenter, near, Quaternion.identity, standing, QueryTriggerInteraction.Ignore))
                    {
                        beside++;
                        Run alone = Take();
                        alone.First = sample;
                        alone.Last = sample;
                        alone.Min = hit.y;
                        alone.Max = hit.y;
                        alone.LastY = hit.y;
                        alone.Single = true;
                        alone.Extended = true;
                        Open.Add(alone);
                        continue;
                    }

                    Run best = null;
                    float bestGap = float.MaxValue;
                    for (int r = 0; r < Open.Count; r++)
                    {
                        Run run = Open[r];
                        if (run.Extended || run.Single || run.Last != sample - 1 || run.Last - run.First + 1 >= longest)
                        {
                            continue;
                        }

                        float gap = Mathf.Abs(hit.y - run.LastY);
                        if (gap > spread || Mathf.Max(run.Max, hit.y) - Mathf.Min(run.Min, hit.y) > spread || gap >= bestGap)
                        {
                            continue;
                        }

                        best = run;
                        bestGap = gap;
                    }

                    if (best == null)
                    {
                        best = Take();
                        best.First = sample;
                        best.Min = hit.y;
                        best.Max = hit.y;
                        best.Single = false;
                        Open.Add(best);
                    }

                    best.Last = sample;
                    best.LastY = hit.y;
                    best.Min = Mathf.Min(best.Min, hit.y);
                    best.Max = Mathf.Max(best.Max, hit.y);
                    best.Extended = true;
                }

                for (int r = Open.Count - 1; r >= 0; r--)
                {
                    if (!Open[r].Extended)
                    {
                        Emit(Open[r], p0, along, step);
                        Spare.Push(Open[r]);
                        Open.RemoveAt(r);
                    }
                }
            }

            for (int r = 0; r < Open.Count; r++)
            {
                Emit(Open[r], p0, along, step);
            }

            ReleaseAll();

            if (Same(links))
            {
                return;
            }

            Points += points;
            Blocked += blocked;
            Beside += beside;

            pathfinding.ClearLinks(links);
            int room = LinkCap - CapMargin - Allocated(pathfinding);
            Queue<NavMeshLinkInstance> queue = pathfinding.m_linkRemoveQueue;
            while (room < Centers.Count && queue.Count > 0)
            {
                NavMesh.RemoveLink(queue.Dequeue());
                Drained++;
                room++;
            }

            List<float> widths = WidthsOf(links);
            widths.Clear();
            for (int i = 0; i < Centers.Count; i++)
            {
                if (room <= 0)
                {
                    Skipped += Centers.Count - i;
                    break;
                }

                Vector3 center = Centers[i];
                NavMeshLinkInstance link = NavMesh.AddLink(new NavMeshLinkData
                {
                    startPosition = center - across * 0.1f,
                    endPosition = center + across * 0.1f,
                    width = CenterWidths[i],
                    costModifier = pathfinding.m_linkCost,
                    bidirectional = true,
                    agentTypeID = settings.m_build.agentTypeID,
                    area = 2
                });
                if (NavMesh.IsLinkValid(link))
                {
                    links.Add(new KeyValuePair<Vector3, NavMeshLinkInstance>(center, link));
                    widths.Add(CenterWidths[i]);
                    Links++;
                    room--;
                }
            }
        }

        private static void Emit(Run run, Vector3 p0, Vector3 along, float step)
        {
            float middle = step / 2f + (run.First + run.Last) * 0.5f * step;
            Vector3 center = p0 + along * middle;
            center.y = (run.Min + run.Max) * 0.5f;
            Centers.Add(center);
            CenterWidths.Add((run.Last - run.First + 1) * step);
        }

        private static bool Same(List<KeyValuePair<Vector3, NavMeshLinkInstance>> links)
        {
            if (links.Count != Centers.Count || !Widths.TryGetValue(links, out List<float> widths) || widths.Count != links.Count)
            {
                return false;
            }

            for (int i = 0; i < Centers.Count; i++)
            {
                if (links[i].Key != Centers[i] || widths[i] != CenterWidths[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static List<float> WidthsOf(List<KeyValuePair<Vector3, NavMeshLinkInstance>> links)
        {
            return Widths.GetValue(links, _ => new List<float>());
        }

        internal static int Allocated(global::Pathfinding pathfinding)
        {
            int count = pathfinding.m_linkRemoveQueue.Count;
            foreach (global::Pathfinding.NavMeshTile tile in pathfinding.m_tiles.Values)
            {
                count += tile.m_links1.Count + tile.m_links2.Count;
            }

            return count;
        }

        private static Run Take()
        {
            return Spare.Count > 0 ? Spare.Pop() : new Run();
        }

        private static void ReleaseAll()
        {
            for (int r = 0; r < Open.Count; r++)
            {
                Spare.Push(Open[r]);
            }

            Open.Clear();
        }

        private static string LayerNames(int mask)
        {
            var names = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }

                string name = LayerMask.LayerToName(i);
                names.Add(string.IsNullOrEmpty(name) ? i.ToString() : name);
            }

            return names.Count == 0 ? "none" : string.Join(", ", names);
        }
    }
}
