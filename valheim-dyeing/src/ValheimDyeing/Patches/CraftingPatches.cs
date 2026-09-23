using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimDyeing.Patches
{
    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class InventoryGui_DoCrafting_Patch
    {
        private static bool Prefix(InventoryGui __instance, Player player)
        {
            DyeJob job = Dyeing.JobFor(__instance.m_craftRecipe);
            if (job == null)
            {
                return true;
            }

            Dye(__instance, player, job);
            return false;
        }

        private static void Dye(InventoryGui gui, Player player, DyeJob job)
        {
            Inventory inventory = player.GetInventory();
            ItemDrop.ItemData source = Dyeing.FindSource(inventory, job);
            CraftingStation station = player.GetCurrentCraftingStation();

            if (source == null)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
                station?.m_craftItemDoneFailEffects.Create(player.transform.position, Quaternion.identity);
                return;
            }

            bool free = player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost);
            string material = job.Material.m_itemData.m_shared.m_name;
            int need = ConfigSync.Current.AmountFor(job.Dye.Key);

            if (!free && inventory.CountItems(material) < need)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
                station?.m_craftItemDoneFailEffects.Create(player.transform.position, Quaternion.identity);
                return;
            }

            ItemDrop.ItemData.SharedData shared = Dyeing.FreshShared(job.ToPrefab);
            if (shared == null)
            {
                Plugin.Log.LogError($"Could not read the shared data of {job.ToPrefab.name}, nothing dyed.");
                return;
            }

            if (!free)
            {
                inventory.RemoveItem(material, need);
            }

            source.m_shared = shared;
            source.m_dropPrefab = job.ToPrefab;
            inventory.Changed();

            if (source.m_equipped)
            {
                player.SetupVisEquipment(player.m_visEquipment, false);
            }

            player.RaiseSkill(Skills.SkillType.Crafting);
            station?.m_craftItemDoneEffects.Create(player.transform.position, Quaternion.identity);

            Plugin.Log.LogInfo(
                $"{player.GetPlayerName()} dyed {job.BaseItem} quality {source.m_quality} " +
                $"{job.Dye.Key.ToLowerInvariant()} at a dyeing tub.");

            gui.UpdateCraftingPanel();
        }
    }

    [HarmonyPatch(typeof(Player), "HaveRequirementItems")]
    internal static class Player_HaveRequirementItems_Patch
    {
        private static void Postfix(Player __instance, Recipe piece, bool discover, ref bool __result)
        {
            DyeJob job = Dyeing.JobFor(piece);
            if (job == null || discover)
            {
                return;
            }

            Inventory inventory = __instance.GetInventory();
            if (inventory == null)
            {
                return;
            }

            bool free = __instance.NoCostCheat() ||
                        (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));

            if (Dyeing.FindSource(inventory, job) == null)
            {
                __result = false;
                return;
            }

            if (free)
            {
                __result = true;
                return;
            }

            int need = ConfigSync.Current.AmountFor(job.Dye.Key);
            __result = inventory.CountItems(job.Material.m_itemData.m_shared.m_name) >= need;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    internal static class InventoryGui_SetupRequirement_Patch
    {
        private static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, bool craft,
            bool __result)
        {
            if (!__result || !craft || req?.m_resItem == null || InventoryGui.instance == null)
            {
                return;
            }

            DyeJob job = Dyeing.JobFor(InventoryGui.instance.m_selectedRecipe.Recipe);
            if (job == null || req.m_resItem.gameObject.name != job.BaseItem)
            {
                return;
            }

            TMP_Text amount = elementRoot.Find("res_amount")?.GetComponent<TMP_Text>();
            if (amount == null)
            {
                return;
            }

            bool have = Dyeing.FindSource(player.GetInventory(), job) != null;
            if (!have)
            {
                amount.color = Mathf.Sin(Time.time * 10f) > 0f ? Color.red : Color.white;
            }

            UITooltip tooltip = elementRoot.GetComponent<UITooltip>();
            if (tooltip != null && ConfigSync.Current.RequireFullDurability)
            {
                tooltip.m_text = Localization.instance.Localize(req.m_resItem.m_itemData.m_shared.m_name) +
                                 "\n$goattech_dye_needs_whole";
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class InventoryGui_UpdateRecipeList_Patch
    {
        private static void Prefix(InventoryGui __instance, List<Recipe> recipes)
        {
            if (recipes == null || __instance.InCraftTab())
            {
                return;
            }

            recipes.RemoveAll(recipe => Dyeing.JobFor(recipe) != null);
        }
    }
}
