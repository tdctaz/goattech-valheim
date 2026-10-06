using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class WaveSpawner
    {
        private const int PlacementTries = 40;
        private const int MemberTries = 4;
        private const float FarthestReach = 2f;

        private static readonly List<string> Dropped = new List<string>();
        private static readonly List<string> Placed = new List<string>();

        private static int _inBase;
        private static int _covered;
        private static int _water;
        private static int _noFloor;

        internal static List<ZDOID> Spawn(RandomEvent raid, Func<SpawnSystem.SpawnData, Vector3?> groupCenter,
            string source, Character tracker, List<string> names, bool mostAlive)
        {
            Begin();
            List<ZDOID> spawned = new List<ZDOID>();
            foreach (SpawnSystem.SpawnData data in raid.m_spawn)
            {
                if (!Usable(data))
                {
                    continue;
                }

                if (mostAlive)
                {
                    Fill(data, MostAlive(data), groupCenter, source, tracker, names, spawned);
                }
                else
                {
                    Group(data, GroupSize(data), groupCenter, source, tracker, names, spawned);
                }
            }

            return spawned;
        }

        internal static int MostAlive(SpawnSystem.SpawnData data)
        {
            return data.m_maxSpawned > 0 ? data.m_maxSpawned : GroupSize(data);
        }

        private static int GroupSize(SpawnSystem.SpawnData data)
        {
            return UnityEngine.Random.Range(data.m_groupSizeMin, data.m_groupSizeMax + 1);
        }

        internal static void Fill(SpawnSystem.SpawnData data, int count,
            Func<SpawnSystem.SpawnData, Vector3?> groupCenter, string source, Character tracker, List<string> names,
            List<ZDOID> spawned)
        {
            while (count > 0)
            {
                int group = Mathf.Clamp(GroupSize(data), 1, count);
                if (!Group(data, group, groupCenter, source, tracker, names, spawned))
                {
                    return;
                }

                count -= group;
            }
        }

        internal static bool Usable(SpawnSystem.SpawnData data)
        {
            return data != null && data.m_enabled && data.m_prefab != null;
        }

        internal static void Begin()
        {
            _inBase = 0;
            _covered = 0;
            _water = 0;
            _noFloor = 0;
            Dropped.Clear();
            Placed.Clear();
        }

        internal static bool Group(SpawnSystem.SpawnData data, int count,
            Func<SpawnSystem.SpawnData, Vector3?> groupCenter, string source, Character tracker, List<string> names,
            List<ZDOID> spawned)
        {
            Vector3? center = count > 0 ? groupCenter(data) : null;
            if (center == null)
            {
                if (count > 0)
                {
                    Dropped.Add(data.m_prefab.name);
                }

                return false;
            }

            Placed.Add(Describe(data.m_prefab.name, count, center.Value));
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

            return true;
        }

        internal static string Rejected()
        {
            string text = Dropped.Count > 0
                ? $" No open ground for {string.Join(", ", Dropped)}, left out of this wave."
                : "";
            if (_inBase + _covered + _water + _noFloor > 0)
            {
                text += $" Placement passed over {_inBase} points inside a player base, {_covered} under cover, " +
                        $"{_water} in water and {_noFloor} with no floor.";
            }

            return text;
        }

        internal static string Placements()
        {
            return Placed.Count > 0 ? $" Placed {string.Join("; ", Placed)}." : "";
        }

        private static string Describe(string name, int count, Vector3 point)
        {
            float player = float.MaxValue;
            foreach (Player each in Player.GetAllPlayers())
            {
                if (each != null)
                {
                    player = Mathf.Min(player, Utils.DistanceXZ(each.transform.position, point));
                }
            }

            float edge = float.MaxValue;
            string piece = "";
            foreach (EffectArea area in EffectArea.GetAllAreas())
            {
                if (area == null || (area.m_type & EffectArea.Type.PlayerBase) == 0)
                {
                    continue;
                }

                float distance = Utils.DistanceXZ(area.transform.position, point) - area.GetRadius();
                if (distance < edge)
                {
                    edge = distance;
                    piece = Utils.GetPrefabName(area.transform.root.gameObject);
                }
            }

            string text = $"{name} x{count} at ({point.x:0}, {point.z:0})";
            text += player < float.MaxValue ? $", {player:0} m from the nearest player" : ", no player loaded";
            if (edge == float.MaxValue)
            {
                return text + ", no base area loaded";
            }

            return text + (edge >= 0f
                ? $", {edge:0} m outside the nearest base area ({piece})"
                : $", {-edge:0} m inside the base area of {piece}");
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
                    _noFloor++;
                    continue;
                }

                if (height < ZoneSystem.instance.m_waterLevel + 0.5f)
                {
                    _water++;
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
                    _noFloor++;
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
