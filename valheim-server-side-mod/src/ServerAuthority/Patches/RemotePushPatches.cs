using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyPushback), typeof(Vector3), typeof(float))]
    internal static class Character_ApplyPushback_Patch
    {
        private static bool Prefix(Character __instance, Vector3 dir, float pushForce)
        {
            if (Plugin.ServerActive)
            {
                return true;
            }

            return !RemotePush.Forward(__instance, dir, pushForce);
        }
    }
}
