using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class Waterborne
    {
        private static readonly HashSet<int> Prefabs = new HashSet<int>();
        private static bool _built;

        internal static void Reset()
        {
            Prefabs.Clear();
            _built = false;
        }

        internal static bool Is(int prefabHash)
        {
            Build();
            return Prefabs.Contains(prefabHash);
        }

        private static void Build()
        {
            if (_built)
            {
                return;
            }

            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
            {
                return;
            }

            _built = true;

            for (int i = 0; i < scene.m_prefabs.Count; i++)
            {
                GameObject prefab = scene.m_prefabs[i];
                if (prefab == null)
                {
                    continue;
                }

                if (prefab.GetComponentInChildren<Ship>(true) == null &&
                    prefab.GetComponentInChildren<Floating>(true) == null)
                {
                    continue;
                }

                Prefabs.Add(prefab.name.GetStableHashCode());
            }

            Plugin.Log.LogInfo(
                $"{Prefabs.Count} waterborne prefab(s) recognised, which ServerOwnsWaterborne " +
                "decides between the server and the nearest client.");
        }
    }
}
