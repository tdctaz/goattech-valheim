using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// The five points a Ship measures the water at, and whether the server holds the zones those
    /// points fall in.
    ///
    /// Ship.CustomFixedUpdate averages Floating.GetWaterLevel over the centre of mass and the four
    /// faces of the float collider, and Floating.GetWaterLevel answers -10000 where no WaterVolume
    /// collider covers the point. A WaterVolume comes from the zone prefab and is a 64 by 64 box
    /// tiling exactly one zone, so a hull wider or longer than a metre reaches into its neighbours,
    /// and any of those neighbours the server has not built takes the average down by two thousand
    /// metres. Both the gate that stops the hull and the diagnostic that explains why work from this
    /// one copy of the geometry, so that neither can drift from the other or from vanilla.
    /// </summary>
    internal static class HullWater
    {
        internal static readonly string[] PointNames = { "centre", "bow", "stern", "port", "starboard" };

        internal const int PointCount = 5;

        private static readonly Vector3[] Scratch = new Vector3[PointCount];

        /// <summary>
        /// Fills <paramref name="into"/> with the five points, in the order <see cref="PointNames"/>
        /// gives them. False means the hull has no float collider or no rigidbody, so there is
        /// nothing to measure and nothing to hold back.
        /// </summary>
        internal static bool TryPoints(Ship ship, Vector3[] into)
        {
            BoxCollider hull = ship.m_floatCollider;
            Rigidbody body = ship.m_body;
            if (hull == null || body == null || into == null || into.Length < PointCount)
            {
                return false;
            }

            Transform transform = hull.transform;
            Vector3 size = hull.size;
            Vector3 centre = transform.position;
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            into[0] = body.worldCenterOfMass;
            into[1] = centre + forward * size.z / 2f;
            into[2] = centre - forward * size.z / 2f;
            into[3] = centre - right * size.x / 2f;
            into[4] = centre + right * size.x / 2f;
            return true;
        }

        /// <summary>
        /// Whether every zone the hull is about to measure exists, and therefore has a water volume
        /// in it to find. This asks about zones rather than about water deliberately: a beached hull
        /// also reads -10000, and there vanilla's answer, falling, is the correct one.
        /// </summary>
        internal static bool Measurable(Ship ship)
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null)
            {
                return false;
            }

            if (!TryPoints(ship, Scratch))
            {
                return true;
            }

            for (int i = 0; i < PointCount; i++)
            {
                if (!zones.IsZoneLoaded(Scratch[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
