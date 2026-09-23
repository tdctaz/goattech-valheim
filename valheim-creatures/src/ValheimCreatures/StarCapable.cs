using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class StarCapable
    {
        private static readonly HashSet<string> Prefabs = new HashSet<string>(StringComparer.Ordinal);
        private static ZNetScene _builtFor;
        private static int _listsSeen = -1;

        internal static bool Can(Character character)
        {
            if (character == null)
            {
                return false;
            }

            Build();
            return Prefabs.Contains(Utils.GetPrefabName(character.gameObject));
        }

        private static void Build()
        {
            ZNetScene scene = ZNetScene.instance;
            int lists = CountLists();
            if (scene == _builtFor && lists == _listsSeen)
            {
                return;
            }

            _builtFor = scene;
            _listsSeen = lists;
            Prefabs.Clear();

            foreach (SpawnSystem system in SpawnSystem.m_instances)
            {
                foreach (SpawnSystemList list in system.m_spawnLists)
                {
                    foreach (SpawnSystem.SpawnData data in list.m_spawners)
                    {
                        if (data.m_prefab != null && data.m_maxLevel > 1)
                        {
                            Prefabs.Add(data.m_prefab.name);
                        }
                    }
                }
            }

            if (scene == null)
            {
                return;
            }

            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                foreach (CreatureSpawner spawner in prefab.GetComponentsInChildren<CreatureSpawner>(true))
                {
                    if (spawner.m_creaturePrefab != null && spawner.m_maxLevel > 1)
                    {
                        Prefabs.Add(spawner.m_creaturePrefab.name);
                    }
                }

                foreach (SpawnArea area in prefab.GetComponentsInChildren<SpawnArea>(true))
                {
                    foreach (SpawnArea.SpawnData data in area.m_prefabs)
                    {
                        if (data.m_prefab != null && data.m_maxLevel > 1)
                        {
                            Prefabs.Add(data.m_prefab.name);
                        }
                    }
                }
            }
        }

        private static int CountLists()
        {
            int count = 0;
            foreach (SpawnSystem system in SpawnSystem.m_instances)
            {
                count += system.m_spawnLists.Count;
            }

            return Math.Min(count, 1);
        }
    }
}
