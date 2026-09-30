using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Pet), nameof(Pet.UpdateMaterial))]
    internal static class Pet_UpdateMaterial_Patch
    {
        private static bool Prefix(Pet __instance)
        {
            if (Plugin.ServerActive)
            {
                return Player.m_localPlayer != null;
            }

            if (__instance.m_materialVariation == null || !PetFaces.IsServerOwned(__instance))
            {
                return true;
            }

            PetFaces.UpdateLocally(__instance);
            return false;
        }
    }
}
