using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Ship), nameof(Ship.TakeAshlandsDamage))]
    internal static class Ship_TakeAshlandsDamage_Patch
    {
        private static void Postfix(Ship __instance)
        {
            if (!Plugin.ServerActive)
            {
                return;
            }

            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem == null || zoneSystem.GetGlobalKey(GlobalKeys.AshlandsOcean) || __instance.m_ashlandsReady)
            {
                return;
            }

            Vector3 position = __instance.transform.position;
            if (WorldGenerator.GetAshlandsOceanGradient(position) < 0f)
            {
                return;
            }

            Player player = PlayerNear(position);
            if (player == null)
            {
                return;
            }

            zoneSystem.SetGlobalKey(GlobalKeys.AshlandsOcean);
            Plugin.Log.LogInfo(
                $"{Utils.GetPrefabName(__instance.gameObject)} took Ashlands ocean damage at {position} with " +
                $"{player.GetPlayerName()} nearby, so the server set the {GlobalKeys.AshlandsOcean} key for Hugin's hint.");
        }

        private static Player PlayerNear(Vector3 position)
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                ZNetView view = player.m_nview;
                if (view != null && view.IsValid() && ZNetScene.InActiveArea(position, player.transform.position))
                {
                    return player;
                }
            }

            return null;
        }
    }
}
