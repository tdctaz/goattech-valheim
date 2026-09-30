using UnityEngine;

namespace ServerAuthority
{
    internal static class PlayerLaunch
    {
        private const string Rpc = "ServerAuthority_PlayerLaunch";

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZPackage>(Rpc, RPC_PlayerLaunch);
        }

        internal static bool Forward(Character character, Vector3 velocity, bool effects)
        {
            if (!(character is Player player))
            {
                return false;
            }

            ZNetView view = player.m_nview;
            if (view != null && view.IsValid() && view.IsOwner())
            {
                return false;
            }

            string name = player.GetPlayerName();
            long peer = view != null && view.IsValid() ? view.GetZDO().GetOwner() : 0L;
            if (peer == 0L || ZNet.instance == null || ZNet.instance.GetPeer(peer) == null || ZRoutedRpc.instance == null)
            {
                Plugin.Log.LogWarning(
                    $"Launch of {(name.Length > 0 ? name : "a player being torn down")} at {velocity.magnitude:0.#} m/s " +
                    "dropped: their client is not connected.");
                return true;
            }

            ZPackage package = new ZPackage();
            package.Write(view.GetZDO().m_uid);
            package.Write(velocity);
            package.Write(effects);
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, Rpc, package);
            Plugin.Log.LogInfo($"Launch of {name} at {velocity.magnitude:0.#} m/s sent to their own client.");
            return true;
        }

        private static void RPC_PlayerLaunch(long sender, ZPackage package)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer() || znet.GetServerPeer()?.m_uid != sender)
            {
                return;
            }

            ZDOID target = package.ReadZDOID();
            Vector3 velocity = package.ReadVector3();
            bool effects = package.ReadBool();
            Player player = Player.m_localPlayer;
            if (player == null || player.GetZDOID() != target)
            {
                Plugin.Log.LogInfo($"Launch from the server for {target} ignored: that is not this client's character now.");
                return;
            }

            player.ForceJump(velocity, effects);
        }
    }
}
