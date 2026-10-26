using System.Collections.Generic;
using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class Projectile_Setup_ThrownSpear_Patch
    {
        private static void Postfix(Projectile __instance)
        {
            ThrownSpears.Thrown(__instance);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.FixedUpdate))]
    internal static class Projectile_FixedUpdate_ThrownSpear_Patch
    {
        private static void Prefix(Projectile __instance, out bool __state)
        {
            __state = ThrownSpears.HoldIfExpiring(__instance);
        }

        private static void Postfix(Projectile __instance, bool __state)
        {
            if (__state)
            {
                ThrownSpears.LandAfterLastStep(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.RemoveObjects))]
    internal static class ZNetScene_RemoveObjects_ThrownSpear_Patch
    {
        private static void Prefix(List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
        {
            ThrownSpears.LandIfLeaving(currentNearObjects, currentDistantObjects);
        }
    }
}
