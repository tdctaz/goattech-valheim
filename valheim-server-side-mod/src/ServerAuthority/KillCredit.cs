using System.Collections.Generic;

namespace ServerAuthority
{
    internal static class KillCredit
    {
        private const string Rpc = "ServerAuthority_KillCredit";

        private sealed class Credit
        {
            internal string Name;
            internal bool Key;
            internal bool LastHit;
        }

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZPackage>(Rpc, RPC_KillCredit);
        }

        internal static void Deliver(Character character)
        {
            ZNetView view = character.m_nview;
            if (view == null || !view.IsValid() || !view.IsOwner() || ZNet.instance == null ||
                ZRoutedRpc.instance == null)
            {
                return;
            }

            ZDO zdo = view.GetZDO();
            string key = character.m_defeatSetGlobalKey ?? "";
            Dictionary<long, Credit> credits = new Dictionary<long, Credit>();
            if (key.Length > 0)
            {
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                {
                    if (peer.IsReady() && zdo.GetBool(ZDOVars.s_attackers + peer.m_playerName))
                    {
                        credits[peer.m_uid] = new Credit { Name = peer.m_playerName, Key = true };
                    }
                }
            }

            Player killer = character.m_lastHit?.GetAttacker() as Player;
            ZDO killerZdo = killer != null && killer.m_nview != null ? killer.m_nview.GetZDO() : null;
            long killerPeer = killerZdo != null ? killerZdo.GetOwner() : 0L;
            if (killerPeer != 0L)
            {
                if (!credits.TryGetValue(killerPeer, out Credit credit))
                {
                    credit = new Credit { Name = killer.GetPlayerName() };
                    credits[killerPeer] = credit;
                }

                credit.LastHit = true;
            }

            bool boss = character.IsBoss();
            bool cheated = zdo.GetBool(ZDOVars.s_cheated);
            List<string> keyed = new List<string>();
            foreach (KeyValuePair<long, Credit> entry in credits)
            {
                ZPackage package = new ZPackage();
                package.Write(entry.Value.Key ? key : "");
                package.Write(entry.Value.LastHit);
                package.Write(boss);
                package.Write(cheated);
                ZRoutedRpc.instance.InvokeRoutedRPC(entry.Key, Rpc, package);
                if (entry.Value.Key)
                {
                    keyed.Add(entry.Value.Name);
                }
            }

            if (key.Length == 0)
            {
                return;
            }

            string name = Utils.GetPrefabName(character.gameObject);
            Plugin.Log.LogInfo(keyed.Count > 0
                ? $"Kill key '{key}' from {name} sent to {string.Join(", ", keyed)}."
                : $"Kill key '{key}' from {name} went to no player, since no connected player hit it.");
        }

        private static void RPC_KillCredit(long sender, ZPackage package)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer() || znet.GetServerPeer()?.m_uid != sender)
            {
                return;
            }

            string key = package.ReadString();
            bool lastHit = package.ReadBool();
            bool boss = package.ReadBool();
            bool cheated = package.ReadBool();

            if (lastHit && Game.instance != null)
            {
                Game.instance.GetPlayerProfile().IncrementStat(
                    boss ? PlayerStatType.BossLastHits : PlayerStatType.EnemyKillsLastHits, 1f, cheated);
            }

            if (key.Length == 0)
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
