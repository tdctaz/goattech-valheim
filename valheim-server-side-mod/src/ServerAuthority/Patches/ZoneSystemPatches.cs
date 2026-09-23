using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// Gives the server real, simulated zones around every player.
    ///
    /// Vanilla builds local zones (terrain, heightmaps, colliders, vegetation) only around the
    /// local reference position, and around remote players it builds ghost zones, which generate
    /// the zone's data and then immediately destroy the GameObjects. A dedicated server therefore
    /// has no ground to stand on anywhere a player actually is, which is why it cannot simulate
    /// even when it holds ownership.
    /// </summary>
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Update))]
    internal static class ZoneSystem_Update_Patch
    {
        private static bool Prefix(ZoneSystem __instance)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            __instance.m_lastFixedTime = Time.fixedTime;

            if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
            {
                return false;
            }

            ServerTickRate.Apply();
            RefreshSimulationDistance(__instance);

            // World location generation still has to finish before anything else happens. A
            // headless server never shows the intro, so it always gets the plain time budget.
            if (!__instance.LocationsGenerated)
            {
                __instance.m_timeSlicedGenerationTimeBudget = 0.1f;
                return false;
            }

            __instance.m_updateTimer += Time.deltaTime;
            if (__instance.m_updateTimer <= 0.1f)
            {
                return false;
            }

            __instance.m_updateTimer = 0f;

            List<Anchor> anchors = SimulationAnchors.Current;

            // Every near zone has its time to live reset before anything is built, because
            // CreateLocalZones stops walking its list the moment it spawns one and leaves the rest
            // of that list unpoked. See KeepNearZonesAlive.
            KeepNearZonesAlive(__instance, anchors);

            // CreateLocalZones builds at most one zone per call, which is the game's own throttle.
            // Calling it once per player lets zone loading keep pace with the number of players
            // rather than with a single reference position.
            bool builtAZone = false;
            for (int i = 0; i < anchors.Count; i++)
            {
                builtAZone |= __instance.CreateLocalZones(anchors[i].Position);
            }

            // Ghost zones generate the outer ring that local zones do not reach, so distant objects
            // exist for clients looking at the horizon. Vanilla only does this once the local zones
            // are satisfied, and so do we.
            if (!builtAZone)
            {
                for (int i = 0; i < anchors.Count; i++)
                {
                    if (__instance.CreateGhostZones(anchors[i].Position))
                    {
                        break;
                    }
                }
            }

            UpdateTTL(__instance, anchors.Count);
            __instance.UpdatePrefabLifetimes();
            return false;
        }


        /// <summary>
        /// Resets the time to live of every zone inside a player's near radius, so that none of them
        /// can be evicted while a player is standing in range of it.
        ///
        /// Vanilla leaks here, and the leak is what destroys moored boats. CreateLocalZones walks the
        /// near zones resetting each one's ttl through PokeLocalZone, but it returns the instant it
        /// spawns a zone, so everything after that point in its iteration keeps ageing. A player who
        /// is moving spawns a zone almost every tick, which starves the tail of the list
        /// indefinitely. UpdateTTL then destroys any zone older than m_zoneTTL, four seconds, whose
        /// sector holds no instance, and ZNetScene.HaveInstanceInSector only protects a zone that
        /// *contains* something. An open stretch of water next to a moored boat contains nothing, so
        /// it is exactly the kind of zone that gets thrown away.
        ///
        /// Losing it takes the WaterVolume with it, and then Floating.GetWaterLevel answers -10000
        /// under whichever part of the hull hung over that zone. Ship.CustomFixedUpdate averages its
        /// five samples, decides the hull is two kilometres above the sea, and skips its entire
        /// buoyancy block. It skips the only WakeUp call in the class along with it, so a sleeping
        /// hull merely sits there, which is why this is invisible in calm water. In a storm the waves
        /// keep the body awake, and an awake body with no buoyancy simply falls: five metres to the
        /// sea floor here, arriving fast enough for ImpactEffect to charge it full price.
        ///
        /// The fix is to poke before building rather than while building. Iterating the near zones
        /// costs a dictionary lookup each and runs ten times a second, against the alternative of a
        /// boat occasionally destroying itself.
        /// </summary>
        private static void KeepNearZonesAlive(ZoneSystem zoneSystem, List<Anchor> anchors)
        {
            int near = zoneSystem.m_simulationDistance.NearSimulationDistance;
            bool classic = zoneSystem.m_simulationDistance.IsClassic;

            for (int i = 0; i < anchors.Count; i++)
            {
                Vector2s centre = anchors[i].Zone;
                for (int y = centre.y - near; y <= centre.y + near; y++)
                {
                    for (int x = centre.x - near; x <= centre.x + near; x++)
                    {
                        Vector2s candidate = new Vector2s((short)x, (short)y);
                        if (!classic && !zoneSystem.ZonesWithinRadius(centre, candidate, near))
                        {
                            continue;
                        }

                        // Deliberately not PokeLocalZone: that spawns a zone it does not find, and
                        // calling it across the whole near radius would build a neighbourhood in one
                        // frame instead of one zone per tick, which is the throttle that keeps the
                        // server responsive while a player explores. Only the ttl of a zone that
                        // already exists is touched here.
                        if (zoneSystem.m_zones.TryGetValue(candidate, out var data))
                        {
                            data.m_ttl = 0f;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// ZoneSystem keeps its own copy of the simulation distance and only refreshes it from
        /// ZNet.ApplySimulationDistance, which skips the call when ZoneSystem.instance does not
        /// exist yet. On a server the simulation distance settles during startup, before ZoneSystem
        /// is there to receive it, so that copy can be left at the default of zero. Everything that
        /// sizes the loaded region reads it, so the server would build only the single zone each
        /// player stands in and simulate almost nothing. Comparing two small structs per tick is
        /// cheap enough to simply keep it correct rather than reason about the ordering.
        /// </summary>
        private static void RefreshSimulationDistance(ZoneSystem zoneSystem)
        {
            SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
            if (zoneSystem.m_simulationDistance.Equals(synced))
            {
                return;
            }

            zoneSystem.ApplySettings();
            Plugin.Log.LogInfo(
                $"Simulation distance changed to near {synced.NearSimulationDistance} " +
                $"far {synced.FarSimulationDistance} classic {synced.IsClassic}.");
        }

        /// <summary>
        /// Vanilla's UpdateTTL unloads at most one expired zone per call, which was sized for a
        /// single player. With several players spread out, the server creates zones faster than
        /// that and the surplus is never reclaimed. Since each call unloads one zone and ages every
        /// zone by dt, we age once and then make extra zero-length calls to catch up.
        /// </summary>
        private static void UpdateTTL(ZoneSystem zoneSystem, int playerCount)
        {
            zoneSystem.UpdateTTL(0.1f);

            int configured = ModConfig.ZoneEvictionsPerTick.Value;
            int budget = configured > 0 ? configured : Mathf.Max(1, playerCount);

            for (int i = 1; i < budget; i++)
            {
                int before = zoneSystem.m_zones.Count;
                zoneSystem.UpdateTTL(0f);
                if (zoneSystem.m_zones.Count == before)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Vanilla asks whether the area around the local reference position is loaded. On a server
    /// that area is the origin and is never built, so the answer would be a permanent no.
    /// </summary>
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.IsActiveAreaLoaded))]
    internal static class ZoneSystem_IsActiveAreaLoaded_Patch
    {
        private static bool Prefix(ZoneSystem __instance, ref bool __result)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            List<Anchor> anchors = SimulationAnchors.Current;
            int near = __instance.m_simulationDistance.NearSimulationDistance;
            bool classic = __instance.m_simulationDistance.IsClassic;

            for (int i = 0; i < anchors.Count; i++)
            {
                Vector2s zone = anchors[i].Zone;
                for (int y = zone.y - near; y <= zone.y + near; y++)
                {
                    for (int x = zone.x - near; x <= zone.x + near; x++)
                    {
                        Vector2s candidate = new Vector2s((short)x, (short)y);
                        if (!classic && !__instance.ZonesWithinRadius(zone, candidate, near))
                        {
                            continue;
                        }

                        if (!__instance.m_zones.ContainsKey(candidate))
                        {
                            __result = false;
                            return false;
                        }
                    }
                }
            }

            __result = true;
            return false;
        }
    }
}
