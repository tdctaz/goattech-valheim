using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.Update))]
    internal static class SEMan_Update_Patch
    {
        private static void Postfix(SEMan __instance, ZDO zdo)
        {
            if (Plugin.ServerActive || !(__instance.m_character is Player) || zdo == null)
            {
                return;
            }

            PlayerEffects.Publish(__instance, zdo);
        }
    }
}
