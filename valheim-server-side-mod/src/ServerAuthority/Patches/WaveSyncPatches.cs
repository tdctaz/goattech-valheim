using HarmonyLib;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// Drives the wave field diagnostic from the ship update, on a server and on a client alike.
    ///
    /// Deliberately not gated on Plugin.ServerActive. The whole question is whether two machines
    /// agree about the sea, which cannot be answered from one of them, and the client half of this
    /// mod is what makes the other log line exist.
    ///
    /// Ship.CustomFixedUpdate returns early for a non-owner, long before any buoyancy runs, but a
    /// postfix still gets called, which is exactly the machine whose view of the water is in
    /// question.
    /// </summary>
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate_WaveSync
    {
        private static void Postfix(Ship __instance)
        {
            if (!WaveSync.Due())
            {
                return;
            }

            WaveSync.LogField();
            WaveSync.LogFish();

            for (int i = 0; i < Ship.Instances.Count; i++)
            {
                if (Ship.Instances[i] is Ship ship)
                {
                    WaveSync.LogHull(ship);
                }
            }
        }
    }
}
