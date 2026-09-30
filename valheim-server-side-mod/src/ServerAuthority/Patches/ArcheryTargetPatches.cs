using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(ArcheryTarget), nameof(ArcheryTarget.OnProjectileHit))]
    internal static class ArcheryTarget_OnProjectileHit_Patch
    {
        private static bool _logged;

        internal static void Reset()
        {
            _logged = false;
        }

        private static bool Prefix(ArcheryTarget __instance, Character owner, Projectile projectile, Vector3 hitPoint,
            ref bool __result)
        {
            if (!Plugin.ServerActive || Player.m_localPlayer != null)
            {
                return true;
            }

            __instance.m_lastHitPos = hitPoint;
            if (__instance.m_projectileStayTTL >= 0f)
            {
                projectile.SetStayTTL(__instance.m_projectileStayTTL);
            }

            float distance = Vector3.Distance(__instance.m_center.transform.position, hitPoint) / __instance.m_targetSize;
            int points = Mathf.Max(0, Mathf.CeilToInt((1f - distance) * __instance.m_points));
            int ammoIndex = __instance.FindAmmoIndex(projectile);
            if ((bool)__instance.m_nview)
            {
                if (__instance.m_nview.IsOwner())
                {
                    __instance.ProjectileHit(points, ammoIndex, hitPoint);
                }
                else
                {
                    __instance.m_nview.InvokeRPC("RPC_ProjectileHit", points, ammoIndex, hitPoint);
                }
            }

            foreach (ProjectileTypeEffect hitEffect in __instance.m_projectileHitEffects)
            {
                if (projectile.m_type.HasFlag(hitEffect.m_type))
                {
                    hitEffect.m_effect.Create(hitPoint, __instance.transform.rotation);
                }
            }

            if (__instance.m_raiseSkillMultiplier > 0f && owner != null)
            {
                owner.RaiseSkill(projectile.m_skill,
                    projectile.m_raiseSkillAmount * __instance.m_raiseSkillMultiplier * (1f - distance));
            }

            if (!_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo(
                    $"{Utils.GetPrefabName(projectile.gameObject)} hit an archery target on the server for {points} points; " +
                    "the score is kept without the score message, which only a player's own client can show. " +
                    "Later server-side hits are not logged.");
            }

            __result = !__instance.m_killProjectile;
            return false;
        }
    }
}
