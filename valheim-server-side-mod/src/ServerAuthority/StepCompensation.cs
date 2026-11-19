using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority
{
    internal static class StepCompensation
    {
        internal const float VanillaStep = 0.02f;

        private static int _override = -1;
        private static float _forStep = -1f;
        private static float _ratio = 1f;
        private static float _follow = 0.2f;
        private static float _turn = 0.5f;

        internal static bool Active
        {
            get
            {
                if (!Plugin.ServerActive || Mathf.Abs(Time.fixedDeltaTime - VanillaStep) < 0.0001f)
                {
                    return false;
                }

                return _override >= 0 ? _override > 0 : PhysicsTuning.CompensatePhysicsRate.Value;
            }
        }

        internal static void SetOverride(int on)
        {
            _override = on;
            Plugin.Log.LogInfo($"Step compensation: {(Active ? "on" : "off")} at a {Time.fixedDeltaTime * 1000f:0.#} ms step.");
        }

        internal static float Ratio
        {
            get
            {
                Refresh();
                return _ratio;
            }
        }

        private static void Refresh()
        {
            float step = Time.fixedDeltaTime;
            if (step == _forStep)
            {
                return;
            }

            _forStep = step;
            _ratio = step / VanillaStep;
            _follow = 1f - Mathf.Pow(0.8f, _ratio);
            _turn = 1f - Mathf.Pow(0.5f, _ratio);
        }

        internal static float Follow()
        {
            if (!Active)
            {
                return 0.2f;
            }

            Refresh();
            return _follow;
        }

        internal static float Turn()
        {
            if (!Active)
            {
                return 0.5f;
            }

            Refresh();
            return _turn;
        }

        internal static float AiStep()
        {
            return Active ? VanillaStep : Time.fixedDeltaTime;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyRootMotion))]
    internal static class Character_ApplyRootMotion_Step_Patch
    {
        private static void Prefix(Character __instance)
        {
            if (StepCompensation.Active)
            {
                __instance.m_rootMotion /= StepCompensation.Ratio;
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdateWalking))]
    internal static class Character_UpdateWalking_Step_Patch
    {
        private static void Prefix(Character __instance, out float __state)
        {
            __state = __instance.m_acceleration;
            if (StepCompensation.Active)
            {
                __instance.m_acceleration *= StepCompensation.Ratio;
            }
        }

        private static void Postfix(Character __instance, float __state)
        {
            __instance.m_acceleration = __state;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdateSwimming))]
    internal static class Character_UpdateSwimming_Step_Patch
    {
        private static void Prefix(Character __instance, out float __state)
        {
            __state = __instance.m_swimAcceleration;
            if (StepCompensation.Active && !__instance.IsPlayer())
            {
                __instance.m_swimAcceleration *= StepCompensation.Ratio;
            }
        }

        private static void Postfix(Character __instance, float __state)
        {
            __instance.m_swimAcceleration = __state;
        }
    }

    [HarmonyPatch(typeof(Floating), nameof(Floating.CustomFixedUpdate))]
    internal static class Floating_CustomFixedUpdate_Step_Patch
    {
        private static void Prefix(Floating __instance, out float __state)
        {
            __state = __instance.m_damping;
            if (StepCompensation.Active)
            {
                __instance.m_damping = Mathf.Min(__instance.m_damping * StepCompensation.Ratio, 1f);
            }
        }

        private static void Postfix(Floating __instance, float __state)
        {
            __instance.m_damping = __state;
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate_Step_Patch
    {
        internal struct Saved
        {
            internal bool Changed;
            internal float Damping;
            internal float Forward;
            internal float Sideway;
            internal float Angular;
            internal float Sail;
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Ship __instance, out Saved __state)
        {
            __state = default;
            if (!StepCompensation.Active)
            {
                return;
            }

            float ratio = StepCompensation.Ratio;
            __state = new Saved
            {
                Changed = true,
                Damping = __instance.m_damping,
                Forward = __instance.m_dampingForward,
                Sideway = __instance.m_dampingSideway,
                Angular = __instance.m_angularDamping,
                Sail = __instance.m_sailForceFactor,
            };
            __instance.m_damping *= ratio;
            __instance.m_dampingForward *= ratio;
            __instance.m_dampingSideway *= ratio;
            __instance.m_angularDamping = Mathf.Min(__instance.m_angularDamping * ratio, 1f);
            __instance.m_sailForceFactor *= ratio;
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Ship __instance, Saved __state)
        {
            if (!__state.Changed)
            {
                return;
            }

            __instance.m_damping = __state.Damping;
            __instance.m_dampingForward = __state.Forward;
            __instance.m_dampingSideway = __state.Sideway;
            __instance.m_angularDamping = __state.Angular;
            __instance.m_sailForceFactor = __state.Sail;
        }
    }

    [HarmonyPatch]
    internal static class ZSyncTransform_Follow_Step_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZSyncTransform), nameof(ZSyncTransform.SyncPosition));
            yield return AccessTools.Method(typeof(ZSyncTransform), nameof(ZSyncTransform.ClientSync));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo follow = AccessTools.Method(typeof(StepCompensation), nameof(StepCompensation.Follow));
            MethodInfo turn = AccessTools.Method(typeof(StepCompensation), nameof(StepCompensation.Turn));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value)
                {
                    if (value == 0.2f)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = follow;
                        replaced++;
                    }
                    else if (value == 0.5f)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = turn;
                        replaced++;
                    }
                }

                yield return instruction;
            }

            int expected = __originalMethod.Name == nameof(ZSyncTransform.SyncPosition) ? 3 : 2;
            if (replaced != expected)
            {
                Plugin.Log.LogWarning($"Step compensation: expected {expected} smoothing factors in ZSyncTransform.{__originalMethod.Name}, replaced {replaced}.");
            }
        }
    }

    [HarmonyPatch]
    internal static class Ai_StepTimer_Step_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(BaseAI), nameof(BaseAI.MoveAndAvoid));
            yield return AccessTools.Method(typeof(Tameable), nameof(Tameable.UpdateSavedFollowTarget));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.fixedDeltaTime));
            MethodInfo step = AccessTools.Method(typeof(StepCompensation), nameof(StepCompensation.AiStep));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = step;
                    replaced++;
                }

                yield return instruction;
            }

            if (replaced != 1)
            {
                Plugin.Log.LogWarning($"Step compensation: expected one step timer in {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}, replaced {replaced}.");
            }
        }
    }
}
