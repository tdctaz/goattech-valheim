using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class HookedFish
    {
        private const double LeaseSeconds = 6.0;

        private struct Fisher
        {
            public long Peer;
            public string Name;
        }

        private static readonly Dictionary<ZDOID, Fisher> Held = new Dictionary<ZDOID, Fisher>();
        private static readonly Dictionary<ZDOID, long> Hooked = new Dictionary<ZDOID, long>();
        private static readonly List<ZDOID> Released = new List<ZDOID>();
        private static readonly HashSet<int> FishPrefabs = new HashSet<int>();
        private static bool _built;

        internal static void Reset()
        {
            Held.Clear();
            Hooked.Clear();
            Released.Clear();
            FishPrefabs.Clear();
            _built = false;
        }

        internal static void Refresh()
        {
            Hooked.Clear();
            foreach (FishingFloat fishingFloat in FishingFloat.GetAllInstances())
            {
                ZNetView view = fishingFloat != null ? fishingFloat.m_nview : null;
                if (view == null || !view.IsValid())
                {
                    continue;
                }

                ZDO floatZdo = view.GetZDO();
                ZDOID fish = floatZdo.GetZDOID(ZDOVars.s_sessionCatchID);
                long fisher = floatZdo.GetOwner();
                if (fish.IsNone() || !SimulationAnchors.IsConnectedPeer(fisher))
                {
                    continue;
                }

                ZDO fishZdo = ZDOMan.instance.GetZDO(fish);
                if (fishZdo == null || !IsFish(fishZdo.GetPrefab()))
                {
                    continue;
                }

                if (Hooked.TryGetValue(fish, out long other))
                {
                    fisher = Prefer(fishZdo.GetOwner(), other, fisher);
                }

                Hooked[fish] = fisher;
            }

            foreach (KeyValuePair<ZDOID, long> entry in Hooked)
            {
                OwnershipLeases.Grant(entry.Key, entry.Value, LeaseSeconds);
                if (Held.TryGetValue(entry.Key, out Fisher before) && before.Peer == entry.Value)
                {
                    continue;
                }

                Fisher now = new Fisher { Peer = entry.Value, Name = Name(entry.Value) };
                Held[entry.Key] = now;
                Plugin.Log.LogInfo($"Hooked fish {entry.Key} kept with its fisher {now.Name} while it is on the line.");
            }

            Released.Clear();
            foreach (KeyValuePair<ZDOID, Fisher> entry in Held)
            {
                if (!Hooked.ContainsKey(entry.Key))
                {
                    Released.Add(entry.Key);
                }
            }

            foreach (ZDOID fish in Released)
            {
                string name = Held[fish].Name;
                Held.Remove(fish);
                OwnershipLeases.Revoke(fish);
                Plugin.Log.LogInfo(ZDOMan.instance.GetZDO(fish) == null
                    ? $"Hooked fish {fish} is gone, caught or despawned, so its lease with {name} ended."
                    : $"Fish {fish} is off the line, so it returns to the ownership policy from {name}.");
            }
        }

        private static long Prefer(long owner, long first, long second)
        {
            if (first == owner || second == owner)
            {
                return owner;
            }

            return first < second ? first : second;
        }

        private static bool IsFish(int prefabHash)
        {
            if (!_built)
            {
                ZNetScene scene = ZNetScene.instance;
                if (scene == null)
                {
                    return false;
                }

                _built = true;
                foreach (GameObject prefab in scene.m_prefabs)
                {
                    if (prefab != null && prefab.GetComponentInChildren<Fish>(true) != null)
                    {
                        FishPrefabs.Add(prefab.name.GetStableHashCode());
                    }
                }
            }

            return FishPrefabs.Contains(prefabHash);
        }

        private static string Name(long peer)
        {
            ZNetPeer znetPeer = ZNet.instance != null ? ZNet.instance.GetPeer(peer) : null;
            return znetPeer != null ? znetPeer.m_playerName : peer.ToString();
        }
    }
}
