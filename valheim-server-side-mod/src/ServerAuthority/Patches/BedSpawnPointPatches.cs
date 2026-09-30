using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Destroy))]
    internal static class WearNTear_Destroy_BedSpawnPoint_Patch
    {
        private static void Prefix(WearNTear __instance)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            BedSpawnPoint.Destroyed(__instance);
        }
    }
}
