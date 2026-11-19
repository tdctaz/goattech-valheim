using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(MonoUpdatersExtra), nameof(MonoUpdatersExtra.UpdateAI))]
    internal static class MonoUpdatersExtra_UpdateAI_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(string profileScope, long __state)
        {
            FrameBudgetHooks.EndScope(profileScope, __state);
        }
    }

    [HarmonyPatch(typeof(MonoUpdatersExtra), nameof(MonoUpdatersExtra.CustomFixedUpdate))]
    internal static class MonoUpdatersExtra_CustomFixedUpdate_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(string profileScope, long __state)
        {
            FrameBudgetHooks.EndScope(profileScope, __state);
        }
    }

    [HarmonyPatch(typeof(MonoUpdatersExtra), nameof(MonoUpdatersExtra.CustomUpdate))]
    internal static class MonoUpdatersExtra_CustomUpdate_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(string profileScope, long __state)
        {
            FrameBudgetHooks.EndScope(profileScope, __state);
        }
    }

    [HarmonyPatch(typeof(MonoUpdatersExtra), nameof(MonoUpdatersExtra.CustomLateUpdate))]
    internal static class MonoUpdatersExtra_CustomLateUpdate_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(string profileScope, long __state)
        {
            FrameBudgetHooks.EndScope(profileScope, __state);
        }
    }

    [HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.Update))]
    internal static class Pathfinding_Update_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.PathfindingScope, __state);
        }
    }

    [HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.GetPath))]
    internal static class Pathfinding_GetPath_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(Vector3 from, bool __result, long __state)
        {
            if (__state == 0L)
            {
                return;
            }

            try
            {
                FrameBudget.End(FrameBudget.PathQueryScope, __state);
                FrameBudget.PathQuery(from, __result);
            }
            catch (Exception e)
            {
                FrameBudget.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.FindValidPoint))]
    internal static class Pathfinding_FindValidPoint_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.PathQueryScope, __state);
        }
    }

    [HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.BuildTile))]
    internal static class Pathfinding_BuildTile_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
            if (__state == 0L)
            {
                return;
            }

            try
            {
                FrameBudget.TileBuildStarted();
            }
            catch (Exception e)
            {
                FrameBudget.Fail(e);
            }
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.NavSourcesScope, __state);
        }
    }

    [HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.RebuildLinks))]
    internal static class Pathfinding_RebuildLinks_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.NavLinksScope, __state);
        }
    }

    [HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.UpdateAsyncBuild))]
    internal static class Pathfinding_UpdateAsyncBuild_FrameBudget_Patch
    {
        private static void Prefix(Pathfinding __instance)
        {
            if (!FrameBudget.Active || __instance.m_buildOperation == null || !__instance.m_buildOperation.isDone)
            {
                return;
            }

            try
            {
                FrameBudget.TileBuildHarvested();
            }
            catch (Exception e)
            {
                FrameBudget.Fail(e);
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateDestroyObjects))]
    internal static class ZNetScene_CreateDestroyObjects_FrameBudget_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.CreationScope, __state);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Update))]
    internal static class ZoneSystem_Update_FrameBudget_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.ZoneScope, __state);
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindEnemy))]
    internal static class BaseAI_FindEnemy_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.EnemySearchScope, __state);
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindClosestStaticPriorityTarget))]
    internal static class BaseAI_FindClosestStaticPriorityTarget_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.StaticTargetScope, __state);
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindRandomStaticTarget))]
    internal static class BaseAI_FindRandomStaticTarget_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.StaticTargetScope, __state);
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget), typeof(Transform), typeof(Vector3), typeof(float), typeof(float), typeof(bool), typeof(bool), typeof(Character))]
    internal static class BaseAI_CanSeeTarget_FrameBudget_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.SightScope, __state);
        }
    }

    [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.Update))]
    internal static class MonoUpdaters_Update_FrameBudget_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.MonoUpdateScope, __state);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateFloaters))]
    internal static class WaterVolume_UpdateFloaters_FrameBudget_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.WaterFloatScope, __state);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateMaterials))]
    internal static class WaterVolume_UpdateMaterials_FrameBudget_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.WaterMaterialScope, __state);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.StaticUpdate))]
    internal static class WaterVolume_StaticUpdate_FrameBudget_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.WaterClockScope, __state);
        }
    }

    [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.FixedUpdate))]
    internal static class MonoUpdaters_FixedUpdate_FrameBudget_Patch
    {
        private static void Postfix()
        {
            if (FrameBudget.Active)
            {
                FrameBudget.FixedScriptsDone();
            }
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))]
    internal static class ZDOMan_SendZDOToPeers2_FrameBudget_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state)
        {
            __state = FrameBudget.Begin();
        }

        private static void Postfix(long __state)
        {
            FrameBudgetHooks.End(FrameBudget.SendScope, __state);
        }
    }

    internal static class FrameBudgetHooks
    {
        internal static void EndScope(string profileScope, long started)
        {
            if (started == 0L)
            {
                return;
            }

            try
            {
                FrameBudget.End(FrameBudget.Scope(profileScope), started);
            }
            catch (Exception e)
            {
                FrameBudget.Fail(e);
            }
        }

        internal static void End(string scope, long started)
        {
            if (started == 0L)
            {
                return;
            }

            try
            {
                FrameBudget.End(scope, started);
            }
            catch (Exception e)
            {
                FrameBudget.Fail(e);
            }
        }
    }
}
