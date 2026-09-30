using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using Steamworks;

namespace ServerAuthority
{
    internal static class SteamSendRate
    {
        private const int VanillaBytesPerSecond = 153600;

        private static int _applied;

        internal static void Reset()
        {
            _applied = 0;
        }

        internal static void Apply()
        {
            int kib = ModConfig.SteamSendRateKiB.Value;
            if (kib <= 0)
            {
                return;
            }

            int bytes = kib * 1024;
            bool changed = _applied != bytes;
            try
            {
                bool ok = Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, bytes) &
                    Set(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, bytes);
                _applied = bytes;
                if (!ok)
                {
                    Plugin.Log.LogWarning($"Steam refused the send rate of {kib} KiB/s per player.");
                }
                else if (changed)
                {
                    Plugin.Log.LogInfo(
                        $"Steam send rate set to {kib} KiB/s per player (vanilla {VanillaBytesPerSecond / 1024}).");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not set the Steam send rate: {e}");
            }
        }

        private static bool Set(ESteamNetworkingConfigValue key, int value)
        {
            GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
            try
            {
                return SteamGameServerNetworkingUtils.SetConfigValue(key,
                    ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero,
                    ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }
    }

    [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
    internal static class ZSteamSocket_RegisterGlobalCallbacks_Patch
    {
        private static void Postfix()
        {
            if (Plugin.ServerActive)
            {
                SteamSendRate.Apply();
            }
        }
    }
}
