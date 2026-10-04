using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class Raiders
    {
        private static readonly int CenterKey = "ValheimCreatures_RaidCenter".GetStableHashCode();
        private static readonly Dictionary<Character, Vector3> Centers = new Dictionary<Character, Vector3>();
        private static readonly List<Character> Scratch = new List<Character>();

        internal static void Reset()
        {
            Centers.Clear();
            Scratch.Clear();
        }

        internal static void Register(Character character)
        {
            if (character.IsPlayer() || !RaidDirector.RunsHere())
            {
                return;
            }

            ZDO zdo = character.m_nview != null ? character.m_nview.GetZDO() : null;
            if (zdo != null && zdo.GetVec3(CenterKey, out Vector3 center))
            {
                Centers[character] = center;
            }
        }

        internal static void Forget(Character character)
        {
            Centers.Remove(character);
        }

        internal static void Enlist(ZDOID id, Vector3 center)
        {
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null)
            {
                return;
            }

            zdo.Set(CenterKey, center);
            GameObject go = ZNetScene.instance.FindInstance(id);
            Character character = go != null ? go.GetComponent<Character>() : null;
            if (character != null)
            {
                Centers[character] = center;
            }
        }

        internal static void Enlist(Character character, Vector3 center)
        {
            ZDO zdo = character.m_nview.GetZDO();
            if (zdo == null)
            {
                return;
            }

            zdo.Set(CenterKey, center);
            Centers[character] = center;
        }

        internal static bool TryCenter(Character character, out Vector3 center)
        {
            center = Vector3.zero;
            return character != null && Centers.TryGetValue(character, out center);
        }

        internal static bool Enlisted(ZDO zdo)
        {
            return zdo.GetVec3(CenterKey, out _);
        }

        internal static bool Ignores(Character a, Character b)
        {
            if (Centers.Count == 0)
            {
                return false;
            }

            bool raiderA = Centers.ContainsKey(a) && !a.IsTamed();
            bool raiderB = Centers.ContainsKey(b) && !b.IsTamed();
            if (raiderA == raiderB)
            {
                return raiderA;
            }

            return Wild(raiderA ? b : a);
        }

        private static bool Wild(Character character)
        {
            if (character.IsPlayer() || character.IsTamed())
            {
                return false;
            }

            Character.Faction faction = character.GetFaction();
            return faction != Character.Faction.PlayerSpawned && faction != Character.Faction.TrainingDummy;
        }

        internal static void Sweep(Func<Vector3, bool> running)
        {
            Scratch.Clear();
            Scratch.AddRange(Centers.Keys);
            int released = 0;
            foreach (Character character in Scratch)
            {
                if (character == null)
                {
                    Centers.Remove(character);
                    continue;
                }

                if (character.m_nview == null || !character.m_nview.IsValid() || !character.m_nview.IsOwner() ||
                    running(Centers[character]))
                {
                    continue;
                }

                Release(character);
                released++;
            }

            Scratch.Clear();
            if (released > 0)
            {
                Plugin.Log.LogInfo(
                    $"Released {released} raiders whose raid is no longer running; they stay as ordinary monsters.");
            }
        }

        internal static void Release(Character character)
        {
            Centers.Remove(character);
            if (character.m_nview == null || !character.m_nview.IsValid() || !character.m_nview.IsOwner())
            {
                return;
            }

            character.m_nview.GetZDO().RemoveVec3(CenterKey);
            character.GetBaseAI()?.SetHuntPlayer(false);
        }
    }
}
