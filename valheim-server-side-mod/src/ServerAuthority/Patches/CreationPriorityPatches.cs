using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize))]
    internal static class ZDO_Deserialize_CreationPriority_Patch
    {
        private static void Postfix(ZDO __instance)
        {
            if (!CreationPriority.Active)
            {
                return;
            }

            try
            {
                CreationPriority.Received(__instance);
            }
            catch (Exception e)
            {
                CreationPriority.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateObjectsSorted))]
    internal static class ZNetScene_CreateObjectsSorted_CreationPriority_Patch
    {
        private static void Prefix(ZNetScene __instance, List<ZDO> currentNearObjects)
        {
            if (!CreationPriority.Active)
            {
                return;
            }

            try
            {
                CreationPriority.CreatePending(__instance, currentNearObjects);
            }
            catch (Exception e)
            {
                CreationPriority.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateObjectsSorted))]
    internal static class ZNetScene_CreateObjectsSorted_CreaturePriority_Patch
    {
        private static void Prefix(ZNetScene __instance, List<ZDO> currentNearObjects)
        {
            if (!CreaturePriority.Active)
            {
                return;
            }

            try
            {
                CreaturePriority.CreateNearby(__instance, currentNearObjects);
            }
            catch (Exception e)
            {
                CreaturePriority.Fail(e);
            }
        }
    }
}
