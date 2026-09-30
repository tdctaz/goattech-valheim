using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Awake))]
    internal static class TreeBase_Awake_Patch
    {
        private static void Postfix(TreeBase __instance)
        {
            if (Plugin.ServerActive)
            {
                EarnedStats.TakeOver(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Awake))]
    internal static class TreeLog_Awake_Patch
    {
        private static void Postfix(TreeLog __instance)
        {
            if (Plugin.ServerActive)
            {
                EarnedStats.TakeOver(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.Start))]
    internal static class MineRock_Start_Patch
    {
        private static void Postfix(MineRock __instance)
        {
            if (Plugin.ServerActive)
            {
                EarnedStats.TakeOver(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Awake))]
    internal static class MineRock5_Awake_Patch
    {
        private static void Postfix(MineRock5 __instance)
        {
            if (Plugin.ServerActive)
            {
                EarnedStats.TakeOver(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    internal static class Character_Awake_EarnedStats_Patch
    {
        private static void Postfix(Character __instance)
        {
            if (Plugin.ServerActive)
            {
                EarnedStats.TakeOver(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Tame))]
    internal static class Tameable_Tame_Patch
    {
        private static void Prefix(Tameable __instance)
        {
            if (Plugin.ServerActive)
            {
                EarnedStats.Tamed(__instance);
            }
        }
    }
}
