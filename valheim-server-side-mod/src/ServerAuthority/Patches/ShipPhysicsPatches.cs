using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// Holds a server-owned hull still while part of it sits over a zone the server has not built,
    /// because the water there cannot be measured and the hull's answer to that is to fall.
    ///
    /// Ship.CustomFixedUpdate samples the water at five points spread across the float collider: the
    /// centre of mass, and the bow, stern, port and starboard faces. A longship's float collider is
    /// 8 by 17 metres and the WaterVolume in the zone prefab is a 64 by 64 box that tiles one zone
    /// exactly, with no overlap, so those outer points routinely land in the neighbouring zone.
    /// Floating.GetWaterLevel answers -10000 for a point with no WaterVolume collider on it, and a
    /// WaterVolume exists only where ZoneSystem has instantiated a local zone root. One missing
    /// neighbour therefore drags the five point average to about -2000 metres, so
    ///
    ///     num2 = centreOfMass.y - averageWaterLevel - m_waterLevelOffset
    ///
    /// comes out at roughly +2000, which is above m_disableLevel, and every line of buoyancy,
    /// damping, sail and rudder force is skipped. The rigidbody keeps its gravity, so the hull
    /// simply falls out of the sea.
    ///
    /// That is fatal rather than cosmetic. Two thirds of a second of free fall reaches 7 m/s, which
    /// is ImpactEffect.m_maxVelocity, so bouncing along the sea floor costs the hull a full strength
    /// self hit, 50 blunt on a longship and 30 on a karve, as often as its half second interval
    /// allows, and once it rolls over Ship.UpdateUpsideDmg adds 20 a second on top. A thousand
    /// health goes in well under a minute with nothing attacking it, which is why this reads as a
    /// boat being slowly destroyed by nothing while somebody walks towards it.
    ///
    /// The window exists because the mod creates objects per zone rather than waiting for the whole
    /// active area. Vanilla's ZNetScene.CreateObjectsSorted refuses to create anything at all until
    /// IsActiveAreaLoaded, so a client never sees a half built neighbourhood; the server cannot use
    /// that gate, since one player still loading would stall object creation for everybody, so it
    /// checks each object's own zone instead. Zones are built at most one per tick per player, so a
    /// hull whose zone is ready can wait seconds for the zone its bow hangs over.
    ///
    /// It is not only a window, either. The furthest a hull can be from a player and still be
    /// claimed by the policy is set by ZNetScene.InActiveArea, and at that distance a sample point
    /// can reach diagonally into a zone whose centre is just outside the near radius, which is never
    /// built as a local zone at all. Waiting is the right answer to both: a hull that is held keeps
    /// its position and is released, unharmed, the moment the water under all of it can be read.
    /// </summary>
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate_Patch
    {
        private static readonly HashSet<int> Held = new HashSet<int>();

        private static bool Prefix(Ship __instance)
        {
            if (!Plugin.ServerActive || !ModConfig.HoldHullsUntilWaterLoads.Value)
            {
                return true;
            }

            ZNetView nview = __instance.m_nview;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return true;
            }

            ZDOID id = nview.GetZDO().m_uid;
            int key = __instance.GetInstanceID();

            if (HullWater.Measurable(__instance))
            {
                if (Held.Remove(key))
                {
                    Release(__instance, id);
                }

                return true;
            }

            Hold(__instance);

            if (Held.Add(key))
            {
                Plugin.Log.LogInfo(
                    $"Holding ship {id} still at ({__instance.transform.position.x:0}, " +
                    $"{__instance.transform.position.z:0}): part of its hull is over a zone the " +
                    "server has not built, so the water under it cannot be measured.");
            }

            return false;
        }

        /// <summary>
        /// Keeps the hull where it is without touching isKinematic, which ZSyncTransform owns.
        /// Zeroing the velocity lets the body fall asleep, which is welcome here because a sleeping
        /// body does not drift at all, but it is the reason Release has to wake it again.
        /// </summary>
        private static void Hold(Ship ship)
        {
            Rigidbody body = ship.m_body;
            if (body == null)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            if (body.useGravity && !body.IsSleeping())
            {
                body.AddForce(-Physics.gravity, ForceMode.Acceleration);
            }
        }

        internal static void Forget(Ship ship)
        {
            Held.Remove(ship.GetInstanceID());
        }

        internal static void Forget()
        {
            Held.Clear();
        }

        private static void Release(Ship ship, ZDOID id)
        {
            // The depth recorded before the wait describes a world that has since changed. Putting
            // it back to the value a newly created hull carries makes the resumed frame read as
            // rising water rather than as a plunge, which is what stops UpdateWaterForce charging
            // the hull for a hard landing it never made.
            ship.m_lastDepth = -9999f;
            ship.m_lastWaterImpactTime = Time.time;

            // Waking it is not optional. Vanilla only ever calls WakeUp inside the buoyancy block,
            // and that block is skipped whenever the hull rides above m_disableLevel, which a wave
            // trough alone is enough to cause. A hull released while asleep and above the surface
            // therefore receives no gravity and hangs in the air until some later wave happens to
            // reach far enough up to run the block for it. Observed doing exactly that.
            Rigidbody body = ship.m_body;
            if (body != null)
            {
                body.WakeUp();
            }

            Plugin.Log.LogInfo($"Releasing ship {id}: the water under all of it can be read again.");
        }
    }
}
