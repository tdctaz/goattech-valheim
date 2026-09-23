using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// Instantiates GameObjects around every player instead of around the server's own unused
    /// reference position. Without this the server can hold ownership but has nothing to run.
    ///
    /// This is a full replacement rather than a redirect because two details of the vanilla
    /// implementation are wrong once there is more than one reference point, and both matter:
    /// objects are sorted by distance to a position that does not exist on a server, and object
    /// creation is gated on a single global "is my area loaded" check that would stall creation
    /// for everybody whenever any one player is still loading.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateDestroyObjects))]
    internal static class ZNetScene_CreateDestroyObjects_Patch
    {
        // Reused every frame. The obvious implementation collects per peer and then calls
        // Distinct().ToList(), which allocates two lists per frame forever.
        private static readonly List<ZDO> Near = new List<ZDO>();
        private static readonly List<ZDO> Distant = new List<ZDO>();
        private static readonly List<ZDO> ScratchNear = new List<ZDO>();
        private static readonly List<ZDO> ScratchDistant = new List<ZDO>();
        private static readonly HashSet<ZDO> NearSeen = new HashSet<ZDO>();
        private static readonly HashSet<ZDO> DistantSeen = new HashSet<ZDO>();
        private static readonly List<ZDO> SortBuffer = new List<ZDO>();
        private static readonly List<ZNetView> Removals = new List<ZNetView>();

        private static bool Prefix(ZNetScene __instance)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            List<Anchor> anchors = SimulationAnchors.Current;

            Near.Clear();
            Distant.Clear();
            NearSeen.Clear();
            DistantSeen.Clear();

            ZDOMan zdoMan = ZDOMan.instance;
            SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();

            for (int i = 0; i < anchors.Count; i++)
            {
                ScratchNear.Clear();
                ScratchDistant.Clear();
                zdoMan.FindSectorObjects(anchors[i].Zone, synced, ScratchNear, ScratchDistant);

                for (int j = 0; j < ScratchNear.Count; j++)
                {
                    if (NearSeen.Add(ScratchNear[j]))
                    {
                        Near.Add(ScratchNear[j]);
                    }
                }

                for (int j = 0; j < ScratchDistant.Count; j++)
                {
                    if (DistantSeen.Add(ScratchDistant[j]))
                    {
                        Distant.Add(ScratchDistant[j]);
                    }
                }
            }

            // With nobody online both lists are empty, which tears the whole scene down and leaves
            // the server idle. That is the intended behaviour, not an edge case.
            CreateObjects(__instance, anchors);
            RemoveObjects(__instance);
            return false;
        }

        private static void CreateObjects(ZNetScene scene, List<Anchor> anchors)
        {
            if (anchors.Count == 0)
            {
                return;
            }

            ZoneSystem zoneSystem = ZoneSystem.instance;
            int perFrame = ModConfig.MaxObjectsCreatedPerFrame.Value;

            SortBuffer.Clear();
            for (int i = 0; i < Near.Count; i++)
            {
                ZDO zdo = Near[i];
                if (zdo.Created)
                {
                    continue;
                }

                zdo.m_tempSortValue = NearestAnchorDistanceSqr(anchors, zdo.GetPosition());
                SortBuffer.Add(zdo);
            }

            int budget = Mathf.Max(SortBuffer.Count / 100, perFrame);
            SortBuffer.Sort(Compare);

            int created = 0;
            for (int i = 0; i < SortBuffer.Count; i++)
            {
                ZDO zdo = SortBuffer[i];
                Vector2s sector = zdo.GetSector();

                // Vanilla checks once, globally, that the active area is fully loaded. Per object
                // is both safer and does not let one loading player block creation everywhere else.
                if (!zoneSystem.IsZoneLoaded(sector) || !zoneSystem.IsZoneReadyForType(sector, zdo.Type))
                {
                    continue;
                }

                if (scene.CreateObject(zdo) != null)
                {
                    created++;
                    if (created >= budget)
                    {
                        return;
                    }
                }
                else
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    ZLog.Log("Destroyed invalid prefab ZDO: " + zdo.m_uid);
                    ZDOMan.instance.DestroyZDO(zdo);
                }
            }

            if (created > perFrame)
            {
                return;
            }

            for (int i = 0; i < Distant.Count; i++)
            {
                ZDO zdo = Distant[i];
                if (zdo.Created)
                {
                    continue;
                }

                if (scene.CreateObject(zdo) != null)
                {
                    created++;
                    if (created > perFrame)
                    {
                        return;
                    }
                }
                else
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    ZLog.Log("Destroyed invalid prefab ZDO: " + zdo.m_uid);
                    ZDOMan.instance.DestroyZDO(zdo);
                }
            }
        }

        private static void RemoveObjects(ZNetScene scene)
        {
            byte mark = (byte)(Time.frameCount & 0xFF);

            for (int i = 0; i < Near.Count; i++)
            {
                Near[i].TempRemoveEarmark = mark;
            }

            for (int i = 0; i < Distant.Count; i++)
            {
                Distant[i].TempRemoveEarmark = mark;
            }

            Removals.Clear();
            foreach (ZNetView view in scene.m_instances.Values)
            {
                if (view.GetZDO().TempRemoveEarmark != mark)
                {
                    Removals.Add(view);
                }
            }

            for (int i = 0; i < Removals.Count; i++)
            {
                ZNetView view = Removals[i];
                ZDO zdo = view.GetZDO();
                view.ResetZDO();
                Object.Destroy(view.gameObject);

                if (!zdo.Persistent && zdo.IsOwner())
                {
                    ZDOMan.instance.DestroyZDO(zdo);
                }

                scene.m_instances.Remove(zdo);
            }
        }

        private static float NearestAnchorDistanceSqr(List<Anchor> anchors, Vector3 point)
        {
            float best = float.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                Vector3 delta = anchors[i].Position - point;
                float sqr = (delta.x * delta.x) + (delta.y * delta.y) + (delta.z * delta.z);
                if (sqr < best)
                {
                    best = sqr;
                }
            }

            return best;
        }

        /// <summary>Mirrors ZNetScene.ZDOCompare: priority type first, then nearest.</summary>
        private static int Compare(ZDO x, ZDO y)
        {
            if (x.Type == y.Type)
            {
                return x.m_tempSortValue.CompareTo(y.m_tempSortValue);
            }

            return ((int)y.Type).CompareTo((int)x.Type);
        }
    }

    /// <summary>
    /// Used by spawners such as bone piles to decide whether they are worth simulating. On a
    /// server the answer has to be "is this near any player", not "is this near the origin".
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.OutsideActiveArea), new[] { typeof(Vector3) })]
    internal static class ZNetScene_OutsideActiveArea_Patch
    {
        private static bool Prefix(ref bool __result, Vector3 point)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            List<Anchor> anchors = SimulationAnchors.Current;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (!ZNetScene.OutsideActiveArea(point, anchors[i].Zone))
                {
                    __result = false;
                    return false;
                }
            }

            __result = true;
            return false;
        }
    }
}
