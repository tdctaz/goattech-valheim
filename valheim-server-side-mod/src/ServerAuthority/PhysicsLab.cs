#if DEBUG_TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority
{
    internal static class PhysicsLab
    {
        private const int Shown = 8;

        private sealed class Bucket
        {
            internal long Ticks;
            internal int Calls;
        }

        private static readonly Dictionary<MethodBase, Bucket> Buckets = new Dictionary<MethodBase, Bucket>();
        private static readonly List<MethodInfo> Patched = new List<MethodInfo>();
        private static HarmonyMethod _prefix;
        private static HarmonyMethod _postfix;
        private static float _until;
        private static float _started;
        private static string _group;

        internal static void Handle(string[] parts)
        {
            string verb = parts.Length > 1 ? parts[1].ToLowerInvariant() : "census";
            float number = parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : 0f;
            float second = parts.Length > 3 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float m) ? m : 0f;
            switch (verb)
            {
                case "census":
                    Census();
                    break;
                case "profile":
                    StartProfile(number > 0f ? number : 30f, parts.Length > 3 ? parts[3].ToLowerInvariant() : "char");
                    break;
                case "fixed":
                    PhysicsTuning.SetFixedRate(number);
                    break;
                case "solver":
                    PhysicsTuning.SetSolver((int)number, (int)second);
                    break;
                case "sleep":
                    PhysicsTuning.SetSleepThreshold(number);
                    break;
                case "autosync":
                    Physics.autoSyncTransforms = parts.Length > 2 && parts[2] == "on";
                    Plugin.Log.LogInfo($"Physics lab: autoSyncTransforms {Physics.autoSyncTransforms}.");
                    break;
                case "ignore":
                    IgnoreLayers(parts);
                    break;
                case "states":
                    CreatureStep.StartTiming(number > 0f ? number : 60f);
                    break;
                case "idle":
                    CreatureStep.SetOverride((int)number);
                    break;
                case "syncstep":
                    SyncStep.SetOverride((int)number);
                    break;
                case "ground":
                    GroundProbes.SetOverride(parts.Length > 2 && parts[2] == "on" ? 1 : parts.Length > 2 && parts[2] == "off" ? 0 : -1);
                    break;
                case "compensate":
                    StepCompensation.SetOverride(parts.Length > 2 && parts[2] == "on" ? 1 : parts.Length > 2 && parts[2] == "off" ? 0 : -1);
                    break;
                case "latesync":
                    FrameSkips.SetSyncOverride(parts.Length > 2 && parts[2] == "on" ? 1 : parts.Length > 2 && parts[2] == "off" ? 0 : -1);
                    break;
                case "floatstep":
                    FrameSkips.SetFloatOverride(parts.Length > 2 && parts[2] == "on" ? 1 : parts.Length > 2 && parts[2] == "off" ? 0 : -1);
                    break;
                case "interp":
                    Interpolation(parts.Length > 2 && parts[2] == "on");
                    break;
                case "watch":
                    StartWatch(number > 0f ? number : 300f, second > 0f ? second : 100f);
                    break;
                default:
                    Plugin.Log.LogInfo("Physics lab: census | profile <s> <char|sync|water> | fixed <hz> | solver <pos> <vel> | sleep <threshold> | autosync on|off | ignore <layerA> <layerB> on|off | states <s> | idle <steps> | syncstep <steps> | watch <s> <radius> | ground on|off|config | compensate on|off|config | latesync on|off|config | floatstep on|off|config | interp on|off.");
                    break;
            }
        }

        internal static int MeleeHits;
        internal static int RangedHits;
        internal static int ProjectileHits;
        internal static int ProjectileCreatureHits;
        internal static bool Watching;
        private static float _watchUntil;
        private static float _watchNext;
        private static float _watchStarted;
        private static float _watchRadius;

        private static void StartWatch(float seconds, float radius)
        {
            _watchStarted = Time.time;
            _watchUntil = _watchStarted + seconds;
            _watchNext = 0f;
            _watchRadius = radius;
            MeleeHits = 0;
            RangedHits = 0;
            ProjectileHits = 0;
            ProjectileCreatureHits = 0;
            Watching = true;
            Plugin.Log.LogInfo($"Physics lab: watching creatures within {radius:F0} m of the first stand-in for {seconds:F0} s.");
        }

        private static void Watch()
        {
            if (!Watching || Time.time < _watchNext)
            {
                return;
            }

            _watchNext = Time.time + 10f;
            List<Anchor> anchors = SimulationAnchors.Current;
            if (anchors.Count == 0)
            {
                return;
            }

            Vector3 centre = anchors[0].Position;
            var counts = new Dictionary<string, int>();
            int all = 0;
            float health = 0f;
            foreach (Character character in Character.s_characters)
            {
                if (character == null || character.IsDead())
                {
                    continue;
                }

                Vector3 offset = character.transform.position - centre;
                offset.y = 0f;
                if (offset.sqrMagnitude > _watchRadius * _watchRadius)
                {
                    continue;
                }

                all++;
                Count(counts, Utils.GetPrefabName(character.gameObject));
                health += character.GetHealth();
            }

            Plugin.Log.LogInfo($"Watch t={Time.time - _watchStarted:F0}s step {Time.fixedDeltaTime * 1000f:0.#} ms: {all} alive within {_watchRadius:F0} m ({Top(counts)}), health {health:F0}; " +
                               $"since last: {MeleeHits} melee and {RangedHits} ranged damage hits, {ProjectileHits} projectile hits ({ProjectileCreatureHits} on creatures).");
            MeleeHits = 0;
            RangedHits = 0;
            ProjectileHits = 0;
            ProjectileCreatureHits = 0;
            if (Time.time >= _watchUntil)
            {
                Watching = false;
            }
        }

        private static float _probeReportAt;

        internal static void Tick()
        {
            Watch();
            if (Time.realtimeSinceStartup >= _probeReportAt)
            {
                _probeReportAt = Time.realtimeSinceStartup + 60f;
                string probes = GroundProbes.ClosePeriod();
                if (probes != null)
                {
                    Plugin.Log.LogInfo(probes);
                }
            }

            if (Patched.Count == 0 || Time.realtimeSinceStartup < _until)
            {
                return;
            }

            float seconds = Mathf.Max(Time.realtimeSinceStartup - _started, 0.001f);
            foreach (MethodInfo method in Patched)
            {
                try
                {
                    Plugin.Harmony.Unpatch(method, _prefix.method);
                    Plugin.Harmony.Unpatch(method, _postfix.method);
                }
                catch (Exception)
                {
                }
            }

            Patched.Clear();
            string top = string.Join("; ", Buckets
                .OrderByDescending(e => e.Value.Ticks)
                .Select(e => $"{e.Key.DeclaringType?.Name}.{e.Key.Name} {Ms(e.Value.Ticks) / seconds:F1} ms/s, {e.Value.Calls / seconds:F0} calls/s, {Ms(e.Value.Ticks) * 1000000.0 / Math.Max(e.Value.Calls, 1):F0} ns per call"));
            Plugin.Log.LogInfo($"Physics lab profile '{_group}' over {seconds:F0} s at {Time.fixedDeltaTime * 1000f:0.#} ms steps: {top}.");
            Buckets.Clear();
        }

        private static void StartProfile(float seconds, string group)
        {
            if (Patched.Count > 0)
            {
                Plugin.Log.LogInfo("Physics lab: a profile is already running.");
                return;
            }

            _prefix = new HarmonyMethod(typeof(PhysicsLab).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));
            _postfix = new HarmonyMethod(typeof(PhysicsLab).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
            Buckets.Clear();
            _group = group;
            foreach (MethodInfo method in Targets(group))
            {
                try
                {
                    Plugin.Harmony.Patch(method, _prefix, _postfix);
                    Patched.Add(method);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Physics lab: could not time {method.DeclaringType?.Name}.{method.Name}: {e.Message}");
                }
            }

            _started = Time.realtimeSinceStartup;
            _until = _started + Mathf.Max(seconds, 5f);
            Plugin.Log.LogInfo($"Physics lab: timing {Patched.Count} methods in group '{group}' for {_until - _started:F0} s.");
        }

        private static IEnumerable<MethodInfo> Targets(string group)
        {
            var wanted = new List<KeyValuePair<Type, string>>();
            void Add(Type type, params string[] names)
            {
                foreach (string name in names)
                {
                    wanted.Add(new KeyValuePair<Type, string>(type, name));
                }
            }

            switch (group)
            {
                case "char":
                    Add(typeof(Character), "CustomFixedUpdate", "CalculateLiquidDepth", "UpdateLayer", "UpdateContinousEffects", "UpdateWater",
                        "UpdateGroundTilt", "SetVisible", "UpdateLookTransition", "UpdateGroundContact", "UpdateNoise", "UpdateStagger",
                        "UpdatePushback", "UpdateMotion", "UpdateWalking", "UpdateSwimming", "UpdateFlying", "UpdateSmoke", "UpdateLava",
                        "UpdateAshlandsWater", "UpdateHeatDamage", "UnderWorldCheck", "UpdatePheromones", "SyncVelocity", "CheckDeath",
                        "UpdateHeatEffects", "OnCollisionStay", "UpdateBodyFriction", "UpdateRotation", "UpdateEyeRotation");
                    Add(typeof(Humanoid), "CustomFixedUpdate", "UpdateAttack", "UpdateEquipment", "UpdateBlock", "UpdateUseVisual");
                    Add(typeof(SEMan), "Update");
                    break;
                case "sync":
                    Add(typeof(ZSyncTransform), "CustomFixedUpdate", "ClientSync", "CustomLateUpdate", "OwnerSync");
                    Add(typeof(ZSyncAnimation), "CustomFixedUpdate");
                    Add(typeof(Floating), "CustomFixedUpdate");
                    Add(typeof(Fish), "CustomFixedUpdate");
                    Add(typeof(CharacterAnimEvent), "CustomFixedUpdate", "CustomLateUpdate");
                    Add(typeof(Character), "OnCollisionStay");
                    Add(typeof(Projectile), "FixedUpdate");
                    Add(typeof(Ragdoll), "FixedUpdate");
                    Add(typeof(Aoe), "CustomFixedUpdate");
                    Add(typeof(EffectArea), "CustomFixedUpdate");
                    Add(typeof(MonsterAI), "UpdateAI");
                    Add(typeof(AnimalAI), "UpdateAI");
                    break;
                case "water":
                    Add(typeof(WaterVolume), "UpdateFloaters", "GetWaterSurface", "CalcWave", "UpdateMaterials", "StaticUpdate");
                    Add(typeof(Character), "SetLiquidLevel");
                    Add(typeof(ItemDrop), "SetLiquidLevel");
                    Add(typeof(Floating), "SetLiquidLevel");
                    break;
            }

            foreach (KeyValuePair<Type, string> entry in wanted)
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(entry.Key))
                {
                    if (method.Name == entry.Value && !method.IsAbstract && !method.IsGenericMethodDefinition)
                    {
                        yield return method;
                    }
                }
            }
        }

        private static void Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void Postfix(MethodBase __originalMethod, long __state)
        {
            long spent = Stopwatch.GetTimestamp() - __state;
            if (!Buckets.TryGetValue(__originalMethod, out Bucket bucket))
            {
                bucket = new Bucket();
                Buckets[__originalMethod] = bucket;
            }

            bucket.Ticks += spent;
            bucket.Calls++;
        }

        private static readonly Dictionary<int, RigidbodyInterpolation> Interpolated = new Dictionary<int, RigidbodyInterpolation>();

        private static void Interpolation(bool on)
        {
            int changed = 0;
            foreach (Rigidbody body in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                int id = body.GetInstanceID();
                if (!on && body.interpolation != RigidbodyInterpolation.None)
                {
                    Interpolated[id] = body.interpolation;
                    body.interpolation = RigidbodyInterpolation.None;
                    changed++;
                }
                else if (on && Interpolated.TryGetValue(id, out RigidbodyInterpolation mode))
                {
                    body.interpolation = mode;
                    changed++;
                }
            }

            if (on)
            {
                Interpolated.Clear();
            }

            Plugin.Log.LogInfo($"Physics lab: interpolation {(on ? "restored" : "turned off")} on {changed} bodies (existing bodies only).");
        }

        private static void IgnoreLayers(string[] parts)
        {
            if (parts.Length < 5)
            {
                Plugin.Log.LogInfo("Physics lab: ignore <layerA> <layerB> on|off.");
                return;
            }

            int a = LayerMask.NameToLayer(parts[2]);
            int b = LayerMask.NameToLayer(parts[3]);
            if (a < 0 || b < 0)
            {
                Plugin.Log.LogInfo($"Physics lab: unknown layer in '{parts[2]}' or '{parts[3]}'.");
                return;
            }

            Physics.IgnoreLayerCollision(a, b, parts[4] == "on");
            Plugin.Log.LogInfo($"Physics lab: collisions between {parts[2]} and {parts[3]} {(Physics.GetIgnoreLayerCollision(a, b) ? "ignored" : "enabled")}.");
        }

        private static void Census()
        {
            long started = Stopwatch.GetTimestamp();
            var text = new StringBuilder();
            text.Append("Physics census: ");
            text.Append($"fixed step {Time.fixedDeltaTime * 1000f:0.##} ms, maximumDeltaTime {Time.maximumDeltaTime * 1000f:0} ms, ");
            text.Append($"solver {Physics.defaultSolverIterations}/{Physics.defaultSolverVelocityIterations}, sleep threshold {Physics.sleepThreshold}, ");
            text.Append($"autoSyncTransforms {Physics.autoSyncTransforms}, contact offset {Physics.defaultContactOffset}, bounce threshold {Physics.bounceThreshold}, ");
            text.Append($"max depenetration {Physics.defaultMaxDepenetrationVelocity}, simulation mode {Physics.simulationMode}, ");
            text.Append($"queriesHitTriggers {Physics.queriesHitTriggers}, reuseCollisionCallbacks {Physics.reuseCollisionCallbacks}, ");
            text.Append($"improvedPatchFriction {Physics.improvedPatchFriction}, invokeCollisionCallbacks {Physics.invokeCollisionCallbacks}. ");

            Rigidbody[] bodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            int kinematic = 0;
            int sleeping = 0;
            int awake = 0;
            int interpolated = 0;
            int continuous = 0;
            var dynamicByLayer = new Dictionary<string, int>();
            var awakeByLayer = new Dictionary<string, int>();
            var awakeByPrefab = new Dictionary<string, int>();
            var sleepingByPrefab = new Dictionary<string, int>();
            var continuousByPrefab = new Dictionary<string, int>();
            var interpolatedByPrefab = new Dictionary<string, int>();
            var kinematicByLayer = new Dictionary<string, int>();
            var solver = new Dictionary<string, int>();
            var usedLayers = new HashSet<int>();
            foreach (Rigidbody body in bodies)
            {
                int layerIndex = body.gameObject.layer;
                usedLayers.Add(layerIndex);
                string layer = LayerMask.LayerToName(layerIndex);
                if (body.interpolation != RigidbodyInterpolation.None)
                {
                    interpolated++;
                    Count(interpolatedByPrefab, Prefab(body));
                }

                if (body.collisionDetectionMode != CollisionDetectionMode.Discrete)
                {
                    continuous++;
                    Count(continuousByPrefab, Prefab(body) + " " + body.collisionDetectionMode);
                }

                if (body.isKinematic)
                {
                    kinematic++;
                    Count(kinematicByLayer, layer);
                    continue;
                }

                Count(dynamicByLayer, layer);
                Count(solver, $"{body.solverIterations}/{body.solverVelocityIterations} sleep {body.sleepThreshold}");
                if (body.IsSleeping())
                {
                    sleeping++;
                    Count(sleepingByPrefab, Prefab(body));
                    continue;
                }

                awake++;
                Count(awakeByLayer, layer);
                Count(awakeByPrefab, Prefab(body));
            }

            text.Append($"Rigidbodies: {bodies.Length}, {kinematic} kinematic ({Top(kinematicByLayer)}), {bodies.Length - kinematic} dynamic ({Top(dynamicByLayer)}), ");
            text.Append($"{sleeping} of them asleep ({Top(sleepingByPrefab)}), {awake} awake by layer ({Top(awakeByLayer)}) and prefab ({Top(awakeByPrefab)}). ");
            text.Append($"Dynamic solver and sleep settings: {Top(solver)}. ");
            text.Append($"{interpolated} interpolated ({Top(interpolatedByPrefab)}), {continuous} with continuous detection ({Top(continuousByPrefab)}). ");

            Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            int triggers = 0;
            int onDynamic = 0;
            int onKinematic = 0;
            var staticByLayer = new Dictionary<string, int>();
            var dynamicColliders = new Dictionary<string, int>();
            foreach (Collider collider in colliders)
            {
                if (!collider.enabled)
                {
                    continue;
                }

                usedLayers.Add(collider.gameObject.layer);
                if (collider.isTrigger)
                {
                    triggers++;
                }

                Rigidbody attached = collider.attachedRigidbody;
                if (attached == null)
                {
                    Count(staticByLayer, LayerMask.LayerToName(collider.gameObject.layer));
                }
                else if (attached.isKinematic)
                {
                    onKinematic++;
                }
                else
                {
                    onDynamic++;
                    Count(dynamicColliders, LayerMask.LayerToName(collider.gameObject.layer) + (collider.isTrigger ? " trigger" : ""));
                }
            }

            text.Append($"Colliders enabled: {colliders.Length} found, {triggers} triggers, {onDynamic} on dynamic bodies ({Top(dynamicColliders)}), {onKinematic} on kinematic bodies, static by layer ({Top(staticByLayer)}). ");

            text.Append("Layer pairs that collide: ");
            List<int> layers = usedLayers.OrderBy(l => l).ToList();
            foreach (int a in layers)
            {
                var with = layers.Where(b => !Physics.GetIgnoreLayerCollision(a, b)).Select(b => LayerMask.LayerToName(b)).ToList();
                text.Append($"{LayerMask.LayerToName(a)}[{a}] with {(with.Count == 0 ? "none" : string.Join(",", with))}; ");
            }

            int syncs = 0;
            int owned = 0;
            int invalid = 0;
            int notOwned = 0;
            int withBody = 0;
            int bodyAwake = 0;
            var notOwnedByPrefab = new Dictionary<string, int>();
            foreach (IMonoUpdater updater in ZSyncTransform.Instances)
            {
                syncs++;
                ZSyncTransform sync = updater as ZSyncTransform;
                if (sync == null || sync.m_nview == null || !sync.m_nview.IsValid())
                {
                    invalid++;
                    continue;
                }

                if (sync.m_body != null)
                {
                    withBody++;
                    if (!sync.m_body.isKinematic && !sync.m_body.IsSleeping())
                    {
                        bodyAwake++;
                    }
                }

                if (sync.m_nview.GetZDO().IsOwner())
                {
                    owned++;
                }
                else
                {
                    notOwned++;
                    Count(notOwnedByPrefab, Utils.GetPrefabName(sync.gameObject) + (sync.m_nview.GetZDO().HasOwner() ? "" : " unowned"));
                }
            }

            long loopStarted = Stopwatch.GetTimestamp();
            int checks = 0;
            foreach (IMonoUpdater updater in ZSyncTransform.Instances)
            {
                ZSyncTransform sync = (ZSyncTransform)updater;
                if (sync.m_nview.IsValid() && sync.m_nview.GetZDO().IsOwner())
                {
                    checks++;
                }
            }

            double loopMs = Ms(Stopwatch.GetTimestamp() - loopStarted);
            text.Append($"ZSyncTransform: {syncs} instances, {owned} owned here, {notOwned} not ({Top(notOwnedByPrefab)}), {invalid} invalid, {withBody} with a rigidbody ({bodyAwake} awake); one pass checking ownership of all took {loopMs:F2} ms. ");

            int characters = 0;
            int dead = 0;
            int tamed = 0;
            int aiAsleep = 0;
            int bodyKinematic = 0;
            int bodySleeping = 0;
            int targeting = 0;
            int moving = 0;
            int inWater = 0;
            int humanoid = 0;
            foreach (IMonoUpdater updater in Character.Instances)
            {
                Character c = updater as Character;
                if (c == null)
                {
                    continue;
                }

                characters++;
                if (c is Humanoid)
                {
                    humanoid++;
                }

                if (c.IsDead())
                {
                    dead++;
                }

                if (c.IsTamed())
                {
                    tamed++;
                }

                if (c.m_baseAI != null && c.m_baseAI.IsSleeping())
                {
                    aiAsleep++;
                }

                if (c.m_body != null)
                {
                    if (c.m_body.isKinematic)
                    {
                        bodyKinematic++;
                    }
                    else if (c.m_body.IsSleeping())
                    {
                        bodySleeping++;
                    }
                }

                if (c.m_baseAI != null && (c.m_baseAI.GetTargetCreature() != null || c.m_baseAI.HaveTarget()))
                {
                    targeting++;
                }

                if (c.m_moveDir.sqrMagnitude > 0.01f)
                {
                    moving++;
                }

                if (c.InWater())
                {
                    inWater++;
                }
            }

            text.Append($"Characters: {characters} ({humanoid} Humanoid), {dead} dead, {tamed} tamed, {aiAsleep} with the AI asleep, {bodyKinematic} kinematic, {bodySleeping} with the body asleep, ");
            text.Append($"{targeting} with a target, {moving} asked to move, {inWater} in water. ");
            text.Append($"Floating: {Floating.Instances.Count}, Fish: {Fish.Instances.Count}, ZSyncAnimation: {ZSyncAnimation.Instances.Count}, CharacterAnimEvent: {CharacterAnimEvent.Instances.Count}, BaseAI: {BaseAI.Instances.Count}. ");

            long gcStarted = Stopwatch.GetTimestamp();
            long sink = 0;
            for (int i = 0; i < 1000; i++)
            {
                sink += GC.GetTotalMemory(false);
            }

            double gcNs = Ms(Stopwatch.GetTimestamp() - gcStarted) * 1000.0;
            text.Append($"GC.GetTotalMemory costs {gcNs:F0} ns per call{(sink == 42 ? "." : ".")} Census took {Ms(Stopwatch.GetTimestamp() - started):F0} ms.");
            Plugin.Log.LogInfo(text.ToString());
        }

        internal static void CountDamage(HitData hit)
        {
            if (!Watching || hit == null)
            {
                return;
            }

            if (hit.m_ranged)
            {
                RangedHits++;
            }
            else
            {
                MeleeHits++;
            }
        }

        internal static void CountProjectileHit(Collider collider)
        {
            if (!Watching)
            {
                return;
            }

            ProjectileHits++;
            if (collider != null && collider.GetComponentInParent<Character>() != null)
            {
                ProjectileCreatureHits++;
            }
        }

        private static string Prefab(Rigidbody body)
        {
            ZNetView view = body.GetComponentInParent<ZNetView>();
            return Utils.GetPrefabName(view != null ? view.gameObject : body.transform.root.gameObject);
        }

        private static void Count(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        private static string Top(Dictionary<string, int> counts)
        {
            return counts.Count == 0
                ? "none"
                : string.Join(", ", counts.OrderByDescending(e => e.Value).Take(Shown).Select(e => $"{e.Value} {e.Key}"));
        }

        private static double Ms(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class Character_ApplyDamage_PhysicsLab_Patch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (PhysicsLab.Watching && !__instance.IsDead())
            {
                PhysicsLab.CountDamage(hit);
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class Projectile_OnHit_PhysicsLab_Patch
    {
        private static void Prefix(Collider collider)
        {
            if (PhysicsLab.Watching)
            {
                PhysicsLab.CountProjectileHit(collider);
            }
        }
    }
}
#endif
