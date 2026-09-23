using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    internal static class LocalWeatherPatches
    {
        internal static bool Owned(ZNetView nview)
        {
            return nview != null && nview.IsValid() && nview.IsOwner();
        }
    }

    /// <summary>
    /// Rain wear. UpdateWear decides whether a roofless piece is being rained on from EnvMan.IsWet,
    /// and in the Deep North how fast snow builds up from the current environment, so on a server
    /// with one global weather every roofless piece in the world wore down in one player's rain.
    /// Scoped to the piece's own position. See <see cref="WeatherScope"/> for why the consumer is
    /// wrapped rather than rewritten.
    ///
    /// UpdateWear runs for every piece in the world every second and does nothing unless the piece
    /// is owned here and past its settling time, so the scope is only opened when it will be read.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    internal static class WearNTear_UpdateWear_Weather
    {
        private static void Prefix(WearNTear __instance, float time, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled && LocalWeatherPatches.Owned(__instance.m_nview) &&
                      __instance.ShouldUpdate(time)
                ? WeatherScope.Weather(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// The roof check. UpdateCover only looks for a roof while it is wet or in the Deep North, and
    /// a piece that never looked keeps whatever it last found, so this has to agree with
    /// UpdateWear about whether it is raining where the piece stands.
    ///
    /// It is called for every piece every second and only looks at the weather once its timer
    /// passes four seconds, so the scope is opened only on that call, by the same test vanilla
    /// makes after adding the step.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateCover))]
    internal static class WearNTear_UpdateCover_Weather
    {
        private static void Prefix(WearNTear __instance, float dt, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled && !(__instance.m_updateCoverTimer + dt <= 4f)
                ? WeatherScope.Weather(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A loose fire, such as burning grass or a campfire's spread, destroys itself in the rain when
    /// it has no roof. With one global weather, a raid's rain elsewhere put out every one of them.
    /// </summary>
    [HarmonyPatch(typeof(Fire), nameof(Fire.UpdateFire))]
    internal static class Fire_UpdateFire_Weather
    {
        private static void Prefix(Fire __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled
                ? WeatherScope.Weather(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A fireplace goes out when it is wet and has no roof, or when the wind is at 0.8 or more and
    /// it has little cover. Both read the global EnvMan, so on a server a raid's rain or a storm
    /// around one player put out every uncovered fire in the world. Scoped to the fireplace's own
    /// weather and wind.
    /// </summary>
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.CheckWet))]
    internal static class Fireplace_CheckWet_Weather
    {
        private static void Prefix(Fireplace __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled
                ? WeatherScope.WeatherAndWind(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A burning fireplace sets light to what is next to it through Cinder.CanBurn, which refuses in
    /// the rain and under water. Both are read from the global weather and the global sea, so on a
    /// server one player's rain stopped every fireplace in the world from spreading. Scoped to the
    /// fireplace's own weather, wind and sea, on its owner, where it runs.
    /// </summary>
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UpdateIgnite))]
    internal static class Fireplace_UpdateIgnite_Weather
    {
        private static void Prefix(Fireplace __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled && LocalWeatherPatches.Owned(__instance.m_nview)
                ? WeatherScope.WeatherAndWind(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A cinder is offset against the wind when it is created, on its owner. Scoped to where it was
    /// created.
    /// </summary>
    [HarmonyPatch(typeof(Cinder), nameof(Cinder.Awake))]
    internal static class Cinder_Awake_Weather
    {
        private static void Prefix(Cinder __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled
                ? WeatherScope.WeatherAndWind(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A cinder drifts with the wind while it flies, and on landing refuses to light anything in
    /// the rain. Both happen inside FixedUpdate, on the owner. Scoped to where the cinder is.
    /// </summary>
    [HarmonyPatch(typeof(Cinder), nameof(Cinder.FixedUpdate))]
    internal static class Cinder_FixedUpdate_Weather
    {
        private static void Prefix(Cinder __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled && LocalWeatherPatches.Owned(__instance.m_nview)
                ? WeatherScope.WeatherAndWind(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A windmill's output is its uncovered fraction times the wind intensity, and the smelter it
    /// drives reads it on the owner to decide how fast to grind. GetPowerOutput is the one place
    /// the intensity is read, so scoping it covers the smelter's calls and the windmill's own
    /// animation alike.
    /// </summary>
    [HarmonyPatch(typeof(Windmill), nameof(Windmill.GetPowerOutput))]
    internal static class Windmill_GetPowerOutput_Weather
    {
        private static void Prefix(Windmill __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled
                ? WeatherScope.WeatherAndWind(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A fish swims to the wave surface through its WaterVolume and drifts with the wind it reads
    /// once per frame into shared statics. The server owns fish, so each owned fish gets the wind
    /// and sea at its own position, and the shared per-frame block is made to run again for it by
    /// clearing the frame it was last run on.
    /// </summary>
    [HarmonyPatch(typeof(Fish), nameof(Fish.CustomFixedUpdate))]
    internal static class Fish_CustomFixedUpdate_Weather
    {
        private static void Prefix(Fish __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled && LocalWeatherPatches.Owned(__instance.m_nview)
                ? WeatherScope.WeatherAndWind(__instance.transform.position)
                : default;
            if (__state.Active)
            {
                Fish.s_updatedFrame = -1;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// The leviathan does not float through a WaterVolume. Its owner asks Floating.GetLiquidLevel
    /// for the surface under it every fixed step and moves the body there, so on a server it rode
    /// the global sea instead of the sea at its position, and players climbing it saw it bob out of
    /// step with the water around it. Scoped to the sea at its position.
    /// </summary>
    [HarmonyPatch(typeof(Leviathan), nameof(Leviathan.FixedUpdate))]
    internal static class Leviathan_FixedUpdate_Weather
    {
        private static void Prefix(Leviathan __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled && LocalWeatherPatches.Owned(__instance.m_nview)
                ? WeatherScope.Water(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A wisp spawner (the wisp torch) only works at night, and Mistlands weather is always dark,
    /// so it reads EnvMan.IsDaylight, which on a server was one player's sky for every torch. The
    /// status is cached for four seconds, so this costs one lookup per torch per four seconds.
    /// </summary>
    [HarmonyPatch(typeof(WispSpawner), nameof(WispSpawner.GetStatus))]
    internal static class WispSpawner_GetStatus_Weather
    {
        private static void Prefix(WispSpawner __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled && Time.time - __instance.m_lastStatusUpdate >= 4f
                ? WeatherScope.Weather(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A lured wisp gives up and leaves in daylight. Its owner decides every two seconds from
    /// EnvMan.IsDaylight; scoped to the wisp's own position.
    /// </summary>
    [HarmonyPatch(typeof(LuredWisp), nameof(LuredWisp.UpdateTarget))]
    internal static class LuredWisp_UpdateTarget_Weather
    {
        private static void Prefix(LuredWisp __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled && LocalWeatherPatches.Owned(__instance.m_nview)
                ? WeatherScope.Weather(__instance.transform.position)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// A ship the server simulates floats, and sails, in the wind and sea at its own position, from
    /// its own held anchors, with Moder's power turning that hull's wind to its heading. See
    /// LocalWind. The scope closes in a first-priority postfix, before the WaveSync diagnostic's
    /// postfix runs, so that the diagnostic still reports the server's global field and asks for
    /// each hull's own explicitly.
    /// </summary>
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate_Weather
    {
        private static void Prefix(Ship __instance, out WeatherScope __state)
        {
            __state = WeatherScope.Enabled && LocalWeatherPatches.Owned(__instance.m_nview)
                ? WeatherScope.Hull(__instance)
                : default;
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref WeatherScope __state)
        {
            __state.Exit();
        }

        private static void Finalizer(Exception __exception, ref WeatherScope __state)
        {
            __state.Exit();
        }
    }

    /// <summary>
    /// Lets <see cref="LocalWeather"/> find the EnvZones a player could be standing in. Vanilla
    /// only learns about them through the local player's trigger contact, which a server has for
    /// nobody.
    /// </summary>
    [HarmonyPatch(typeof(EnvZone), nameof(EnvZone.Awake))]
    internal static class EnvZone_Awake_Register
    {
        private static void Postfix(EnvZone __instance)
        {
            LocalWeather.Register(__instance);
        }
    }

    /// <summary>
    /// Floats everything in a WaterVolume on the sea at its own position.
    ///
    /// Floating objects, swimming characters and fish do not sample the water themselves. Once a
    /// frame, each WaterVolume computes the surface under everything inside it and hands it over
    /// through SetLiquidLevel, so the wind that surface is built from has to be swapped per floater
    /// inside this loop rather than around any one floater's own update.
    ///
    /// Replaced rather than transpiled, because the loop is short and the only change is the scope
    /// around one call; a replacement is diffed against a fresh decompilation in a minute, where a
    /// transpiler anchoring on that call would silently stop matching. Volumes that ignore the wind
    /// (m_useGlobalWind off) are left to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateFloaters))]
    internal static class WaterVolume_UpdateFloaters_Patch
    {
        private static bool Prefix(WaterVolume __instance)
        {
            if (!WeatherScope.Enabled || !__instance.m_useGlobalWind)
            {
                return true;
            }

            List<IWaterInteractable> inWater = __instance.m_inWater;
            int count = inWater.Count;
            if (count == 0)
            {
                return false;
            }

            List<int> remove = WaterVolume.s_inWaterRemoveIndices;
            remove.Clear();
            for (int i = 0; i < count; i++)
            {
                IWaterInteractable floater = inWater[i];
                if (floater == null)
                {
                    remove.Add(i);
                    continue;
                }

                Transform transform = floater.GetTransform();
                if (!transform)
                {
                    remove.Add(i);
                    continue;
                }

                Vector3 position = transform.position;
                WeatherScope scope = WeatherScope.Water(position);
                float surface;
                try
                {
                    surface = __instance.GetWaterSurface(position);
                }
                finally
                {
                    scope.Exit();
                }

                floater.SetLiquidLevel(surface, LiquidType.Water, __instance);
            }

            for (int i = remove.Count - 1; i >= 0; i--)
            {
                inWater.RemoveAt(remove[i]);
            }

            return false;
        }
    }
}
