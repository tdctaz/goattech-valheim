using System.Collections.Generic;
using UnityEngine;

namespace ValheimRebalanced
{
    internal static class Items
    {
        private static readonly HashSet<string> Missing = new HashSet<string>();

        internal static ItemDrop.ItemData.SharedData Shared(ObjectDB db, string prefabName, string what)
        {
            ItemDrop item = Item(db, prefabName);
            if (item == null)
            {
                if (Missing.Add(prefabName))
                {
                    Plugin.Log.LogWarning($"No {prefabName} item, leaving {what} alone until one appears.");
                }

                return null;
            }

            Missing.Remove(prefabName);
            return item.m_itemData.m_shared;
        }

        internal static ItemDrop Item(ObjectDB db, string prefabName)
        {
            GameObject prefab = db != null ? db.GetItemPrefab(prefabName) : null;
            return prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        }
    }
}
