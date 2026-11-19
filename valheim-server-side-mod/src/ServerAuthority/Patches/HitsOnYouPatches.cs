using System;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateObject))]
    internal static class ZNetScene_CreateObject_HitsOnYou_Patch
    {
        private static void Postfix(ZDO zdo, GameObject __result)
        {
            if (!HitsOnYou.Active)
            {
                return;
            }

            try
            {
                HitsOnYou.Created(zdo, __result);
            }
            catch (Exception e)
            {
                HitsOnYou.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer))]
    internal static class Player_SetLocalPlayer_HitsOnYou_Patch
    {
        private static void Postfix(Player __instance)
        {
            if (!HitsOnYou.Active)
            {
                return;
            }

            try
            {
                HitsOnYou.TakeOver(__instance);
            }
            catch (Exception e)
            {
                HitsOnYou.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class Player_OnDeath_HitsOnYou_Patch
    {
        private static void Prefix(Player __instance)
        {
            if (!HitsOnYou.Active)
            {
                return;
            }

            try
            {
                HitsOnYou.Died(__instance);
            }
            catch (Exception e)
            {
                HitsOnYou.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    internal static class ZNet_Update_HitsOnYou_Patch
    {
        private static void Postfix()
        {
            if (!HitsOnYou.Active)
            {
                return;
            }

            try
            {
                HitsOnYou.Tick();
            }
            catch (Exception e)
            {
                HitsOnYou.Fail(e);
            }
        }
    }
}
