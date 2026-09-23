namespace ValheimCreatures
{
    internal sealed class Balance
    {
        internal bool Enabled;

        internal int MaxStars = 2;
        internal int MaxStarsBeforeTrophy = 2;
        internal int MaxStarsWithoutBoss = 2;
        internal float LevelUpChance = 10f;
        internal float BossDefeatedChanceMultiplier = 1f;
        internal string BossFactions = "";
        internal string BossCreatures = "";
        internal string StarlessBosses = "";
        internal int StarsPerBoss = 1;
        internal string CenterProtectionBoss = "";

        internal float[] CreatureWeights = new float[StarColors.Count];
        internal float[] BossWeights = new float[StarColors.Count];

        internal float FastMoveSpeed;
        internal float FastTurnSpeed;
        internal float AggressiveAttackSpeed;
        internal float AggressiveAttackInterval;
        internal float AggressiveCircleDuration;
        internal float AggressiveCircleInterval;
        internal float RegeneratingRate;
        internal float CuriousSenseRange;
        internal float ArmoredDamageTaken;
        internal float ArmoredMoveSpeed;

        internal float BossFastMoveSpeed;
        internal float BossFastTurnSpeed;
        internal float BossAggressiveAttackSpeed;
        internal float BossAggressiveAttackInterval;
        internal float BossRegeneratingPercent;
        internal float BossShieldedMagic;
        internal float BossShieldedArrows;
        internal string BossWaves = "";
        internal int BossWaveCount;
        internal float BossWaveStep;
        internal float BossWaveDistance;
        internal bool RaidWaves;
        internal float RaidWaveInterval;
        internal int RaidMaxWaves;
        internal int RaidPlayerWavesMax;
        internal int RaidComfortPerWave;
        internal float RaidTimeoutMinutes;
        internal float RaidSpawnDistance;

        internal static readonly Balance Vanilla = new Balance();

        internal static Balance FromConfig()
        {
            Balance balance = new Balance
            {
                Enabled = true,
                MaxStars = ModConfig.MaxStars.Value,
                MaxStarsBeforeTrophy = ModConfig.MaxStarsBeforeTrophy.Value,
                MaxStarsWithoutBoss = ModConfig.MaxStarsWithoutBoss.Value,
                LevelUpChance = ModConfig.LevelUpChance.Value,
                BossDefeatedChanceMultiplier = ModConfig.BossDefeatedChanceMultiplier.Value,
                BossFactions = ModConfig.BossFactions.Value,
                BossCreatures = ModConfig.BossCreatures.Value,
                StarlessBosses = ModConfig.StarlessBosses.Value,
                StarsPerBoss = ModConfig.StarsPerBoss.Value,
                CenterProtectionBoss = ModConfig.CenterProtectionBoss.Value,
                FastTurnSpeed = ModConfig.FastTurnSpeed.Value,
                BossFastTurnSpeed = ModConfig.BossFastTurnSpeed.Value,
                FastMoveSpeed = ModConfig.FastMoveSpeed.Value,
                AggressiveAttackSpeed = ModConfig.AggressiveAttackSpeed.Value,
                AggressiveAttackInterval = ModConfig.AggressiveAttackInterval.Value,
                AggressiveCircleDuration = ModConfig.AggressiveCircleDuration.Value,
                AggressiveCircleInterval = ModConfig.AggressiveCircleInterval.Value,
                RegeneratingRate = ModConfig.RegeneratingRate.Value,
                CuriousSenseRange = ModConfig.CuriousSenseRange.Value,
                ArmoredDamageTaken = ModConfig.ArmoredDamageTaken.Value,
                ArmoredMoveSpeed = ModConfig.ArmoredMoveSpeed.Value,
                BossFastMoveSpeed = ModConfig.BossFastMoveSpeed.Value,
                BossAggressiveAttackSpeed = ModConfig.BossAggressiveAttackSpeed.Value,
                BossAggressiveAttackInterval = ModConfig.BossAggressiveAttackInterval.Value,
                BossRegeneratingPercent = ModConfig.BossRegeneratingPercent.Value,
                BossShieldedMagic = ModConfig.BossShieldedMagic.Value,
                BossShieldedArrows = ModConfig.BossShieldedArrows.Value,
                BossWaves = ModConfig.BossWaves.Value,
                BossWaveCount = ModConfig.BossWaveCount.Value,
                BossWaveStep = ModConfig.BossWaveStep.Value,
                BossWaveDistance = ModConfig.BossWaveDistance.Value,
                RaidWaves = ModConfig.RaidWaves.Value,
                RaidWaveInterval = ModConfig.RaidWaveInterval.Value,
                RaidMaxWaves = ModConfig.RaidMaxWaves.Value,
                RaidPlayerWavesMax = ModConfig.RaidPlayerWavesMax.Value,
                RaidComfortPerWave = ModConfig.RaidComfortPerWave.Value,
                RaidTimeoutMinutes = ModConfig.RaidTimeoutMinutes.Value,
                RaidSpawnDistance = ModConfig.RaidSpawnDistance.Value,
            };

            for (int i = 0; i < StarColors.Count; i++)
            {
                balance.CreatureWeights[i] = ModConfig.CreatureWeights[i]?.Value ?? 0f;
                balance.BossWeights[i] = ModConfig.BossWeights[i]?.Value ?? 0f;
            }

            return balance;
        }

        internal void Write(ZPackage package)
        {
            package.Write(Enabled);
            package.Write(MaxStars);
            package.Write(MaxStarsBeforeTrophy);
            package.Write(MaxStarsWithoutBoss);
            package.Write(LevelUpChance);
            package.Write(BossDefeatedChanceMultiplier);
            package.Write(BossFactions);
            package.Write(BossCreatures);
            package.Write(StarlessBosses);
            package.Write(StarsPerBoss);
            package.Write(CenterProtectionBoss);
            for (int i = 0; i < StarColors.Count; i++)
            {
                package.Write(CreatureWeights[i]);
                package.Write(BossWeights[i]);
            }

            package.Write(FastMoveSpeed);
            package.Write(AggressiveAttackSpeed);
            package.Write(AggressiveAttackInterval);
            package.Write(AggressiveCircleDuration);
            package.Write(AggressiveCircleInterval);
            package.Write(RegeneratingRate);
            package.Write(CuriousSenseRange);
            package.Write(ArmoredDamageTaken);
            package.Write(ArmoredMoveSpeed);
            package.Write(BossFastMoveSpeed);
            package.Write(BossAggressiveAttackSpeed);
            package.Write(BossAggressiveAttackInterval);
            package.Write(BossRegeneratingPercent);
            package.Write(BossShieldedMagic);
            package.Write(BossShieldedArrows);
            package.Write(FastTurnSpeed);
            package.Write(BossFastTurnSpeed);
            package.Write(BossWaves);
            package.Write(BossWaveCount);
            package.Write(BossWaveStep);
            package.Write(BossWaveDistance);
            package.Write(RaidWaves);
            package.Write(RaidWaveInterval);
            package.Write(RaidMaxWaves);
            package.Write(RaidPlayerWavesMax);
            package.Write(RaidComfortPerWave);
            package.Write(RaidTimeoutMinutes);
            package.Write(RaidSpawnDistance);
        }

        internal static Balance Read(ZPackage package)
        {
            Balance balance = new Balance
            {
                Enabled = package.ReadBool(),
                MaxStars = package.ReadInt(),
                MaxStarsBeforeTrophy = package.ReadInt(),
                MaxStarsWithoutBoss = package.ReadInt(),
                LevelUpChance = package.ReadSingle(),
                BossDefeatedChanceMultiplier = package.ReadSingle(),
                BossFactions = package.ReadString(),
                BossCreatures = package.ReadString(),
                StarlessBosses = package.ReadString(),
                StarsPerBoss = package.ReadInt(),
                CenterProtectionBoss = package.ReadString(),
            };

            for (int i = 0; i < StarColors.Count; i++)
            {
                balance.CreatureWeights[i] = package.ReadSingle();
                balance.BossWeights[i] = package.ReadSingle();
            }

            balance.FastMoveSpeed = package.ReadSingle();
            balance.AggressiveAttackSpeed = package.ReadSingle();
            balance.AggressiveAttackInterval = package.ReadSingle();
            balance.AggressiveCircleDuration = package.ReadSingle();
            balance.AggressiveCircleInterval = package.ReadSingle();
            balance.RegeneratingRate = package.ReadSingle();
            balance.CuriousSenseRange = package.ReadSingle();
            balance.ArmoredDamageTaken = package.ReadSingle();
            balance.ArmoredMoveSpeed = package.ReadSingle();
            balance.BossFastMoveSpeed = package.ReadSingle();
            balance.BossAggressiveAttackSpeed = package.ReadSingle();
            balance.BossAggressiveAttackInterval = package.ReadSingle();
            balance.BossRegeneratingPercent = package.ReadSingle();
            balance.BossShieldedMagic = package.ReadSingle();
            balance.BossShieldedArrows = package.ReadSingle();
            balance.FastTurnSpeed = package.ReadSingle();
            balance.BossFastTurnSpeed = package.ReadSingle();
            balance.BossWaves = package.ReadString();
            balance.BossWaveCount = package.ReadInt();
            balance.BossWaveStep = package.ReadSingle();
            balance.BossWaveDistance = package.ReadSingle();
            balance.RaidWaves = package.ReadBool();
            balance.RaidWaveInterval = package.ReadSingle();
            balance.RaidMaxWaves = package.ReadInt();
            balance.RaidPlayerWavesMax = package.ReadInt();
            balance.RaidComfortPerWave = package.ReadInt();
            balance.RaidTimeoutMinutes = package.ReadSingle();
            balance.RaidSpawnDistance = package.ReadSingle();
            return balance;
        }

        public override string ToString()
        {
            return $"max stars {MaxStars}, before trophy {MaxStarsBeforeTrophy}, without boss {MaxStarsWithoutBoss}, " +
                   $"level up chance {LevelUpChance}%, boss defeated x{BossDefeatedChanceMultiplier}";
        }
    }
}
