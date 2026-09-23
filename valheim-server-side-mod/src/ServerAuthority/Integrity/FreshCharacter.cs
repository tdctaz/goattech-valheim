using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority.Integrity
{
    internal static class FreshCharacter
    {
        internal static byte[] BuildPlayerData(PlayerDataSummary appearance)
        {
            Player prefab = Game.instance != null && Game.instance.m_playerPrefab != null
                ? Game.instance.m_playerPrefab.GetComponent<Player>()
                : null;

            float baseHp = prefab != null ? prefab.m_baseHP : 25f;
            float baseStamina = prefab != null ? prefab.m_baseStamina : 75f;

            ZPackage pkg = new ZPackage();
            pkg.Write(PlayerDataSummary.SupportedPlayerDataVersion);
            pkg.Write(baseHp);
            pkg.Write(baseHp);
            pkg.Write(baseStamina);
            pkg.Write(999999f);
            pkg.Write("");
            pkg.Write(0f);

            StartingInventory(prefab).Save(pkg);

            pkg.Write(0);
            pkg.Write(0);
            pkg.Write(0);
            pkg.Write(0);
            pkg.Write(0);
            pkg.Write(0);
            pkg.Write(0);
            pkg.Write(0);

            pkg.Write(appearance.Beard ?? "");
            pkg.Write(appearance.Hair ?? "");
            pkg.Write(appearance.SkinColor);
            pkg.Write(appearance.HairColor);
            pkg.Write(appearance.Model);

            pkg.Write(0);

            pkg.Write(2);
            pkg.Write(0);

            pkg.Write(0);
            pkg.Write(baseStamina);
            pkg.Write(0f);
            pkg.Write(0f);
            pkg.Write(new byte[0]);

            return pkg.GetArray();
        }

        private static Inventory StartingInventory(Player prefab)
        {
            Inventory inventory = new Inventory("Inventory", null, 8, 4);
            if (prefab?.m_defaultItems != null)
            {
                foreach (GameObject itemPrefab in prefab.m_defaultItems)
                {
                    AddStacks(inventory, itemPrefab, 1);
                }
            }

            foreach (KeyValuePair<GameObject, int> entry in ResolveKit(ModConfig.StartingKit.Value))
            {
                AddStacks(inventory, entry.Key, entry.Value);
            }

            return inventory;
        }

        private static void AddStacks(Inventory inventory, GameObject itemPrefab, int count)
        {
            ItemDrop drop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                return;
            }

            int maxStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            int remaining = Mathf.Max(1, count);
            while (remaining > 0)
            {
                ItemDrop.ItemData item = drop.m_itemData.Clone();
                item.m_dropPrefab = itemPrefab;
                item.m_stack = Mathf.Min(remaining, maxStack);
                item.m_quality = 1;
                item.m_durability = item.GetMaxDurability();
                if (!inventory.AddItem(item))
                {
                    Plugin.Log.LogWarning($"Starting kit: no room left for {itemPrefab.name} x{remaining}.");
                    return;
                }

                remaining -= item.m_stack;
            }
        }

        internal static Dictionary<GameObject, int> ResolveKit(string spec)
        {
            Dictionary<GameObject, int> kit = new Dictionary<GameObject, int>();
            if (string.IsNullOrWhiteSpace(spec))
            {
                return kit;
            }

            foreach (string raw in spec.Split(','))
            {
                string entry = raw.Trim();
                if (entry.Length == 0)
                {
                    continue;
                }

                string name = entry;
                int count = 1;
                int colon = entry.IndexOf(':');
                if (colon >= 0)
                {
                    name = entry.Substring(0, colon).Trim();
                    if (!int.TryParse(entry.Substring(colon + 1).Trim(), out count) || count < 1)
                    {
                        Plugin.Log.LogWarning($"Starting kit: '{entry}' has no valid count, skipped.");
                        continue;
                    }
                }

                GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
                if (item != null && item.GetComponent<ItemDrop>() != null)
                {
                    Add(kit, item, count);
                    continue;
                }

                GameObject pieceObject = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
                Piece piece = pieceObject != null ? pieceObject.GetComponent<Piece>() : null;
                if (piece == null)
                {
                    Plugin.Log.LogWarning($"Starting kit: '{name}' is neither an item nor a buildable piece, skipped.");
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    AddPiece(kit, piece);
                }

                Piece station = piece.m_craftingStation != null
                    ? piece.m_craftingStation.GetComponent<Piece>()
                    : null;
                if (station != null)
                {
                    AddPiece(kit, station);
                }
            }

            List<string> lines = new List<string>();
            foreach (KeyValuePair<GameObject, int> entry in kit)
            {
                lines.Add($"{entry.Key.name} x{entry.Value}");
            }

            Plugin.Log.LogInfo($"Starting kit: {(lines.Count > 0 ? string.Join(", ", lines) : "nothing resolved")}.");
            return kit;
        }

        private static void AddPiece(Dictionary<GameObject, int> kit, Piece piece)
        {
            foreach (Piece.Requirement requirement in piece.m_resources)
            {
                if (requirement?.m_resItem != null && requirement.m_amount > 0)
                {
                    Add(kit, requirement.m_resItem.gameObject, requirement.m_amount);
                }
            }

            GameObject tool = ToolFor(piece);
            if (tool == null)
            {
                Plugin.Log.LogWarning($"Starting kit: found no tool that builds {piece.name}.");
            }
            else if (!kit.ContainsKey(tool))
            {
                kit[tool] = 1;
            }
        }

        private static GameObject ToolFor(Piece piece)
        {
            if (ObjectDB.instance == null)
            {
                return null;
            }

            foreach (GameObject itemPrefab in ObjectDB.instance.m_items)
            {
                ItemDrop drop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
                PieceTable table = drop != null ? drop.m_itemData.m_shared.m_buildPieces : null;
                if (table != null && table.m_pieces.Contains(piece.gameObject))
                {
                    return itemPrefab;
                }
            }

            return null;
        }

        private static void Add(Dictionary<GameObject, int> kit, GameObject item, int count)
        {
            kit.TryGetValue(item, out int existing);
            kit[item] = existing + count;
        }
    }
}
