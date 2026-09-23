#if DEBUG_TOOLS
using System.Collections.Generic;
using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate_Diagnostic
    {
        private static readonly Dictionary<int, long> LastOwner = new Dictionary<int, long>();
        private static double _lastLog;

        internal static void Forget(Ship ship)
        {
            LastOwner.Remove(ship.GetInstanceID());
        }

        internal static void Forget()
        {
            LastOwner.Clear();
        }

        private static void Postfix(Ship __instance)
        {
            if (!Plugin.ServerActive || !ModConfig.LogShipState.Value)
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

            int key = __instance.GetInstanceID();
            bool changed = !LastOwner.TryGetValue(key, out long previous) || previous != owner;
            LastOwner[key] = owner;

            double now = OwnershipPolicy.Clock;
            if (!changed && now - _lastLog < 0.5)
            {
                return;
            }

            _lastLog = now;

            WaterVolume probe = null;
            float water = Floating.GetWaterLevel(__instance.transform.position, ref probe);
            float velocity = __instance.m_body != null ? __instance.m_body.linearVelocity.magnitude : 0f;
            bool kinematic = __instance.m_body != null && __instance.m_body.isKinematic;
            WearNTear wear = __instance.GetComponent<WearNTear>();

            string who = owner == server ? "SERVER" : (owner == 0L ? "nobody" : owner.ToString());
            string previousWho = previous == server ? "SERVER" : (previous == 0L ? "nobody" : previous.ToString());

            Plugin.Log.LogInfo(
                $"Ship {zdo.m_uid}: owner={who}" +
                (changed ? $" CHANGED from {previousWho}" : string.Empty) +
                $" isOwner={zdo.IsOwner()} crew={__instance.m_players.Count}" +
                $" speed={__instance.m_speed} rudder={__instance.m_rudderValue:0.00}" +
                $" y={__instance.transform.position.y:0.00} water={water:0.00}" +
                $" vel={velocity:0.0} kinematic={kinematic}" +
                $" upY={__instance.transform.up.y:0.00}" +
                (wear != null ? $" health={wear.GetHealthPercentage() * 100f:0}%" : string.Empty) +
                $" zonesUnderHull={(HullWater.Measurable(__instance) ? "all loaded" : "INCOMPLETE")}");
        }
    }
}
#endif
