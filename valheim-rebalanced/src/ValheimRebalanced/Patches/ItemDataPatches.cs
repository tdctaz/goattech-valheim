using HarmonyLib;

namespace ValheimRebalanced.Patches
{
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    internal static class ObjectDB_Awake_Patch
    {
        private static void Postfix()
        {
            TowerShields.Refresh();
            RootArmor.Refresh();
            StaffOfProtection.Refresh();
            StaffOfEmbers.Refresh();
            StaffOfFracturing.Refresh();
            Trollstav.Refresh();
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB_Patch
    {
        private static void Postfix()
        {
            TowerShields.Refresh();
            RootArmor.Refresh();
            StaffOfProtection.Refresh();
            StaffOfEmbers.Refresh();
            StaffOfFracturing.Refresh();
            Trollstav.Refresh();
        }
    }
}
