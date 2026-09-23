using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class WaveSpawner
    {
        private const int PlacementTries = 10;

        internal static List<ZDOID> Spawn(RandomEvent raid, Func<Vector3?> groupCenter, string source, Character tracker,
            List<string> names)
        {
            List<ZDOID> spawned = new List<ZDOID>();
            foreach (SpawnSystem.SpawnData data in raid.m_spawn)
            {
                if (data == null || !data.m_enabled || data.m_prefab == null)
                {
                    continue;
                }

                int count = UnityEngine.Random.Range(data.m_groupSizeMin, data.m_groupSizeMax + 1);
                Vector3? center = count > 0 ? groupCenter() : null;
                if (center == null)
                {
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    Vector2 jitter = count > 1 ? UnityEngine.Random.insideUnitCircle * data.m_groupRadius : Vector2.zero;
                    Vector3 position = center.Value + new Vector3(jitter.x, 0f, jitter.y);
                    if (ZoneSystem.instance.FindFloor(position + Vector3.up * 50f, out float height))
                    {
                        position.y = height + data.m_groundOffset;
                    }

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

        internal static Vector3? Around(Vector3 origin, float distance)
        {
            for (int i = 0; i < PlacementTries; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float reach = distance * UnityEngine.Random.Range(0.8f, 1.2f);
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
                return point;
            }

            return null;
        }
    }
}
