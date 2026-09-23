using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// Records every hit a hull takes, with the hit type that caused it.
    ///
    /// This exists because of how the boat investigation went. A moored boat was destroyed with no
    /// enemy present, and the mechanism was then argued from first principles rather than measured:
    /// read Ship.CustomFixedUpdate, find that a missing water volume disables buoyancy, work out
    /// that the resulting fall reaches ImpactEffect's velocity cap, conclude that self inflicted
    /// impact damage must be the answer. That fault is real and reproducible, but when it was
    /// finally measured the window was sub-second and cost the hull nothing. Three mechanisms were
    /// proposed and eliminated. HitData carried the answer the whole time.
    ///
    /// The hook goes on ApplyDamage rather than Damage, and that distinction is the whole reason
    /// this file is worth reading. WearNTear.Damage only calls InvokeRPC("RPC_Damage", hit): it
    /// sends, and applies nothing. A player swinging a sword runs Damage on their own client, and
    /// the server runs RPC_Damage. So a hook on Damage records nothing at all on a dedicated server,
    /// which is exactly what the first version of this patch did while a hull was beaten from 97% to
    /// 51% health. ApplyDamage is where the health value actually changes, it runs on the owner, and
    /// it is reached by every path: hits arrive through RPC_Damage with their HitData, while rain,
    /// support and biome wear come straight from UpdateWear with hitData null. A null therefore
    /// means wear rather than a hit, which is a distinction worth having rather than a gap.
    ///
    /// RPC_Damage would work too and is deliberately not used: patching an RPC method has already
    /// killed this server once, and the README says not to unless nothing else will do.
    ///
    /// On by default, unlike the other debug logging, because a hull is hit only occasionally and
    /// the one time it matters is the time nobody was watching.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
    internal static class WearNTear_ApplyDamage_ShipPatch
    {
        private static void Prefix(WearNTear __instance, float damage, HitData hitData)
        {
            if (!Plugin.ServerActive || !ModConfig.LogShipDamage.Value || damage <= 0f)
            {
                return;
            }

            Ship ship = __instance.GetComponent<Ship>();
            if (ship == null)
            {
                return;
            }

            ZNetView nview = __instance.m_nview;
            if (nview == null || !nview.IsValid())
            {
                return;
            }

            ZDO zdo = nview.GetZDO();
            long owner = zdo.GetOwner();
            long server = ZDOMan.GetSessionID();
            string who = owner == server ? "SERVER" : (owner == 0L ? "nobody" : owner.ToString());

            float health = zdo.GetFloat(ZDOVars.s_health, __instance.m_health);
            Vector3 position = __instance.transform.position;

            string cause;
            if (hitData == null)
            {
                cause = "wear (no hit data: rain, support or biome)";
            }
            else
            {
                Character attacker = hitData.GetAttacker();
                cause =
                    $"hitType={hitData.m_hitType} attacker={(attacker != null ? attacker.name : "none")} " +
                    $"blunt={hitData.m_damage.m_blunt:0.0} chop={hitData.m_damage.m_chop:0.0} " +
                    $"fire={hitData.m_damage.m_fire:0.0} pierce={hitData.m_damage.m_pierce:0.0}";
            }

            Plugin.Log.LogInfo(
                $"Ship damage {zdo.m_uid} at ({position.x:0}, {position.y:0.00}, {position.z:0}): " +
                $"{damage:0.0} damage, {health:0.0} -> {health - damage:0.0} health, {cause}, " +
                $"owner={who} upY={__instance.transform.up.y:0.00} " +
                $"zonesUnderHull={(HullWater.Measurable(ship) ? "all loaded" : "INCOMPLETE")}");
        }
    }

    /// <summary>
    /// Notices a hull losing health even when the server never applied the damage itself.
    ///
    /// The ApplyDamage hook above cannot see everything, and the blind spot is the case that
    /// matters. WearNTear.Damage forwards to RPC_Damage, which runs on the ZDO's owner, so when a
    /// client owns a hull the client subtracts the health and the server only receives the new
    /// value. A server-side patch has nothing to hook. That is exactly the configuration a lost boat
    /// was running under, with ServerOwnsWaterborne off, so a hull sank to the sea floor and lost
    /// health with not one line recorded against it.
    ///
    /// Health lives in the ZDO though, and the server always has that. Watching it for a decrease
    /// costs one float comparison per hull per tick and reports the loss with the state that
    /// surrounds it, which is what attribution actually needs: whether the zones under the hull were
    /// complete, whether it was upright, and where it was in the water column. It cannot name a
    /// HitType, so it says so rather than guessing.
    /// </summary>
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_HealthWatch_Patch
    {
        private static readonly Dictionary<int, float> LastHealth = new Dictionary<int, float>();

        internal static void Forget(Ship ship)
        {
            LastHealth.Remove(ship.GetInstanceID());
        }

        internal static void Forget()
        {
            LastHealth.Clear();
        }

        private static void Postfix(Ship __instance)
        {
            if (!Plugin.ServerActive || !ModConfig.LogShipDamage.Value)
            {
                return;
            }

            ZNetView nview = __instance.m_nview;
            if (nview == null || !nview.IsValid())
            {
                return;
            }

            WearNTear wear = __instance.GetComponent<WearNTear>();
            if (wear == null)
            {
                return;
            }

            ZDO zdo = nview.GetZDO();
            float health = zdo.GetFloat(ZDOVars.s_health, wear.m_health);
            int key = __instance.GetInstanceID();

            if (!LastHealth.TryGetValue(key, out float previous))
            {
                LastHealth[key] = health;
                return;
            }

            LastHealth[key] = health;

            // Repairs and the initial full value move it upward, which is not what this is for.
            if (health >= previous - 0.01f)
            {
                return;
            }

            long owner = zdo.GetOwner();
            long server = ZDOMan.GetSessionID();
            Vector3 position = __instance.transform.position;
            float ground = ZoneSystem.instance != null
                ? ZoneSystem.instance.GetGroundHeight(position)
                : 0f;

            Plugin.Log.LogInfo(
                $"Ship health drop {zdo.m_uid} at ({position.x:0}, {position.y:0.00}, {position.z:0}): " +
                $"{previous:0.0} -> {health:0.0} ({previous - health:0.0} lost), " +
                $"owner={(owner == server ? "SERVER" : owner == 0L ? "nobody" : owner.ToString())}, " +
                $"upY={__instance.transform.up.y:0.00}, seabed {ground:0.0} " +
                $"({position.y - ground:0.0}m above it), " +
                $"zonesUnderHull={(HullWater.Measurable(__instance) ? "all loaded" : "INCOMPLETE")}. " +
                "No HitType available: if the owner is a client it applied the damage itself and the " +
                "server only saw the result.");
        }
    }
}
