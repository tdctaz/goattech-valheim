using HarmonyLib;
using UnityEngine;

namespace ValheimRebalanced.Patches
{
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    internal static class ZNetScene_Awake_Patch
    {
        private static void Postfix(ZNetScene __instance)
        {
            StaffOfProtection.BuildPrefabs(__instance);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class Projectile_OnHit_Patch
    {
        private static bool Prefix(Projectile __instance, Collider collider, Vector3 hitPoint)
        {
            if (!StaffOfProtection.IsBolt(__instance))
            {
                return true;
            }

            StaffOfProtection.OnBoltHit(__instance, collider, hitPoint);
            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateBlock))]
    internal static class Humanoid_UpdateBlock_Patch
    {
        private static void Prefix(Humanoid __instance, out bool __state)
        {
            __state = __instance.m_internalBlockingState;
        }

        private static void Postfix(Humanoid __instance, bool __state)
        {
            if (!__state && __instance.m_internalBlockingState)
            {
                StaffOfProtection.OnBlockStarted(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoNonAttack))]
    internal static class Attack_DoNonAttack_Patch
    {
        private static void Postfix(Attack __instance)
        {
            StaffOfProtection.OnNonAttack(__instance);
        }
    }
}
