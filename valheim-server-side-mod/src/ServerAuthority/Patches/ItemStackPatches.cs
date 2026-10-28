using HarmonyLib;

namespace ServerAuthority.Patches
{
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.AutoStackItems))]
    internal static class ItemDrop_AutoStackItems_Patch
    {
        private static void Prefix(ItemDrop __instance)
        {
            ItemStacks.BeforeAutoStack(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.RemoveOne))]
    internal static class ItemDrop_RemoveOne_Patch
    {
        private static void Prefix(ItemDrop __instance)
        {
            ItemStacks.BeforeRemoveOne(__instance);
        }
    }
}
