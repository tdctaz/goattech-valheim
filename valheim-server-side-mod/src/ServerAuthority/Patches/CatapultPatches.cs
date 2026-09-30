using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Catapult), nameof(Catapult.Start))]
    internal static class Catapult_Start_Patch
    {
        private static void Postfix(Catapult __instance)
        {
            if (Plugin.ServerActive)
            {
                CatapultLoad.TakeOver(__instance);
            }
        }
    }
}
