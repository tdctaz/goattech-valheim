using HarmonyLib;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// Replaces vanilla's ownership assignment pass with the mod's policy.
    ///
    /// Vanilla runs ReleaseNearbyZDOS once per peer, and each of those calls scans the peer's whole
    /// active area, so overlapping players are scanned repeatedly and the last peer processed wins.
    /// The replacement walks each sector once and decides from the number of players covering it,
    /// which is both cheaper and the only way to express a policy that depends on player count.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ReleaseZDOS))]
    internal static class ZDOMan_ReleaseZDOS_Patch
    {
        private static bool Prefix(ZDOMan __instance, float dt)
        {
            if (!Plugin.ServerActive || ModConfig.Mode.Value == OwnershipMode.Vanilla)
            {
                return true;
            }

            OwnershipPolicy.Advance(dt);

            __instance.m_releaseZDOTimer += dt;
            if (__instance.m_releaseZDOTimer <= 2f)
            {
                return false;
            }

            __instance.m_releaseZDOTimer = 0f;
            OwnershipPolicy.Run(__instance);
            return false;
        }
    }
}
