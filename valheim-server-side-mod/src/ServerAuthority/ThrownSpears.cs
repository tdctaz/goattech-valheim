using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class ThrownSpears
    {
        private static readonly List<Projectile> InFlight = new List<Projectile>();

        internal static void Reset()
        {
            InFlight.Clear();
        }

        internal static void Thrown(Projectile projectile)
        {
            if (IsSpearInFlight(projectile))
            {
                InFlight.Add(projectile);
            }
        }

        internal static bool HoldIfExpiring(Projectile projectile)
        {
            if (projectile.m_ttl <= 0f || projectile.m_ttl - Time.fixedDeltaTime > 0f || !IsSpearInFlight(projectile))
            {
                return false;
            }

            projectile.m_ttl = Time.fixedDeltaTime * 2f;
            return true;
        }

        internal static void LandAfterLastStep(Projectile projectile)
        {
            if (!IsSpearInFlight(projectile))
            {
                return;
            }

            Land(projectile, "its flight time ran out", true);
            ZNetScene.instance.Destroy(projectile.gameObject);
        }

        internal static void LandIfLeaving(List<ZDO> near, List<ZDO> distant)
        {
            for (int i = InFlight.Count - 1; i >= 0; i--)
            {
                Projectile projectile = InFlight[i];
                if (!IsSpearInFlight(projectile))
                {
                    InFlight.RemoveAt(i);
                    continue;
                }

                ZDO zdo = projectile.m_nview.GetZDO();
                if (near.Contains(zdo) || (distant != null && distant.Contains(zdo)))
                {
                    continue;
                }

                InFlight.RemoveAt(i);
                Land(projectile, "it left the area loaded around its thrower", false);
            }
        }

        private static bool IsSpearInFlight(Projectile projectile)
        {
            if (projectile == null || projectile.m_didHit)
            {
                return false;
            }

            ItemDrop.ItemData item = projectile.m_spawnItem;
            ZNetView view = projectile.m_nview;
            return item != null && item.m_dropPrefab != null && item.m_shared.m_skillType == Skills.SkillType.Spears &&
                view != null && view.IsValid() && view.IsOwner();
        }

        private static void Land(Projectile projectile, string reason, bool keepMoving)
        {
            ItemDrop.ItemData item = projectile.m_spawnItem;
            projectile.m_spawnItem = null;
            string name = item.m_dropPrefab.name;

            try
            {
                Transform transform = projectile.transform;
                Vector3 position = transform.position + transform.TransformDirection(projectile.m_spawnOffset);
                ItemDrop drop = ItemDrop.DropItem(item, 1, position, transform.rotation);
                Rigidbody body = drop.GetComponent<Rigidbody>();
                if (keepMoving && body != null)
                {
                    body.linearVelocity = projectile.m_vel;
                }

                Character thrower = projectile.m_owner;
                string from = thrower != null
                    ? $"{Vector3.Distance(thrower.transform.position, position):F0} m from {thrower.GetHoverName()}"
                    : "with no thrower known";
                string crafter = string.IsNullOrEmpty(item.m_crafterName) ? "" : $", made by {item.m_crafterName}";
                string after = keepMoving
                    ? $"still moving {projectile.m_vel.magnitude:F0} m/s"
                    : "where it waits at rest until somebody loads that area";
                Plugin.Log.LogInfo(
                    $"Thrown {name} (quality {item.m_quality}{crafter}) was still flying {from} when {reason}, so " +
                    $"it was dropped as an item at {position.ToString("F1")}, {after}, instead of being deleted.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Thrown {name} could not be dropped as an item when {reason}, so it is lost: {e}");
            }
        }
    }
}
