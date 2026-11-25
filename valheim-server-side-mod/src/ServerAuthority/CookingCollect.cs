using UnityEngine;

namespace ServerAuthority
{
    internal static class CookingCollect
    {
        private sealed class Collector
        {
            internal long Peer;
            internal ZDOID CharacterId;
            internal bool HasCharacter;
            internal long PlayerId;
            internal string Name;
        }

        internal static void TakeOver(CookingStation station)
        {
            RpcTakeover.Replace<Vector3, int>(station, station.m_nview, "RPC_RemoveDoneItem",
                (sender, userPoint, amount) => RemoveDoneItem(station, sender, userPoint, amount));
        }

        private static void RemoveDoneItem(CookingStation station, long sender, Vector3 userPoint, int amount)
        {
            if (station == null)
            {
                return;
            }

            for (int i = 0; i < station.m_slots.Length; i++)
            {
                station.GetSlot(i, out string itemName, out _, out _, out bool cheated);
                if (itemName == "" || !station.IsItemDone(itemName))
                {
                    continue;
                }

                Collector collector = Identify(sender);
                long adder = station.m_recordCrafter ? station.m_nview.GetZDO().GetLong(ZDOVars.s_playerID + i, 0L) : 0L;
                Collector crafter = adder != 0L ? Identify(adder) : collector;
                try
                {
                    for (int j = 0; j < amount; j++)
                    {
                        SpawnItem(station, itemName, i, userPoint, cheated, collector, crafter);
                    }
                }
                finally
                {
                    station.SetSlot(i, "", 0f, CookingStation.Status.NotDone, cheated: false);
                    station.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_SetSlotVisual", i, "");
                }

                Report(station, itemName, amount, collector, crafter, adder);
                return;
            }
        }

        private static Collector Identify(long sender)
        {
            ZNetPeer peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
            if (peer == null)
            {
                return null;
            }

            ZDO character = peer.m_characterID.IsNone() ? null : ZDOMan.instance.GetZDO(peer.m_characterID);
            string name = character != null ? character.GetString(ZDOVars.s_playerName) : "";
            return new Collector
            {
                Peer = sender,
                CharacterId = peer.m_characterID,
                HasCharacter = character != null,
                PlayerId = character != null ? character.GetLong(ZDOVars.s_playerID) : 0L,
                Name = string.IsNullOrEmpty(name) ? peer.m_playerName : name
            };
        }

        private static void SpawnItem(CookingStation station, string name, int slot, Vector3 userPoint, bool cheated,
            Collector collector, Collector crafter)
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab(name);
            Vector3 position;
            Vector3 direction;
            if (station.m_spawnPoint != null)
            {
                position = station.m_spawnPoint.position;
                direction = station.m_spawnPoint.forward;
            }
            else
            {
                Vector3 slotPosition = station.m_slots[slot].position;
                Vector3 towardUser = userPoint - slotPosition;
                towardUser.y = 0f;
                towardUser.Normalize();
                position = slotPosition + towardUser * 0.5f;
                direction = towardUser;
            }

            Quaternion rotation = Quaternion.Euler(0f, Random.Range(0, 360), 0f);
            GameObject spawned = Object.Instantiate(prefab, position, rotation);
            ItemDrop item = spawned.GetComponent<ItemDrop>();
            ItemDrop.OnCreateNew(item, cheated);
            if (station.m_spawnFullDurability && item)
            {
                item.m_itemData.m_durability = item.m_itemData.m_shared.m_maxDurability;
            }

            if (station.m_recordCrafter && crafter != null && crafter.PlayerId != 0L)
            {
                item.m_itemData.m_crafterID = crafter.PlayerId;
                item.m_itemData.m_crafterName = crafter.Name;
            }

            spawned.GetComponent<Rigidbody>().linearVelocity = direction * station.m_spawnForce;
            station.m_pickEffector.Create(position, Quaternion.identity);
            if (crafter != null)
            {
                StatCredit.ItemCraft(crafter.Peer, item.m_itemData.m_shared.m_name);
            }
        }

        private static void Report(CookingStation station, string itemName, int amount, Collector collector, Collector crafter,
            long adder)
        {
            string stationName = Utils.GetPrefabName(station.gameObject);
            string gave = collector != null
                ? $"{stationName} gave {amount} x {itemName} to {collector.Name}"
                : $"{stationName} gave {amount} x {itemName} to a sender with no connected peer";
            if (!station.m_recordCrafter)
            {
                if (collector == null)
                {
                    Plugin.Log.LogWarning($"{gave}, so no craft stat was recorded.");
                }

                return;
            }

            string who = adder != 0L ? "who put it in" : "who collected it, as nobody was recorded putting it in";
            if (crafter == null)
            {
                string missing = adder != 0L
                    ? $"Peer {adder}, who put it in, is no longer connected"
                    : "Nobody was recorded putting it in";
                string text = $"{gave}. {missing}, so no crafter or craft stat was recorded.";
                if (adder != 0L && collector != null)
                {
                    Plugin.Log.LogInfo(text);
                }
                else
                {
                    Plugin.Log.LogWarning(text);
                }

                return;
            }

            if (!crafter.HasCharacter)
            {
                Plugin.Log.LogWarning(
                    $"{gave}. The character ZDO {crafter.CharacterId} of {crafter.Name}, {who}, was not found, " +
                    "so no crafter was recorded. The craft stat went to them.");
                return;
            }

            if (crafter.PlayerId == 0L)
            {
                Plugin.Log.LogWarning(
                    $"{gave}. The character ZDO {crafter.CharacterId} of {crafter.Name}, {who}, has no player id, " +
                    "so no crafter was recorded. The craft stat went to them.");
                return;
            }

            if (collector == null)
            {
                Plugin.Log.LogWarning($"{gave}. Crafted by {crafter.Name}, {who}, who got the craft stat.");
                return;
            }

            Plugin.Log.LogInfo($"{gave}, crafted by {crafter.Name}, {who}, who got the craft stat.");
        }
    }
}
