using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.UnsummonMaxInstances))]
    internal static class Tameable_UnsummonMaxInstances_Patch
    {
        private static void Prefix(Tameable __instance, int maxInstances)
        {
            if (!Plugin.ServerActive || Player.m_localPlayer != null)
            {
                return;
            }

            ZNetView view = __instance.m_nview;
            Character character = __instance.m_character;
            if (view == null || !view.IsValid() || !view.IsOwner() || !character || !__instance.m_monsterAI)
            {
                return;
            }

            GameObject followTarget = __instance.m_monsterAI.GetFollowTarget();
            Player summoner = followTarget != null ? followTarget.GetComponent<Player>() : null;
            if (summoner == null)
            {
                return;
            }

            string name = summoner.GetPlayerName();
            int count = 0;
            foreach (Character item in Character.GetAllCharacters())
            {
                if (item.m_name != character.m_name || item.GetComponent<MonsterAI>() == null)
                {
                    continue;
                }

                ZNetView itemView = item.GetComponent<ZNetView>();
                ZDO itemZdo = itemView != null ? itemView.GetZDO() : null;
                if ((itemZdo != null ? itemZdo.GetString(ZDOVars.s_follow) : "") == name)
                {
                    count++;
                }
            }

            if (count <= maxInstances)
            {
                return;
            }

            summoner.Message(MessageHud.MessageType.Center, __instance.m_maxSummonReached);
            Plugin.Log.LogInfo(
                $"{name} has {count} {Utils.GetPrefabName(__instance.gameObject)} following against a limit of {maxInstances}; " +
                "the oldest are unsummoned and the limit message sent to their client.");
        }
    }
}
