using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// A point the server has to simulate around, which is one connected player.
    /// </summary>
    internal struct Anchor
    {
        public long Uid;
        public Vector3 Position;
        public Vector2s Zone;
    }

    /// <summary>
    /// Vanilla builds zones and objects around <c>ZNet.GetReferencePosition()</c>, which on a
    /// dedicated server is the origin and means nothing. Everything in this mod works from this
    /// list of connected players instead. It is rebuilt at most once per frame and reuses its
    /// buffers, because it is read from the per-frame object creation path.
    /// </summary>
    internal static class SimulationAnchors
    {
        private static readonly List<Anchor> Anchors = new List<Anchor>();
        private static readonly HashSet<ZDOID> Characters = new HashSet<ZDOID>();
        private static readonly HashSet<long> Peers = new HashSet<long>();
        private static int _builtOnFrame = -1;

        internal static List<Anchor> Current
        {
            get { Refresh(); return Anchors; }
        }

        /// <summary>
        /// The ZDOs of the connected players' own bodies. These must never be reassigned: a player
        /// whose character is simulated by the server would rubber-band on every input.
        /// </summary>
        internal static HashSet<ZDOID> PlayerCharacters
        {
            get { Refresh(); return Characters; }
        }

        internal static bool IsConnectedPeer(long uid)
        {
            if (uid == 0L)
            {
                return false;
            }

            Refresh();
            return Peers.Contains(uid);
        }

        internal static void Reset()
        {
            Anchors.Clear();
            Characters.Clear();
            Peers.Clear();
            _builtOnFrame = -1;
        }

        private static void Refresh()
        {
            if (_builtOnFrame == Time.frameCount)
            {
                return;
            }

            _builtOnFrame = Time.frameCount;
            Anchors.Clear();
            Characters.Clear();
            Peers.Clear();

            ZNet znet = ZNet.instance;
            if (znet == null)
            {
                return;
            }

            List<ZNetPeer> peers = znet.m_peers;
            for (int i = 0; i < peers.Count; i++)
            {
                ZNetPeer peer = peers[i];
                if (peer == null || !peer.IsReady())
                {
                    continue;
                }

                Peers.Add(peer.m_uid);

                if (!peer.m_characterID.IsNone())
                {
                    Characters.Add(peer.m_characterID);
                }

                Vector3 pos = peer.GetRefPos();
                if (pos.sqrMagnitude < 1f)
                {
                    continue;
                }

                Anchors.Add(new Anchor
                {
                    Uid = peer.m_uid,
                    Position = pos,
                    Zone = ZoneSystem.GetZone(pos),
                });
            }
        }
    }
}
