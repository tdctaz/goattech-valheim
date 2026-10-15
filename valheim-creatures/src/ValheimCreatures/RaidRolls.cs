using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class RaidRolls
    {
        private const float DungeonHeight = 3000f;

        private static bool _inUpdate;
        private static bool _predicted;
        private static bool _searched;
        private static float _before;
        private static float _roll;

        internal static float Minutes(RandEventSystem events)
        {
            return events.m_eventIntervalMin * Game.m_eventRate;
        }

        internal static float Chance(RandEventSystem events)
        {
            return Mathf.Min(100f, events.m_eventChance / Game.m_eventRate);
        }

        private static bool LogsHere()
        {
            ZNet znet = ZNet.instance;
            return znet != null && znet.IsServer() && znet.IsDedicated();
        }

        internal static void Before(RandEventSystem events, float dt)
        {
            _inUpdate = false;
            _predicted = false;
            _searched = false;
            if (!LogsHere())
            {
                return;
            }

            _inUpdate = true;
            _before = events.m_eventTimer;
            float next = events.m_eventTimer + dt;
            if (Game.m_eventRate > 0f && next > events.m_eventIntervalMin * 60f * Game.m_eventRate)
            {
                Random.State state = Random.state;
                _roll = Random.Range(0f, 100f);
                Random.state = state;
                _predicted = true;
            }
        }

        internal static void After(RandEventSystem events)
        {
            bool rolled = _inUpdate && events.m_eventTimer <= _before;
            _inUpdate = false;
            if (!rolled || _searched)
            {
                return;
            }

            string roll = _predicted
                ? $"{_roll:0.0} against a {Chance(events):0.#}% chance"
                : $"missed its {Chance(events):0.#}% chance";
            Plugin.Log.LogInfo(
                $"Raid roll at {System.DateTime.Now:HH:mm:ss}: {roll}, so no raid. {Players(events, null)}. " +
                $"The next roll is in {Minutes(events):0.#} minutes.");
        }

        internal static void Searched(RandEventSystem events, List<KeyValuePair<RandomEvent, Vector3>> possible)
        {
            if (!LogsHere())
            {
                return;
            }

            _searched = true;
            string roll = _predicted
                ? $"{_roll:0.0} against a {Chance(events):0.#}% chance"
                : $"came up on its {Chance(events):0.#}% chance";
            string start = _inUpdate
                ? $"Raid roll at {System.DateTime.Now:HH:mm:ss}: {roll}, so a raid starts if a player qualifies."
                : $"Raid search at {System.DateTime.Now:HH:mm:ss}, started by a command.";

            List<RandomEvent> allowed = Allowed(events);
            List<string> names = new List<string>();
            foreach (RandomEvent ev in allowed)
            {
                names.Add(ev.m_name);
            }

            string keys = names.Count > 0
                ? $"Raids the world's keys allow: {string.Join(", ", names)}."
                : "The world's keys allow no raid yet.";

            List<string> found = new List<string>();
            foreach (KeyValuePair<RandomEvent, Vector3> entry in possible)
            {
                found.Add($"{entry.Key.m_name} at ({entry.Value.x:0}, {entry.Value.z:0})");
            }

            string result = found.Count > 0
                ? $"Possible: {string.Join(", ", found)}, and vanilla picks one at random."
                : "No raid is possible, so none starts.";
            string next = _inUpdate ? $" The next roll is in {Minutes(events):0.#} minutes." : "";
            Plugin.Log.LogInfo($"{start} {keys} {Players(events, allowed)}. {result}{next}");
        }

        private static List<RandomEvent> Allowed(RandEventSystem events)
        {
            List<RandomEvent> allowed = new List<RandomEvent>();
            foreach (RandomEvent ev in events.m_events)
            {
                if (ev.m_enabled && ev.m_random && events.HaveGlobalKeys(ev, RandEventSystem.s_playerEventDatas))
                {
                    allowed.Add(ev);
                }
            }

            return allowed;
        }

        private static string Players(RandEventSystem events, List<RandomEvent> allowed)
        {
            List<string> players = new List<string>();
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer.IsReady())
                {
                    players.Add(Player(events, peer, allowed));
                }
            }

            return players.Count > 0 ? string.Join("; ", players) : "No player is online";
        }

        private static string Player(RandEventSystem events, ZNetPeer peer, List<RandomEvent> allowed)
        {
            string name = string.IsNullOrEmpty(peer.m_playerName) ? $"Peer {peer.m_uid}" : peer.m_playerName;
            if (!RandEventSystem.RetrievePlayerEventData(peer.m_serverSyncedPlayerData, peer.m_refPos,
                    out RandEventSystem.PlayerEventData data))
            {
                return $"{name} has sent no base value or raid list yet, so counts for no raid";
            }

            Vector3 position = data.position;
            if (position.y > DungeonHeight)
            {
                return $"{name} is in a dungeon or above the world with base value {data.baseValue}, so counts for no raid";
            }

            Heightmap.Biome biome = WorldGenerator.instance != null
                ? WorldGenerator.instance.GetBiome(position)
                : Heightmap.Biome.None;
            string at = $"{name} at ({position.x:0}, {position.z:0}) in {biome} has base value {data.baseValue}";
            if (allowed == null)
            {
                return at;
            }

            bool playerEvents = ZoneSystem.instance.GetGlobalKey(GlobalKeys.PlayerEvents);
            List<string> fits = new List<string>();
            bool inBiome = false;
            bool outsideBase = false;
            bool notReady = false;
            foreach (RandomEvent ev in allowed)
            {
                if (!events.InValidBiome(ev, position))
                {
                    continue;
                }

                inBiome = true;
                if (!events.CheckBase(ev, data))
                {
                    outsideBase = true;
                    continue;
                }

                if (!playerEvents || data.possibleEvents.Contains(ev.m_name))
                {
                    fits.Add(ev.m_name);
                }
                else
                {
                    notReady = true;
                }
            }

            if (fits.Count > 0)
            {
                return $"{at}, so could get {string.Join(", ", fits)}";
            }

            if (outsideBase && notReady)
            {
                return $"{at}, below the 3 a raid at a base needs, and not ready for the others the keys allow";
            }

            if (outsideBase)
            {
                return $"{at}, below the 3 a raid at a base needs";
            }

            return inBiome
                ? $"{at}, but is not ready for any raid the keys allow here"
                : $"{at}, where no raid the keys allow can come";
        }
    }
}
