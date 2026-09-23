using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ValheimCreatures.Patches
{
    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    internal static class Character_Awake_Patch
    {
        private static void Postfix(Character __instance)
        {
            CreatureTraits.Attach(__instance);
            LevelRoll.Capture(__instance);
        }
    }

    [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Spawn))]
    internal static class SpawnSystem_Spawn_Patch
    {
        private static void Prefix()
        {
            LevelRoll.BeginCapture();
        }

        private static void Postfix(SpawnSystem.SpawnData critter, Vector3 spawnPoint, bool eventSpawner)
        {
            Character character = LevelRoll.EndCapture();
            string source = eventSpawner ? "raid" : "world";
            bool eligible = critter.m_maxLevel > 1 || (eventSpawner && StarCapable.Can(character));
            if (character == null || !eligible)
            {
                LevelRoll.Skipped(character, source, $"spawn max level {critter.m_maxLevel}");
                return;
            }

            if (critter.m_levelUpMinCenterDistance > 0f && spawnPoint.magnitude <= critter.m_levelUpMinCenterDistance &&
                !BossProgress.CenterProtectionLifted())
            {
                LevelRoll.Skipped(character, source, "too close to the world center");
                return;
            }

            LevelRoll.Roll(character, critter.m_minLevel, critter.m_overrideLevelupChance, spawnPoint, source);
        }

        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (__exception != null)
            {
                LevelRoll.EndCapture();
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Spawn))]
    internal static class CreatureSpawner_Spawn_Patch
    {
        private static void Postfix(CreatureSpawner __instance, ZNetView __result)
        {
            if (__result == null)
            {
                return;
            }

            Character character = __result.GetComponent<Character>();
            if (character == null)
            {
                return;
            }

            int minLevel = __instance.m_minLevel;
            int maxLevel = __instance.m_maxLevel;
            float chance = __instance.m_levelupChance;
            Location location = __instance.m_location;
            if (location != null && !location.m_excludeEnemyLevelOverrideGroups.Contains(__instance.m_spawnGroupID))
            {
                if (location.m_enemyMinLevelOverride >= 0)
                {
                    minLevel = location.m_enemyMinLevelOverride;
                }

                if (location.m_enemyMaxLevelOverride >= 0)
                {
                    maxLevel = location.m_enemyMaxLevelOverride;
                }

                if (location.m_enemyLevelUpOverride >= 0f)
                {
                    chance = location.m_enemyLevelUpOverride;
                }
            }

            if (maxLevel <= 1)
            {
                LevelRoll.Skipped(character, "location", $"spawn max level {maxLevel}");
                return;
            }

            LevelRoll.Roll(character, minLevel, chance, character.transform.position, "location");
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanHearTarget), typeof(Transform), typeof(float), typeof(Character))]
    internal static class BaseAI_CanHearTarget_Patch
    {
        private static void Postfix(Transform me, float hearRange, Character target, ref bool __result)
        {
            if (__result || me == null || target == null)
            {
                return;
            }

            float distance = Vector3.Distance(target.transform.position, me.position);
            if (Character.InInterior(me))
            {
                hearRange = Mathf.Min(12f, hearRange);
            }

            if (distance > hearRange)
            {
                return;
            }

            CreatureTraits traits = CreatureTraits.Of(me.GetComponent<Character>());
            if (traits == null || traits.HearingMultiplier <= 1f)
            {
                return;
            }

            if (distance < target.GetNoiseRange() * traits.HearingMultiplier &&
                (!(target is Player player) || (!player.InDebugFlyMode() && !player.InGhostMode())))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SetRandomEvent))]
    internal static class RandEventSystem_SetRandomEvent_Patch
    {
        private static bool Prefix(RandomEvent ev, Vector3 pos)
        {
            return !RaidDirector.Intercept(ev, pos);
        }
    }

    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SendCurrentRandomEvent))]
    internal static class RandEventSystem_SendCurrentRandomEvent_Patch
    {
        private static bool Prefix(RandEventSystem __instance)
        {
            return !RaidDirector.SuppressVanillaBroadcast(__instance);
        }
    }

    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.GetCurrentSpawners))]
    internal static class RandEventSystem_GetCurrentSpawners_Patch
    {
        private static void Postfix(ref List<SpawnSystem.SpawnData> __result)
        {
            if (__result != null && RaidDirector.HoldOffSpawners())
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath_Patch
    {
        private static void Prefix(Character __instance)
        {
            Splitting.OnDeath(__instance);
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.OnDamaged))]
    internal static class SEMan_OnDamaged_Patch
    {
        private static void Prefix(SEMan __instance, HitData hit)
        {
            CreatureTraits traits = CreatureTraits.Of(__instance.m_character);
            if (traits == null || hit == null)
            {
                return;
            }

            float factor = traits.DamageTakenFactor(hit);
            if (factor < 1f)
            {
                hit.ApplyModifier(factor);
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class Humanoid_StartAttack_Patch
    {
        private static void Postfix(Humanoid __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }

            CreatureTraits traits = CreatureTraits.Of(__instance);
            if (traits == null || traits.AttackIntervalReduction <= 0f || !__instance.m_nview.IsOwner())
            {
                return;
            }

            ItemDrop.ItemData weapon = __instance.m_currentAttack?.m_weapon;
            if (weapon?.m_shared != null)
            {
                weapon.m_lastAttackTime -= weapon.m_shared.m_aiAttackInterval * traits.AttackIntervalReduction;
            }
        }
    }

    [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
    internal static class CharacterAnimEvent_CustomFixedUpdate_Patch
    {
        private static void Postfix(CharacterAnimEvent __instance)
        {
            float extra = AttackSpeed.Extra(__instance);
            if (extra > 0f && Mathf.Approximately(__instance.m_animator.speed, 1f))
            {
                __instance.m_animator.speed = 1f + extra;
            }
        }
    }

    [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.Speed))]
    internal static class CharacterAnimEvent_Speed_Patch
    {
        private static void Prefix(CharacterAnimEvent __instance, ref float speedScale)
        {
            float extra = AttackSpeed.Extra(__instance);
            if (extra > 0f)
            {
                speedScale *= 1f + extra;
            }
        }
    }

    internal static class AttackSpeed
    {
        internal static float Extra(CharacterAnimEvent animEvent)
        {
            Character character = animEvent.m_character;
            if (character == null || animEvent.m_nview == null || !animEvent.m_nview.IsValid() ||
                !animEvent.m_nview.IsOwner() || !character.InAttack())
            {
                return 0f;
            }

            CreatureTraits traits = CreatureTraits.Of(character);
            return traits != null ? traits.AttackSpeed : 0f;
        }
    }

    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    internal static class CharacterDrop_GenerateDropList_Patch
    {
        private static readonly MethodInfo Pow = AccessTools.Method(typeof(Mathf), nameof(Mathf.Pow));
        private static readonly MethodInfo Multiplier = AccessTools.Method(typeof(Loot), nameof(Loot.LevelMultiplier));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(Pow))
                {
                    instruction.operand = Multiplier;
                    replaced++;
                }

                yield return instruction;
            }

            if (replaced != 1)
            {
                Plugin.Log.LogWarning(
                    $"CharacterDrop.GenerateDropList has {replaced} calls to Mathf.Pow where 1 was expected; " +
                    "loot for creatures above two stars may be wrong.");
            }
        }

        private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
        {
            CreatureTraits traits = CreatureTraits.Of(__instance.m_character);
            if (traits != null && traits.DropsSuppressed)
            {
                __result.Clear();
            }

#if DEBUG_TOOLS
            TestCommands.OnDrops(__instance.m_character, __result);
#endif
        }
    }

    [HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.SetupLevelVisualization))]
    internal static class LevelEffects_SetupLevelVisualization_Patch
    {
        private static void Prefix(LevelEffects __instance, ref int level)
        {
            int count = __instance.m_levelSetups.Count;
            if (count > 0 && level > count + 1)
            {
                level = count + 1;
            }
        }

        private static void Postfix(LevelEffects __instance, int level)
        {
            Looks.KeepVanillaSizeIndoors(__instance, level);
        }
    }

    [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
    internal static class EnemyHud_UpdateHuds_Patch
    {
        private static void Postfix(EnemyHud __instance)
        {
            StarHud.Update(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    internal static class ZNetScene_Awake_Patch
    {
        private static void Postfix(ZNetScene __instance)
        {
            Looks.ExtendLevelSetups(__instance);
#if DEBUG_TOOLS
            BossProgress.LogFactions();
#endif
        }
    }
}
