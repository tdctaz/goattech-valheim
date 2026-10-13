using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class OwnershipHandback
    {
        private const string AskRpc = "ServerAuthority_Handback";
        private const string ReplyRpc = "ServerAuthority_HandbackReply";

        internal const double FallbackSeconds = 10.0;
        internal const double VerifySeconds = 6.0;
        private const double UnseenSeconds = 3.0;
        private const float ReleasedQuietSeconds = 3f;

        private struct Ask
        {
            public long Peer;
            public double Since;
            public ushort Revision;
        }

        private struct Check
        {
            public long Peer;
            public double Since;
            public double Seen;
        }

        private static readonly Dictionary<ZDOID, Ask> Pending = new Dictionary<ZDOID, Ask>();
        private static readonly Dictionary<ZDOID, Check> InUse = new Dictionary<ZDOID, Check>();
        private static readonly List<ZDOID> Stale = new List<ZDOID>();
        private static readonly Dictionary<ZDOID, float> Released = new Dictionary<ZDOID, float>();

        private static int _asked;
        private static int _returned;
        private static int _taken;
        private static int _repaired;

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZPackage>(AskRpc, RPC_Ask);
            ZRoutedRpc.instance?.Register<ZPackage>(ReplyRpc, RPC_Reply);
        }

        internal static void Reset()
        {
            Pending.Clear();
            InUse.Clear();
            Stale.Clear();
            Released.Clear();
            _asked = 0;
            _returned = 0;
            _taken = 0;
            _repaired = 0;
        }

        internal static bool IsAwaiting(ZDO zdo, long peer)
        {
            return Pending.TryGetValue(zdo.m_uid, out Ask ask) && Describes(ask, zdo, peer);
        }

        internal static bool TakeNow(ZDO zdo, long peer)
        {
            if (Pending.TryGetValue(zdo.m_uid, out Ask ask))
            {
                if (!Describes(ask, zdo, peer))
                {
                    Settle(ask);
                    Pending.Remove(zdo.m_uid);
                }
            }

            if (Pending.TryGetValue(zdo.m_uid, out ask))
            {
                if (OwnershipPolicy.Clock - ask.Since < FallbackSeconds)
                {
                    return false;
                }

                Pending.Remove(zdo.m_uid);
                _taken++;
                Plugin.Log.LogWarning(
                    $"{PeerName(peer)} did not hand back {Describe(zdo)} within {FallbackSeconds:0} s of being asked, " +
                    "so the server took it without waiting any longer.");
                return true;
            }

            if (!Send(peer, zdo.m_uid, false))
            {
                return true;
            }

            Pending[zdo.m_uid] = new Ask { Peer = peer, Since = OwnershipPolicy.Clock, Revision = zdo.OwnerRevision };
            _asked++;
#if DEBUG_TOOLS
            if (ModConfig.LogOwnershipChanges.Value)
            {
                Plugin.Log.LogInfo($"Handback of {Describe(zdo)} asked from {PeerName(peer)}.");
            }
#endif
            return false;
        }

        internal static void WhileInUse(ZDO zdo)
        {
            Pending.Remove(zdo.m_uid);

            long peer = zdo.GetOwner();
            double now = OwnershipPolicy.Clock;
            if (!InUse.TryGetValue(zdo.m_uid, out Check check) || check.Peer != peer || now - check.Seen > UnseenSeconds)
            {
                InUse[zdo.m_uid] = new Check { Peer = peer, Since = now, Seen = now };
                return;
            }

            check.Seen = now;
            if (now - check.Since >= VerifySeconds && Send(peer, zdo.m_uid, true))
            {
                check.Since = now;
            }

            InUse[zdo.m_uid] = check;
        }

        internal static void Prune()
        {
            ZDOMan zdoMan = ZDOMan.instance;
            if (zdoMan == null)
            {
                return;
            }

            double now = OwnershipPolicy.Clock;

            if (Pending.Count > 0)
            {
                Stale.Clear();
                foreach (KeyValuePair<ZDOID, Ask> entry in Pending)
                {
                    ZDO zdo = zdoMan.GetZDO(entry.Key);
                    if (zdo != null && Describes(entry.Value, zdo, entry.Value.Peer) &&
                        SimulationAnchors.IsConnectedPeer(entry.Value.Peer) &&
                        now - entry.Value.Since < FallbackSeconds * 3.0)
                    {
                        continue;
                    }

                    if (zdo != null && !Describes(entry.Value, zdo, entry.Value.Peer))
                    {
                        Settle(entry.Value);
                    }

                    Stale.Add(entry.Key);
                }

                for (int i = 0; i < Stale.Count; i++)
                {
                    Pending.Remove(Stale[i]);
                }
            }

            if (InUse.Count > 0)
            {
                Stale.Clear();
                foreach (KeyValuePair<ZDOID, Check> entry in InUse)
                {
                    if (now - entry.Value.Seen > VerifySeconds)
                    {
                        Stale.Add(entry.Key);
                    }
                }

                for (int i = 0; i < Stale.Count; i++)
                {
                    InUse.Remove(Stale[i]);
                }
            }
        }

        internal static string ClosePeriod(float seconds)
        {
            if (_asked == 0 && _returned == 0 && _taken == 0 && _repaired == 0)
            {
                return null;
            }

            string line =
                $"Handbacks over {seconds:F0}s: {_asked} asked, {_returned} handed back, " +
                $"{_taken} taken after {FallbackSeconds:0} s without an answer, {_repaired} ownership split(s) repaired.";
            _asked = 0;
            _returned = 0;
            _taken = 0;
            _repaired = 0;
            return line;
        }

        internal static void ClearStaleInUse(ZDO zdo, long previousOwner)
        {
            if (zdo.GetInt(ZDOVars.s_inUse) == 0 || !zdo.IsOwner())
            {
                return;
            }

            zdo.Set(ZDOVars.s_inUse, 0);
            Plugin.Log.LogInfo(
                $"{Describe(zdo)} was still marked in use by {OwnerName(previousOwner, ZDOMan.GetSessionID())}, " +
                "who no longer holds it, so the server cleared the mark.");
        }

        private static bool Describes(Ask ask, ZDO zdo, long peer)
        {
            return ask.Peer == peer && zdo.GetOwner() == peer && ask.Revision == zdo.OwnerRevision;
        }

        private static void Settle(Ask ask)
        {
            if (SimulationAnchors.IsConnectedPeer(ask.Peer))
            {
                _returned++;
            }
        }

#if DEBUG_TOOLS
        internal static bool AskForTest(ZDO zdo, long peer)
        {
            return Send(peer, zdo.m_uid, false);
        }
#endif

        private static bool Send(long peer, ZDOID id, bool verifyOnly)
        {
            if (ZRoutedRpc.instance == null)
            {
                return false;
            }

            ZPackage package = new ZPackage();
            package.Write(id);
            package.Write(verifyOnly);
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, AskRpc, package);
            return true;
        }

        private static void RPC_Ask(long sender, ZPackage package)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer() || znet.GetServerPeer()?.m_uid != sender || ZDOMan.instance == null)
            {
                return;
            }

            ZDOID id = package.ReadZDOID();
            bool verifyOnly = package.ReadBool();
            ZDO zdo = ZDOMan.instance.GetZDO(id);

            if (Released.TryGetValue(id, out float releasedAt) && Time.time - releasedAt < ReleasedQuietSeconds)
            {
                return;
            }

            if (zdo == null || !zdo.IsOwner())
            {
                long believed = zdo != null ? zdo.GetOwner() : 0L;
                ushort revision = zdo != null ? zdo.OwnerRevision : (ushort)0;
                Plugin.Log.LogWarning(
                    $"The server has {(zdo != null ? Describe(zdo) : id.ToString())} as held by this client, which has it " +
                    $"as {(zdo == null ? "unknown" : "owned by " + OwnerName(believed, sender))}. Telling the server.");

                ZPackage reply = new ZPackage();
                reply.Write(id);
                reply.Write(believed);
                reply.Write(revision);
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, ReplyRpc, reply);
                return;
            }

            if (verifyOnly)
            {
                return;
            }

            if (IsInUseHere(zdo))
            {
                Plugin.Log.LogInfo($"The server asked for {Describe(zdo)} back while it is open here, so this client keeps it.");
                return;
            }

            zdo.SetOwner(sender);
            RememberReleased(id);
        }

        private static void RememberReleased(ZDOID id)
        {
            float now = Time.time;
            if (Released.Count > 64)
            {
                Stale.Clear();
                foreach (KeyValuePair<ZDOID, float> entry in Released)
                {
                    if (now - entry.Value >= ReleasedQuietSeconds)
                    {
                        Stale.Add(entry.Key);
                    }
                }

                for (int i = 0; i < Stale.Count; i++)
                {
                    Released.Remove(Stale[i]);
                }
            }

            Released[id] = now;
        }

        private static void RPC_Reply(long sender, ZPackage package)
        {
            if (!Plugin.ServerActive || !SimulationAnchors.IsConnectedPeer(sender) || ZDOMan.instance == null)
            {
                return;
            }

            ZDOID id = package.ReadZDOID();
            long believed = package.ReadLong();
            ushort revision = package.ReadUShort();

            Pending.Remove(id);
            InUse.Remove(id);

            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null || zdo.GetOwner() != sender)
            {
                return;
            }

            long server = ZDOMan.GetSessionID();
            if (believed != 0L && believed != server && SimulationAnchors.IsConnectedPeer(believed))
            {
                return;
            }

            if (revision < zdo.OwnerRevision)
            {
                return;
            }

            bool wasInUse = zdo.GetInt(ZDOVars.s_inUse) != 0;
            zdo.SetOwner(server);
            if (revision >= zdo.OwnerRevision && revision < ushort.MaxValue)
            {
                zdo.OwnerRevision = (ushort)(revision + 1);
                ZDOMan.instance.SetDirtySector(zdo);
            }

            if (wasInUse)
            {
                zdo.Set(ZDOVars.s_inUse, 0);
            }

            _repaired++;
            Plugin.Log.LogWarning(
                $"Ownership split repaired on {Describe(zdo)}{(wasInUse ? ", marked in use" : "")}: the server had it as held " +
                $"by {PeerName(sender)}, whose client has it as owned by {OwnerName(believed, server)}. Each side was " +
                "waiting on the other, which leaves a chest refusing everyone. The server took it.");
        }

        private static bool IsInUseHere(ZDO zdo)
        {
            if (zdo.GetInt(ZDOVars.s_inUse) != 0)
            {
                return true;
            }

            InventoryGui gui = InventoryGui.instance;
            Container open = gui != null ? gui.m_currentContainer : null;
            if (open != null && open.m_nview != null && open.m_nview.GetZDO() == zdo)
            {
                return true;
            }

            ZNetView view = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
            if (view == null)
            {
                return false;
            }

            Container[] containers = view.GetComponentsInChildren<Container>(true);
            for (int i = 0; i < containers.Length; i++)
            {
                if (containers[i].m_inUse && containers[i].m_nview == view)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(ZDO zdo)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
            return $"{(prefab != null ? prefab.name : "prefab " + zdo.GetPrefab())} {zdo.m_uid}";
        }

        private static string PeerName(long uid)
        {
            ZNetPeer peer = ZNet.instance != null ? ZNet.instance.GetPeer(uid) : null;
            return peer != null && !string.IsNullOrEmpty(peer.m_playerName) ? $"{peer.m_playerName} ({uid})" : $"peer {uid}";
        }

        private static string OwnerName(long uid, long server)
        {
            if (uid == 0L)
            {
                return "nobody";
            }

            return uid == server ? "the server" : $"peer {uid}";
        }
    }
}
