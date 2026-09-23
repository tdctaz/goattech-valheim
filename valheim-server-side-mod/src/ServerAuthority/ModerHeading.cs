using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// Carries the heading a ship's wind anchor was taken with under Moder's power, from the
    /// machine that simulates the hull to the clients aboard it.
    ///
    /// Moder's power turns the wind to the heading of the ship the player is standing on, and
    /// <see cref="WaveField"/> takes that heading into an anchor when the anchor is evaluated. The
    /// wave phase at a point is proportional to the point's position dotted with the wind
    /// direction, so a heading that differs by a fraction of a degree moves the phase by an amount
    /// that grows with distance from the world origin. Scaled from the measurement in WaveField's
    /// notes (3.8 rad/s of phase from a heading turning 0.1 rad/s, 950m out), one degree of heading
    /// is about three and a half radians of phase 5000m out, over half a wave. The server reads its
    /// hull's heading from the rigidbody it is simulating; a client reads it from a transform that
    /// trails the server's by its interpolation lag, so the two never agree exactly and the hull
    /// rides a different sea from the one its crew sees.
    ///
    /// So the server writes the heading it actually used, per anchor, and the client uses that one.
    /// Two slots, by anchor parity, because two anchors are in use at any moment: the one being
    /// blended out and the one being blended in. A zero heading means the server decided Moder was
    /// not active for that anchor, which a client must honour too, or a client whose status effect
    /// arrives a frame earlier than the server's would turn its sea alone.
    ///
    /// The server writes it for every anchor of every hull it simulates, whether or not it counts
    /// anyone aboard. A player who boards just before an anchor rolls over takes that anchor from
    /// their own heading and waits for the server's; if the server has not seen them aboard yet and
    /// wrote nothing, the wait never ends and the crew rides a different sea for two periods.
    /// Written regardless, the answer is "not steered", which is what the server did use. A ZDO
    /// field set to the value it already holds sends nothing, so an idle hull costs only the
    /// anchor number every ten seconds.
    /// </summary>
    internal static class ModerHeading
    {
        private static readonly int[] AnchorKeys =
        {
            "ServerAuthority.ModerAnchor0".GetStableHashCode(),
            "ServerAuthority.ModerAnchor1".GetStableHashCode(),
        };

        private static readonly int[] HeadingKeys =
        {
            "ServerAuthority.ModerHeading0".GetStableHashCode(),
            "ServerAuthority.ModerHeading1".GetStableHashCode(),
        };

        internal static void Publish(ZDO zdo, long anchor, bool active, Vector3 heading)
        {
            if (zdo == null)
            {
                return;
            }

            int slot = Slot(anchor);
            zdo.Set(HeadingKeys[slot], active ? heading : Vector3.zero);
            zdo.Set(AnchorKeys[slot], anchor);
        }

        internal static bool TryRead(ZDO zdo, long anchor, out bool active, out Vector3 heading)
        {
            active = false;
            heading = Vector3.zero;
            if (zdo == null)
            {
                return false;
            }

            int slot = Slot(anchor);
            if (zdo.GetLong(AnchorKeys[slot], long.MinValue) != anchor)
            {
                return false;
            }

            heading = zdo.GetVec3(HeadingKeys[slot], Vector3.zero);
            active = heading.sqrMagnitude > 0f;
            return true;
        }

        private static int Slot(long anchor)
        {
            return (int)(anchor & 1L);
        }
    }
}
