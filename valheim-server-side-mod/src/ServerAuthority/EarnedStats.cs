using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class EarnedStats
    {
        private const float TameMessageRange = 30f;

        private static readonly HashSet<PlayerStatType> Logged = new HashSet<PlayerStatType>();
        private static readonly List<int> MineRockHealthKeys = new List<int>();

        internal static void Reset()
        {
            Logged.Clear();
        }

        internal static void TakeOver(TreeBase tree)
        {
            RpcTakeover.Replace<HitData>(tree, tree.m_nview, "RPC_Damage", (sender, hit) => TreeDamage(tree, sender, hit));
        }

        internal static void TakeOver(TreeLog log)
        {
            RpcTakeover.Replace<HitData>(log, log.m_nview, "RPC_Damage", (sender, hit) => LogDamage(log, sender, hit));
        }

        internal static void TakeOver(MineRock rock)
        {
            RpcTakeover.Replace<HitData, int>(rock, rock.m_nview, "Hit", (sender, hit, area) => RockHit(rock, sender, hit, area));
        }

        internal static void TakeOver(MineRock5 rock)
        {
            RpcTakeover.Replace<HitData, int>(rock, rock.m_nview, "RPC_Damage", (sender, hit, area) => Rock5Damage(rock, sender, hit, area));
        }

        internal static void TakeOver(Character character)
        {
            if (character.IsPlayer())
            {
                return;
            }

            RpcTakeover.Replace<HitData>(character, character.m_nview, "RPC_Damage",
                (sender, hit) => CharacterDamage(character, sender, hit));
        }

        internal static void Tamed(Tameable tameable)
        {
            string name = Utils.GetPrefabName(tameable.gameObject);
            Vector3 position = tameable.transform.position;
            Player player = Player.GetClosestPlayer(position, TameMessageRange);
            string chosen = $"the closest player within {TameMessageRange:0} m";
            if (!Remote(player))
            {
                player = ClosestSimulating(position);
                chosen = "the closest player whose active area holds it";
            }

            if (!Remote(player))
            {
                Plugin.Log.LogInfo(
                    $"{name} was tamed with no player within {TameMessageRange:0} m or near enough to keep it simulated, " +
                    "so no player was credited.");
                return;
            }

            bool sent = StatCredit.Stat(player.m_nview.GetZDO().GetOwner(), PlayerStatType.CreatureTamed);
            Plugin.Log.LogInfo(sent
                ? $"{name} was tamed, credited to {player.GetPlayerName()}, {chosen}."
                : $"{name} was tamed near {player.GetPlayerName()}, {chosen}, but their client is not connected, so no credit was sent.");
        }

        private static Player ClosestSimulating(Vector3 position)
        {
            Player closest = null;
            float best = float.MaxValue;
            foreach (Player candidate in Player.GetAllPlayers())
            {
                if (!Remote(candidate) || !ZNetScene.InActiveArea(position, candidate.transform.position))
                {
                    continue;
                }

                float distance = Vector3.Distance(position, candidate.transform.position);
                if (distance < best)
                {
                    best = distance;
                    closest = candidate;
                }
            }

            return closest;
        }

        private static void TreeDamage(TreeBase tree, long sender, HitData hit)
        {
            Player player = Attacker(hit);
            ZNetView view = tree.m_nview;
            if (player == null || !view.IsValid() || !view.IsOwner())
            {
                tree.RPC_Damage(sender, hit);
                return;
            }

            float before = view.GetZDO().GetFloat(ZDOVars.s_health, tree.m_health);
            tree.RPC_Damage(sender, hit);
            if (before <= 0f)
            {
                return;
            }

            bool felled = !view.IsValid();
            if (!felled && view.GetZDO().GetFloat(ZDOVars.s_health, tree.m_health) >= before)
            {
                return;
            }

            Credit(player, PlayerStatType.TreeChops, false);
            if (felled)
            {
                Credit(player, PlayerStatType.Tree, false);
                CreditTier(player, PlayerStatType.TreeTier0, tree.m_minToolTier, false);
            }
        }

        private static void LogDamage(TreeLog log, long sender, HitData hit)
        {
            Player player = Attacker(hit);
            ZNetView view = log.m_nview;
            if (player == null || !view.IsValid() || !view.IsOwner())
            {
                log.RPC_Damage(sender, hit);
                return;
            }

            float before = view.GetZDO().GetFloat(ZDOVars.s_health);
            log.RPC_Damage(sender, hit);
            if (before <= 0f)
            {
                return;
            }

            bool destroyed = !view.IsValid();
            if (!destroyed && view.GetZDO().GetFloat(ZDOVars.s_health) >= before)
            {
                return;
            }

            Credit(player, PlayerStatType.LogChops, true);
            if (destroyed)
            {
                Credit(player, PlayerStatType.Logs, true);
            }
        }

        private static void RockHit(MineRock rock, long sender, HitData hit, int area)
        {
            Player player = Attacker(hit);
            ZNetView view = rock.m_nview;
            if (player == null || !view.IsValid() || !view.IsOwner() || rock.GetHitArea(area) == null)
            {
                rock.RPC_Hit(sender, hit, area);
                return;
            }

            int key = MineRockHealthKey(area);
            float before = view.GetZDO().GetFloat(key, rock.GetHealth());
            rock.RPC_Hit(sender, hit, area);
            if (before <= 0f)
            {
                return;
            }

            float after = view.IsValid() ? view.GetZDO().GetFloat(key, rock.GetHealth()) : 0f;
            if (after >= before)
            {
                return;
            }

            Credit(player, PlayerStatType.MineHits, true);
            if (after <= 0f)
            {
                Credit(player, PlayerStatType.Mines, true);
                CreditTier(player, PlayerStatType.MineTier0, rock.m_minToolTier, true);
            }
        }

        private static void Rock5Damage(MineRock5 rock, long sender, HitData hit, int area)
        {
            Player player = Attacker(hit);
            ZNetView view = rock.m_nview;
            if (player == null || !view.IsValid() || !view.IsOwner() || rock.GetHitArea(area) == null)
            {
                rock.RPC_Damage(sender, hit, area);
                return;
            }

            uint revision = view.GetZDO().DataRevision;
            rock.RPC_Damage(sender, hit, area);
            bool gone = !view.IsValid();
            if (!gone && view.GetZDO().DataRevision == revision)
            {
                return;
            }

            Credit(player, PlayerStatType.MineHits, true);
            if (gone || rock.GetHitArea(area).m_health <= 0f)
            {
                Credit(player, PlayerStatType.Mines, true);
                CreditTier(player, PlayerStatType.MineTier0, rock.m_minToolTier, true);
            }
        }

        private static void CharacterDamage(Character character, long sender, HitData hit)
        {
            Player player = Attacker(hit);
            ZNetView view = character.m_nview;
            bool owned = view.IsValid() && view.IsOwner();
            character.RPC_Damage(sender, hit);
            if (player != null && owned)
            {
                Credit(player, PlayerStatType.EnemyHits, false);
            }
        }

        private static Player Attacker(HitData hit)
        {
            if (!hit.HaveAttacker())
            {
                return null;
            }

            Player player = hit.GetAttacker() as Player;
            return Remote(player) ? player : null;
        }

        private static bool Remote(Player player)
        {
            if (player == null)
            {
                return false;
            }

            ZNetView view = player.m_nview;
            return view != null && view.IsValid() && !view.IsOwner();
        }

        private static void CreditTier(Player player, PlayerStatType tier0, int tier, bool tool)
        {
            if (tier >= 0 && tier <= 5)
            {
                Credit(player, tier0 + tier, tool);
            }
        }

        private static void Credit(Player player, PlayerStatType stat, bool tool)
        {
            long peer = player.m_nview.GetZDO().GetOwner();
            bool sent = tool ? StatCredit.ToolStat(peer, stat) : StatCredit.Stat(peer, stat);
            if (sent && Logged.Add(stat))
            {
                Plugin.Log.LogInfo(
                    $"{stat} earned by {player.GetPlayerName()} sent to their own profile. Later {stat} credits are not logged.");
            }
        }

        private static int MineRockHealthKey(int area)
        {
            while (MineRockHealthKeys.Count <= area)
            {
                MineRockHealthKeys.Add(("Health" + MineRockHealthKeys.Count).GetStableHashCode());
            }

            return MineRockHealthKeys[area];
        }
    }
}
