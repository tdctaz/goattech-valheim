using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// Where a headless server should stand when the game asks it a question that assumes a
    /// viewpoint.
    ///
    /// EnvMan decides the weather, and therefore the wind range that sets wave height, from the
    /// main camera's position. A dedicated server has a camera object but nothing ever moves it,
    /// because GameCamera.UpdateCamera returns as soon as it finds no local player, so every such
    /// question is answered for the world origin.
    ///
    /// The lowest connected peer id is used rather than the nearest player to anything, or simply
    /// the first in the list. Nearest has no meaning for a single global like wind, and the list
    /// order changes as peers come and go, which would make the server's weather flip for no
    /// reason. The lowest id is stable for as long as that player stays connected.
    ///
    /// This is now only the fallback. With ServerWeatherPerPosition on, everything the server
    /// simulates asks <see cref="LocalWeather"/> for its own position's weather, and the global
    /// weather taken here is left for the few readers that are not wrapped and for the log.
    /// </summary>
    internal static class ServerViewpoint
    {
        private static Vector3 _position;
        private static bool _have;
        private static int _frame = -1;

        internal static void Reset()
        {
            _position = Vector3.zero;
            _have = false;
            _frame = -1;
        }

        internal static bool TryPosition(out Vector3 position)
        {
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                _have = Choose(out _position);
            }

            position = _position;
            return _have;
        }

        private static bool Choose(out Vector3 position)
        {
            position = Vector3.zero;

            List<Anchor> anchors = SimulationAnchors.Current;
            if (anchors.Count == 0)
            {
                return false;
            }

            long best = long.MaxValue;
            bool found = false;

            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].Uid < best)
                {
                    best = anchors[i].Uid;
                    position = anchors[i].Position;
                    found = true;
                }
            }

            return found;
        }
    }
}
