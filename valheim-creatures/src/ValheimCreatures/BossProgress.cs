using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal enum BossStage
    {
        NoBoss,
        Alive,
        Defeated,
        TrophyPlaced,
    }

    internal static class BossProgress
    {
        private const float RereadSeconds = 10f;
        private const float RescanSeconds = 300f;
        private const float EmptyRescanSeconds = 60f;

        private sealed class Boss
        {
            internal string Prefab;
            internal string DefeatKey;
            internal string Trophy;
        }

        private static readonly Dictionary<Character.Faction, Boss> ByFaction = new Dictionary<Character.Faction, Boss>();
        private static readonly Dictionary<string, Boss> ByCreature = new Dictionary<string, Boss>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Boss> ByPrefab = new Dictionary<string, Boss>(StringComparer.Ordinal);
        private static readonly HashSet<string> PlacedTrophies = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<int> StonePrefabs = new HashSet<int>();
        private static readonly List<ZDOID> Stones = new List<ZDOID>();
        private static readonly HashSet<string> Scratch = new HashSet<string>(StringComparer.Ordinal);

        private static string _parsedFrom;
        private static ZNetScene _resolvedFor;
        private static float _nextReread;
        private static float _nextRescan;

        internal static void Reset()
        {
            PlacedTrophies.Clear();
            Stones.Clear();
            StonePrefabs.Clear();
            _parsedFrom = null;
            _resolvedFor = null;
            _nextReread = 0f;
            _nextRescan = 0f;
        }

        internal static void Refresh()
        {
            _parsedFrom = null;
        }

        internal static BossStage StageOf(Character character)
        {
            return StageOf(Utils.GetPrefabName(character.gameObject), character.GetFaction());
        }

        internal static BossStage StageOf(string creature, Character.Faction faction)
        {
            EnsureMapping();
            if (creature == null || !ByCreature.TryGetValue(creature, out Boss boss))
            {
                if (!ByFaction.TryGetValue(faction, out boss))
                {
                    return BossStage.NoBoss;
                }
            }

            return StageOf(boss);
        }

        private static BossStage StageOf(Boss boss)
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || string.IsNullOrEmpty(boss.DefeatKey) || !zones.GetGlobalKey(boss.DefeatKey))
            {
                return BossStage.Alive;
            }

            if (boss.Trophy == null)
            {
                return BossStage.TrophyPlaced;
            }

            return PlacedTrophies.Contains(boss.Trophy) ? BossStage.TrophyPlaced : BossStage.Defeated;
        }

        internal static bool CenterProtectionLifted()
        {
            string boss = ConfigSync.Current.CenterProtectionBoss;
            if (string.IsNullOrEmpty(boss))
            {
                return false;
            }

            EnsureMapping();
            return StageOf(Resolve(boss.Trim(), ZNetScene.instance)) == BossStage.TrophyPlaced;
        }

        internal static void WritePlacedTrophies(ZPackage package)
        {
            package.Write(PlacedTrophies.Count);
            foreach (string trophy in PlacedTrophies)
            {
                package.Write(trophy);
            }
        }

        internal static void ReadPlacedTrophies(ZPackage package)
        {
            PlacedTrophies.Clear();
            int count = package.ReadInt();
            for (int i = 0; i < count; i++)
            {
                PlacedTrophies.Add(package.ReadString());
            }

            Plugin.Log.LogInfo($"Boss trophies on the sacrificial stones: {Describe()}.");
        }

        internal static void Update(ZNet znet)
        {
            if (!znet.IsServer() || ZDOMan.instance == null || ZNetScene.instance == null)
            {
                return;
            }

            float now = Time.time;
            if (now < _nextReread)
            {
                return;
            }

            _nextReread = now + RereadSeconds;

            if (now >= _nextRescan)
            {
                FindStones();
                _nextRescan = now + (Stones.Count > 0 ? RescanSeconds : EmptyRescanSeconds);
            }

            Scratch.Clear();
            foreach (ZDOID id in Stones)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                int item = zdo != null ? zdo.GetInt(ZDOVars.s_item) : 0;
                GameObject prefab = item != 0 ? ZNetScene.instance.GetPrefab(item) : null;
                if (prefab != null)
                {
                    Scratch.Add(prefab.name);
                }
            }

            if (Scratch.SetEquals(PlacedTrophies))
            {
                return;
            }

            PlacedTrophies.Clear();
            PlacedTrophies.UnionWith(Scratch);
            Plugin.Log.LogInfo($"Boss trophies on the sacrificial stones: {Describe()}.");
            ConfigSync.BroadcastTrophies();
        }

        private static void FindStones()
        {
            if (StonePrefabs.Count == 0)
            {
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null)
                    {
                        continue;
                    }

                    ItemStand stand = prefab.GetComponentInChildren<ItemStand>(true);
                    if (prefab.GetComponentInChildren<BossStone>(true) != null ||
                        (stand != null && stand.m_guardianPower != null))
                    {
                        StonePrefabs.Add(prefab.name.GetStableHashCode());
                    }
                }
            }

            Stones.Clear();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (StonePrefabs.Contains(zdo.GetPrefab()))
                {
                    Stones.Add(zdo.m_uid);
                }
            }
        }

        private static void EnsureMapping()
        {
            Balance balance = ConfigSync.Current;
            string wanted = (balance.BossFactions ?? "") + "|" + (balance.BossCreatures ?? "");
            ZNetScene scene = ZNetScene.instance;
            if (wanted == _parsedFrom && scene == _resolvedFor)
            {
                return;
            }

            _parsedFrom = wanted;
            _resolvedFor = scene;
            ByFaction.Clear();
            ByCreature.Clear();
            ByPrefab.Clear();

            foreach (KeyValuePair<string, string> pair in Pairs(balance.BossFactions, "BossFactions"))
            {
                if (!Enum.TryParse(pair.Value, true, out Character.Faction faction))
                {
                    Plugin.Log.LogWarning($"BossFactions: '{pair.Value}' is not a faction.");
                    continue;
                }

                ByFaction[faction] = Resolve(pair.Key, scene);
            }

            foreach (KeyValuePair<string, string> pair in Pairs(balance.BossCreatures, "BossCreatures"))
            {
                ByCreature[pair.Key] = Resolve(pair.Value, scene);
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> Pairs(string text, string setting)
        {
            foreach (string entry in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
                {
                    Plugin.Log.LogWarning($"{setting}: '{entry.Trim()}' is not a Name:Name pair.");
                    continue;
                }

                yield return new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim());
            }
        }

        private static Boss Resolve(string prefabName, ZNetScene scene)
        {
            if (ByPrefab.TryGetValue(prefabName, out Boss boss))
            {
                return boss;
            }

            boss = new Boss { Prefab = prefabName };
            ByPrefab[prefabName] = boss;
            GameObject prefab = scene != null ? scene.GetPrefab(prefabName) : null;
            if (prefab == null)
            {
                if (scene != null)
                {
                    Plugin.Log.LogWarning($"There is no boss prefab called {prefabName}.");
                }

                return boss;
            }

            Character character = prefab.GetComponent<Character>();
            boss.DefeatKey = character != null ? character.m_defeatSetGlobalKey : null;
            boss.Trophy = FindTrophy(prefab);
            if (string.IsNullOrEmpty(boss.DefeatKey))
            {
                Plugin.Log.LogWarning(
                    $"{prefabName} sets no defeat key, so the creatures it rules never get past their first star cap.");
            }

            return boss;
        }

        private static string FindTrophy(GameObject bossPrefab)
        {
            CharacterDrop drops = bossPrefab.GetComponent<CharacterDrop>();
            if (drops == null)
            {
                return null;
            }

            foreach (CharacterDrop.Drop drop in drops.m_drops)
            {
                if (drop.m_prefab != null && drop.m_prefab.name.StartsWith("Trophy", StringComparison.Ordinal))
                {
                    return drop.m_prefab.name;
                }
            }

            return null;
        }

        internal static string DescribeStages()
        {
            EnsureMapping();
            List<string> parts = new List<string>();
            foreach (Boss boss in ByPrefab.Values)
            {
                parts.Add($"{boss.Prefab}: {StageOf(boss)}");
            }

            return $"{string.Join(", ", parts)}. Trophies on the stones: {Describe()}. " +
                   $"Stones found: {Stones.Count}.";
        }

        private static string Describe()
        {
            return PlacedTrophies.Count == 0 ? "none" : string.Join(", ", PlacedTrophies);
        }

#if DEBUG_TOOLS
        internal static void LogFactions()
        {
            if (!ModConfig.LogFactions.Value || ZNetScene.instance == null)
            {
                return;
            }

            SortedDictionary<string, List<string>> byFaction = new SortedDictionary<string, List<string>>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Character character = prefab != null ? prefab.GetComponent<Character>() : null;
                if (character == null || character is Player)
                {
                    continue;
                }

                string key = character.IsBoss() ? $"{character.m_faction} (boss)" : character.m_faction.ToString();
                if (!byFaction.TryGetValue(key, out List<string> names))
                {
                    byFaction[key] = names = new List<string>();
                }

                names.Add(character.IsBoss()
                    ? $"{prefab.name} [key '{character.m_defeatSetGlobalKey}', trophy '{FindTrophy(prefab)}']"
                    : prefab.name);
            }

            foreach (KeyValuePair<string, List<string>> entry in byFaction)
            {
                entry.Value.Sort(StringComparer.Ordinal);
                Plugin.Log.LogInfo($"Faction {entry.Key}: {string.Join(", ", entry.Value)}");
            }
        }
#endif
    }
}
