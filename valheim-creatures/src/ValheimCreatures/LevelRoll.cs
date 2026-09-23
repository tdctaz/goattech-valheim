using UnityEngine;

namespace ValheimCreatures
{
    internal static class LevelRoll
    {
        private const float VanillaLevelUpChance = 10f;

        private static int _captureDepth;
        private static Character _captured;

        internal static void BeginCapture()
        {
            if (_captureDepth++ == 0)
            {
                _captured = null;
            }
        }

        internal static void Capture(Character character)
        {
            if (_captureDepth > 0 && _captured == null)
            {
                _captured = character;
            }
        }

        internal static Character EndCapture()
        {
            if (_captureDepth == 0)
            {
                return null;
            }

            Character captured = _captured;
            if (--_captureDepth == 0)
            {
                _captured = null;
            }

            return captured;
        }

        internal static void Roll(Character character, int minLevel, float spawnChance, Vector3 position, string source)
        {
            Balance balance = ConfigSync.Current;
            if (!balance.Enabled || character == null || character.IsBoss() || character.IsPlayer() ||
                character.m_nview == null || !character.m_nview.IsValid() || !character.m_nview.IsOwner())
            {
                Skipped(character, source, "not enabled, a boss, a player or not owned here");
                return;
            }

            BossStage stage = BossProgress.StageOf(character);
#if DEBUG_TOOLS
            int vanillaLevel = character.GetLevel();
#endif
            int level = RollLevel(stage, minLevel, spawnChance, position);
            character.SetLevel(level);
            StarColor color = RollColor(level);
            CreatureTraits.SetColor(character, color);
#if DEBUG_TOOLS
            if (ModConfig.LogRolls.Value)
            {
                Plugin.Log.LogInfo(
                    $"Roll: {source} {Utils.GetPrefabName(character.gameObject)} {stage}, min level {minLevel}, " +
                    $"chance {spawnChance}, vanilla level {vanillaLevel} -> {level - 1} stars, {color}.");
            }
#endif
        }

        internal static void Skipped(Character character, string source, string reason)
        {
#if DEBUG_TOOLS
            if (ModConfig.LogRolls.Value)
            {
                string name = character != null ? Utils.GetPrefabName(character.gameObject) : "nothing captured";
                int level = character != null ? character.GetLevel() : 0;
                Plugin.Log.LogInfo($"Roll: {source} {name} skipped ({reason}), stays at level {level}.");
            }
#endif
        }

        internal static int RollLevel(BossStage stage, int minLevel, float spawnChance, Vector3 position)
        {
            Balance balance = ConfigSync.Current;
            int maxStars;
            switch (stage)
            {
                case BossStage.NoBoss:
                    maxStars = balance.MaxStarsWithoutBoss;
                    break;
                case BossStage.TrophyPlaced:
                    maxStars = Mathf.Max(balance.MaxStars, balance.MaxStarsBeforeTrophy);
                    break;
                default:
                    maxStars = balance.MaxStarsBeforeTrophy;
                    break;
            }

            float baseChance = spawnChance > 0f ? spawnChance : VanillaLevelUpChance;
            baseChance *= balance.LevelUpChance / VanillaLevelUpChance;
            float chance = baseChance > 0f ? SpawnSystem.GetLevelUpChance(position, baseChance) : 0f;
            if (stage == BossStage.Defeated || stage == BossStage.TrophyPlaced)
            {
                chance *= balance.BossDefeatedChanceMultiplier;
            }

            int level = Mathf.Max(1, minLevel);
            int maxLevel = Mathf.Max(level, maxStars + 1);
            while (level < maxLevel && chance > 0f && Random.Range(0f, 100f) <= chance)
            {
                level++;
            }

            return level;
        }

        internal static StarColor RollColor(int level)
        {
            return level > 1 ? Pick(ConfigSync.Current.CreatureWeights, StarColors.Creature) : StarColor.None;
        }

        internal static void RollBoss(out StarColor first, out StarColor second)
        {
            Balance balance = ConfigSync.Current;
            first = Pick(balance.BossWeights, StarColors.Boss);
            second = StarColor.None;
            if (balance.StarsPerBoss < 2)
            {
                return;
            }

            float[] weights = (float[])balance.BossWeights.Clone();
            weights[(int)first] = 0f;
            if (Total(weights, StarColors.Boss) <= 0f)
            {
                return;
            }

            second = Pick(weights, StarColors.Boss);
            if (second == first)
            {
                second = StarColor.None;
            }
        }

        private static float Total(float[] weights, StarColor[] colors)
        {
            float total = Mathf.Max(0f, weights[(int)StarColor.None]);
            foreach (StarColor color in colors)
            {
                total += Mathf.Max(0f, weights[(int)color]);
            }

            return total;
        }

        private static StarColor Pick(float[] weights, StarColor[] colors)
        {
            float total = Total(weights, colors);
            if (total <= 0f)
            {
                return colors == StarColors.Boss ? colors[Random.Range(0, colors.Length)] : StarColor.None;
            }

            float pick = Random.Range(0f, total);
            float normal = Mathf.Max(0f, weights[(int)StarColor.None]);
            if (pick < normal)
            {
                return StarColor.None;
            }

            pick -= normal;
            foreach (StarColor color in colors)
            {
                float weight = Mathf.Max(0f, weights[(int)color]);
                if (pick < weight)
                {
                    return color;
                }

                pick -= weight;
            }

            for (int i = colors.Length - 1; i >= 0; i--)
            {
                if (weights[(int)colors[i]] > 0f)
                {
                    return colors[i];
                }
            }

            return StarColor.None;
        }
    }
}
