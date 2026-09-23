using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// Makes sure the layer mask every water query depends on is actually set.
    ///
    /// Floating keeps the WaterVolume layer mask in a static that starts at zero:
    ///
    ///     private static int s_waterVolumeMask = 0;
    ///
    /// and the only places that fill it are Floating.Awake, which needs an instance of a Floating
    /// component to exist, and one of the three query methods, which guards itself with
    /// "if (s_waterVolumeMask == 0)". Floating.GetWaterLevel, the one Ship.CustomFixedUpdate
    /// measures the sea with, has no such guard. It passes the mask straight to
    /// Physics.OverlapSphereNonAlloc, and a mask of zero matches no layers at all, so the query
    /// returns no colliders and the method answers -10000: its value for "there is no water here".
    ///
    /// A Ship reads -10000 as being ten kilometres above the sea, skips every line of buoyancy,
    /// damping, sail and rudder force, and keeps its gravity. It falls to the sea floor and beats
    /// itself apart on it. That is the "boat destroyed by nothing" fault, and it is why it looked
    /// intermittent and unreproducible: near a shore or a base there are floating logs, dropped
    /// items and corpses, so something with a Floating component awakes and quietly fixes the mask
    /// for the rest of the process. Out in open ocean, in a freshly started server, nothing does.
    ///
    /// Vanilla never meets this because a vanilla server has no ships instantiated and a client
    /// has a local player whose swim checks go through the guarded call site within seconds of
    /// spawning. This mod gives the server hulls to float and no player to do that for it.
    ///
    /// Measured at (-837, 468) with two hulls on the sea floor: a live WaterVolume covered the
    /// point and knew its surface was 29.87, while GetWaterLevel answered -10000 and Floating's
    /// collider cache was still empty after minutes of a ship querying it five times per physics
    /// step. An empty cache proves the loop body never ran, which only happens when the overlap
    /// query matches nothing.
    /// </summary>
    internal static class WaterQueries
    {
        /// <summary>
        /// Sets the mask if nothing else has, and says what it found. Idempotent: Floating.Awake
        /// assigns the same value, so this only ever wins the race rather than fighting it.
        /// </summary>
        internal static void EnsureLayerMask()
        {
            int before = Floating.s_waterVolumeMask;
            int correct = LayerMask.GetMask("WaterVolume");

            if (before == correct)
            {
                return;
            }

            Floating.s_waterVolumeMask = correct;

            Plugin.Log.LogWarning(
                $"Floating's WaterVolume layer mask was {before} and is now {correct}. While it " +
                "was zero, Floating.GetWaterLevel matched no colliders and answered -10000 " +
                "everywhere, which a Ship reads as being far above the sea: it skips all buoyancy " +
                "and falls to the sea floor. Only Floating.Awake and one of the three query " +
                "methods ever set this, and neither is reached on a server with no floating " +
                "objects nearby and no local player.");
        }
    }
}
