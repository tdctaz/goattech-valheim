using HarmonyLib;

namespace ValheimRebalanced.Patches
{
    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class Attack_Start_Patch
    {
        private static bool Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
        {
            if (Trollstav.CastAllowed(__instance, character, weapon))
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static void Postfix(Attack __instance, bool __result, Humanoid character, ItemDrop.ItemData weapon)
        {
            if (__result)
            {
                Trollstav.OnAttackStarted(__instance, character, weapon);
            }
        }
    }

    [HarmonyPatch(typeof(SpawnAbility), nameof(SpawnAbility.Setup))]
    internal static class SpawnAbility_Setup_Patch
    {
        private static void Postfix(SpawnAbility __instance, Character owner, ItemDrop.ItemData item)
        {
            Trollstav.OnCastReleased(__instance, owner, item);
        }
    }
}
