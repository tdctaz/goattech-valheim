using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class Raiders
    {
        private const float CheckSeconds = 1f;
        private const float GraceSeconds = 15f;
        private const float PauseSeconds = 10f;
        private const float CalmSeconds = 90f;
        private const float EngageRange = 30f;
        private const float TargetRange = 60f;

        private static readonly int CenterKey = "ValheimCreatures_RaidCenter".GetStableHashCode();
        private static readonly Dictionary<Character, Raider> Known = new Dictionary<Character, Raider>();
        private static readonly List<Character> Scratch = new List<Character>();

        private sealed class Raider
        {
            internal Vector3 Center;
            internal float NextCheck;
            internal float ReturnUntil;
            internal float LastReturn;
            internal int Returns;
        }

        internal static void Reset()
        {
            Known.Clear();
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
                Known[character] = new Raider { Center = center, NextCheck = Time.time + GraceSeconds };
            }
        }

        internal static void Forget(Character character)
        {
            Known.Remove(character);
        }

        internal static void Enlist(ZDOID id, Vector3 center)
        {
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null)
            {
                return;
            }

            Mark(zdo, center);
            GameObject go = ZNetScene.instance.FindInstance(id);
            Character character = go != null ? go.GetComponent<Character>() : null;
            if (character != null)
            {
                Track(character, center);
            }
        }

        internal static void Enlist(Character character, Vector3 center)
        {
            ZDO zdo = character.m_nview.GetZDO();
            if (zdo == null)
            {
                return;
            }

            Mark(zdo, center);
            Track(character, center);
        }

        private static void Mark(ZDO zdo, Vector3 center)
        {
            zdo.Set(CenterKey, center);
            zdo.Set(ZDOVars.s_patrolPoint, center);
            zdo.Set(ZDOVars.s_patrol, true);
        }

        private static void Track(Character character, Vector3 center)
        {
            Known[character] = new Raider { Center = center, NextCheck = Time.time + GraceSeconds };
        }

        internal static bool TryCenter(Character character, out Vector3 center)
        {
            center = Vector3.zero;
            if (character == null || !Known.TryGetValue(character, out Raider raider))
            {
                return false;
            }

            center = raider.Center;
            return true;
        }

        internal static bool Enlisted(ZDO zdo)
        {
            return zdo.GetVec3(CenterKey, out _);
        }

        internal static bool Ignores(Character a, Character b)
        {
            if (Known.Count == 0)
            {
                return false;
            }

            bool raiderA = Known.ContainsKey(a) && !a.IsTamed();
            bool raiderB = Known.ContainsKey(b) && !b.IsTamed();
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

        internal static bool Returning(MonsterAI ai)
        {
            if (Known.Count == 0 || ai.m_character == null || !Known.TryGetValue(ai.m_character, out Raider raider) ||
                ai.m_nview == null || !ai.m_nview.IsValid() || !ai.m_nview.IsOwner() || ai.m_character.IsTamed() ||
                ai.IsSleeping() || ai.m_avoidLand || ai.m_serpentMovement)
            {
                return false;
            }

            Balance balance = ConfigSync.Current;
            float now = Time.time;
            Vector3 position = ai.transform.position;
            float distance = Utils.DistanceXZ(position, raider.Center);
            if (raider.ReturnUntil > 0f)
            {
                if (distance <= balance.RaidReturnDistance * 0.5f)
                {
                    EndReturn(ai, raider, now, $"is back, {distance:0} m from the raid centre");
                    return false;
                }

                if (Player.IsPlayerInRange(position, EngageRange))
                {
                    EndReturn(ai, raider, now, $"met a player {distance:0} m from the raid centre");
                    return false;
                }

                if (now >= raider.ReturnUntil)
                {
                    EndReturn(ai, raider, now, $"is still {distance:0} m from the raid centre after " +
                                               $"{balance.RaidReturnSeconds:0} s");
                    return false;
                }

                return true;
            }

            if (balance.RaidReturnSeconds <= 0f || now < raider.NextCheck)
            {
                return false;
            }

            raider.NextCheck = now + CheckSeconds;
            if (distance <= balance.RaidReturnDistance || Engaged(ai, position))
            {
                return false;
            }

            if (raider.Returns > 0 && now - raider.LastReturn > CalmSeconds)
            {
                raider.Returns = 0;
            }

            if (raider.Returns >= balance.RaidReturnTries)
            {
                Plugin.Log.LogInfo(
                    $"Raider {Name(ai.m_character)} has wandered {distance:0} m from the raid centre again after " +
                    $"{raider.Returns} trips back, so it leaves the raid as an ordinary monster.");
                Release(ai.m_character);
                return false;
            }

            raider.Returns++;
            raider.ReturnUntil = now + balance.RaidReturnSeconds;
            Plugin.Log.LogInfo(
                $"Raider {Name(ai.m_character)} is {distance:0} m from the raid centre at ({raider.Center.x:0}, " +
                $"{raider.Center.z:0}) and not fighting a player, so it heads back for " +
                $"{balance.RaidReturnSeconds:0} s ignoring everything else, trip {raider.Returns} of " +
                $"{balance.RaidReturnTries}.");
            return true;
        }

        private static bool Engaged(MonsterAI ai, Vector3 position)
        {
            if (Player.IsPlayerInRange(position, EngageRange))
            {
                return true;
            }

            Character target = ai.m_targetCreature;
            return target != null && target.IsPlayer() &&
                   Vector3.Distance(target.transform.position, position) < TargetRange;
        }

        private static void EndReturn(MonsterAI ai, Raider raider, float now, string what)
        {
            raider.ReturnUntil = 0f;
            raider.LastReturn = now;
            raider.NextCheck = now + PauseSeconds;
            ai.m_updateTargetTimer = 0f;
            Plugin.Log.LogInfo($"Raider {Name(ai.m_character)} {what}.");
        }

        internal static void Return(MonsterAI ai, float dt)
        {
            if (!Known.TryGetValue(ai.m_character, out Raider raider))
            {
                return;
            }

            ai.m_targetCreature = null;
            ai.m_targetStatic = null;
            ai.SetTargetInfo(ZDOID.None);
            ai.ChargeStop();
            if (!ai.m_character.InAttack())
            {
                ai.MoveTo(dt, raider.Center, 0f, true);
            }
        }

        internal static void Sweep(Func<Vector3, bool> running)
        {
            Scratch.Clear();
            Scratch.AddRange(Known.Keys);
            int released = 0;
            foreach (Character character in Scratch)
            {
                if (character == null)
                {
                    Known.Remove(character);
                    continue;
                }

                if (character.m_nview == null || !character.m_nview.IsValid() || !character.m_nview.IsOwner() ||
                    running(Known[character].Center))
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
            Known.Remove(character);
            if (character.m_nview == null || !character.m_nview.IsValid() || !character.m_nview.IsOwner())
            {
                return;
            }

            character.m_nview.GetZDO().RemoveVec3(CenterKey);
            BaseAI ai = character.GetBaseAI();
            if (ai != null)
            {
                ai.SetHuntPlayer(false);
                if (!character.IsTamed())
                {
                    ai.ResetPatrolPoint();
                }
            }
        }

        private static string Name(Character character)
        {
            Vector3 position = character.transform.position;
            return $"{Utils.GetPrefabName(character.gameObject)} at ({position.x:0}, {position.z:0})";
        }
    }
}
