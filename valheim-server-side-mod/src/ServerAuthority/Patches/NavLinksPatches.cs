using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.ConnectAlongEdge))]
    internal static class Pathfinding_ConnectAlongEdge_NavLinks_Patch
    {
        private static bool Prefix(Pathfinding __instance, List<KeyValuePair<Vector3, NavMeshLinkInstance>> links,
            Vector3 p0, Vector3 p1, float step, Pathfinding.AgentSettings settings)
        {
            if (!NavLinks.Active)
            {
                return true;
            }

            try
            {
                NavLinks.Connect(__instance, links, p0, p1, step, settings);
                return false;
            }
            catch (Exception e)
            {
                NavLinks.Fail(e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.UpdateAsyncBuild))]
    internal static class Pathfinding_UpdateAsyncBuild_NavLinks_Patch
    {
        private static bool _failed;

        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Pathfinding __instance, ref bool __result)
        {
            if (_failed)
            {
                return true;
            }

            try
            {
                if (!NavLinks.DiscardStaleBuild(__instance))
                {
                    return true;
                }

                __result = true;
                return false;
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Navigation: dropping stale tile builds failed and is off until restart: {e}");
                return true;
            }
        }
    }
}
