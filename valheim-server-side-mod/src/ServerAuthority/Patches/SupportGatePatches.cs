using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateSupport))]
    internal static class WearNTear_UpdateSupport_Gate
    {
        private static bool Prefix(WearNTear __instance, out bool __state)
        {
            __state = false;
            if (!Plugin.ServerActive)
            {
                return true;
            }

            if (ModConfig.HoldSupportUntilNeighboursLoad.Value && !SupportGate.Ready(__instance.transform.position))
            {
                SupportGate.Held();
                return false;
            }

            __state = true;
            return true;
        }

        private static void Postfix(WearNTear __instance, bool __state)
        {
            if (__state && !__instance.HaveSupport())
            {
                SupportGate.Unsupported(__instance);
            }
        }
    }
}
