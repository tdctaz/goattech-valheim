using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority.Integrity
{
    internal sealed class Baseline
    {
        internal long PlayerId;
        internal Dictionary<int, float> Skills;
        internal DateTime Taken;
    }

    internal static class CharacterValidator
    {
        internal static List<string> Validate(
            PlayerProfile profile,
            PlayerDataSummary summary,
            string expectedName,
            Baseline baseline,
            bool isNewCharacter)
        {
            List<string> problems = new List<string>();

            if (!string.Equals(profile.GetName(), expectedName, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"character name '{profile.GetName()}' does not match the connected '{expectedName}'");
            }

            if (baseline != null && profile.GetPlayerID() != baseline.PlayerId)
            {
                problems.Add("character id changed since the server's copy");
            }

            if (ModConfig.RejectCheatedProfiles.Value && profile.m_usedCheats)
            {
                problems.Add("the character is flagged as having used devcommands");
            }

            if (summary == null)
            {
                if (isNewCharacter && ModConfig.NewCharacters.Value == NewCharacterPolicy.RequireNew && !profile.m_firstSpawn)
                {
                    problems.Add("this server only accepts brand new characters, and this one has already been played");
                }

                return problems;
            }

            ObjectDB db = ObjectDB.instance;
            foreach (ItemSummary item in summary.Items)
            {
                GameObject prefab = db != null ? db.GetItemPrefab(item.PrefabHash) : null;
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null)
                {
                    problems.Add($"unknown item (prefab hash {item.PrefabHash})");
                    continue;
                }

                ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                if (item.Stack < 1 || item.Stack > Math.Max(1, shared.m_maxStackSize))
                {
                    problems.Add($"{prefab.name} stack of {item.Stack}, the most is {shared.m_maxStackSize}");
                }

                if (item.Quality < 1 || item.Quality > Math.Max(1, shared.m_maxQuality))
                {
                    problems.Add($"{prefab.name} at quality {item.Quality}, the most is {shared.m_maxQuality}");
                }

                if (ModConfig.RejectCheatedItems.Value && item.Cheated)
                {
                    problems.Add($"{prefab.name} was spawned with devcommands");
                }
            }

            float maxLevel = ModConfig.MaxSkillLevel.Value;
            float gainPerHour = ModConfig.MaxSkillGainPerHour.Value;
            double hours = baseline != null ? Math.Max(0.0, (DateTime.UtcNow - baseline.Taken).TotalHours) : 0.0;

            foreach (KeyValuePair<int, float> skill in summary.Skills)
            {
                string name = ((Skills.SkillType)skill.Key).ToString();
                if (float.IsNaN(skill.Value) || skill.Value < 0f || skill.Value > maxLevel + 0.001f)
                {
                    problems.Add($"{name} skill at {skill.Value:0.##}, the most is {maxLevel:0.##}");
                    continue;
                }

                if (gainPerHour <= 0f || baseline == null)
                {
                    continue;
                }

                baseline.Skills.TryGetValue(skill.Key, out float before);
                double allowed = gainPerHour * hours + 1.0;
                if (skill.Value - before > allowed)
                {
                    problems.Add(
                        $"{name} skill rose from {before:0.##} to {skill.Value:0.##} in {hours * 60.0:0} minutes");
                }
            }

            if (isNewCharacter && ModConfig.NewCharacters.Value == NewCharacterPolicy.RequireNew)
            {
                problems.AddRange(NotNewReasons(profile, summary));
            }

            return problems;
        }

        private static IEnumerable<string> NotNewReasons(PlayerProfile profile, PlayerDataSummary summary)
        {
            if (!profile.m_firstSpawn)
            {
                yield return "this server only accepts brand new characters, and this one has already been played";
                yield break;
            }

            foreach (KeyValuePair<int, float> skill in summary.Skills)
            {
                if (skill.Value >= 1f)
                {
                    yield return "this server only accepts brand new characters, and this one has trained skills";
                    yield break;
                }
            }

            if (summary.Uniques.Count > 0 || summary.Trophies.Count > 0)
            {
                yield return "this server only accepts brand new characters, and this one has trophies or powers";
                yield break;
            }

            HashSet<int> defaults = DefaultItemHashes();
            foreach (ItemSummary item in summary.Items)
            {
                if (!defaults.Contains(item.PrefabHash))
                {
                    yield return "this server only accepts brand new characters, and this one carries items";
                    yield break;
                }
            }
        }

        private static HashSet<int> DefaultItemHashes()
        {
            HashSet<int> hashes = new HashSet<int>();
            Player player = Game.instance != null && Game.instance.m_playerPrefab != null
                ? Game.instance.m_playerPrefab.GetComponent<Player>()
                : null;

            if (player?.m_defaultItems == null)
            {
                return hashes;
            }

            foreach (GameObject item in player.m_defaultItems)
            {
                if (item != null)
                {
                    hashes.Add(item.name.GetStableHashCode());
                }
            }

            return hashes;
        }

        internal static Baseline BaselineOf(PlayerProfile profile, PlayerDataSummary summary)
        {
            return new Baseline
            {
                PlayerId = profile.GetPlayerID(),
                Skills = summary != null ? new Dictionary<int, float>(summary.Skills) : new Dictionary<int, float>(),
                Taken = DateTime.UtcNow,
            };
        }
    }
}
