#if DEBUG_TOOLS
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ServerAuthority
{
    internal static class NavTest
    {
        private const float Range = 96f;
        private const float Reach = 4f;
        private const float Spacing = 2f;
        private const float Detour = 1.5f;

        internal static void Run(Vector3 around, string agentName)
        {
            global::Pathfinding pathfinding = global::Pathfinding.instance;
            if (pathfinding == null)
            {
                return;
            }

            global::Pathfinding.AgentType agent = global::Pathfinding.AgentType.Humanoid;
            if (!string.IsNullOrEmpty(agentName) && !Enum.TryParse(agentName, true, out agent))
            {
                Plugin.Log.LogWarning($"Nav test: no agent type '{agentName}'.");
                return;
            }

            global::Pathfinding.AgentSettings settings = pathfinding.GetSettings(agent);
            var floors = new List<Vector3>();
            int skipped = 0;
            var filter = new NavMeshQueryFilter { agentTypeID = settings.m_build.agentTypeID, areaMask = settings.m_areaMask };
            var path = new NavMeshPath();
            float half = pathfinding.m_tileSize / 2f;
            int tiles = 0;
            int tested = 0;
            int complete = 0;
            int partial = 0;
            int failed = 0;
            int measured = 0;
            int detours = 0;
            float deviation = 0f;
            float stretch = 0f;
            int links = 0;

            foreach (global::Pathfinding.NavMeshTile tile in pathfinding.m_tiles.Values)
            {
                if (tile.m_tile.z != (int)agent || !tile.m_instance.valid)
                {
                    continue;
                }

                if (!pathfinding.m_tiles.ContainsKey(new Vector3Int(tile.m_tile.x + 1, tile.m_tile.y, tile.m_tile.z)) ||
                    !pathfinding.m_tiles.ContainsKey(new Vector3Int(tile.m_tile.x, tile.m_tile.y + 1, tile.m_tile.z)))
                {
                    continue;
                }

                Vector3 offset = tile.m_center - around;
                offset.y = 0f;
                if (offset.magnitude > Range)
                {
                    continue;
                }

                tiles++;
                links += tile.m_links1.Count + tile.m_links2.Count;
                Edge(tile.m_center + new Vector3(half, 0f, half), tile.m_center + new Vector3(half, 0f, -half));
                Edge(tile.m_center + new Vector3(-half, 0f, half), tile.m_center + new Vector3(half, 0f, half));
            }

            Plugin.Log.LogInfo(
                $"Nav test on {tiles} {agent} tiles within {Range:0} m of ({around.x:0}, {around.z:0}), {links} links on their edges: " +
                $"{tested} paths across a tile edge every {Spacing:0} m on every floor, {skipped} floors skipped with no mesh on both sides, " +
                $"{complete} complete, {partial} partial, {failed} failed; " +
                $"crossing {(measured > 0 ? deviation / measured : 0f):0.00} m from the straight line on average, " +
                $"path {(measured > 0 ? stretch / measured : 0f):0.00}x the straight distance on average, {detours} detours over {Detour:0.0}x.");

            void Edge(Vector3 p0, Vector3 p1)
            {
                Vector3 along = (p1 - p0).normalized;
                Vector3 across = Vector3.Cross(Vector3.up, along);
                float length = Vector3.Distance(p0, p1);
                for (float d = Spacing / 2f; d < length; d += Spacing)
                {
                    floors.Clear();
                    pathfinding.FindGround(p0 + along * d, settings.m_canSwim, floors, settings);
                    foreach (Vector3 crossing in floors)
                    {
                        Cross(crossing, along, across, p0);
                    }
                }
            }

            void Cross(Vector3 crossing, Vector3 along, Vector3 across, Vector3 p0)
            {
                {
                    if (!NavMesh.SamplePosition(crossing - across * Reach, out NavMeshHit from, 1f, filter) ||
                        !NavMesh.SamplePosition(crossing + across * Reach, out NavMeshHit to, 1f, filter))
                    {
                        skipped++;
                        return;
                    }

                    tested++;
                    if (!NavMesh.CalculatePath(from.position, to.position, filter, path) || path.status == NavMeshPathStatus.PathInvalid)
                    {
                        failed++;
                        return;
                    }

                    if (path.status == NavMeshPathStatus.PathPartial)
                    {
                        partial++;
                        return;
                    }

                    complete++;
                    Vector3 a = from.position;
                    Vector3 b = to.position;
                    float span = Vector3.Dot(b - a, across);
                    if (Mathf.Abs(span) < 0.01f)
                    {
                        return;
                    }

                    Vector3 expected = a + (b - a) * (Vector3.Dot(p0 - a, across) / span);
                    Vector3[] corners = path.corners;
                    float length2 = 0f;
                    float found = -1f;
                    for (int c = 1; c < corners.Length; c++)
                    {
                        length2 += Vector3.Distance(corners[c - 1], corners[c]);
                        float s0 = Vector3.Dot(corners[c - 1] - p0, across);
                        float s1 = Vector3.Dot(corners[c] - p0, across);
                        if (found >= 0f || (s0 > 0f) == (s1 > 0f) || Mathf.Abs(s1 - s0) < 0.0001f)
                        {
                            continue;
                        }

                        Vector3 hit = Vector3.Lerp(corners[c - 1], corners[c], s0 / (s0 - s1));
                        found = Mathf.Abs(Vector3.Dot(hit - expected, along));
                    }

                    float ratio = length2 / Mathf.Max(Vector3.Distance(a, b), 0.01f);
                    if (found >= 0f)
                    {
                        measured++;
                        deviation += found;
                        stretch += ratio;
                    }

                    if (ratio > Detour)
                    {
                        detours++;
                    }
                }
            }
        }
    }
}
#endif
