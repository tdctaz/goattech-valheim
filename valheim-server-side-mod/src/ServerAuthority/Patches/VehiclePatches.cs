using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// The fallback behind KeepShipOwnedByDriver, which is off by default. The server owning a ship
    /// is the intended state, so every passenger gets the same hull physics. With the setting on,
    /// the driver keeps ownership so the rudder answers without a round trip, and an empty ship
    /// goes back to the server so it still drifts, takes damage and stays synced when nobody is
    /// aboard.
    /// </summary>
    [HarmonyPatch(typeof(Ship), nameof(Ship.UpdateOwner))]
    internal static class Ship_UpdateOwner_Patch
    {
        /// <summary>
        /// Comfortably longer than the two second interval UpdateOwner runs on, so a lease is
        /// always refreshed before it lapses, but short enough that a crash or disconnect returns
        /// the ship to the server quickly.
        /// </summary>
        internal const double LeaseSeconds = 12.0;

        private const float MaxHelmDistance = 30f;

        private static bool Prefix(Ship __instance)
        {
            if (!Plugin.ServerActive || !ModConfig.KeepShipOwnedByDriver.Value)
            {
                return true;
            }

            ZNetView nview = __instance.m_nview;
            if (nview == null || !nview.IsValid())
            {
                return false;
            }

            ZDO zdo = nview.GetZDO();

            // Reassigning a ship whose cargo container is open kicks the player out of it.
            if (zdo.GetInt(ZDOVars.s_inUse) != 0)
            {
                return false;
            }

            long helmUser = __instance.m_shipControlls != null ? __instance.m_shipControlls.GetUser() : 0L;
            long driverPeer = FindUserPeer(helmUser, __instance.transform.position, MaxHelmDistance);

#if DEBUG_TOOLS
            if (ModConfig.LogShipState.Value)
            {
                Plugin.Log.LogInfo(
                    $"UpdateOwner {zdo.m_uid}: inUse={zdo.GetInt(ZDOVars.s_inUse)} helmUser={helmUser} " +
                    $"driverPeer={driverPeer} currentOwner={zdo.GetOwner()} clock={OwnershipPolicy.Clock:0.0}");
            }
#endif

            if (driverPeer == 0L)
            {
                // Nobody is steering, so drop the lease and let the ownership policy decide. With
                // ServerOwnsWaterborne on, which is the default, that means the server, so an
                // abandoned hull drifts and takes damage consistently instead of depending on which
                // client happens to be nearest.
                OwnershipLeases.Revoke(zdo.m_uid);
                __instance.m_lastWaterImpactTime = Time.time;
                return false;
            }

            OwnershipLeases.Grant(zdo.m_uid, driverPeer, LeaseSeconds);
            if (zdo.GetOwner() != driverPeer)
            {
                zdo.SetOwner(driverPeer);
            }

            return false;
        }


        /// <summary>
        /// Resolves the peer simulating the player at a vehicle's controls.
        ///
        /// The stored user id is a persistent playerID, not a peer session id and not the UserID
        /// half of a ZDOID, which are different number spaces entirely. Comparing it against
        /// ZDOID.UserID never matches, so no driver is ever found, no lease is ever granted and the
        /// sector policy reclaims the hull every two seconds. Two machines then take turns
        /// simulating the same rigidbody, which drives a boat into the seabed and destroys it.
        /// Player.GetPlayer does the lookup in the right space, against instantiated players whose
        /// ids come from their ZDOs, so it works on the server.
        /// </summary>
        internal static long FindUserPeer(long playerID, Vector3 origin, float maxDistance)
        {
            if (playerID == 0L)
            {
                return 0L;
            }

            Player player = Player.GetPlayer(playerID);
            if (player == null)
            {
                return 0L;
            }

            if (Vector3.Distance(player.transform.position, origin) > maxDistance)
            {
                return 0L;
            }

            return player.GetOwner();
        }
    }

    /// <summary>
    /// Taking the helm is granted by the current owner answering a request, and the answer passes
    /// through the server on its way back. Handing the ship over at that moment means the new driver
    /// is authoritative from their first input rather than from the next two second tick.
    /// </summary>
    [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.RouteRPC))]
    internal static class ZRoutedRpc_RouteRPC_Patch
    {
        private static readonly int RequestResponseHash = "RequestRespons".GetStableHashCode();

        private static void Prefix(ZRoutedRpc.RoutedRPCData rpcData)
        {
            if (!Plugin.ServerActive || !ModConfig.KeepShipOwnedByDriver.Value)
            {
                return;
            }

            if (rpcData == null || rpcData.m_methodHash != RequestResponseHash)
            {
                return;
            }

            if (rpcData.m_targetZDO.IsNone() || rpcData.m_targetPeerID == 0L)
            {
                return;
            }

            // Read a copy so the packet the RPC itself will read stays untouched.
            ZPackage parameters = rpcData.m_parameters;
            int position = parameters.GetPos();
            bool granted;
            try
            {
                parameters.SetPos(0);
                granted = parameters.ReadBool();
            }
            finally
            {
                parameters.SetPos(position);
            }

            if (!granted)
            {
                return;
            }

            ZDO zdo = ZDOMan.instance.GetZDO(rpcData.m_targetZDO);
            if (zdo == null)
            {
                return;
            }

            OwnershipLeases.Grant(zdo.m_uid, rpcData.m_targetPeerID, Ship_UpdateOwner_Patch.LeaseSeconds);
            if (zdo.GetOwner() != rpcData.m_targetPeerID)
            {
                zdo.SetOwner(rpcData.m_targetPeerID);
            }
        }
    }

    /// <summary>
    /// Mounts grant ownership straight to the rider when they climb on, but unlike a ship nothing
    /// refreshes it afterwards, so the sector policy would take the animal back on its next pass and
    /// leave the rider steering a round trip away. Hold the mount with its current owner for as long
    /// as somebody is riding it.
    ///
    /// Sadle.GetUser() stores the rider's peer session id, the same ZDOID.UserID space Ship uses,
    /// not the persistent playerID that FindUserPeer expects, so the two cannot share a lookup.
    /// FindRiderPeer matches it against a connected player's own ZDOID.UserID instead.
    /// </summary>
    [HarmonyPatch(typeof(Sadle), nameof(Sadle.FixedUpdate))]
    internal static class Sadle_FixedUpdate_Patch
    {
        private static void Postfix(Sadle __instance)
        {
            if (!Plugin.ServerActive || !ModConfig.KeepVehiclesOwnedByUser.Value)
            {
                return;
            }

            if (__instance.m_nview == null || !__instance.m_nview.IsValid())
            {
                return;
            }

            ZDO sadleZdo = __instance.m_nview.GetZDO();
            long user = __instance.GetUser();
            long rider = FindRiderPeer(user, __instance.transform.position, __instance.m_maxUseRange);

            if (rider == 0L)
            {
                if (OwnershipLeases.Revoke(sadleZdo.m_uid))
                {
                    Plugin.Log.LogInfo(user == 0L
                        ? $"Mount lease on {sadleZdo.m_uid} released: the rider dismounted."
                        : $"Mount lease on {sadleZdo.m_uid} released: rider {user} is gone or out of range.");
                }

                return;
            }

            OwnershipLeases.Grant(sadleZdo.m_uid, rider, Ship_UpdateOwner_Patch.LeaseSeconds);
        }

        private static long FindRiderPeer(long user, Vector3 origin, float maxDistance)
        {
            if (user == 0L)
            {
                return 0L;
            }

            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];
                if (player == null || player.GetZDOID().UserID != user)
                {
                    continue;
                }

                if (Vector3.Distance(player.transform.position, origin) > maxDistance)
                {
                    return 0L;
                }

                return SimulationAnchors.IsConnectedPeer(user) ? user : 0L;
            }

            return 0L;
        }
    }

    /// <summary>
    /// A cart is pulled by exactly one machine, the one holding the physics joint. IsAttached falls
    /// back to a networked flag, but m_attachJoin is local, so any owner without the joint concludes
    /// the cart is attached to nothing and detaches it. Once the server owns an attached cart it
    /// therefore rips it off whoever is pulling, within a frame, before any lease can intervene.
    /// The server simply does not run this method for a cart somebody else is pulling, and keeps the
    /// lease alive while that is true. If the puller is gone, vanilla runs and cleans the flag up.
    /// </summary>
    [HarmonyPatch(typeof(Vagon), nameof(Vagon.Update))]
    internal static class Vagon_Update_Patch
    {
        private static bool Prefix(Vagon __instance)
        {
            if (!Plugin.ServerActive || !ModConfig.KeepVehiclesOwnedByUser.Value)
            {
                return true;
            }

            ZNetView nview = __instance.m_nview;
            if (nview == null || !nview.IsValid())
            {
                return true;
            }

            ZDO zdo = nview.GetZDO();
            if (!zdo.GetBool(ZDOVars.s_attachJointHash) || __instance.m_attachJoin != null)
            {
                return true;
            }

            long owner = zdo.GetOwner();
            if (!SimulationAnchors.IsConnectedPeer(owner))
            {
                return true;
            }

            OwnershipLeases.Grant(zdo.m_uid, owner, Ship_UpdateOwner_Patch.LeaseSeconds);
            return false;
        }
    }



    internal static class VehicleLease
    {
        internal static void HoldWhileInUse(ZDO zdo, bool inUse)
        {
            if (zdo == null)
            {
                return;
            }

            if (!inUse)
            {
                OwnershipLeases.Revoke(zdo.m_uid);
                return;
            }

            long owner = zdo.GetOwner();
            if (owner == 0L || owner == ZDOMan.GetSessionID())
            {
                return;
            }

            OwnershipLeases.Grant(zdo.m_uid, owner, Ship_UpdateOwner_Patch.LeaseSeconds);
        }
    }
}
