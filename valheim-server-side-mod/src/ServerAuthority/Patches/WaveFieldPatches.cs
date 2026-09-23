using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// Replaces the wind target latch with a function of world time, so that every machine
    /// computes the same sea. See <see cref="WaveField"/> for what was wrong and what the
    /// replacement does.
    ///
    /// Not gated on Plugin.ServerActive, and it cannot be: the whole point is that the server and
    /// every client arrive at the same answer, which needs the same code running on all of them.
    /// This is the part of Server Authority that has to be installed on players' machines.
    /// </summary>
    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.UpdateWind))]
    internal static class EnvMan_UpdateWind_Patch
    {
        private static bool Prefix(EnvMan __instance)
        {
            if (!ModConfig.DeterministicWind.Value)
            {
                return true;
            }

            return !WaveField.ApplyWind(__instance);
        }
    }

    /// <summary>
    /// Puts the rendered water surface back on the clock buoyancy uses. See
    /// <see cref="WaveField.AlignWaterTime"/>.
    ///
    /// StaticUpdate is replaced rather than UpdateWaterTime, although UpdateWaterTime is where the
    /// fault lives. It is a small private static and Mono is free to inline it into its only
    /// caller, which would leave a patch on it installed and never executed. StaticUpdate is the
    /// caller and does only these two things, so replacing it is both safe and no larger.
    /// </summary>
    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.StaticUpdate))]
    internal static class WaterVolume_StaticUpdate_Patch
    {
        private static bool Prefix()
        {
            if (!ModConfig.AlignRenderedWaterWithPhysics.Value || !WaveField.AlignWaterTime())
            {
                return true;
            }

            if (EnvMan.instance != null)
            {
                EnvMan.instance.GetWindData(
                    out WaterVolume.s_globalWind1,
                    out WaterVolume.s_globalWind2,
                    out WaterVolume.s_globalWindAlpha);
            }

            return false;
        }
    }

    /// <summary>
    /// Gives a headless server the viewpoint it structurally lacks.
    ///
    /// EnvMan resolves the current environment, and therefore the wind range wave height is drawn
    /// from, at the main camera. A dedicated server has a camera object but never moves it, because
    /// GameCamera.UpdateCamera returns as soon as it finds no local player, so it sits wherever the
    /// scene left it and the server picks the weather for the world origin no matter where anybody
    /// is sailing. Measured on the live server: the server reported a wind range of 0.10-0.30 while
    /// the client standing in the same biome reported 0.10-0.50.
    ///
    /// Both of these answer from a connected player instead. The lowest peer id is used rather than
    /// the nearest or the first, because it is stable: the server's weather should not flip every
    /// time the peer list is reordered or one player walks into another biome. With
    /// ServerWeatherPerPosition on this global is only a fallback; see <see cref="LocalWeather"/>.
    /// </summary>
    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetBiome))]
    internal static class EnvMan_GetBiome_Patch
    {
        private static bool Prefix(EnvMan __instance, ref BiomeSector __result)
        {
            if (!Plugin.ServerActive || !ModConfig.ServerWeatherFollowsPlayers.Value)
            {
                return true;
            }

            if (!ServerViewpoint.TryPosition(out Vector3 position) || WorldGenerator.instance == null)
            {
                return true;
            }

            __result = WorldGenerator.instance.GetBiomeSector(position);

            bool ashlands = WorldGenerator.IsAshlands(position.x, position.z);
            bool deepnorth = WorldGenerator.IsDeepnorth(position.x, position.y);
            if (ashlands | deepnorth)
            {
                if (__instance.m_cachedHeightmap == null ||
                    !__instance.m_cachedHeightmap.IsPointInside(position))
                {
                    __instance.m_cachedHeightmap = Heightmap.FindHeightmap(position);
                }

                if (__instance.m_cachedHeightmap != null)
                {
                    __instance.m_cachedHeightmap.GetWorldHeight(position, out float height);
                    if (height <= __instance.m_oceanLevelEnvCheckAshlandsDeepnorth)
                    {
                        __result = ZNet.World.m_biomeData
                            .Biomes[ashlands ? Heightmap.Biome.AshLands : Heightmap.Biome.DeepNorth]
                            .Sectors[0];
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The other half of the same problem. UpdateEnvironment returns without touching anything when
    /// it cannot find a camera, and uses the camera's position for the Ashlands and Deep North
    /// overrides even when it can, so on a server the queued environment never follows any player.
    ///
    /// The server's global weather is now simply <see cref="LocalWeather"/> at the viewpoint, the
    /// same answer every per-position consumer gets for that spot. In particular it no longer
    /// takes vanilla's GetEnvironmentOverride, which is where a raid's weather used to leak into
    /// the whole world: RandEventSystem sets its active event on the server whenever any player
    /// is inside a raid, and the override then applied whenever the viewpoint player's biome was in
    /// the raid's mask, however far away. Now a raid only reaches the global weather if the
    /// viewpoint itself is inside it.
    ///
    /// Replaced rather than transpiled, on the same reasoning as the spawn gate: a replacement
    /// fails loudly against a fresh decompilation when the original changes, where a transpiler
    /// silently patches the wrong branch.
    /// </summary>
    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.UpdateEnvironment))]
    internal static class EnvMan_UpdateEnvironment_Patch
    {
        private static EnvSetup _loggedWeather;
        private static WeatherSource _loggedSource;

        internal static void Forget()
        {
            _loggedWeather = null;
            _loggedSource = WeatherSource.None;
        }

        private static bool Prefix(EnvMan __instance, long sec, BiomeSector biome)
        {
            if (!Plugin.ServerActive || !ModConfig.ServerWeatherFollowsPlayers.Value)
            {
                return true;
            }

            if (!ServerViewpoint.TryPosition(out Vector3 position))
            {
                return true;
            }

            __instance.m_currentBiome = biome;
            if (__instance.m_environmentDuration > 0)
            {
                __instance.m_environmentPeriod = sec / __instance.m_environmentDuration;
            }

            EnvSetup chosen = LocalWeather.At(position, out WeatherSource source);
            if (chosen == null)
            {
                return false;
            }

            __instance.QueueEnvironment(chosen.m_name);

            if (chosen != _loggedWeather || source != _loggedSource)
            {
                _loggedWeather = chosen;
                _loggedSource = source;
                Plugin.Log.LogInfo(
                    $"Server global weather: {chosen.m_name} ({source}) at the viewpoint ({position.x:0}, {position.z:0}), " +
                    $"biome {biome.Biome}. Only what still reads the global weather follows this.");
            }

            return false;
        }
    }

    /// <summary>
    /// Keeps a raid's weather off the server's global weather altogether. Vanilla applies it
    /// whenever the event is active and EnvMan's current biome is in the raid's mask, and on a
    /// server the event is active whenever any player is in the raid, so the viewpoint's weather
    /// went to the raid's however far away the viewpoint was. <see cref="LocalWeather"/> applies
    /// the raid itself, only where a client standing there would.
    ///
    /// RandEventSystem's active event is left alone, because that is what makes the raid spawn.
    ///
    /// Only with ServerWeatherPerPosition on. With it off the server falls back to vanilla's one
    /// weather at the viewpoint, raid included, because nothing else would then apply it.
    /// </summary>
    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.GetEnvOverride))]
    internal static class RandEventSystem_GetEnvOverride_Patch
    {
        private static bool Prefix(ref string __result)
        {
            if (!Plugin.ServerActive || !ModConfig.ServerWeatherPerPosition.Value)
            {
                return true;
            }

            __result = null;
            return false;
        }
    }

#if DEBUG_TOOLS
    /// <summary>
    /// Applies Debug.ForceEnvironment, on a server and on a client alike.
    ///
    /// Weather is not sent over the network. Both ends derive it from the world clock and the
    /// biome, so testing a storm means setting the same name on every machine; setting it on one
    /// is how you reproduce a wave desync deliberately, which is what proved the diagnostic could
    /// still see one.
    /// </summary>
    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.Awake))]
    internal static class EnvMan_Awake_Patch
    {
        private static void Postfix(EnvMan __instance)
        {
            string forced = ModConfig.ForceEnvironment.Value;
            if (string.IsNullOrEmpty(forced))
            {
                return;
            }

            __instance.SetForceEnvironment(forced);
            Plugin.Log.LogWarning(
                $"Weather is pinned to '{forced}' by Debug.ForceEnvironment. Every machine must " +
                "set the same value or they will compute different seas. Clear it for normal play.");
        }
    }
#endif

    /// <summary>
    /// Lays the boat's waterline effects on the sea instead of nailing them to the hull.
    ///
    /// ShipEffects.m_shadow points at a child called WaterSurface, which on a longship sits at a
    /// fixed local (0, 0.55, 0) with identity rotation and holds two children: a mesh called
    /// shadow, the dark patch under the hull, and a particle system called vfx_water_surface, the
    /// foam ring at the waterline. Nothing in the game ever moves or rotates that transform;
    /// ShipEffects only calls SetActive on it.
    ///
    /// Rigidly parented, it inherits the hull's heave and its pitch and roll, so it tilts with the
    /// boat rather than lying flat on the sea and holds one height while the hull's real waterline
    /// moves. Measured in mild swell, the hull rode between 0.72m and 0.94m into the water.
    ///
    /// The mesh children are moved onto the surface and levelled. The shared WaterSurface parent is
    /// not, because moving it would take the foam emitter with it without moving the foam: those
    /// particles simulate in world space, so they stay where they were emitted no matter what the
    /// emitter does afterwards.
    ///
    /// The foam therefore needs the particles themselves moved, which is what LevelFoam does. Read
    /// out of the longship prefab, vfx_water_surface emits 40 particles a second over a flat
    /// 5m x 20m ellipse, each living 2 seconds with a start speed of zero, no gravity and no force,
    /// velocity or noise module enabled. Every particle is therefore frozen in world space for its
    /// whole life at whatever height the emitter had when it was born, and the emitter's height is
    /// the hull's. So the ring traces the hull's heave, which buoyancy damps and delays, rather
    /// than the sea's, and in any real swell it reads as foam following a wave that is not there.
    /// Roughly eighty particles are alive at a time, so rewriting each one's height from the same
    /// Floating.GetWaterLevel the hull and the shadow mesh use costs about as much as one more
    /// buoyancy sample and leaves every other property vanilla.
    ///
    /// The water level depends only on x and z, so the emitter's own height is used for the volume
    /// lookup rather than the particle's. A particle left above the volume's collider by the
    /// previous frame would otherwise match nothing and never be brought back down.
    ///
    /// Placing every particle exactly on the surface is correct and looks wrong. The ring becomes
    /// one rigid sheet, conforming perfectly and moving in perfect step, which reads as a decal
    /// laid on the water rather than as foam floating in it. ShipFoamSpread and ShipFoamLift put
    /// the variation back: each particle reads the sea a fixed short distance away in a fixed
    /// direction, and sits a fixed small height above it. The short wave components are only a few
    /// metres long, so neighbours a metre apart genuinely sit at different heights and rise and
    /// fall slightly out of step, and because wave height scales with wind this scales itself,
    /// from near flat in a calm to churned in a storm.
    ///
    /// The ring's terms are upward only, and the wake's are not, which is the difference between
    /// something that belongs on the surface and something that belongs churned through it. Vanilla
    /// emitted the wake at a fixed height on the hull and let the hull's own heave scatter it
    /// through the surface, so some of it was always part submerged; putting every particle exactly
    /// on the water took that away and left the wake one opaque mass. ShipWakeSink gives each wake
    /// particle a depth of its own and fades it toward a quarter of its opacity at the bottom of
    /// that range, which restores the variation without depending on how the water and the foam
    /// happen to sort against each other.
    ///
    /// Every term is derived from ParticleSystem.Particle.randomSeed, which is fixed for a
    /// particle's life, so the scatter is stable rather than boiling frame to frame.
    ///
    /// This is all vanilla behaviour and identical on every machine, so it is not a sync fault. It
    /// only became noticeable once the hull itself was being placed correctly.
    /// </summary>
    [HarmonyPatch(typeof(ShipEffects), nameof(ShipEffects.CustomLateUpdate))]
    internal static class ShipEffects_CustomLateUpdate_Patch
    {
        private static readonly Dictionary<int, Transform[]> Meshes = new Dictionary<int, Transform[]>();

        private sealed class Sheet
        {
            internal ParticleSystem System;
            internal Color32 Base;
            internal bool Wake;
        }

        private static readonly Dictionary<int, Sheet[]> Foam = new Dictionary<int, Sheet[]>();

        private static readonly List<Sheet> Scratch = new List<Sheet>();

        private static ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[128];

        private static WaterVolume _probe;
        private static Collider _probeCollider;

        internal static void Forget()
        {
            Meshes.Clear();
            Foam.Clear();
        }

        internal static void Forget(ShipEffects effects)
        {
            int id = effects.GetInstanceID();
            Meshes.Remove(id);
            Foam.Remove(id);
        }

        private static void Postfix(ShipEffects __instance)
        {
            if (!ModConfig.LevelShipWaterline.Value || Plugin.ServerActive)
            {
                return;
            }

            Transform surface = __instance.m_shadow;
            if (surface == null || !surface.gameObject.activeSelf)
            {
                return;
            }

            int id = __instance.GetInstanceID();
            float probeHeight = surface.position.y;

            if (!Meshes.TryGetValue(id, out Transform[] meshes))
            {
                MeshRenderer[] renderers = surface.GetComponentsInChildren<MeshRenderer>(true);
                meshes = new Transform[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    meshes[i] = renderers[i].transform;
                }

                Meshes[id] = meshes;
            }

            for (int i = 0; i < meshes.Length; i++)
            {
                Transform mesh = meshes[i];
                if (mesh == null)
                {
                    continue;
                }

                Vector3 position = mesh.position;
                float level = SurfaceAt(position.x, position.z, probeHeight);
                if (level <= -9999f)
                {
                    continue;
                }

                mesh.position = new Vector3(position.x, level + __instance.m_offset, position.z);
                mesh.rotation = Quaternion.Euler(0f, mesh.eulerAngles.y, 0f);
            }

            if (!Foam.TryGetValue(id, out Sheet[] sheets))
            {
                sheets = CollectSheets(__instance);
                Foam[id] = sheets;
            }

            for (int i = 0; i < sheets.Length; i++)
            {
                LevelFoam(sheets[i], probeHeight, __instance.m_offset);
            }
        }

        /// <summary>
        /// Puts one foam system's emitter and every particle alive in it on the water surface.
        ///
        /// Only a world space system is touched. A local space one already rides the emitter, so
        /// moving the emitter is the whole fix and rewriting positions on top of it would apply the
        /// correction twice; a custom space one belongs to something this does not know about.
        ///
        /// The emitter is moved as well as the particles, even though every particle is rewritten a
        /// frame later anyway, because the emission shape is a flat ellipse. Left on the hull it
        /// tilts with the hull, which foreshortens where along the boat particles are born.
        /// </summary>
        private static void LevelFoam(Sheet sheet, float probeHeight, float offset)
        {
            ParticleSystem system = sheet.System;
            if (system == null)
            {
                return;
            }

            Transform emitter = system.transform;
            Vector3 origin = emitter.position;
            float emitterLevel = SurfaceAt(origin.x, origin.z, probeHeight);
            if (emitterLevel > -9999f)
            {
                emitter.position = new Vector3(origin.x, emitterLevel + offset, origin.z);
                emitter.rotation = Quaternion.Euler(0f, emitter.eulerAngles.y, 0f);
            }

            int alive = system.particleCount;
            if (alive <= 0)
            {
                return;
            }

            if (_particles.Length < alive)
            {
                _particles = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(alive)];
            }

            int count = system.GetParticles(_particles);
            float spread = ModConfig.ShipFoamSpread.Value;
            float lift = sheet.Wake ? 0f : ModConfig.ShipFoamLift.Value;
            float sink = sheet.Wake ? ModConfig.ShipWakeSink.Value : 0f;
            bool moved = false;

            for (int i = 0; i < count; i++)
            {
                Vector3 position = _particles[i].position;
                float level = SurfaceAt(position.x, position.z, probeHeight);
                if (level <= -9999f)
                {
                    continue;
                }

                uint seed = _particles[i].randomSeed;

                if (spread > 0f)
                {
                    float angle = Unit(seed, 1) * 2f * Mathf.PI;
                    float distance = Unit(seed, 2) * spread;
                    float nearby = SurfaceAt(
                        position.x + Mathf.Cos(angle) * distance,
                        position.z + Mathf.Sin(angle) * distance,
                        probeHeight);

                    if (sheet.Wake || nearby > level)
                    {
                        level = nearby;
                    }
                }

                if (lift > 0f)
                {
                    level += Unit(seed, 3) * lift;
                }

                if (sink > 0f)
                {
                    float depth = Unit(seed, 3);
                    level -= depth * sink;

                    Color32 tint = sheet.Base;
                    tint.a = (byte)(tint.a * Mathf.Lerp(1f, 0.25f, depth));
                    _particles[i].startColor = tint;
                }

                _particles[i].position = new Vector3(position.x, level + offset, position.z);
                moved = true;
            }

            if (moved)
            {
                system.SetParticles(_particles, count);
            }
        }

        /// <summary>
        /// Every particle system on the boat that is meant to lie on the water, from the waterline
        /// rig and from the speed wake alike.
        ///
        /// The wake splits into two species, and only one of them belongs here. Read out of the
        /// longship prefab: aft_particles, front_particles and Trail have a start speed of zero, no
        /// gravity and no module that moves a particle after birth, and they emit from a disc or a
        /// point a few tens of centimetres across. They are flat sheets of foam whose whole job is
        /// to lie on the surface, exactly like the ring. The other three emit from cones at 1 to 2
        /// m/s under gravity with a velocity clamp: that is bow spray and rudder churn, it is meant
        /// to arc through the air, and flattening it onto the water would be wrong.
        ///
        /// So the test is the thing itself rather than a name: a system nothing moves after birth
        /// is a sheet, and a system with speed or gravity is spray. That survives Iron Gate
        /// retuning the prefabs, which a list of names would not.
        ///
        /// Trail is why this mattered enough to notice. It emits size 8 patches that live ten
        /// seconds, so fifty of them overlap at once, each pinned at the height the hull had up to
        /// ten seconds earlier. Left alone while the ring was corrected, they stacked into a solid
        /// slab sitting at its own height beside foam that was sitting correctly.
        /// </summary>
        private static Sheet[] CollectSheets(ShipEffects effects)
        {
            Scratch.Clear();
            Gather(effects.m_shadow != null ? effects.m_shadow.gameObject : null, false);
            Gather(effects.m_speedWakeRoot, true);
            return Scratch.ToArray();
        }

        private static void Gather(GameObject root, bool wake)
        {
            if (root == null)
            {
                return;
            }

            ParticleSystem[] found = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < found.Length; i++)
            {
                if (!LiesOnWater(found[i]))
                {
                    continue;
                }

                CapLifetime(found[i]);
                Scratch.Add(new Sheet
                {
                    System = found[i],
                    Base = found[i].main.startColor.color,
                    Wake = wake,
                });
            }
        }

        /// <summary>
        /// Holds a sheet's particles to ShipWakeTrailSeconds, and leaves anything already shorter
        /// alone.
        ///
        /// Unlike everything else here this is a retune rather than a correction, and it is here
        /// because vanilla's wake reads as a speedboat's once it is sitting on the water properly.
        /// Trail emits an eight metre patch five times a second and gives each one ten seconds, so
        /// fifty overlap at once, and its alpha curve holds full opacity from three percent of a
        /// particle's life to sixty-five percent: at sailing speed that is some fifty metres of
        /// solid white before it begins to fade at all.
        ///
        /// The curve is normalised over the lifetime, so capping the lifetime compresses the whole
        /// fade rather than truncating it, and the overlap count falls in proportion. Applied once
        /// when the system is first seen, and idempotent, because a second pass reads the capped
        /// value and leaves it.
        /// </summary>
        private static void CapLifetime(ParticleSystem system)
        {
            float cap = ModConfig.ShipWakeTrailSeconds.Value;
            if (cap <= 0f)
            {
                return;
            }

            ParticleSystem.MainModule main = system.main;
            if (main.startLifetimeMultiplier > cap)
            {
                main.startLifetimeMultiplier = cap;
            }
        }

        private static bool LiesOnWater(ParticleSystem system)
        {
            if (system == null)
            {
                return false;
            }

            ParticleSystem.MainModule main = system.main;
            if (main.simulationSpace != ParticleSystemSimulationSpace.World)
            {
                return false;
            }

            if (!Mathf.Approximately(main.startSpeedMultiplier, 0f) ||
                !Mathf.Approximately(main.gravityModifierMultiplier, 0f))
            {
                return false;
            }

            return !system.velocityOverLifetime.enabled &&
                   !system.forceOverLifetime.enabled &&
                   !system.inheritVelocity.enabled &&
                   !system.externalForces.enabled &&
                   !system.noise.enabled;
        }

        /// <summary>
        /// A number in 0..1 that is fixed for a particle's whole life and uncorrelated between
        /// channels, so that the scatter is stable rather than flickering frame to frame.
        ///
        /// ParticleSystem.Particle.randomSeed is assigned at birth and does not change, which makes
        /// it the only per-particle identity available: the array GetParticles hands back is not in
        /// a stable order, so an index cannot be used. Hashing it here rather than drawing from
        /// UnityEngine.Random also keeps this off the shared generator, which EnvMan's own wind
        /// octaves seed and read on the same frame.
        /// </summary>
        private static float Unit(uint seed, uint channel)
        {
            uint hash = seed * 747796405u + channel * 2891336453u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            return (hash & 0xFFFFu) * (1f / 65535f);
        }

        /// <summary>
        /// Wave height is a function of x and z alone: CreateWave takes only worldPos.x and
        /// worldPos.z, and Depth takes the point's position across the volume. The height passed in
        /// is therefore only used to find the volume, and a known wet one is used for every sample
        /// so that a particle which drifted outside the collider still gets an answer.
        ///
        /// The volume's collider is cached alongside the volume. This is otherwise the same as
        /// Floating.GetWaterLevel, which reaches for gameObject.GetComponent&lt;Collider&gt;() on
        /// every call even when it already holds the right volume; that is two native calls per
        /// sample, and there are now a few hundred samples per boat per frame.
        /// </summary>
        private static float SurfaceAt(float x, float z, float probeHeight)
        {
            Vector3 point = new Vector3(x, probeHeight, z);

            if (_probe != null && _probeCollider != null && _probeCollider.bounds.Contains(point))
            {
                return _probe.GetWaterSurface(point);
            }

            float level = Floating.GetWaterLevel(point, ref _probe);
            _probeCollider = _probe != null ? _probe.GetComponent<Collider>() : null;
            return level;
        }
    }
}
