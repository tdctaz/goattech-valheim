using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// Carries the wind range a ship's wind anchors are built from, from the machine that simulates
    /// the hull to everyone aboard it, one anchor ahead of when it is needed.
    ///
    /// <see cref="WindRange"/> is the same function on every machine, but a function of a position,
    /// and nobody agrees on where a moving ship is. The owner reads the hull's position from the
    /// rigidbody it simulates; everyone else reads a transform that trails it by the interpolation
    /// lag, a few metres at sailing speed. Inside the one-zone ramp at a border between weathers
    /// that is enough to give the crew a different wave height from the hull, which is exactly the
    /// disagreement the blend exists to remove. So, as with <see cref="ModerHeading"/>, the owner
    /// writes the value it used and the crew use that one.
    ///
    /// Unlike the Moder heading it is written one anchor early. When anchor k comes into use, the
    /// owner evaluates the range for anchor k + 2 at the hull's position and writes it; by the time
    /// k + 2 is taken, a whole anchor period later, it has long since reached every client. A
    /// client therefore never has to take an anchor from a local guess and correct it afterwards,
    /// which for a range would mean moving the strength of an anchor in use: the vegetation sway
    /// fault <see cref="WaveField.ApplyWind"/> holds anchors to avoid. The cost is that wave
    /// strength follows where the hull was one anchor period earlier, which is less lag than
    /// vanilla's own camera-triggered blend followed by a latched wind target.
    ///
    /// Three slots by anchor modulo three, because three anchors are live at once: the two being
    /// cross-faded and the one written ahead. Each slot holds its anchor number next to the value,
    /// so a stale slot is recognised rather than used. The owner fills a missing slot on demand
    /// too, so a hull that has just been loaded, or just changed hands, carries on from the
    /// previous owner's values where it can and publishes its own where it cannot.
    /// </summary>
    internal static class HullWindRange
    {
        private const int Slots = 3;

        private static readonly int[] AnchorKeys =
        {
            "ServerAuthority.WindRangeAnchor0".GetStableHashCode(),
            "ServerAuthority.WindRangeAnchor1".GetStableHashCode(),
            "ServerAuthority.WindRangeAnchor2".GetStableHashCode(),
        };

        private static readonly int[] RangeKeys =
        {
            "ServerAuthority.WindRange0".GetStableHashCode(),
            "ServerAuthority.WindRange1".GetStableHashCode(),
            "ServerAuthority.WindRange2".GetStableHashCode(),
        };

        /// <summary>
        /// The range published for an anchor, as (min, max).
        /// </summary>
        internal static bool TryRead(ZDO zdo, long anchor, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (zdo == null)
            {
                return false;
            }

            int slot = Slot(anchor);
            if (zdo.GetLong(AnchorKeys[slot], long.MinValue) != anchor)
            {
                return false;
            }

            Vector3 range = zdo.GetVec3(RangeKeys[slot], Vector3.zero);
            min = range.x;
            max = range.y;
            return true;
        }

        /// <summary>
        /// The range for an anchor on a hull this machine owns: the published one if there is one,
        /// otherwise evaluated now at the hull's position for the anchor's time and published.
        /// <paramref name="trace"/> is filled only when it is evaluated here.
        /// </summary>
        internal static bool Take(ZDO zdo, long anchor, float windPeriod, Vector3 position,
            out float min, out float max, out bool evaluated, WindTrace trace)
        {
            evaluated = false;
            if (TryRead(zdo, anchor, out min, out max))
            {
                return true;
            }

            if (!WindRange.TryAt(position, WindBlend.AnchorTime(anchor, windPeriod), out min, out max, trace))
            {
                return false;
            }

            evaluated = true;
            if (zdo != null && zdo.IsOwner())
            {
                int slot = Slot(anchor);
                zdo.Set(RangeKeys[slot], new Vector3(min, max, 0f));
                zdo.Set(AnchorKeys[slot], anchor);
            }

            return true;
        }

        private static int Slot(long anchor)
        {
            long slot = anchor % Slots;
            return (int)(slot < 0 ? slot + Slots : slot);
        }
    }
}
