using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    /// <summary>
    /// The contract another mod uses to see the raids RaidWaves is running on the server, where it
    /// takes a raid over from vanilla before RandEventSystem.m_randomEvent is ever set and vanilla
    /// itself never learns one is active. Server Authority binds to this by reflection so its
    /// per-position weather also finds a Creatures raid, not only a vanilla one; see
    /// ServerAuthority.CreaturesRaids and LocalWeather.RaidEnvironment there.
    ///
    /// Only BCL and Unity/game types cross this boundary, and only raids whose template forces an
    /// environment are returned, since a raid with no forced environment cannot change anyone's
    /// weather. Do not change this signature without checking that caller.
    /// </summary>
    public static class RaidWeather
    {
        /// <summary>
        /// Fills the supplied lists, one entry per active raid with a forced environment, and
        /// returns how many. All five lists must be non-null; they are cleared and refilled in
        /// place, so the caller can reuse the same lists on every call without allocating, and
        /// index i in every list describes the same raid: its centre, the range vanilla's
        /// IsInsideRandomEventArea would test against, the biome mask vanilla's InEventBiome would
        /// test against, the environment it forces, and its name for logging.
        /// </summary>
        public static int Get(List<Vector3> positions, List<float> ranges, List<Heightmap.Biome> biomes,
            List<string> environments, List<string> names)
        {
            return RaidDirector.FillWeather(positions, ranges, biomes, environments, names);
        }
    }
}
