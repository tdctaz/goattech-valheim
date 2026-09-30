using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Character), nameof(Character.ForceJump))]
    internal static class Character_ForceJump_Patch
    {
        private static bool Prefix(Character __instance, Vector3 vel, bool effects)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            return !PlayerLaunch.Forward(__instance, vel, effects);
        }
    }
}
