using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class ConfigSync
    {
        private const string HelloRpc = "ValheimCreatures_Hello";
        private const string BalanceRpc = "ValheimCreatures_Balance";
        private const string TrophiesRpc = "ValheimCreatures_Trophies";
        private const float HelloTimeoutSeconds = 15f;
        private const float DisconnectDelaySeconds = 2f;

        private static readonly Dictionary<ZNetPeer, float> AwaitingHello = new Dictionary<ZNetPeer, float>();
        private static readonly Dictionary<ZNetPeer, float> Rejected = new Dictionary<ZNetPeer, float>();
        private static readonly List<ZNetPeer> Scratch = new List<ZNetPeer>();

        private static Balance _local;
        private static Balance _fromServer;

        internal static Balance Current
        {
            get
            {
                ZNet znet = ZNet.instance;
                if (znet != null && !znet.IsServer())
                {
                    return _fromServer ?? Balance.Vanilla;
                }

                return _local ?? (_local = Balance.FromConfig());
            }
        }

        internal static void Reset()
        {
            AwaitingHello.Clear();
            Rejected.Clear();
            _fromServer = null;
            BossProgress.Reset();
#if DEBUG_TOOLS
            TestCommands.Reset();
#endif
            BossWaves.Reset();
            RaidDirector.Reset();
            Applied();
        }

        internal static void BroadcastTrophies()
        {
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer())
            {
                return;
            }

            foreach (ZNetPeer peer in znet.GetPeers())
            {
                if (!AwaitingHello.ContainsKey(peer) && !Rejected.ContainsKey(peer))
                {
                    SendTrophies(peer);
                }
            }
        }

        internal static void LocalConfigChanged()
        {
            _local = null;
            Applied();

            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer())
            {
                return;
            }

            foreach (ZNetPeer peer in znet.GetPeers())
            {
                if (!AwaitingHello.ContainsKey(peer) && !Rejected.ContainsKey(peer))
                {
                    Send(peer);
                }
            }
        }

        internal static void OnNewConnection(ZNet znet, ZNetPeer peer)
        {
            if (znet.IsServer())
            {
                peer.m_rpc.Register<string>(HelloRpc, (rpc, version) => OnHello(peer, version));
                AwaitingHello[peer] = Time.time;
                return;
            }

            peer.m_rpc.Register<ZPackage>(BalanceRpc, (rpc, package) => OnBalance(package));
            peer.m_rpc.Register<ZPackage>(TrophiesRpc, (rpc, package) => BossProgress.ReadPlacedTrophies(package));
            peer.m_rpc.Invoke(HelloRpc, Plugin.Version);
        }

        internal static void Update(ZNet znet)
        {
            if (!znet.IsServer() || (AwaitingHello.Count == 0 && Rejected.Count == 0))
            {
                return;
            }

            List<ZNetPeer> peers = znet.GetPeers();
            float now = Time.time;

            Scratch.Clear();
            foreach (KeyValuePair<ZNetPeer, float> entry in AwaitingHello)
            {
                if (!peers.Contains(entry.Key) || !ModConfig.RequireClientMod.Value)
                {
                    Scratch.Add(entry.Key);
                }
                else if (now - entry.Value > HelloTimeoutSeconds)
                {
                    Scratch.Add(entry.Key);
                    Reject(entry.Key, "does not have Valheim Creatures installed");
                }
            }

            foreach (ZNetPeer peer in Scratch)
            {
                AwaitingHello.Remove(peer);
            }

            Scratch.Clear();
            foreach (KeyValuePair<ZNetPeer, float> entry in Rejected)
            {
                if (!peers.Contains(entry.Key))
                {
                    Scratch.Add(entry.Key);
                }
                else if (now - entry.Value > DisconnectDelaySeconds)
                {
                    Scratch.Add(entry.Key);
                    znet.Disconnect(entry.Key);
                }
            }

            foreach (ZNetPeer peer in Scratch)
            {
                Rejected.Remove(peer);
            }
        }

        private static void OnHello(ZNetPeer peer, string version)
        {
            AwaitingHello.Remove(peer);

            if (version != Plugin.Version)
            {
                if (ModConfig.RequireClientMod.Value)
                {
                    Reject(peer, $"has Valheim Creatures {version}, the server runs {Plugin.Version}");
                    return;
                }

                Plugin.Log.LogWarning(
                    $"{Describe(peer)} has Valheim Creatures {version}, the server runs {Plugin.Version}. " +
                    "Allowed because RequireClientMod is off.");
            }
            else
            {
                Plugin.Log.LogInfo($"{Describe(peer)} connected with Valheim Creatures {version}.");
            }

            Send(peer);
            SendTrophies(peer);
        }

        private static void SendTrophies(ZNetPeer peer)
        {
            ZPackage package = new ZPackage();
            BossProgress.WritePlacedTrophies(package);
            peer.m_rpc.Invoke(TrophiesRpc, package);
        }

        private static void Send(ZNetPeer peer)
        {
            ZPackage package = new ZPackage();
            Current.Write(package);
            peer.m_rpc.Invoke(BalanceRpc, package);
        }

        private static void OnBalance(ZPackage package)
        {
            _fromServer = Balance.Read(package);
            Plugin.Log.LogInfo($"Using the server's creature settings: {_fromServer}.");
            Applied();
        }

        private static void Reject(ZNetPeer peer, string reason)
        {
            Plugin.Log.LogWarning($"Disconnecting {Describe(peer)}: {reason}.");
            peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
            Rejected[peer] = Time.time;
        }

        private static string Describe(ZNetPeer peer)
        {
            if (!string.IsNullOrEmpty(peer.m_playerName))
            {
                return peer.m_playerName;
            }

            return peer.m_socket != null ? peer.m_socket.GetHostName() : "a peer";
        }

        private static void Applied()
        {
            BossProgress.Refresh();
            CreatureTraits.RefreshAll();
        }
    }
}
