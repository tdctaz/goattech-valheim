using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class SummonSkill
    {
        private static readonly HashSet<int> Forwarded = new HashSet<int>();
        private static readonly HashSet<int> Dropped = new HashSet<int>();

        internal static void Reset()
        {
            Forwarded.Clear();
            Dropped.Clear();
        }

        internal static bool Forward(Character creature, float value)
        {
            if (!creature.IsTamed())
            {
                return false;
            }

            if (!creature.m_tameable)
            {
                Tameable found = creature.GetComponent<Tameable>();
                MonsterAI foundAi = creature.GetComponent<MonsterAI>();
                if (!found || !foundAi)
                {
                    return false;
                }

                creature.m_tameable = found;
                creature.m_tameableMonsterAI = foundAi;
            }

            Tameable tameable = creature.m_tameable;
            MonsterAI ai = creature.m_tameableMonsterAI;
            if (!ai || tameable.m_levelUpOwnerSkill == Skills.SkillType.None)
            {
                return false;
            }

            GameObject target = ai.GetFollowTarget();
            Player player = target != null ? target.GetComponent<Player>() : null;
            if (player == null)
            {
                return false;
            }

            ZNetView view = player.m_nview;
            bool valid = view != null && view.IsValid();
            if (valid && view.IsOwner())
            {
                return false;
            }

            int prefab = creature.m_nview.GetZDO().GetPrefab();
            long peer = valid ? view.GetZDO().GetOwner() : 0L;
            if (peer == 0L)
            {
                if (Dropped.Add(prefab))
                {
                    string name = Utils.GetPrefabName(creature.gameObject);
                    string who = valid ? $"character {view.GetZDO().m_uid}" : "a character being torn down";
                    Plugin.Log.LogWarning(
                        $"Skill gain from tamed {name} dropped, since its follow target, {who}, has no owning peer. " +
                        $"Later drops from {name} are not logged.");
                }

                return true;
            }

            StatCredit.Skill(peer, tameable.m_levelUpOwnerSkill, value * tameable.m_levelUpFactor);
            if (Forwarded.Add(prefab))
            {
                string name = Utils.GetPrefabName(creature.gameObject);
                Plugin.Log.LogInfo(
                    $"Skill gain {tameable.m_levelUpOwnerSkill} from tamed {name} forwarded to {player.GetPlayerName()}'s own client. " +
                    $"Later gains from {name} are not logged.");
            }

            return true;
        }
    }
}
