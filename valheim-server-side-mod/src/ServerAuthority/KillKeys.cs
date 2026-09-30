using System.Collections.Generic;

namespace ServerAuthority
{
    internal static class KillKeys
    {
        private const string Rpc = "ServerAuthority_KillKey";

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<string>(Rpc, RPC_KillKey);
        }

        internal static void Deliver(Character character)
        {
            string key = character.m_defeatSetGlobalKey;
            ZNetView view = character.m_nview;
            if (string.IsNullOrEmpty(key) || view == null || !view.IsValid() || !view.IsOwner() ||
                ZNet.instance == null || ZRoutedRpc.instance == null)
            {
                return;
            }

            ZDO zdo = view.GetZDO();
            List<string> given = new List<string>();
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (!peer.IsReady() || !zdo.GetBool(ZDOVars.s_attackers + peer.m_playerName))
                {
                    continue;
                }

                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, Rpc, key);
                given.Add(peer.m_playerName);
            }

            string name = Utils.GetPrefabName(character.gameObject);
            Plugin.Log.LogInfo(given.Count > 0
                ? $"Kill key '{key}' from {name} sent to {string.Join(", ", given)}."
                : $"Kill key '{key}' from {name} went to no player, since no connected player hit it.");
        }

        private static void RPC_KillKey(long sender, string key)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer() || string.IsNullOrEmpty(key) || znet.GetServerPeer()?.m_uid != sender)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                Player.m_addUniqueKeyQueue.Add(key);
                Plugin.Log.LogInfo($"Kill key '{key}' from the server is held until the character spawns.");
                return;
            }

            bool had = player.HaveUniqueKey(key);
            player.AddUniqueKey(key);
            Plugin.Log.LogInfo($"Kill key '{key}' from the server {(had ? "was already held" : "added")}.");
        }
    }
}
