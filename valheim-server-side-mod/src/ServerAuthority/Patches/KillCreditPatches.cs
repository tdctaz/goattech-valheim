using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath_KillCredit_Patch
    {
        private static void Prefix(Character __instance)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            KillCredit.Deliver(__instance);
        }
    }
}
