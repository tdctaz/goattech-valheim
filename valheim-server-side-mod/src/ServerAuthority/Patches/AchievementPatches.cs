using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Achievements), nameof(Achievements.IsCheatedAtAll))]
    internal static class Achievements_IsCheatedAtAll_Patch
    {
        private static bool _lowered;

        private static void Prefix()
        {
            if (AchievementGate.IgnoreModdedFlag && Game.isModded)
            {
                Game.isModded = false;
                _lowered = true;
            }
        }

        private static void Finalizer()
        {
            if (_lowered)
            {
                Game.isModded = true;
                _lowered = false;
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Player_OnSpawned_Achievements_Patch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
            {
                AchievementGate.Report(__instance);
            }
        }
    }
}
