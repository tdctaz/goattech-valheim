using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class WaveSpawner
    {
        private const int PlacementTries = 20;
        private const int MemberTries = 4;
        private const float FarthestReach = 2f;

        private static int _inBase;
        private static int _covered;

        internal static List<ZDOID> Spawn(RandomEvent raid, Func<SpawnSystem.SpawnData, Vector3?> groupCenter,
            string source, Character tracker, List<string> names)
        {
            _inBase = 0;
            _covered = 0;
            List<ZDOID> spawned = new List<ZDOID>();
            foreach (SpawnSystem.SpawnData data in raid.m_spawn)
            {
                if (data == null || !data.m_enabled || data.m_prefab == null)
                {
                    continue;
                }

                int count = UnityEngine.Random.Range(data.m_groupSizeMin, data.m_groupSizeMax + 1);
                Vector3? center = count > 0 ? groupCenter(data) : null;
                if (center == null)
                {
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    Vector3 position = count > 1 ? Member(center.Value, data) : center.Value;
                    position.y += data.m_groundOffset;

                    GameObject go = UnityEngine.Object.Instantiate(data.m_prefab, position, Quaternion.identity);
                    names?.Add(data.m_prefab.name);
                    ZNetView view = go.GetComponent<ZNetView>();
                    if (view != null && view.GetZDO() != null)
                    {
                        spawned.Add(view.GetZDO().m_uid);
                    }

                    Character character = go.GetComponent<Character>();
                    if (character == null)
                    {
                        continue;
                    }

                    character.GetBaseAI()?.SetHuntPlayer(true);

                    bool eligible = data.m_maxLevel > 1 || StarCapable.Can(character);
                    bool protectedCenter = data.m_levelUpMinCenterDistance > 0f &&
                                           position.magnitude <= data.m_levelUpMinCenterDistance &&
                                           !BossProgress.CenterProtectionLifted();
                    if (eligible && !protectedCenter)
                    {
                        LevelRoll.Roll(character, data.m_minLevel, data.m_overrideLevelupChance, position, source);
                    }

#if DEBUG_TOOLS
                    if (tracker != null && TestCommands.IsTracked(tracker))
                    {
                        TestCommands.Track(character);
                    }
#endif
                }
            }

            return spawned;
        }

        internal static string Rejected()
        {
            return _inBase == 0 && _covered == 0
                ? ""
                : $" Placement passed over {_inBase} points inside a player base and {_covered} under cover.";
        }

        internal static Vector3? Around(Vector3 origin, float distance, SpawnSystem.SpawnData data)
        {
            for (int i = 0; i < PlacementTries; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float outward = Mathf.Lerp(1f, FarthestReach, (float)i / (PlacementTries - 1));
                float reach = distance * UnityEngine.Random.Range(0.8f, 1.2f) * outward;
                Vector3 point = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * reach;
                if (!ZoneSystem.instance.FindFloor(point + Vector3.up * 50f, out float height))
                {
                    continue;
                }

                if (height < ZoneSystem.instance.m_waterLevel + 0.5f)
                {
                    continue;
                }

                point.y = height;
                if (Open(point, data))
                {
                    return point;
                }
            }

            return null;
        }

        private static Vector3 Member(Vector3 center, SpawnSystem.SpawnData data)
        {
            for (int i = 0; i < MemberTries; i++)
            {
                Vector2 jitter = UnityEngine.Random.insideUnitCircle * data.m_groupRadius;
                Vector3 point = center + new Vector3(jitter.x, 0f, jitter.y);
                if (!ZoneSystem.instance.FindFloor(point + Vector3.up * 50f, out float height))
                {
                    continue;
                }

                point.y = height;
                if (Open(point, data))
                {
                    return point;
                }
            }

            return center;
        }

        private static bool Open(Vector3 point, SpawnSystem.SpawnData data)
        {
            if (ZoneSystem.instance.IsBlocked(point))
            {
                _covered++;
                return false;
            }

            if (!data.m_insidePlayerBase && EffectArea.IsPointInsideArea(point, EffectArea.Type.PlayerBase) != null)
            {
                _inBase++;
                return false;
            }

            return true;
        }
    }
}
