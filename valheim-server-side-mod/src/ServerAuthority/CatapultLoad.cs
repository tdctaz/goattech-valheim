using System.Collections.Generic;

namespace ServerAuthority
{
    internal static class CatapultLoad
    {
        private const double LeaseSeconds = 6.0;

        private struct Loader
        {
            public long Peer;
            public string Name;
            public ushort OwnerRevision;
        }

        private static readonly Dictionary<ZDOID, Loader> Held = new Dictionary<ZDOID, Loader>();
        private static readonly List<ZDOID> Ended = new List<ZDOID>();

        internal static void Reset()
        {
            Held.Clear();
            Ended.Clear();
        }

        internal static void TakeOver(Catapult catapult)
        {
            RpcTakeover.Replace<string>(catapult, catapult.m_nview, "RPC_SetLoadedVisual",
                (sender, name) => Loaded(catapult, sender, name));
        }

        internal static void Refresh()
        {
            Ended.Clear();
            foreach (KeyValuePair<ZDOID, Loader> entry in Held)
            {
                if (!Ends(entry.Key, entry.Value, out string reason))
                {
                    continue;
                }

                Ended.Add(entry.Key);
                if (reason != null)
                {
                    Plugin.Log.LogInfo($"Catapult {entry.Key} lease with its loader {entry.Value.Name} ended: {reason}.");
                }
            }

            foreach (ZDOID id in Ended)
            {
                Held.Remove(id);
            }
        }

        private static bool Ends(ZDOID id, Loader loader, out string reason)
        {
            reason = null;
            if (!SimulationAnchors.IsConnectedPeer(loader.Peer))
            {
                return true;
            }

            bool leased = OwnershipLeases.TryGetOwner(id, out long lessee);
            bool ours = leased && lessee == loader.Peer;
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null)
            {
                reason = "the catapult is gone";
            }
            else if (zdo.GetOwner() != loader.Peer && SimulationAnchors.IsConnectedPeer(zdo.GetOwner()) &&
                zdo.OwnerRevision > loader.OwnerRevision)
            {
                reason = $"{Name(zdo.GetOwner())} took it over";
            }
            else if (!ours)
            {
                reason = leased ? $"its lease passed to {Name(lessee)}" : "the lease lapsed";
                return true;
            }
            else
            {
                return false;
            }

            if (ours)
            {
                OwnershipLeases.Revoke(id);
            }

            return true;
        }

        private static void Loaded(Catapult catapult, long sender, string item)
        {
            catapult.RPC_SetLoadedVisual(sender, item);

            ZNetView view = catapult.m_nview;
            if (ModConfig.Mode.Value == OwnershipMode.Vanilla || view == null || !view.IsValid() ||
                !SimulationAnchors.IsConnectedPeer(sender))
            {
                return;
            }

            ZDO zdo = view.GetZDO();
            long owner = zdo.GetOwner();
            string loader = Name(sender);
            if (catapult.m_wagon != null && catapult.m_wagon.InUse())
            {
                if (owner != sender)
                {
                    Plugin.Log.LogInfo(
                        $"Catapult {zdo.m_uid} loaded by {loader} while {Name(owner)} holds it, so it stays with them as in vanilla.");
                }

                return;
            }

            if (Held.TryGetValue(zdo.m_uid, out Loader first) && first.Peer != sender &&
                OwnershipLeases.TryGetOwner(zdo.m_uid, out long leased) && leased == first.Peer)
            {
                Plugin.Log.LogInfo(
                    $"Catapult {zdo.m_uid} loaded by {loader} while {first.Name}'s load is still pending, so it stays with {first.Name}.");
                return;
            }

            OwnershipLeases.Grant(zdo.m_uid, sender, LeaseSeconds);
            if (owner != sender)
            {
                zdo.SetOwner(sender);
            }

            Held[zdo.m_uid] = new Loader { Peer = sender, Name = loader, OwnerRevision = zdo.OwnerRevision };

            Plugin.Log.LogInfo(owner != sender
                ? $"Catapult {zdo.m_uid} loaded with {item} by {loader}, handed to them from {Name(owner)} until it fires."
                : $"Catapult {zdo.m_uid} loaded with {item} by {loader}, kept with them until it fires.");
        }

        private static string Name(long peer)
        {
            if (peer == 0L)
            {
                return "nobody";
            }

            if (peer == ZDOMan.GetSessionID())
            {
                return "the server";
            }

            ZNetPeer znetPeer = ZNet.instance != null ? ZNet.instance.GetPeer(peer) : null;
            return znetPeer != null ? znetPeer.m_playerName : peer.ToString();
        }
    }
}
