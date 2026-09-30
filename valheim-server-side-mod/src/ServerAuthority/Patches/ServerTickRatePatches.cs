using UnityEngine;

namespace ServerAuthority.Patches
{
    internal static class ServerTickRate
    {
        internal static void Apply()
        {
            int desired = ModConfig.ServerFrameRate.Value;
            if (desired <= 0)
            {
                return;
            }

            if (Application.targetFrameRate != desired)
            {
                Application.targetFrameRate = desired;
                Plugin.Log.LogInfo($"Server frame rate set to {desired}.");
            }
        }
    }
}
