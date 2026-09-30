using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.InGhostMode))]
    internal static class Player_InGhostMode_Patch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (!__result && Plugin.ServerActive)
            {
                __result = CreatureSenses.Ghost(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanHearTarget), typeof(Transform), typeof(float), typeof(Character))]
    internal static class BaseAI_CanHearTarget_Patch
    {
        private static bool Prefix(Character target, ref bool __result)
        {
            if (!Plugin.ServerActive || !CreatureSenses.HiddenFlying(target))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget), typeof(Transform), typeof(Vector3), typeof(float),
        typeof(float), typeof(bool), typeof(bool), typeof(Character))]
    internal static class BaseAI_CanSeeTarget_Patch
    {
        private static bool Prefix(Character target, ref bool __result)
        {
            if (!Plugin.ServerActive || !CreatureSenses.HiddenFlying(target))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindEnemy))]
    internal static class BaseAI_FindEnemy_Patch
    {
        private static bool Prefix(BaseAI __instance, ref Character __result)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            __result = CreatureSenses.FindEnemy(__instance);
            return false;
        }
    }
}
