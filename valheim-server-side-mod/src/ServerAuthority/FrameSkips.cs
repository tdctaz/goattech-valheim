using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ServerAuthority
{
    internal static class FrameSkips
    {
        private static readonly List<WaterVolume> Volumes = new List<WaterVolume>();

        private static int _syncOverride = -1;
        private static int _floatOverride = -1;
        private static bool _steppedSinceLateUpdate = true;

        internal static int SkippedFrames;
        internal static int SyncedFrames;

        internal static bool SyncAfterPhysicsOnly => Plugin.ServerActive &&
            (_syncOverride >= 0 ? _syncOverride > 0 : PhysicsTuning.SyncOnlyAfterPhysics.Value);

        internal static bool FloatPerStep => Plugin.ServerActive &&
            (_floatOverride >= 0 ? _floatOverride > 0 : PhysicsTuning.FloatPerPhysicsStep.Value);

        internal static void SetSyncOverride(int on)
        {
            _syncOverride = on;
            Plugin.Log.LogInfo($"Frame skips: owner position sync {(SyncAfterPhysicsOnly ? "only on frames after a physics step" : "every frame as in vanilla")}.");
        }

        internal static void SetFloatOverride(int on)
        {
            _floatOverride = on;
            Plugin.Log.LogInfo($"Frame skips: water levels under floating objects {(FloatPerStep ? "measured once per physics step" : "measured every frame as in vanilla")}.");
        }

        internal static void PhysicsStepped()
        {
            _steppedSinceLateUpdate = true;
        }

        internal static bool SkipLateSync()
        {
            if (!SyncAfterPhysicsOnly)
            {
                _steppedSinceLateUpdate = true;
                return false;
            }

            if (_steppedSinceLateUpdate)
            {
                _steppedSinceLateUpdate = false;
                SyncedFrames++;
                return false;
            }

            SkippedFrames++;
            return true;
        }

        internal static void FloatersFromUpdate(WaterVolume volume)
        {
            if (!FloatPerStep)
            {
                volume.UpdateFloaters();
            }
        }

        internal static void FloatersFromFixedUpdate()
        {
            if (!FloatPerStep)
            {
                return;
            }

            Volumes.AddRange(WaterVolume.Instances);
            try
            {
                foreach (WaterVolume volume in Volumes)
                {
                    if (volume != null)
                    {
                        volume.UpdateFloaters();
                    }
                }
            }
            finally
            {
                Volumes.Clear();
            }
        }
    }

    [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.FixedUpdate))]
    internal static class MonoUpdaters_FixedUpdate_FrameSkips_Patch
    {
        private static void Postfix()
        {
            FrameSkips.PhysicsStepped();
        }
    }

    [HarmonyPatch(typeof(MonoUpdatersExtra), nameof(MonoUpdatersExtra.CustomLateUpdate))]
    internal static class MonoUpdatersExtra_CustomLateUpdate_FrameSkips_Patch
    {
        private static bool _failed;

        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(List<IMonoUpdater> source)
        {
            if (_failed || source != ZSyncTransform.Instances)
            {
                return true;
            }

            try
            {
                return !FrameSkips.SkipLateSync();
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Frame skips: owner sync skipping failed and is off for this session: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.Update))]
    internal static class MonoUpdaters_Update_FrameSkips_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(WaterVolume), nameof(WaterVolume.UpdateFloaters));
            MethodInfo wrapper = AccessTools.Method(typeof(FrameSkips), nameof(FrameSkips.FloatersFromUpdate));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = wrapper;
                    replaced++;
                }

                yield return instruction;
            }

            if (replaced != 1)
            {
                Plugin.Log.LogWarning($"Frame skips: expected one floater update in MonoUpdaters.Update, replaced {replaced}.");
            }
        }
    }
}
