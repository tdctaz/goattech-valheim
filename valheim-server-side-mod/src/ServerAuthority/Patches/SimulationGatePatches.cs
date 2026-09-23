using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// Vanilla refuses to spawn anything unless Player.m_localPlayer exists, which is never true
    /// on a headless server. Once the server owns the zone controllers it has to be allowed to run
    /// their spawn lists, otherwise the world simply stops producing creatures.
    ///
    /// The spawn lists run inside a <see cref="WeatherScope"/> for this spawner's own zone, because
    /// UpdateSpawnList tests m_requiredEnvironments against EnvMan's current environment. Six
    /// spawners are weather gated (the Neck in rain, Draugr in mist, the Serpent in storms and
    /// three Ashlands cinder spawners), and with one global weather they all followed a single
    /// player's sky.
    ///
    /// The old Serverside Simulations mod did this with an IL transpiler that flipped the branch.
    /// That is the single biggest reason it broke on almost every game update. Replacing the whole
    /// method instead fails loudly and obviously when the original changes, and is easy to diff
    /// against a fresh decompilation.
    /// </summary>
    [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.UpdateSpawning))]
    internal static class SpawnSystem_UpdateSpawning_Patch
    {
        private static bool Prefix(SpawnSystem __instance)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            if (!__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner())
            {
                return false;
            }

            SpawnSystem.m_tempNearPlayers.Clear();
            __instance.GetPlayersInZone(SpawnSystem.m_tempNearPlayers);
            if (SpawnSystem.m_tempNearPlayers.Count == 0)
            {
                return false;
            }

            DateTime time = ZNet.instance.GetTime();
            WeatherScope weather = WeatherScope.Weather(__instance.transform.position);
            try
            {
                for (int i = 0; i < __instance.m_spawnLists.Count; i++)
                {
                    __instance.UpdateSpawnList(__instance.m_spawnLists[i].m_spawners, time, eventSpawners: false, "b_");
                }

                List<SpawnSystem.SpawnData> eventSpawners = GetEventSpawners(__instance);
                if (eventSpawners != null)
                {
                    __instance.UpdateSpawnList(eventSpawners, time, eventSpawners: true, "e_");
                }

                for (int i = 0; i < __instance.m_heightmap.m_cornerAltBiomes.Count; i++)
                {
                    AltBiome altBiome = __instance.m_heightmap.m_cornerAltBiomes[i];
                    if (altBiome.m_spawn.Count > 0)
                    {
                        __instance.UpdateSpawnList(altBiome.m_spawn, time, eventSpawners: false, $"m{i}");
                    }
                }
            }
            finally
            {
                weather.Exit();
            }

            return false;
        }

        /// <summary>
        /// Vanilla decides which event spawners are live from whether the local player is inside the
        /// event area, which collapses to "no event spawners, ever" on a server. We ask instead
        /// whether any of the players already known to be in this spawner's own zone is inside the
        /// event area, which keeps raid spawning local to the raid rather than global.
        /// </summary>
        private static List<SpawnSystem.SpawnData> GetEventSpawners(SpawnSystem spawnSystem)
        {
            RandEventSystem events = RandEventSystem.instance;
            if (events == null || events.m_randomEvent == null || events.m_activeEvent == null)
            {
                return null;
            }

            List<Player> nearby = SpawnSystem.m_tempNearPlayers;
            for (int i = 0; i < nearby.Count; i++)
            {
                if (events.IsInsideRandomEventArea(events.m_randomEvent, nearby[i].transform.position))
                {
                    return events.GetCurrentSpawners();
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The active event is what makes raid spawners live, and vanilla only ever sets it by testing
    /// the local player's position. On a server that test never passes, so m_activeEvent stays null,
    /// GetCurrentSpawners always returns null, and raids make noise but never send anything.
    /// The replacement drives the active event from whether any player is in the area.
    ///
    /// The active event is only what makes spawners live. Its weather is not applied from here:
    /// RandEventSystem_GetEnvOverride_Patch keeps it off the server's global weather, and
    /// <see cref="LocalWeather"/> applies it only inside the raid's area and biome.
    /// </summary>
    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.FixedUpdate))]
    internal static class RandEventSystem_FixedUpdate_Patch
    {
        private static bool Prefix(RandEventSystem __instance)
        {
            if (!Plugin.ServerActive)
            {
                return true;
            }

            float dt = Time.fixedDeltaTime;
            __instance.UpdateForcedEvents(dt);
            __instance.UpdateRandomEvent(dt);

            if (__instance.m_forcedEvent != null)
            {
                __instance.m_forcedEvent.Update(
                    server: true,
                    active: __instance.m_forcedEvent == __instance.m_activeEvent,
                    playerInArea: true,
                    dt: dt);
            }

            bool playerInRandomEventArea = false;
            if (__instance.m_randomEvent != null)
            {
                playerInRandomEventArea = __instance.IsAnyPlayerInEventArea(__instance.m_randomEvent);
                if (__instance.m_randomEvent.Update(
                        server: true,
                        active: __instance.m_randomEvent == __instance.m_activeEvent,
                        playerInArea: playerInRandomEventArea,
                        dt: dt))
                {
                    __instance.SetRandomEvent(null, Vector3.zero);
                }
            }

            if (__instance.m_forcedEvent != null)
            {
                __instance.SetActiveEvent(__instance.m_forcedEvent);
            }
            else if (__instance.m_randomEvent != null && playerInRandomEventArea)
            {
                __instance.SetActiveEvent(__instance.m_randomEvent);
            }
            else
            {
                __instance.SetActiveEvent(null);
            }

            return false;
        }
    }
}
