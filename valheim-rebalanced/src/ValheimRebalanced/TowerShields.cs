using System.Collections.Generic;
using UnityEngine;

namespace ValheimRebalanced
{
    internal static class TowerShields
    {
        private static readonly Dictionary<ItemDrop.ItemData.SharedData, List<HitData.DamageModPair>> Originals =
            new Dictionary<ItemDrop.ItemData.SharedData, List<HitData.DamageModPair>>();

        internal static void Refresh()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null)
            {
                return;
            }

            Balance balance = ConfigSync.Current;

            foreach (GameObject prefab in db.m_items)
            {
                ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (item == null)
                {
                    continue;
                }

                ItemDrop.ItemData.SharedData shared = item.m_itemData.m_shared;
                if (shared.m_itemType != ItemDrop.ItemData.ItemType.Shield || shared.m_timedBlockBonus > 1f)
                {
                    continue;
                }

                if (!Originals.TryGetValue(shared, out List<HitData.DamageModPair> original))
                {
                    original = new List<HitData.DamageModPair>(shared.m_damageModifiers);
                    Originals[shared] = original;
                }

                List<HitData.DamageModPair> modifiers = new List<HitData.DamageModPair>(original);
                Add(modifiers, HitData.DamageType.Pierce, balance.TowerShieldPierce);
                Add(modifiers, HitData.DamageType.Blunt, balance.TowerShieldBlunt);
                Add(modifiers, HitData.DamageType.Slash, balance.TowerShieldSlash);
                shared.m_damageModifiers = modifiers;
            }
        }

        private static void Add(List<HitData.DamageModPair> modifiers, HitData.DamageType type, HitData.DamageModifier modifier)
        {
            if (modifier == HitData.DamageModifier.Normal)
            {
                return;
            }

            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                if (modifiers[i].m_type != type)
                {
                    continue;
                }

                if (IsResistance(modifiers[i].m_modifier))
                {
                    return;
                }

                modifiers.RemoveAt(i);
            }

            modifiers.Add(new HitData.DamageModPair { m_type = type, m_modifier = modifier });
        }

        private static bool IsResistance(HitData.DamageModifier modifier)
        {
            return modifier == HitData.DamageModifier.SlightlyResistant ||
                   modifier == HitData.DamageModifier.Resistant ||
                   modifier == HitData.DamageModifier.VeryResistant ||
                   modifier == HitData.DamageModifier.Immune ||
                   modifier == HitData.DamageModifier.Ignore;
        }
    }
}
