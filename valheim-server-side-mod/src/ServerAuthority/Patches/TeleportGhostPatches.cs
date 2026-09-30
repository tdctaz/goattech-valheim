using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.InternalSetPosition))]
    internal static class ZDO_InternalSetPosition_Patch
    {
        private const float ReportedJumpMetres = 100f;

        private static void Prefix(ZDO __instance, out Vector3 __state)
        {
            __state = __instance.m_position;
        }

        private static void Postfix(ZDO __instance, Vector3 __state)
        {
            if (!Plugin.ServerActive || !ModConfig.ClearTeleportGhosts.Value)
            {
                return;
            }

            Vector3 position = __instance.m_position;
            if (ZoneSystem.GetZone(__state) == ZoneSystem.GetZone(position))
            {
                return;
            }

            ZDOMan man = ZDOMan.instance;
            Game game = Game.instance;
            if (man == null || game == null || game.PortalPrefabHash.Contains(__instance.GetPrefab()))
            {
                return;
            }

            int dropped = 0;
            foreach (ZDOMan.ZDOPeer peer in man.m_peers)
            {
                bool had = peer.m_zdos.ContainsKey(__instance.m_uid);
                peer.ZDOSectorInvalidated(__instance);
                if (had && !peer.m_zdos.ContainsKey(__instance.m_uid))
                {
                    dropped++;
                }
            }

            float jump = Vector3.Distance(__state, position);
            if (dropped > 0 && jump >= ReportedJumpMetres)
            {
                Plugin.Log.LogInfo(
                    $"Cleared a teleport ghost: told {dropped} player(s) to drop {Describe(__instance)} " +
                    $"at its old position after it moved {jump:F0} m.");
            }
        }

        private static string Describe(ZDO zdo)
        {
            ZNet znet = ZNet.instance;
            if (znet != null)
            {
                foreach (ZNetPeer peer in znet.m_peers)
                {
                    if (peer.m_characterID == zdo.m_uid)
                    {
                        return $"{peer.m_playerName}'s character";
                    }
                }
            }

            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
            return prefab != null ? prefab.name : zdo.GetPrefab().ToString();
        }
    }
}
