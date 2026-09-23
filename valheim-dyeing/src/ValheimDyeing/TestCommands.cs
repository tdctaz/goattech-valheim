#if DEBUG_TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ValheimDyeing
{
    internal static class TestCommands
    {
        private const float PollSeconds = 1f;
        private const float SettleSeconds = 5f;

        private static string _path;
        private static DateTime _lastSeen = DateTime.MinValue;
        private static float _nextPoll;
        private static float _playerSince = -1f;

        internal static void Reset()
        {
            _lastSeen = DateTime.MinValue;
            _nextPoll = 0f;
            _playerSince = -1f;
        }

        internal static void Poll(ZNet znet)
        {
            if (!ModConfig.EnableTestCommands.Value || !znet.IsServer() || ZNetScene.instance == null ||
                Time.time < _nextPoll)
            {
                return;
            }

            _nextPoll = Time.time + PollSeconds;

            if (!TryFindPlayer(znet, out Vector3 here))
            {
                _playerSince = -1f;
                return;
            }

            if (_playerSince < 0f)
            {
                _playerSince = Time.time;
            }

            if (Time.time - _playerSince < SettleSeconds)
            {
                return;
            }

            if (_path == null)
            {
                _path = Path.Combine(Paths.ConfigPath, "valheimdyeing_test.txt");
            }

            if (!File.Exists(_path))
            {
                return;
            }

            DateTime written = File.GetLastWriteTimeUtc(_path);
            if (written == _lastSeen)
            {
                return;
            }

            _lastSeen = written;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(_path);
            }
            catch (IOException)
            {
                _lastSeen = DateTime.MinValue;
                return;
            }

            int placed = 0;
            foreach (string line in lines)
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#"))
                {
                    continue;
                }

                if (Run(text, here, placed))
                {
                    placed++;
                }
            }
        }

        private static bool Run(string line, Vector3 here, int index)
        {
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0].ToLowerInvariant() != "give")
            {
                Plugin.Log.LogWarning($"Test command: cannot read \"{line}\". Expected: give <prefab> [count]");
                return false;
            }

            string name = parts[1];
            int count = 1;
            if (parts.Length >= 3 && !int.TryParse(parts[2], out count))
            {
                count = 1;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
            {
                Plugin.Log.LogWarning($"Test command: there is no item prefab named {name}.");
                return false;
            }

            int perStack = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            int left = Mathf.Max(1, count);
            int stack = 0;

            while (left > 0)
            {
                int take = Mathf.Min(left, perStack);
                left -= take;
                Place(prefab, take, here, index, stack);
                stack++;
            }

            Plugin.Log.LogInfo($"Test command: dropped {count} {name} at the player's feet.");
            return true;
        }

        private static void Place(GameObject prefab, int stack, Vector3 here, int index, int slot)
        {
            float angle = (index * 7 + slot) * 0.9f;
            Vector3 at = here + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (1f + slot * 0.25f) +
                         Vector3.up * 0.75f;

            GameObject spawned = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
            ItemDrop drop = spawned.GetComponent<ItemDrop>();
            if (drop == null)
            {
                return;
            }

            drop.m_itemData.m_stack = stack;
            drop.Save();
        }

        private static bool TryFindPlayer(ZNet znet, out Vector3 position)
        {
            position = Vector3.zero;

            foreach (ZNetPeer peer in znet.GetPeers())
            {
                if (peer != null && peer.m_refPos != Vector3.zero)
                {
                    position = peer.m_refPos;
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
