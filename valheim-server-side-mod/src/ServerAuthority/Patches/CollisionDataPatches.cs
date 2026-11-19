using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(Character), nameof(Character.OnCollisionStay))]
    internal static class Character_OnCollisionStay_CollisionData_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(Collision), nameof(Collision.contacts));
            MethodInfo reader = AccessTools.Method(typeof(CollisionData), nameof(CollisionData.Contacts));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = reader;
                    replaced++;
                }

                yield return instruction;
            }

            if (replaced != 1)
            {
                Plugin.Log.LogWarning($"Collision data: expected one contact read in Character.OnCollisionStay, replaced {replaced}.");
            }
        }
    }
}
