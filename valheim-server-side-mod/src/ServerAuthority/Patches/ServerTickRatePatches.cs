using HarmonyLib;
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

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))]
    internal static class ZDOMan_SendZDOToPeers2_Patch
    {
        private static bool Prefix(ZDOMan __instance, float dt)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            if (__instance.m_peers.Count == 0)
            {
                return false;
            }

            __instance.m_sendTimer += dt;

            if (__instance.m_nextSendPeer < 0)
            {
                if (__instance.m_sendTimer > 1f / ModConfig.ZdoSendRate.Value)
                {
                    __instance.m_nextSendPeer = 0;
                    __instance.m_sendTimer = 0f;
                }

                return false;
            }

            if (__instance.m_nextSendPeer < __instance.m_peers.Count)
            {
                __instance.SendZDOs(__instance.m_peers[__instance.m_nextSendPeer], flush: false);
            }

            __instance.m_nextSendPeer++;
            if (__instance.m_nextSendPeer >= __instance.m_peers.Count)
            {
                __instance.m_nextSendPeer = -1;
            }

            return false;
        }
    }
}
