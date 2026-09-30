using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Awake))]
    internal static class CookingStation_Awake_Patch
    {
        private static void Postfix(CookingStation __instance)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            CookingCollect.TakeOver(__instance);
        }
    }
}
