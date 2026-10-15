using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class PrefabKinds
    {
        private const long FreshTicks = 10 * TimeSpan.TicksPerSecond;

        private static HashSet<int> _ragdolls;
        private static HashSet<int> _items;

        internal static void Reset()
        {
            _ragdolls = null;
            _items = null;
        }

        internal static bool IsRagdoll(int prefab)
        {
            return Build() && _ragdolls.Contains(prefab);
        }

        internal static bool IsItem(int prefab)
        {
            return Build() && _items.Contains(prefab);
        }

        internal static bool IsFreshDrop(ZDO zdo)
        {
            long spawned = zdo.GetLong(ZDOVars.s_spawnTime);
            return spawned != 0 && ZNet.instance != null && ZNet.instance.GetTime().Ticks - spawned < FreshTicks;
        }

        private static bool Build()
        {
            if (_ragdolls != null)
            {
                return true;
            }

            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
            {
                return false;
            }

            var ragdolls = new HashSet<int>();
            var items = new HashSet<int>();
            foreach (KeyValuePair<int, GameObject> entry in scene.m_namedPrefabs)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                if (entry.Value.GetComponent<Ragdoll>() != null)
                {
                    ragdolls.Add(entry.Key);
                }
                else if (entry.Value.GetComponent<ItemDrop>() != null)
                {
                    items.Add(entry.Key);
                }
            }

            _items = items;
            _ragdolls = ragdolls;
            return true;
        }
    }
}
