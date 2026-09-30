using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))]
    internal static class ZDOMan_SendZDOToPeers2_Patch
    {
        private static float _owed;
        private static int _cursor;

        internal static void Reset()
        {
            _owed = 0f;
            _cursor = 0;
        }

        private static bool Prefix(ZDOMan __instance, float dt)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            List<ZDOMan.ZDOPeer> peers = __instance.m_peers;
            if (peers.Count == 0)
            {
                _owed = 0f;
                return false;
            }

            if (!ModConfig.FairZdoSending.Value)
            {
                TakeTurns(__instance, dt);
                return false;
            }

            int count = peers.Count;
            _owed += count * dt * ModConfig.ZdoSendRate.Value;
            int serve = (int)_owed;
            if (serve >= count)
            {
                serve = count;
                _owed = 0f;
            }
            else
            {
                _owed -= serve;
            }

            for (int i = 0; i < serve; i++)
            {
                __instance.SendZDOs(peers[(_cursor + i) % count], flush: false);
            }

            _cursor = (_cursor + serve) % count;
            __instance.m_sendTimer = 0f;
            __instance.m_nextSendPeer = -1;
            return false;
        }

        private static void TakeTurns(ZDOMan man, float dt)
        {
            man.m_sendTimer += dt;

            if (man.m_nextSendPeer < 0)
            {
                if (man.m_sendTimer > 1f / ModConfig.ZdoSendRate.Value)
                {
                    man.m_nextSendPeer = 0;
                    man.m_sendTimer = 0f;
                }

                return;
            }

            if (man.m_nextSendPeer < man.m_peers.Count)
            {
                man.SendZDOs(man.m_peers[man.m_nextSendPeer], flush: false);
            }

            man.m_nextSendPeer++;
            if (man.m_nextSendPeer >= man.m_peers.Count)
            {
                man.m_nextSendPeer = -1;
            }
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs))]
    internal static class ZDOMan_SendZDOs_Patch
    {
        private const int VanillaWindow = 10240;
        private const int MinimumPackage = 2048;
        private const float RoundTripAllowance = 1.25f;
        private const float SendIntervalAllowance = 2f;

        private static readonly ZPackage Package = new ZPackage();
        private static readonly ZPackage Data = new ZPackage();

        private static bool Prefix(ZDOMan __instance, ZDOMan.ZDOPeer peer, bool flush, ref bool __result)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            __result = Send(__instance, peer, flush);
            return false;
        }

        private static int Window(ZDOMan.ZDOPeer peer)
        {
            int max = ModConfig.ZdoSendWindowMaxKiB.Value * 1024;
            if (max <= VanillaWindow || !NetworkStats.TryGetLink(peer.m_peer.m_uid, out int ping, out int rate))
            {
                return VanillaWindow;
            }

            float seconds = ping / 1000f * RoundTripAllowance + SendIntervalAllowance / ModConfig.ZdoSendRate.Value;
            return Mathf.Clamp((int)(rate * seconds), VanillaWindow, max);
        }

        private static bool Send(ZDOMan man, ZDOMan.ZDOPeer peer, bool flush)
        {
            long started = Stopwatch.GetTimestamp();
            int window = Window(peer);
            int queue = peer.m_peer.m_socket.GetSendQueueSize();
            int budget = window - queue;
            if ((!flush && queue > window) || budget < MinimumPackage)
            {
                NetworkStats.RecordQueueFull(peer.m_peer, window);
                return false;
            }

            List<ZDO> toSync = man.m_tempToSync;
            toSync.Clear();
            man.CreateSyncList(peer, toSync);
            if (toSync.Count == 0 && peer.m_invalidSector.Count == 0)
            {
                NetworkStats.RecordTurn(peer.m_peer, window, 0, 0, 0, Stopwatch.GetTimestamp() - started);
                return false;
            }

            ZPackage package = Package;
            package.Clear();

            bool invalidated = peer.m_invalidSector.Count > 0;
            if (invalidated)
            {
                package.Write(peer.m_invalidSector.Count);
                foreach (ZDOID id in peer.m_invalidSector)
                {
                    package.Write(id);
                }

                peer.m_invalidSector.Clear();
            }
            else
            {
                package.Write(0);
            }

            float time = Time.time;
            bool isServer = ZNet.instance.IsServer();
            int size = package.Size();
            int sent = 0;
            int index = 0;
            for (; index < toSync.Count; index++)
            {
                if (size > budget)
                {
                    break;
                }

                ZDO zdo = toSync[index];
                peer.m_forceSend.Remove(zdo.m_uid);
                if (!isServer)
                {
                    man.m_clientChangeQueue.Remove(zdo.m_uid);
                }

                package.Write(zdo.m_uid);
                package.Write(zdo.OwnerRevision);
                package.Write(zdo.DataRevision);
                package.Write(zdo.GetOwner());
                package.Write(zdo.GetPosition());
                Data.Clear();
                zdo.Serialize(Data);
                package.Write(Data);
                peer.m_zdos[zdo.m_uid] = new ZDOMan.ZDOPeer.PeerZDOInfo(zdo.DataRevision, zdo.OwnerRevision, time);
                man.m_zdosSent++;
                sent++;

                int after = package.Size();
                NetworkStats.RecordObject(zdo.GetPrefab(), after - size);
                size = after;
            }

            package.Write(ZDOID.None);
            bool flushed = sent > 0 || invalidated;
            if (flushed)
            {
                peer.m_peer.m_rpc.Invoke("ZDOData", package);
            }

            NetworkStats.RecordTurn(peer.m_peer, window, sent, flushed ? package.Size() : 0, toSync.Count - index,
                Stopwatch.GetTimestamp() - started);
            return flushed;
        }
    }
}
