namespace ValheimRebalanced
{
    internal sealed class Balance
    {
        internal int MaxStaffOfTheWildRoots;
        internal HitData.DamageModifier TowerShieldPierce;
        internal HitData.DamageModifier TowerShieldBlunt;
        internal HitData.DamageModifier TowerShieldSlash;
        internal bool SwapRootArmorBonuses;
        internal bool StaffOfProtectionSingleTarget;
        internal float StaffOfEmbersBlunt;
        internal float StaffOfFracturingFire;
        internal int TrollstavMaxSummons;
        internal float TrollstavHealthCost;
        internal bool TrollstavLethalHealthCost;
        internal int BurntWoodCoalDivisor;

        internal static readonly Balance Vanilla = new Balance
        {
            StaffOfEmbersBlunt = 1f,
            StaffOfFracturingFire = 1f,
            TrollstavMaxSummons = 2,
            BurntWoodCoalDivisor = 1,
        };

        internal static Balance FromConfig()
        {
            return new Balance
            {
                MaxStaffOfTheWildRoots = ModConfig.MaxStaffOfTheWildRoots.Value,
                TowerShieldPierce = ModConfig.TowerShieldPierce.Value,
                TowerShieldBlunt = ModConfig.TowerShieldBlunt.Value,
                TowerShieldSlash = ModConfig.TowerShieldSlash.Value,
                SwapRootArmorBonuses = ModConfig.SwapRootArmorBonuses.Value,
                StaffOfProtectionSingleTarget = ModConfig.StaffOfProtectionSingleTarget.Value,
                StaffOfEmbersBlunt = ModConfig.StaffOfEmbersBlunt.Value,
                StaffOfFracturingFire = ModConfig.StaffOfFracturingFire.Value,
                TrollstavMaxSummons = ModConfig.TrollstavMaxSummons.Value,
                TrollstavHealthCost = ModConfig.TrollstavHealthCost.Value,
                TrollstavLethalHealthCost = ModConfig.TrollstavLethalHealthCost.Value,
                BurntWoodCoalDivisor = ModConfig.BurntWoodCoalDivisor.Value,
            };
        }

        internal void Write(ZPackage package)
        {
            package.Write(MaxStaffOfTheWildRoots);
            package.Write((int)TowerShieldPierce);
            package.Write((int)TowerShieldBlunt);
            package.Write((int)TowerShieldSlash);
            package.Write(SwapRootArmorBonuses);
            package.Write(StaffOfProtectionSingleTarget);
            package.Write(StaffOfEmbersBlunt);
            package.Write(StaffOfFracturingFire);
            package.Write(TrollstavMaxSummons);
            package.Write(TrollstavHealthCost);
            package.Write(TrollstavLethalHealthCost);
            package.Write(BurntWoodCoalDivisor);
        }

        internal static Balance Read(ZPackage package)
        {
            return new Balance
            {
                MaxStaffOfTheWildRoots = package.ReadInt(),
                TowerShieldPierce = (HitData.DamageModifier)package.ReadInt(),
                TowerShieldBlunt = (HitData.DamageModifier)package.ReadInt(),
                TowerShieldSlash = (HitData.DamageModifier)package.ReadInt(),
                SwapRootArmorBonuses = package.ReadBool(),
                StaffOfProtectionSingleTarget = package.ReadBool(),
                StaffOfEmbersBlunt = package.ReadSingle(),
                StaffOfFracturingFire = package.ReadSingle(),
                TrollstavMaxSummons = package.ReadInt(),
                TrollstavHealthCost = package.ReadSingle(),
                TrollstavLethalHealthCost = package.ReadBool(),
                BurntWoodCoalDivisor = package.ReadInt(),
            };
        }

        public override string ToString()
        {
            return $"roots per player {MaxStaffOfTheWildRoots}, tower shield pierce {TowerShieldPierce}, " +
                   $"blunt {TowerShieldBlunt}, slash {TowerShieldSlash}, root armor swapped {SwapRootArmorBonuses}, " +
                   $"staff of protection single target {StaffOfProtectionSingleTarget}, " +
                   $"staff of embers blunt multiplier {StaffOfEmbersBlunt}, " +
                   $"staff of fracturing fire multiplier {StaffOfFracturingFire}, " +
                   $"trollstav summons {TrollstavMaxSummons}, least health cost {TrollstavHealthCost}, " +
                   $"lethal {TrollstavLethalHealthCost}, wood per burnt coal {BurntWoodCoalDivisor}";
        }
    }
}
