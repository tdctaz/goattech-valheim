using UnityEngine;

namespace ServerAuthority
{
    internal static class CookingCollect
    {
        private const string Rpc = "RPC_RemoveDoneItem";
        private static readonly int RpcHash = Rpc.GetStableHashCode();
        private static bool _warnedMissing;

        private sealed class Collector
        {
            internal long Peer;
            internal ZDOID CharacterId;
            internal bool HasCharacter;
            internal long PlayerId;
            internal string Name;
        }

        internal static void Reset()
        {
            _warnedMissing = false;
        }

        internal static void TakeOver(CookingStation station)
        {
            ZNetView view = station.m_nview;
            if (view == null || view.GetZDO() == null)
            {
                return;
            }

            if (!view.m_functions.ContainsKey(RpcHash))
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Plugin.Log.LogWarning(
                        $"{Utils.GetPrefabName(station.gameObject)} has no {Rpc} handler to take over, so collecting " +
                        "from cooking stations runs vanilla's code on the server. Re-read CookingStation after this game update.");
                }

                return;
            }

            view.Unregister(Rpc);
            view.Register<Vector3, int>(Rpc, (sender, userPoint, amount) => RemoveDoneItem(station, sender, userPoint, amount));
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
                try
                {
                    for (int j = 0; j < amount; j++)
                    {
                        SpawnItem(station, itemName, i, userPoint, cheated, collector);
                    }
                }
                finally
                {
                    station.SetSlot(i, "", 0f, CookingStation.Status.NotDone, cheated: false);
                    station.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_SetSlotVisual", i, "");
                }

                Report(station, itemName, amount, collector);
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
            Collector collector)
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

            if (station.m_recordCrafter && collector != null && collector.PlayerId != 0L)
            {
                item.m_itemData.m_crafterID = collector.PlayerId;
                item.m_itemData.m_crafterName = collector.Name;
            }

            spawned.GetComponent<Rigidbody>().linearVelocity = direction * station.m_spawnForce;
            station.m_pickEffector.Create(position, Quaternion.identity);
            if (collector != null)
            {
                StatCredit.ItemCraft(collector.Peer, item.m_itemData.m_shared.m_name);
            }
        }

        private static void Report(CookingStation station, string itemName, int amount, Collector collector)
        {
            string stationName = Utils.GetPrefabName(station.gameObject);
            if (collector == null)
            {
                Plugin.Log.LogWarning(
                    $"{stationName} gave {amount} x {itemName} to a sender with no connected peer, so no craft stat or crafter was recorded.");
                return;
            }

            if (station.m_recordCrafter && !collector.HasCharacter)
            {
                Plugin.Log.LogWarning(
                    $"{stationName} gave {amount} x {itemName} to {collector.Name}, whose character ZDO {collector.CharacterId} " +
                    "was not found, so no crafter was recorded.");
                return;
            }

            if (station.m_recordCrafter && collector.PlayerId == 0L)
            {
                Plugin.Log.LogWarning(
                    $"{stationName} gave {amount} x {itemName} to {collector.Name}, whose character ZDO {collector.CharacterId} " +
                    "has no player id, so no crafter was recorded.");
                return;
            }

            if (station.m_recordCrafter)
            {
                Plugin.Log.LogInfo($"{stationName} gave {amount} x {itemName} to {collector.Name}.");
            }
        }
    }
}
