using HarmonyLib;

namespace ValheimRebalanced.Patches
{
    [HarmonyPatch(typeof(CharacterTimedDestruction), nameof(CharacterTimedDestruction.Awake))]
    internal static class CharacterTimedDestruction_Awake_Patch
    {
        private static void Postfix(CharacterTimedDestruction __instance)
        {
            StaffOfTheWild.Observe(__instance.m_nview);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Update))]
    internal static class ZNetScene_Update_Patch
    {
        private static void Postfix()
        {
            StaffOfTheWild.Tick();
        }
    }
}
