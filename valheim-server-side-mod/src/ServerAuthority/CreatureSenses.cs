using UnityEngine;

namespace ServerAuthority
{
    internal static class CreatureSenses
    {
        private static bool _loggedGhost;
        private static bool _loggedFly;
        private static bool _loggedCinematic;

        internal static void Reset()
        {
            _loggedGhost = false;
            _loggedFly = false;
            _loggedCinematic = false;
        }

        internal static bool Ghost(Player player)
        {
            if (!PlayerFlags.Has(player, PlayerFlags.Ghost))
            {
                return false;
            }

            if (!_loggedGhost)
            {
                _loggedGhost = true;
                Plugin.Log.LogInfo(
                    $"The server honoured {player.GetPlayerName()}'s ghost mode, as published by their client. Later cases are not logged.");
            }

            return true;
        }

        internal static bool HiddenFlying(Character target)
        {
            if (!(target is Player player) || !player.IsDebugFlying())
            {
                return false;
            }

            if (!_loggedFly)
            {
                _loggedFly = true;
                Plugin.Log.LogInfo($"Creatures ignore {player.GetPlayerName()}, who is debug flying. Later cases are not logged.");
            }

            return true;
        }

        internal static Character FindEnemy(BaseAI ai)
        {
            Character best = null;
            float bestDistance = 99999f;
            foreach (Character item in Character.GetAllCharacters())
            {
                if (!BaseAI.IsEnemy(ai.m_character, item) || item.IsDead() || InCinematic(item) || item.m_aiSkipTarget)
                {
                    continue;
                }

                BaseAI other = item.GetBaseAI();
                if ((other == null || !other.IsSleeping()) && ai.CanSenseTarget(item))
                {
                    float distance = Vector3.Distance(item.transform.position, ai.transform.position);
                    if (distance < bestDistance || best == null)
                    {
                        best = item;
                        bestDistance = distance;
                    }
                }
            }

            if (best == null && ai.HuntPlayer())
            {
                Player closest = Player.GetClosestPlayer(ai.transform.position, 200f);
                if ((bool)closest && (closest.IsDebugFlying() || closest.InGhostMode()))
                {
                    return null;
                }

                return closest;
            }

            return best;
        }

        private static bool InCinematic(Character item)
        {
            if (!(item is Player player))
            {
                return false;
            }

            if (player == Player.m_localPlayer)
            {
                return CinematicsManager.IsPlaying();
            }

            if (!PlayerFlags.Has(player, PlayerFlags.Cinematic))
            {
                return false;
            }

            if (!_loggedCinematic)
            {
                _loggedCinematic = true;
                Plugin.Log.LogInfo(
                    $"Creatures leave {player.GetPlayerName()} alone while a cinematic plays for them. Later cases are not logged.");
            }

            return true;
        }
    }
}
