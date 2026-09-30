using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath_KillKeys_Patch
    {
        private static void Prefix(Character __instance)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            KillKeys.Deliver(__instance);
        }
    }
}
