using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace ServerAuthority
{
    internal static class CreationPriority
    {
        private const float NearbyMetres = 48f;
        private const float FloorMetres = 16f;
        private const double ExpireMs = 10000.0;
        private const int PerTick = 10;
        private const int MaxPending = 256;

        internal static bool Active;

        private struct Pending
        {
            internal ZDOID Uid;
            internal long At;
        }

        private static readonly List<Pending> Queue = new List<Pending>();
        private static readonly List<ZDO> Unbuilt = new List<ZDO>();

        internal static void Reset(ZNet znet)
        {
            Active = znet != null && !znet.IsServer() && ModConfig.CreateFreshDropsFirst.Value;
            Queue.Clear();
            Unbuilt.Clear();
        }

        internal static void Fail(Exception e)
        {
            if (!Active)
            {
                return;
            }

            Active = false;
            Queue.Clear();
            Unbuilt.Clear();
            Plugin.Log.LogError($"Creating fresh drops first stopped after an error and stays off until the next session: {e}");
        }

        internal static void Received(ZDO zdo)
        {
            if (!Active || zdo.Created || Queue.Count >= MaxPending)
            {
                return;
            }

            int prefab = zdo.GetPrefab();
            bool ragdoll = PrefabKinds.IsRagdoll(prefab);
            if (!ragdoll && !PrefabKinds.IsItem(prefab))
            {
                return;
            }

            ZNet znet = ZNet.instance;
            if (znet == null || Player.m_localPlayer == null || !Nearby(zdo, znet) ||
                (!ragdoll && !PrefabKinds.IsFreshDrop(zdo)))
            {
                return;
            }

            for (int i = 0; i < Queue.Count; i++)
            {
                if (Queue[i].Uid == zdo.m_uid)
                {
                    return;
                }
            }

            Queue.Add(new Pending { Uid = zdo.m_uid, At = Stopwatch.GetTimestamp() });
        }

        internal static void CreatePending(ZNetScene scene, List<ZDO> currentNearObjects)
        {
            if (!Active || Queue.Count == 0)
            {
                return;
            }

            ZoneSystem zones = ZoneSystem.instance;
            ZDOMan man = ZDOMan.instance;
            ZNet znet = ZNet.instance;
            if (zones == null || man == null || znet == null || currentNearObjects == null)
            {
                return;
            }

            Unbuilt.Clear();
            for (int i = 0; i < currentNearObjects.Count; i++)
            {
                ZDO near = currentNearObjects[i];
                if (!near.Created && near.Type != ZDO.ObjectType.Default)
                {
                    Unbuilt.Add(near);
                }
            }

            long now = Stopwatch.GetTimestamp();
            int made = 0;
            for (int i = 0; i < Queue.Count && made < PerTick;)
            {
                Pending pending = Queue[i];
                ZDO zdo = man.GetZDO(pending.Uid);
                if (zdo == null || zdo.Created || !Nearby(zdo, znet) ||
                    (now - pending.At) * 1000.0 / Stopwatch.Frequency > ExpireMs)
                {
                    Queue.RemoveAt(i);
                    continue;
                }

                Vector2s sector = zdo.GetSector();
                if (!zones.IsZoneLoaded(sector) || !zones.IsZoneReadyForType(sector, zdo.Type) ||
                    UnbuiltBeneath(zdo.GetPosition(), sector))
                {
                    i++;
                    continue;
                }

                Queue.RemoveAt(i);
                if (scene.CreateObject(zdo) != null)
                {
                    made++;
                }
            }

            Unbuilt.Clear();
        }

        private static bool Nearby(ZDO zdo, ZNet znet)
        {
            return (zdo.GetPosition() - znet.GetReferencePosition()).sqrMagnitude <= NearbyMetres * NearbyMetres;
        }

        private static bool UnbuiltBeneath(Vector3 position, Vector2s sector)
        {
            for (int i = 0; i < Unbuilt.Count; i++)
            {
                ZDO zdo = Unbuilt[i];
                if (zdo.Type == ZDO.ObjectType.Terrain
                        ? ReachesZone(position, zdo.GetSector(), sector)
                        : (zdo.GetPosition() - position).sqrMagnitude <= FloorMetres * FloorMetres)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReachesZone(Vector3 position, Vector2s zone, Vector2s own)
        {
            if (zone == own)
            {
                return true;
            }

            Vector3 centre = ZoneSystem.GetZonePos(zone);
            float half = (float)ZoneSystem.c_ZoneHalfSizeDouble;
            float dx = Mathf.Max(Mathf.Abs(position.x - centre.x) - half, 0f);
            float dz = Mathf.Max(Mathf.Abs(position.z - centre.z) - half, 0f);
            return dx * dx + dz * dz <= FloorMetres * FloorMetres;
        }
    }
}
