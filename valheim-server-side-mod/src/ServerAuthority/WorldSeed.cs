using System;
using HarmonyLib;

namespace ServerAuthority
{
    [HarmonyPatch(typeof(World), nameof(World.GenerateSeed))]
    internal static class World_GenerateSeed_Patch
    {
        private static bool Prefix(ref string __result)
        {
            string seed = CommandLineSeed();
            if (string.IsNullOrEmpty(seed))
            {
                return true;
            }

            Plugin.Log.LogInfo($"Creating the new world with seed '{seed}' from the -seed argument.");
            __result = seed;
            return false;
        }

        private static string CommandLineSeed()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("-seed", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1].Trim();
                }
            }

            return null;
        }
    }
}
