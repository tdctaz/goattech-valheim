using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority
{
    internal static class GroundProbes
    {
        private const float GroundMove = 0.25f;
        private const float GroundAge = 1f;
        private const float TiltMove = 0.1f;
        private const float TiltAge = 0.2f;
        private const float TiltTurn = 0.985f;
        private const float ForgetSeconds = 60f;

        private struct Ground
        {
            internal float Time;
            internal Vector3 At;
            internal Vector3 Point;
            internal Vector3 Normal;
            internal Heightmap.Biome Biome;
            internal Heightmap.BiomeArea Area;
            internal Heightmap Map;
        }

        private struct Tilt
        {
            internal float Time;
            internal Vector3 At;
            internal Vector3 Forward;
            internal Vector3 FrontNormal;
            internal Vector3 BackNormal;
            internal float FrontHeight;
            internal float BackHeight;
            internal bool FrontHit;
            internal bool BackHit;
        }

        private static readonly Dictionary<int, Ground> Grounds = new Dictionary<int, Ground>();
        private static readonly Dictionary<int, Tilt> Tilts = new Dictionary<int, Tilt>();
        private static float _forgetAt;
        private static int _backFrom;
        private static Tilt _pending;
        private static int _override = -1;

        internal static int Probes;
        internal static int Reused;

        internal static bool Active => Plugin.ServerActive &&
                                       (_override >= 0 ? _override > 0 : PhysicsTuning.CacheGroundProbes.Value);

        internal static void SetOverride(int on)
        {
            _override = on;
            Grounds.Clear();
            Tilts.Clear();
            Plugin.Log.LogInfo($"Ground probes: creature ground probes {(Active ? "reused while a creature stays put" : "cast every step as in vanilla")}.");
        }

        internal static string ClosePeriod()
        {
            if (Probes + Reused == 0)
            {
                return null;
            }

            string text = $"Ground probes: {Probes} cast, {Reused} reused.";
            Probes = 0;
            Reused = 0;
            return text;
        }

        private static void Forget(float now)
        {
            if (now < _forgetAt)
            {
                return;
            }

            _forgetAt = now + ForgetSeconds;
            Grounds.Clear();
            Tilts.Clear();
        }

        internal static void GroundData(ZoneSystem zones, ref Vector3 p, out Vector3 normal, out Heightmap.Biome biome,
            out Heightmap.BiomeArea area, out Heightmap map, Character character)
        {
            if (!Active || character == null)
            {
                zones.GetGroundData(ref p, out normal, out biome, out area, out map);
                return;
            }

            float now = Time.fixedTime;
            Forget(now);
            int id = character.GetInstanceID();
            if (Grounds.TryGetValue(id, out Ground cached) && now - cached.Time < GroundAge && now >= cached.Time &&
                Flat(p - cached.At) < GroundMove * GroundMove)
            {
                p.y = cached.Point.y;
                normal = cached.Normal;
                biome = cached.Biome;
                area = cached.Area;
                map = cached.Map;
                Reused++;
                return;
            }

            Vector3 at = p;
            zones.GetGroundData(ref p, out normal, out biome, out area, out map);
            Probes++;
            Grounds[id] = new Ground { Time = now, At = at, Point = p, Normal = normal, Biome = biome, Area = area, Map = map };
        }

        internal static bool TiltHeight(Character character, Vector3 p, out float height, out Vector3 normal, int side)
        {
            if (!Active)
            {
                return character.GetGroundHeight(p, out height, out normal);
            }

            int id = character.GetInstanceID();
            if (side == 0)
            {
                float now = Time.fixedTime;
                Forget(now);
                Transform transform = character.transform;
                Vector3 at = transform.position;
                Vector3 forward = transform.forward;
                if (Tilts.TryGetValue(id, out Tilt cached) && now - cached.Time < TiltAge && now >= cached.Time &&
                    Flat(at - cached.At) < TiltMove * TiltMove && Vector3.Dot(forward, cached.Forward) > TiltTurn)
                {
                    _backFrom = id;
                    height = cached.FrontHeight;
                    normal = cached.FrontNormal;
                    Reused++;
                    return cached.FrontHit;
                }

                _backFrom = 0;
                bool hit = character.GetGroundHeight(p, out height, out normal);
                Probes++;
                _pending = new Tilt { Time = now, At = at, Forward = forward, FrontHeight = height, FrontNormal = normal, FrontHit = hit };
                return hit;
            }

            if (_backFrom == id && Tilts.TryGetValue(id, out Tilt kept))
            {
                _backFrom = 0;
                height = kept.BackHeight;
                normal = kept.BackNormal;
                Reused++;
                return kept.BackHit;
            }

            bool backHit = character.GetGroundHeight(p, out height, out normal);
            Probes++;
            _pending.BackHeight = height;
            _pending.BackNormal = normal;
            _pending.BackHit = backHit;
            Tilts[id] = _pending;
            return backHit;
        }

        private static float Flat(Vector3 offset)
        {
            return offset.x * offset.x + offset.z * offset.z;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdateLava))]
    internal static class Character_UpdateLava_GroundProbes_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(ZoneSystem), nameof(ZoneSystem.GetGroundData));
            MethodInfo cached = AccessTools.Method(typeof(GroundProbes), nameof(GroundProbes.GroundData));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = cached;
                    replaced++;
                }

                yield return instruction;
            }

            if (replaced != 1)
            {
                Plugin.Log.LogWarning($"Ground probes: expected one ground query in Character.UpdateLava, replaced {replaced}.");
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdateGroundTilt))]
    internal static class Character_UpdateGroundTilt_GroundProbes_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(Character), nameof(Character.GetGroundHeight));
            MethodInfo cached = AccessTools.Method(typeof(GroundProbes), nameof(GroundProbes.TiltHeight));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4, replaced).MoveLabelsFrom(instruction);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = cached;
                    replaced++;
                }

                yield return instruction;
            }

            if (replaced != 2)
            {
                Plugin.Log.LogWarning($"Ground probes: expected two ground rays in Character.UpdateGroundTilt, replaced {replaced}.");
            }
        }
    }
}
