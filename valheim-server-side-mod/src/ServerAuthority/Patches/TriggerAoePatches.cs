using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
    internal static class Aoe_Setup_Patch
    {
        private static void Postfix(Aoe __instance, Character owner, HitData hitData, ItemDrop.ItemData item)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            TriggerAoeSetup.Publish(__instance, owner, hitData, item);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateObject))]
    internal static class ZNetScene_CreateObject_TriggerAoe_Patch
    {
        private static void Postfix(GameObject __result, ZDO zdo)
        {
            if (Plugin.ServerActive)
            {
                return;
            }

            TriggerAoeSetup.Replay(__result, zdo);
        }
    }
}
