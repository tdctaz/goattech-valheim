using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.PheromoneFleeCheck))]
    internal static class MonsterAI_PheromoneFleeCheck_Patch
    {
        private static void Postfix(MonsterAI __instance, Character target, ref bool __result)
        {
            if (__result || !Plugin.ServerActive)
            {
                return;
            }

            __result = Pheromones.Flees(__instance.m_character, target);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdatePheromones))]
    internal static class Character_UpdatePheromones_Patch
    {
        private static void Prefix(Character __instance, float dt, out bool __state)
        {
            __state = Plugin.ServerActive && __instance.m_pheromoneTimer >= 0f &&
                __instance.m_pheromoneTimer - dt <= 0f && __instance.m_pheromoneLoveEffect.HasEffects();
        }

        private static void Postfix(Character __instance, bool __state)
        {
            if (__state)
            {
                Pheromones.Attract(__instance);
            }
        }
    }
}
