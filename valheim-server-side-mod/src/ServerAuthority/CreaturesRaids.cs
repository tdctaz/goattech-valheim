using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// A soft link to Valheim Creatures' RaidWaves, which on a dedicated server intercepts a raid
    /// before RandEventSystem.m_randomEvent is ever set, so vanilla and everything that reads that
    /// field never learns a raid is running there. This assembly carries no reference to Creatures
    /// and loads the same with or without it: the plugin is found by its BepInEx GUID, its
    /// ValheimCreatures.RaidWeather.Get method is found by name and bound once to a delegate whose
    /// parameter types are only BCL and Unity/game types, and every call after that is a plain
    /// delegate call. If Creatures is not loaded, or its contract does not match, this reports no
    /// raids and <see cref="LocalWeather"/> falls back to vanilla's own m_randomEvent exactly as it
    /// did before Creatures existed.
    /// </summary>
    internal static class CreaturesRaids
    {
        private const string PluginGuid = "valheim.creatures";
        private const string TypeName = "ValheimCreatures.RaidWeather";
        private const string MethodName = "Get";

        private delegate int GetDelegate(List<Vector3> positions, List<float> ranges, List<Heightmap.Biome> biomes,
            List<string> environments, List<string> names);

        private static bool _resolved;
        private static GetDelegate _get;

        /// <summary>
        /// Fills the supplied lists exactly as <see cref="GetDelegate"/> promises, or clears them
        /// and returns 0 when Creatures is not loaded or its hook could not be bound.
        /// </summary>
        internal static int Get(List<Vector3> positions, List<float> ranges, List<Heightmap.Biome> biomes,
            List<string> environments, List<string> names)
        {
            if (!_resolved)
            {
                Resolve();
            }

            if (_get == null)
            {
                positions.Clear();
                ranges.Clear();
                biomes.Clear();
                environments.Clear();
                names.Clear();
                return 0;
            }

            return _get(positions, ranges, biomes, environments, names);
        }

        internal static void Reset()
        {
            _resolved = false;
            _get = null;
        }

        private static void Resolve()
        {
            _resolved = true;

            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out PluginInfo info) || info.Instance == null)
            {
                Plugin.Log.LogInfo("Creatures raid hook: Valheim Creatures is not loaded, server weather will only follow vanilla raids.");
                return;
            }

            Type type = info.Instance.GetType().Assembly.GetType(TypeName);
            MethodInfo method = type?.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                Plugin.Log.LogWarning(
                    $"Creatures raid hook: Valheim Creatures is loaded but {TypeName}.{MethodName} was not found, " +
                    "server weather will only follow vanilla raids. The two mods may be out of step with each other.");
                return;
            }

            try
            {
                _get = (GetDelegate)Delegate.CreateDelegate(typeof(GetDelegate), method);
            }
            catch (ArgumentException ex)
            {
                Plugin.Log.LogWarning(
                    $"Creatures raid hook: {TypeName}.{MethodName} has an unexpected signature ({ex.Message}), " +
                    "server weather will only follow vanilla raids. The two mods may be out of step with each other.");
                return;
            }

            Plugin.Log.LogInfo("Creatures raid hook: found, server weather will also follow Creatures raids.");
        }
    }
}
