using System;
using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.ServerLoadWorld))]
    internal static class ZNet_ServerLoadWorld_PityCounters_Patch
    {
        private static void Postfix()
        {
            try
            {
                PityCounters.Load();
            }
            catch (Exception e)
            {
                PityCounters.Reset();
                Plugin.Log.LogError($"Pity counters: loading failed, so they start fresh and are not saved: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.SaveWorld))]
    internal static class ZNet_SaveWorld_PityCounters_Patch
    {
        private static void Postfix()
        {
            try
            {
                PityCounters.Save();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Pity counters: saving failed: {e}");
            }
        }
    }
}
