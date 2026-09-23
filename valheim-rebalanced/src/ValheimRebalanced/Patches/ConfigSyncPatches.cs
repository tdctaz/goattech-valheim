using HarmonyLib;

namespace ValheimRebalanced.Patches
{
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    internal static class ZNet_Awake_Patch
    {
        private static void Postfix()
        {
            ConfigSync.Reset();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class ZNet_OnDestroy_Patch
    {
        private static void Postfix()
        {
            ConfigSync.Reset();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
    internal static class ZNet_OnNewConnection_Patch
    {
        private static void Postfix(ZNet __instance, ZNetPeer peer)
        {
            ConfigSync.OnNewConnection(__instance, peer);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    internal static class ZNet_Update_Patch
    {
        private static void Postfix(ZNet __instance)
        {
            ConfigSync.Update(__instance);
        }
    }
}
