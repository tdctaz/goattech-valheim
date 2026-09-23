using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Ship), nameof(Ship.OnDisable))]
    internal static class Ship_OnDisable_Patch
    {
        private static void Postfix(Ship __instance)
        {
            Ship_CustomFixedUpdate_Patch.Forget(__instance);
            Ship_HealthWatch_Patch.Forget(__instance);
#if DEBUG_TOOLS
            Ship_CustomFixedUpdate_Diagnostic.Forget(__instance);
#endif
        }
    }

    [HarmonyPatch(typeof(ShipEffects), nameof(ShipEffects.OnDisable))]
    internal static class ShipEffects_OnDisable_Patch
    {
        private static void Postfix(ShipEffects __instance)
        {
            ShipEffects_CustomLateUpdate_Patch.Forget(__instance);
        }
    }
}
