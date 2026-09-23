using System;
using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// Makes every machine compute the same sea.
    ///
    /// Waves are never sent over the network. WaterVolume.GetWaterSurface builds the height at a
    /// point out of the point, ZNet's world time, and the global wind EnvMan hands it. World time
    /// is synchronised and the point is the point, so the wind is the only input that can differ,
    /// and in vanilla it differs badly.
    ///
    /// EnvMan.UpdateWind derives a wind target from noise seeded by the whole second, which is the
    /// same on every machine. What it then does with that target is not. SetTargetWind refuses to
    /// take a new one while m_windTransitionTimer is running, and that timer is a local ramp of
    /// m_windTransitionDuration seconds started whenever the previous one ended. Targets change
    /// several times per ramp, so each machine keeps whichever target happened to be current at the
    /// instant its own ramp finished, and nothing ever pulls the two back together.
    ///
    /// Measured on a live server against a connected client, at the same second of world time: the
    /// server held wind (0.413, 0.911) at intensity 0.240 and had settled, while the client held
    /// (-0.318, -0.590) at intensity 0.067 and was still ramping toward a target the server had
    /// passed ten seconds earlier. Opposite directions, three and a half times the amplitude. Wave
    /// height scales linearly with intensity and the leading wave runs along the wind, so the two
    /// machines were rendering genuinely different seas.
    ///
    /// The replacement keeps vanilla's noise and vanilla's transition shape and throws away only
    /// the latch. Wind is defined as the interpolation between the noise evaluated at two fixed
    /// anchors on a grid of m_windTransitionDuration seconds of world time. That is a pure function
    /// of world time, so every machine lands on the same value without anything being sent, and it
    /// still moves at the same rate and with the same character as before.
    ///
    /// The two local overrides are kept: the edge of the world turns the wind outward for the
    /// player who sails into it, and Moder's power turns it to the heading of the ship the local
    /// player is standing on. Both are taken into an anchor when it is evaluated, as vanilla takes
    /// them into a target, so they swing round over the ordinary transition rather than snapping
    /// or turning a field already in use. The server simulates that ship on its own sea, so for
    /// Moder the server decides and sends the exact heading it used with each anchor; see
    /// <see cref="ModerHeading"/> and <see cref="LocalWind"/>.
    /// </summary>
    internal static class WaveField
    {
        private static bool _haveAnchors;
        private static long _firstAnchor;
        private static Vector4 _firstWind;
        private static Vector4 _secondWind;

        private static bool _firstProvisional;
        private static bool _secondProvisional;
        private static ZDOID _provisionalShip;

        private static readonly AnchorRange FirstRange = new AnchorRange();
        private static readonly AnchorRange SecondRange = new AnchorRange();
        private static AnchorRange _firstRange = FirstRange;
        private static AnchorRange _secondRange = SecondRange;
        private static ZDOID _missingShip;
        private static long _missingAnchor = long.MinValue;

        private static float _lastWrapped;
        private static bool _haveLastWrapped;
        private static int _waterFrame = -1;

        private const float MaxCatchUpRate = 4f;
        private const float SnapThreshold = 10f;

        internal static void Reset()
        {
            _haveAnchors = false;
            _firstProvisional = false;
            _secondProvisional = false;
            _missingAnchor = long.MinValue;
            _lastWrapped = 0f;
            _haveLastWrapped = false;
            _waterFrame = -1;
        }

        /// <summary>
        /// Replaces the latch with a function of world time. False means this machine should fall
        /// back to vanilla, which is the case for the debug wind the test command drives.
        ///
        /// Each anchor is evaluated once, when it first comes into use, and then held until it
        /// retires, exactly as vanilla holds a target for the length of its ramp. The vegetation
        /// and grass shaders take their sway phase as _Time * _SwaySpeed * (wind.w * 0.5 + 0.5),
        /// so an anchor whose intensity moves while in use shifts that phase by the seconds since
        /// the scene loaded times the rate of change. Re-evaluating the anchors every frame did
        /// exactly that whenever the weather blended into a new wind range: ten minutes into a
        /// session, a range sliding from 0.10-0.30 to 0.80-1.00 over fifteen seconds swayed every
        /// tree and blade of grass at about twenty times its speed until the blend finished. The
        /// noise is still a pure function of world time, so machines agree on everything except
        /// the weather each saw at the instant an anchor was taken, and that heals within two
        /// periods.
        ///
        /// The one thing allowed to change in a held anchor is its direction, once, when the
        /// server's Moder heading for it arrives. See <see cref="Settle"/>.
        /// </summary>
        internal static bool ApplyWind(EnvMan env)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || env == null || env.m_debugWind || env.GetCurrentEnvironment() == null)
            {
                _haveAnchors = false;
                return false;
            }

            if (!WindMath.Clock(env, znet, out long anchor, out float alpha, out float period))
            {
                _haveAnchors = false;
                return false;
            }

            if (!_haveAnchors || anchor != _firstAnchor)
            {
                if (_haveAnchors && anchor == _firstAnchor + 1)
                {
                    _firstWind = _secondWind;
                    _firstProvisional = _secondProvisional;
                    AnchorRange retired = _firstRange;
                    _firstRange = _secondRange;
                    _secondRange = retired;
                }
                else
                {
                    if (_haveAnchors)
                    {
                        Plugin.Log.LogInfo(
                            $"Wind anchors rebuilt: world time moved from anchor {_firstAnchor} to {anchor} " +
                            $"({(anchor - _firstAnchor) * (double)period:0.0}s) in one frame.");
                    }

                    _firstWind = WindAt(env, anchor, period, _firstRange, out _firstProvisional);
                }

                _secondWind = WindAt(env, anchor + 1, period, _secondRange, out _secondProvisional);
                _firstAnchor = anchor;
                _haveAnchors = true;
                PublishAhead(anchor + 2, period);
            }

            if (_firstProvisional)
            {
                _firstProvisional = !Settle(env, anchor, period, ref _firstWind);
            }

            if (_secondProvisional)
            {
                _secondProvisional = !Settle(env, anchor + 1, period, ref _secondWind);
            }

            Publish(env, _firstWind, _secondWind, alpha);
            return true;
        }

        /// <summary>
        /// Vanilla's own target computation, at an anchor rather than at this second. The noise and
        /// the arithmetic are <see cref="WindMath"/>'s, which the server's per-position and per-hull
        /// winds use too, so the two cannot drift apart by a rounding difference.
        /// </summary>
        private static Vector4 WindAt(EnvMan env, long anchor, float period, AnchorRange range, out bool provisional)
        {
            float noise = WindMath.Noise(env, WindMath.AnchorSecond(anchor, period), out Vector3 noiseDir);
            RangeFor(env, anchor, period, range);

            LocalOverride kind = CurrentOverride(env, anchor, out Vector3 overrideDir, out float edge, out provisional);
            return WindMath.Compose(noiseDir, noise, range.Min, range.Max,
                kind != LocalOverride.None, overrideDir, kind == LocalOverride.EdgeOfWorld ? edge : 0f);
        }

        /// <summary>
        /// Where an anchor's wind range came from, for the WaveSync log.
        /// </summary>
        internal enum RangeSource
        {
            Vanilla,
            Viewpoint,
            ShipPublished,
            ShipOwn,
            ShipLocal,
        }

        internal sealed class AnchorRange
        {
            internal long Anchor = long.MinValue;
            internal float Min;
            internal float Max;
            internal RangeSource Source;
            internal readonly WindTrace Trace = new WindTrace();
        }

        internal static AnchorRange CurrentFirstRange => _haveAnchors ? _firstRange : null;
        internal static AnchorRange CurrentSecondRange => _haveAnchors ? _secondRange : null;

        /// <summary>
        /// The wind range an anchor is built from, taken once when the anchor is and held with it.
        ///
        /// With BlendedWeatherWind off, the current environment's, as vanilla takes it. With it on,
        /// <see cref="WindRange"/> for the anchor's time, at one of two places:
        ///
        /// - Aboard a ship, the ship's. Everyone aboard renders the sea the hull floats on, so they
        ///   all take the range its owner published for the anchor (<see cref="HullWindRange"/>),
        ///   whoever that owner is. This client publishes it if it owns the ship itself, which is
        ///   the usual case for a driver. Only when nothing was published, because the ship has
        ///   just come into view or just changed hands, is it evaluated here at this client's view
        ///   of the hull, which trails the owner's by a few metres; that is logged once and heals
        ///   within two anchors.
        /// - Anywhere else, the local player's position, the same place vanilla judges the edge of
        ///   the world from. The camera would do as well; it is a few metres away, and a player is
        ///   what the server knows the position of. A server has neither, and uses the viewpoint
        ///   its global weather is taken at.
        /// </summary>
        private static void RangeFor(EnvMan env, long anchor, float period, AnchorRange range)
        {
            range.Anchor = anchor;
            range.Trace.Blended = false;
            range.Trace.Here = null;

            if (WindRange.Enabled)
            {
                double time = WindBlend.AnchorTime(anchor, period);
                Ship ship = LocalShip(out ZDO zdo);
                if (ship != null)
                {
                    if (HullWindRange.TryRead(zdo, anchor, out range.Min, out range.Max))
                    {
                        range.Source = RangeSource.ShipPublished;
                        return;
                    }

                    if (HullWindRange.Take(zdo, anchor, period, ship.transform.position,
                            out range.Min, out range.Max, out _, range.Trace))
                    {
                        range.Source = zdo.IsOwner() ? RangeSource.ShipOwn : RangeSource.ShipLocal;
                        if (range.Source == RangeSource.ShipLocal && (zdo.m_uid != _missingShip || anchor != _missingAnchor + 1))
                        {
                            Plugin.Log.LogInfo(
                                $"No wind range published for anchor {anchor} on ship {zdo.m_uid}, so this client " +
                                $"took it from its own view of the hull ({range.Min:0.0000}-{range.Max:0.0000}); the " +
                                "hull's owner may differ for this anchor.");
                        }

                        if (range.Source == RangeSource.ShipLocal)
                        {
                            _missingShip = zdo.m_uid;
                            _missingAnchor = anchor;
                        }

                        return;
                    }
                }
                else if (Viewpoint(out Vector3 viewpoint) &&
                         WindRange.TryAt(viewpoint, time, out range.Min, out range.Max, range.Trace))
                {
                    range.Source = RangeSource.Viewpoint;
                    return;
                }
            }

            EnvSetup current = env.GetCurrentEnvironment();
            range.Min = current.m_windMin;
            range.Max = current.m_windMax;
            range.Source = RangeSource.Vanilla;
        }

        /// <summary>
        /// Publishes the range for the anchor after next on a ship this client owns and is aboard,
        /// so that its crew have it before they need it. See <see cref="HullWindRange"/>.
        /// </summary>
        private static void PublishAhead(long anchor, float period)
        {
            if (!WindRange.Enabled)
            {
                return;
            }

            Ship ship = LocalShip(out ZDO zdo);
            if (ship != null && zdo.IsOwner())
            {
                HullWindRange.Take(zdo, anchor, period, ship.transform.position, out _, out _, out _, null);
            }
        }

        private static Ship LocalShip(out ZDO zdo)
        {
            zdo = null;
            Player player = Player.m_localPlayer;
            if (player == null || player.InInterior())
            {
                return null;
            }

            Ship ship = Ship.GetLocalShip();
            if (ship == null || ship.m_nview == null || !ship.m_nview.IsValid())
            {
                return null;
            }

            zdo = ship.m_nview.GetZDO();
            return ship;
        }

        private static bool Viewpoint(out Vector3 position)
        {
            Player player = Player.m_localPlayer;
            if (player != null)
            {
                position = player.transform.position;
                return true;
            }

            if (Plugin.ServerActive && ServerViewpoint.TryPosition(out position))
            {
                return true;
            }

            Camera camera = Utils.GetMainCamera();
            if (camera != null)
            {
                position = camera.transform.position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        private enum LocalOverride
        {
            None,
            EdgeOfWorld,
            Moder,
        }

        /// <summary>
        /// The two cases vanilla lets the local player bend the wind with. The edge of the world
        /// turns it outward and drives it toward full strength, and stays local: it is judged by
        /// where the player is. Moder's power turns it to the heading of the ship the local player
        /// is standing on and leaves the strength alone, and that one is the server's to decide
        /// when the server simulates the hull, because the hull floats on the server's sea.
        ///
        /// The server writes, per anchor, whether Moder was active and the exact heading it took
        /// (<see cref="ModerHeading"/>). When that is already here, it is used as it is. When it is
        /// not yet (it is written at the moment the anchor is taken, and arrives a round trip
        /// later), the local answer stands in, from the lagged transform, and the anchor is marked
        /// provisional so that <see cref="Settle"/> can swap in the server's once it lands. A client
        /// aboard a hull nobody publishes for, such as one its driver owns, keeps the local answer.
        /// </summary>
        private static LocalOverride CurrentOverride(EnvMan env, long anchor, out Vector3 dir, out float edge,
            out bool provisional)
        {
            dir = Vector3.forward;
            edge = 0f;
            provisional = false;

            Player player = Player.m_localPlayer;
            if (player == null || player.InInterior())
            {
                return LocalOverride.None;
            }

            if (WindMath.EdgeOfWorld(player.transform.position, env.m_edgeOfWorldWidth, out dir, out edge))
            {
                return LocalOverride.EdgeOfWorld;
            }

            Ship ship = Ship.GetLocalShip();
            if (ship == null)
            {
                return LocalOverride.None;
            }

            ZDO zdo = ship.m_nview != null && ship.m_nview.IsValid() ? ship.m_nview.GetZDO() : null;
            if (zdo != null && ModerHeading.TryRead(zdo, anchor, out bool active, out Vector3 heading))
            {
                if (active)
                {
                    dir = heading;
                    return LocalOverride.Moder;
                }

                return LocalOverride.None;
            }

            if (zdo != null && !zdo.IsOwner())
            {
                provisional = true;
                _provisionalShip = zdo.m_uid;
            }

            if (ship.IsWindControllActive())
            {
                dir = ship.transform.forward;
                return LocalOverride.Moder;
            }

            return LocalOverride.None;
        }

        /// <summary>
        /// Swaps the server's Moder decision into a held anchor that was taken before it arrived.
        /// True once the anchor is settled, either because the decision arrived or because it no
        /// longer can: the player left the ship, or the ship is now this machine's own.
        ///
        /// Only the direction is replaced. The strength is the one this client took the anchor
        /// with, because Moder never changes the strength, and moving the strength of an anchor in
        /// use is the vegetation sway fault ApplyWind holds anchors to avoid. The direction of the
        /// incoming anchor is almost all of what changes, and it arrives a round trip into a ten
        /// second blend, so the field it turns carries a weight of a percent or two at that moment.
        /// </summary>
        private static bool Settle(EnvMan env, long anchor, float period, ref Vector4 wind)
        {
            Ship ship = Ship.GetLocalShip();
            ZNetView nview = ship != null ? ship.m_nview : null;
            if (nview == null || !nview.IsValid() || nview.GetZDO().m_uid != _provisionalShip || nview.IsOwner())
            {
                return true;
            }

            if (!ModerHeading.TryRead(nview.GetZDO(), anchor, out bool active, out Vector3 heading))
            {
                return false;
            }

            Vector3 dir = heading;
            if (!active)
            {
                WindMath.Noise(env, WindMath.AnchorSecond(anchor, period), out dir);
            }

            float turned = Vector3.Angle(new Vector3(wind.x, wind.y, wind.z), dir);
            if (turned > 1f)
            {
                Plugin.Log.LogInfo(
                    $"Moder heading for anchor {anchor} settled from ship {_provisionalShip}: the server " +
                    $"{(active ? "steered" : "did not steer")} it, {turned:0.0} degrees from what this client " +
                    "had taken locally.");
            }

            wind = new Vector4(dir.x, dir.y, dir.z, wind.w);
            return true;
        }

        /// <summary>
        /// Hands the two anchors and the blend between them to everything that reads wind, in the
        /// shape vanilla's own UpdateWindTransition leaves them in.
        ///
        /// Two static fields cross-faded, rather than one field whose direction turns, and that is
        /// not a stylistic choice. CreateWave takes a wave's spatial phase from the wind direction:
        ///
        ///     vector = -(worldPos.z * dir + worldPos.x * tangent)   // = -(worldPos . dir)
        ///     phase  = time * waveSpeed + vector.y * waveLength
        ///
        /// so the phase at a fixed point is proportional to the dot product of the position with
        /// the wind direction. Turn that direction and the whole field slides, by an amount that
        /// grows with distance from the world origin. A version of this published a single wind
        /// interpolated between the anchors, and 950m out a heading turning one radian per ten
        /// seconds moved the phase at about 3.8 rad/s against an intended wave speed of 0.5: the
        /// sea raced, and it read as the world clock having gone into fast forward. Cross-fading
        /// two fields that each keep a fixed direction has no such term.
        ///
        /// WaterVolume reads wind1, wind2 and the alpha through GetWindData and interpolates the
        /// two wave results rather than the two winds, so handing it the anchors rather than a
        /// blended vector is also what keeps the water identical to vanilla's.
        /// </summary>
        private static void Publish(EnvMan env, Vector4 first, Vector4 second, float alpha)
        {
            env.m_windDir1 = first;
            env.m_windDir2 = second;
            env.m_windTransitionTimer = alpha * env.m_windTransitionDuration;

            Shader.SetGlobalVector(EnvMan.s_globalWind1, first);
            Shader.SetGlobalVector(EnvMan.s_globalWind2, second);
            Shader.SetGlobalFloat(EnvMan.s_globalWindAlpha, alpha);

            env.m_wind = Vector4.Lerp(first, second, alpha);

            if (env.m_clothWindZone != null)
            {
                env.m_clothWindZone.SetWindDirection(env.GetWindDir());
                env.m_clothWindZone.main = Mathf.Pow(env.GetWindIntensity(), 2f) * 100f;
            }

            Shader.SetGlobalVector(EnvMan.s_globalWindForce, env.GetWindForce());
        }

        /// <summary>
        /// Puts the rendered water surface back on the same clock buoyancy uses.
        ///
        /// Buoyancy reads s_wrappedDayTimeSeconds, the shader reads s_waterTime, and vanilla's
        /// UpdateWaterTime adds a whole frame delta to the second on every call while pulling only
        /// five percent back toward the first. MonoUpdaters calls it from FixedUpdate, Update and
        /// LateUpdate alike, so it adds three or more frame deltas per frame and settles at roughly
        /// twelve frame times ahead of the truth: a tenth of a second at 144fps, a quarter at 60,
        /// half a second at 30. The boat therefore floats on one surface while the player looks at
        /// another, by an amount that depends on their frame rate. Measured on the live server and
        /// a connected client, both at a 20ms frame time: 0.184 seconds ahead on each.
        ///
        /// The two are simply tied together here, so the error is zero rather than small. What the
        /// smoothing was for is kept as a rate limit instead of a lag: the world clock is set
        /// absolutely by the server's NetTime message every two seconds, and a client whose frame
        /// rate collapsed can be seconds behind when one arrives. Catching that up at four times
        /// real time is quick enough not to be noticed and smooth enough not to jolt, and unlike a
        /// lag filter it converges on exactly the right value and then stays there.
        ///
        /// Only gaps up to ten seconds are caught up that way; anything larger snaps, as vanilla's
        /// own ten second reset does. Sleeping fast-forwards the world clock, which a client sees
        /// as a jump of ninety to a hundred seconds every couple of seconds. Rate limited, the water
        /// fell hundreds of seconds behind and then ran at four times speed for minutes afterwards.
        ///
        /// An earlier attempt carried the correction as a decaying offset instead. It measured the
        /// correction by comparing the clock against the frame delta, which does not work: the
        /// clock advances in fixed steps and the frames do not, so the mismatch never settled at
        /// zero. It left 0.031 seconds on the server and 0.090 on the client, which is smaller than
        /// vanilla's error but is still an error, and still a different one per machine.
        /// </summary>
        internal static bool AlignWaterTime()
        {
            ZNet znet = ZNet.instance;
            if (znet == null)
            {
                return false;
            }

            float wrapped = znet.GetWrappedDayTimeSeconds();

            if (_waterFrame != Time.frameCount)
            {
                _waterFrame = Time.frameCount;

                float target = wrapped;

                if (_haveLastWrapped)
                {
                    float step = wrapped - _lastWrapped;
                    float limit = Time.unscaledDeltaTime * MaxCatchUpRate;

                    if (Mathf.Abs(step) <= SnapThreshold)
                    {
                        target = _lastWrapped + Mathf.Clamp(step, -limit, limit);
                    }
                }

                _haveLastWrapped = true;
                _lastWrapped = target;
            }

            WaterVolume.s_wrappedDayTimeSeconds = wrapped;
            WaterVolume.s_waterTime = _lastWrapped;
            return true;
        }
    }
}
