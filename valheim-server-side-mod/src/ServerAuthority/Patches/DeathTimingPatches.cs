using System;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    internal static class Character_RPC_Damage_DeathTiming_Patch
    {
        private static void Prefix(Character __instance, long sender, HitData hit)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            try
            {
                DeathTiming.RecordHit(__instance, sender, hit);
            }
            catch (Exception e)
            {
                DeathTiming.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath_DeathTiming_Patch
    {
        private static void Prefix(Character __instance)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            try
            {
                DeathTiming.BeginDeath(__instance);
            }
            catch (Exception e)
            {
                DeathTiming.Fail(e);
            }
        }

        private static void Finalizer(Exception __exception)
        {
            DeathTiming.EndDeath();
        }
    }

    [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
    internal static class Ragdoll_Setup_DeathTiming_Patch
    {
        private static void Postfix(Ragdoll __instance)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            try
            {
                DeathTiming.RagdollMade(__instance);
            }
            catch (Exception e)
            {
                DeathTiming.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.DestroyNow))]
    internal static class Ragdoll_DestroyNow_DeathTiming_Patch
    {
        private static void Prefix(Ragdoll __instance)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            try
            {
                DeathTiming.BeginLoot(__instance);
            }
            catch (Exception e)
            {
                DeathTiming.Fail(e);
            }
        }

        private static void Finalizer(Exception __exception)
        {
            DeathTiming.EndLoot();
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.CreateNewZDO), typeof(Vector3), typeof(int))]
    internal static class ZDOMan_CreateNewZDO_DeathTiming_Patch
    {
        private static void Postfix(ZDO __result, int prefabHash)
        {
            try
            {
                DeathTiming.Created(__result, prefabHash);
            }
            catch (Exception e)
            {
                DeathTiming.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Character_Damage_DeathTiming_Patch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            try
            {
                DeathTimingClient.LocalHit(__instance, hit);
            }
            catch (Exception e)
            {
                DeathTimingClient.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize))]
    internal static class ZDO_Deserialize_DeathTiming_Patch
    {
        private static void Postfix(ZDO __instance)
        {
            try
            {
                DeathTimingClient.Received(__instance);
            }
            catch (Exception e)
            {
                DeathTimingClient.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.HandleDestroyedZDO))]
    internal static class ZDOMan_HandleDestroyedZDO_DeathTiming_Patch
    {
        private static void Prefix(ZDOID uid)
        {
            try
            {
                DeathTimingClient.Destroyed(uid);
            }
            catch (Exception e)
            {
                DeathTimingClient.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Awake))]
    internal static class Ragdoll_Awake_DeathTiming_Patch
    {
        private static void Postfix(Ragdoll __instance)
        {
            try
            {
                DeathTimingClient.RagdollShown(__instance);
            }
            catch (Exception e)
            {
                DeathTimingClient.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Awake))]
    internal static class ItemDrop_Awake_DeathTiming_Patch
    {
        private static void Postfix(ItemDrop __instance)
        {
            try
            {
                DeathTimingClient.ItemShown(__instance);
            }
            catch (Exception e)
            {
                DeathTimingClient.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    internal static class ZNet_Update_DeathTimingClient_Patch
    {
        private static void Postfix()
        {
            if (!DeathTimingClient.Active)
            {
                return;
            }

            try
            {
                DeathTimingClient.Tick();
            }
            catch (Exception e)
            {
                DeathTimingClient.Fail(e);
            }
        }
    }
}
