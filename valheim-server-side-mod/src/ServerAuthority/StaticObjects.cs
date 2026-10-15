using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class StaticObjects
    {
        private static readonly HashSet<Type> Passive = new HashSet<Type>
        {
            typeof(ZNetView),
            typeof(Piece),
            typeof(WearNTear),
            typeof(TerrainComp),
            typeof(TreeBase),
            typeof(Destructible),
            typeof(MineRock),
            typeof(MaterialVariation)
        };

        private static readonly Dictionary<int, bool> Kinds = new Dictionary<int, bool>();
        private static readonly List<string> Released = new List<string>();
        private static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>();

        internal static void Reset()
        {
            Kinds.Clear();
            Released.Clear();
            Behaviours.Clear();
        }

        internal static bool StaysOwned(ZDO zdo)
        {
            if (!ModConfig.KeepStaticObjectsOwned.Value ||
                (zdo.Type != ZDO.ObjectType.Solid && zdo.Type != ZDO.ObjectType.Terrain))
            {
                return false;
            }

            int prefab = zdo.GetPrefab();
            if (!Kinds.TryGetValue(prefab, out bool passive))
            {
                passive = Classify(prefab);
                Kinds[prefab] = passive;
            }

            return passive;
        }

        internal static string TakeReleased()
        {
            if (Released.Count == 0)
            {
                return null;
            }

            string text = string.Join(", ", Released);
            Released.Clear();
            return text;
        }

        private static bool Classify(int hash)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
            if (prefab == null)
            {
                return false;
            }

            string reason = null;
            if (prefab.GetComponentInChildren<Rigidbody>(true) != null)
            {
                reason = "Rigidbody";
            }
            else
            {
                Behaviours.Clear();
                prefab.GetComponentsInChildren(true, Behaviours);
                for (int i = 0; i < Behaviours.Count && reason == null; i++)
                {
                    MonoBehaviour behaviour = Behaviours[i];
                    if (behaviour == null)
                    {
                        continue;
                    }

                    if (!Passive.Contains(behaviour.GetType()))
                    {
                        reason = behaviour.GetType().Name;
                    }
                    else if (behaviour is Destructible destructible && destructible.m_ttl > 0f)
                    {
                        reason = "Destructible with a timer";
                    }
                }

                Behaviours.Clear();
            }

            if (reason != null)
            {
                Released.Add($"{prefab.name} ({reason})");
            }

            return reason == null;
        }
    }
}
