using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class CreaturePriority
    {
        private const float NearbyMetres = 64f;
        private const int PerTick = 10;

        internal static bool Active;
        internal static int CreatedAhead;
        internal static int MostWaitingAhead;

        private static readonly List<ZDO> Candidates = new List<ZDO>();

        internal static void Reset(ZNet znet)
        {
            Active = znet != null && !znet.IsServer() && ModConfig.CreateCreaturesFirst.Value;
            Candidates.Clear();
            CreatedAhead = 0;
            MostWaitingAhead = 0;
        }

        internal static void Fail(Exception e)
        {
            if (!Active)
            {
                return;
            }

            Active = false;
            Candidates.Clear();
            Plugin.Log.LogError($"Creating creatures first stopped after an error and stays off until the next session: {e}");
        }

        internal static void TakeCounts(out int created, out int mostWaiting)
        {
            created = CreatedAhead;
            mostWaiting = MostWaitingAhead;
            CreatedAhead = 0;
            MostWaitingAhead = 0;
        }

        internal static void CreateNearby(ZNetScene scene, List<ZDO> currentNearObjects)
        {
            ZoneSystem zones = ZoneSystem.instance;
            ZNet znet = ZNet.instance;
            if (!Active || zones == null || znet == null || currentNearObjects == null)
            {
                return;
            }

            Vector3 reference = znet.GetReferencePosition();
            float range = NearbyMetres * NearbyMetres;
            int ahead = 0;
            Candidates.Clear();
            for (int i = 0; i < currentNearObjects.Count; i++)
            {
                ZDO zdo = currentNearObjects[i];
                if (zdo.Created)
                {
                    continue;
                }

                if (zdo.Type >= ZDO.ObjectType.Solid)
                {
                    ahead++;
                    continue;
                }

                if (!zdo.HasOwner() || zdo.IsOwner() || !PrefabKinds.IsCharacterOrProjectile(zdo.GetPrefab()))
                {
                    continue;
                }

                float distance = (zdo.GetPosition() - reference).sqrMagnitude;
                if (distance > range)
                {
                    continue;
                }

                zdo.m_tempSortValue = distance;
                Candidates.Add(zdo);
            }

            if (Candidates.Count == 0 || (ahead == 0 && zones.IsActiveAreaLoaded()))
            {
                Candidates.Clear();
                return;
            }

            if (Candidates.Count > PerTick)
            {
                Candidates.Sort(ByDistance);
            }

            int made = 0;
            for (int i = 0; i < Candidates.Count && made < PerTick; i++)
            {
                ZDO zdo = Candidates[i];
                Vector2s sector = zdo.GetSector();
                if (!zones.IsZoneLoaded(sector) || !zones.IsZoneReadyForType(sector, zdo.Type))
                {
                    continue;
                }

                if (scene.CreateObject(zdo) != null)
                {
                    made++;
                }
            }

            Candidates.Clear();
            if (made > 0)
            {
                CreatedAhead += made;
                MostWaitingAhead = Math.Max(MostWaitingAhead, ahead);
            }
        }

        private static int ByDistance(ZDO a, ZDO b)
        {
            return a.m_tempSortValue.CompareTo(b.m_tempSortValue);
        }
    }
}
