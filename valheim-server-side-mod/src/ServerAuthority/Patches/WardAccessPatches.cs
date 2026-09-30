using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.HaveLocalAccess))]
    internal static class PrivateArea_HaveLocalAccess_Patch
    {
        private static bool Prefix(PrivateArea __instance, ref bool __result)
        {
            if (!Plugin.ServerActive || Player.m_localPlayer != null)
            {
                return true;
            }

            __result = WardAccess.Decide(__instance);
            return false;
        }
    }
}
