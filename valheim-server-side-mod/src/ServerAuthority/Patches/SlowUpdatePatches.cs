using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// SlowUpdater hands every SlowUpdate instance the zone of ZNet.GetReferencePosition, which on a
    /// server is the origin. StaticPhysics gates its settling and falling on that zone, so anything
    /// more than an active area from world centre, which is everything, decides it is out of range
    /// and never runs. Felled logs then never settle on the server that owns them. Rewriting the
    /// argument to the nearest player's zone leaves vanilla's own logic untouched.
    ///
    /// Plant is the only other SlowUpdate and takes the argument without using it, so crops are
    /// unaffected either way.
    /// </summary>
    [HarmonyPatch(typeof(StaticPhysics), nameof(StaticPhysics.SUpdate))]
    internal static class StaticPhysics_SUpdate_Patch
    {
        private static void Prefix(StaticPhysics __instance, ref Vector2s referenceZone)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            List<Anchor> anchors = SimulationAnchors.Current;
            if (anchors.Count == 0)
            {
                return;
            }

            Vector3 position = __instance.transform.position;
            float best = float.MaxValue;

            for (int i = 0; i < anchors.Count; i++)
            {
                float distance = (anchors[i].Position - position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    referenceZone = anchors[i].Zone;
                }
            }
        }
    }
}
