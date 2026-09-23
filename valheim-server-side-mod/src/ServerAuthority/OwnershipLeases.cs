using System.Collections.Generic;

namespace ServerAuthority
{
    /// <summary>
    /// Short lived pins that keep a specific object owned by a specific client despite the sector
    /// policy. The one case that needs this today is a crewed ship: steering an object the server
    /// owns costs a full round trip per rudder input, so the driver keeps it.
    ///
    /// Leases expire on their own so that a client which unloads or stops driving cannot strand an
    /// object in client ownership forever, and are dropped at once when their owner disconnects.
    /// </summary>
    internal static class OwnershipLeases
    {
        private struct Lease
        {
            public long Owner;
            public double Expires;
        }

        private static readonly Dictionary<ZDOID, Lease> Active = new Dictionary<ZDOID, Lease>();
        private static readonly List<ZDOID> Expired = new List<ZDOID>();

        internal static void Grant(ZDOID id, long owner, double ttlSeconds)
        {
            Active[id] = new Lease
            {
                Owner = owner,
                Expires = OwnershipPolicy.Clock + ttlSeconds,
            };
        }

        internal static bool Revoke(ZDOID id)
        {
            return Active.Remove(id);
        }

        internal static bool TryGetOwner(ZDOID id, out long owner)
        {
            owner = 0L;
            if (!Active.TryGetValue(id, out Lease lease))
            {
                return false;
            }

            if (IsStale(id, lease))
            {
                Active.Remove(id);
                return false;
            }

            owner = lease.Owner;
            return true;
        }

        internal static void PruneExpired()
        {
            if (Active.Count == 0)
            {
                return;
            }

            Expired.Clear();
            foreach (KeyValuePair<ZDOID, Lease> entry in Active)
            {
                if (IsStale(entry.Key, entry.Value))
                {
                    Expired.Add(entry.Key);
                }
            }

            for (int i = 0; i < Expired.Count; i++)
            {
                Active.Remove(Expired[i]);
            }
        }

        private static bool IsStale(ZDOID id, Lease lease)
        {
            if (lease.Expires <= OwnershipPolicy.Clock)
            {
                return true;
            }

            if (SimulationAnchors.IsConnectedPeer(lease.Owner))
            {
                return false;
            }

            Plugin.Log.LogInfo(
                $"Lease on {id} dropped: owner {lease.Owner} is no longer connected, " +
                $"{lease.Expires - OwnershipPolicy.Clock:0.0}s before it would have expired.");
            return true;
        }

        internal static void Clear()
        {
            Active.Clear();
            Expired.Clear();
        }
    }
}
