using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimRebalanced.Patches
{
    [HarmonyPatch(typeof(Game), nameof(Game.CheckDropConversion))]
    internal static class Game_CheckDropConversion_Patch
    {
        private static void Postfix(GameObject dropPrefab, ref int dropCount, GameObject __result)
        {
            dropCount = BurntWood.Coal(dropPrefab, __result, dropCount);
        }
    }

    [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Destroy))]
    internal static class TreeLog_Destroy_Patch
    {
        private static void Prefix(TreeLog __instance, out string __state)
        {
            __state = Utils.GetPrefabName(__instance.gameObject.name);
            BurntWood.BeginGroup();
        }

        private static Exception Finalizer(string __state, Exception __exception)
        {
            BurntWood.EndGroup(__state);
            return __exception;
        }
    }
}
