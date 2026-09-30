using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
    internal static class Character_RaiseSkill_Patch
    {
        private static bool Prefix(Character __instance, float value)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            return !SummonSkill.Forward(__instance, value);
        }
    }
}
