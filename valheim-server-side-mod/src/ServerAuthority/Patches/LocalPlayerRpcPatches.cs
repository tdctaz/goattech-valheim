using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.RPC_Pick))]
    internal static class Pickable_RPC_Pick_Patch
    {
        private static bool Prefix(Pickable __instance, int bonus)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            if (!__instance.m_nview.IsOwner() || __instance.m_picked)
            {
                return false;
            }

            Vector3 basePos = __instance.m_pickEffectAtSpawnPoint
                ? __instance.transform.position + __instance.GetSpawnOffset()
                : __instance.transform.position;
            __instance.m_pickEffector.Create(basePos, Quaternion.identity, null, 1f, -1, ZDOID.None);

            int amount = __instance.m_dontScale
                ? __instance.m_amount
                : Mathf.Max(
                    __instance.m_minAmountScaled,
                    Game.instance.ScaleDrops(__instance.m_itemPrefab, __instance.m_amount));
            amount += bonus;

            int offset = 0;
            for (int i = 0; i < amount; i++)
            {
                __instance.Drop(__instance.m_itemPrefab, offset++, 1);
            }

            if (!__instance.m_extraDrops.IsEmpty())
            {
                foreach (ItemDrop.ItemData item in __instance.m_extraDrops.GetDropListItems())
                {
                    __instance.Drop(item.m_dropPrefab, offset++, item.m_stack);
                }
            }

            if (__instance.m_aggravateRange > 0f)
            {
                BaseAI.AggravateAllInArea(
                    __instance.transform.position,
                    __instance.m_aggravateRange,
                    BaseAI.AggravatedReason.Theif);
            }

            __instance.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Leviathan), nameof(Leviathan.RPC_Left))]
    internal static class Leviathan_RPC_Left_Patch
    {
        private static bool Prefix()
        {
            return !Plugin.ServerActive;
        }
    }

    [HarmonyPatch(typeof(MusicVolume), nameof(MusicVolume.RPC_PlayMusic))]
    internal static class MusicVolume_RPC_PlayMusic_Patch
    {
        private static bool Prefix()
        {
            return !Plugin.ServerActive;
        }
    }

    [HarmonyPatch(typeof(Trap), nameof(Trap.RPC_OnStateChanged))]
    internal static class Trap_RPC_OnStateChanged_Patch
    {
        private static bool Prefix(Trap __instance, int value, long idOfClientModifyingState)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            if (!__instance.m_nview.IsValid())
            {
                return false;
            }

            __instance.m_onReceiveOwnershipActions.Clear();

            if (value == 2)
            {
                if (idOfClientModifyingState == ZNet.GetUID())
                {
                    if (__instance.m_nview.IsOwner())
                    {
                        __instance.TriggerTrap();
                    }
                    else
                    {
                        __instance.m_onReceiveOwnershipActions.Add(__instance.TriggerTrap);
                    }
                }
            }
            else
            {
                __instance.m_tempTriggeringHumanoid = null;
            }

            __instance.UpdateState((Trap.TrapState)value);
            return false;
        }
    }
}
