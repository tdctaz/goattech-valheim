using System.Collections.Generic;
using UnityEngine;

namespace ValheimRebalanced
{
    internal static class RootArmor
    {
        private const string SetEffectName = "SE_Rebalanced_RootArmorSet";
        private static readonly string[] Pieces = { "ArmorRootChest", "ArmorRootLegs", "HelmetRoot" };

        private static StatusEffect _archery;
        private static StatusEffect _chestEquipEffect;
        private static List<HitData.DamageModPair> _chestModifiers;
        private static SE_Stats _set;
        private static bool _swapped;

        internal static void Refresh()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null)
            {
                return;
            }

            ItemDrop.ItemData.SharedData chest = Shared(db, Pieces[0]);
            if (chest == null || !Capture(chest))
            {
                return;
            }

            bool swap = ConfigSync.Current.SwapRootArmorBonuses;

            if (swap)
            {
                chest.m_damageModifiers = Without(_chestModifiers, HitData.DamageType.Pierce);
                chest.m_equipStatusEffect = _archery;
                SetEffect(db, _set);
                if (!db.m_StatusEffects.Contains(_set))
                {
                    db.m_StatusEffects.Add(_set);
                }
            }
            else
            {
                chest.m_damageModifiers = new List<HitData.DamageModPair>(_chestModifiers);
                chest.m_equipStatusEffect = _chestEquipEffect;
                SetEffect(db, _archery);
            }

            if (swap != _swapped)
            {
                _swapped = swap;
                ReapplyLocalPlayer();
            }
        }

        private static bool Capture(ItemDrop.ItemData.SharedData chest)
        {
            if (_archery != null)
            {
                return true;
            }

            if (!(chest.m_setStatusEffect is SE_Stats archery))
            {
                Plugin.Log.LogWarning("Root armor has no stat based set effect, leaving it alone.");
                return false;
            }

            _archery = archery;
            _chestEquipEffect = chest.m_equipStatusEffect;
            _chestModifiers = new List<HitData.DamageModPair>(chest.m_damageModifiers);

            _set = Object.Instantiate(archery);
            _set.name = SetEffectName;
            _set.m_name = "Root armor";
            _set.m_tooltip = "The woven roots turn arrows and spears aside.";
            _set.m_skillLevel = Skills.SkillType.None;
            _set.m_skillLevelModifier = 0f;
            _set.m_skillLevel2 = Skills.SkillType.None;
            _set.m_skillLevelModifier2 = 0f;
            _set.m_mods = new List<HitData.DamageModPair>
            {
                new HitData.DamageModPair { m_type = HitData.DamageType.Pierce, m_modifier = PierceOf(_chestModifiers) },
            };

            return true;
        }

        private static void SetEffect(ObjectDB db, StatusEffect effect)
        {
            foreach (string piece in Pieces)
            {
                ItemDrop.ItemData.SharedData shared = Shared(db, piece);
                if (shared != null)
                {
                    shared.m_setStatusEffect = effect;
                }
            }
        }

        private static ItemDrop.ItemData.SharedData Shared(ObjectDB db, string prefabName)
        {
            GameObject prefab = db.GetItemPrefab(prefabName);
            ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return item != null ? item.m_itemData.m_shared : null;
        }

        private static HitData.DamageModifier PierceOf(List<HitData.DamageModPair> modifiers)
        {
            foreach (HitData.DamageModPair pair in modifiers)
            {
                if (pair.m_type == HitData.DamageType.Pierce)
                {
                    return pair.m_modifier;
                }
            }

            return HitData.DamageModifier.Resistant;
        }

        private static List<HitData.DamageModPair> Without(List<HitData.DamageModPair> modifiers, HitData.DamageType type)
        {
            List<HitData.DamageModPair> result = new List<HitData.DamageModPair>();
            foreach (HitData.DamageModPair pair in modifiers)
            {
                if (pair.m_type != type)
                {
                    result.Add(pair);
                }
            }

            return result;
        }

        private static void ReapplyLocalPlayer()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            foreach (StatusEffect effect in player.m_equipmentStatusEffects)
            {
                player.m_seman.RemoveStatusEffect(effect.NameHash(), true);
            }

            player.m_equipmentStatusEffects.Clear();
            player.UpdateEquipmentStatusEffects();
        }
    }
}
