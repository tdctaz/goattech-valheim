using System.Collections.Generic;

namespace ServerAuthority
{
    internal static class WardAccess
    {
        private static readonly HashSet<ZDOID> Allowed = new HashSet<ZDOID>();
        private static readonly HashSet<ZDOID> Denied = new HashSet<ZDOID>();

        internal static void Reset()
        {
            Allowed.Clear();
            Denied.Clear();
        }

        internal static bool Decide(PrivateArea ward)
        {
            long creator = ward.m_piece != null ? ward.m_piece.GetCreator() : 0L;
            foreach (Player player in Player.GetAllPlayers())
            {
                ZNetView view = player.m_nview;
                if (view == null || !view.IsValid() ||
                    !ZNetScene.InActiveArea(ward.transform.position, player.transform.position))
                {
                    continue;
                }

                long id = player.GetPlayerID();
                if (id != 0L && (id == creator || ward.IsPermitted(id)))
                {
                    LogFirst(ward, Allowed, $"allowed, since {player.GetPlayerName()} is nearby and has access");
                    return true;
                }
            }

            LogFirst(ward, Denied, "denied, since no player near it has access");
            return false;
        }

        private static void LogFirst(PrivateArea ward, HashSet<ZDOID> logged, string outcome)
        {
            ZNetView view = ward.m_nview;
            if (view == null || !view.IsValid() || !logged.Add(view.GetZDO().m_uid))
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"Ward {view.GetZDO().m_uid} of player {(ward.m_piece != null ? ward.m_piece.GetCreator() : 0L)} at " +
                $"{ward.transform.position}: a server-side access check was {outcome}. Later answers the same way for " +
                "this ward are not logged.");
        }
    }
}
