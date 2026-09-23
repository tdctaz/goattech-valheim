using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class BossWaves
    {
        private static readonly int WavesKey = "ValheimCreatures_Waves".GetStableHashCode();

#if DEBUG_TOOLS
        private static bool _raidsLogged;
#endif

        internal static void Reset()
        {
#if DEBUG_TOOLS
            _raidsLogged = false;
#endif
        }

#if DEBUG_TOOLS
        internal static void LogRaidsOnce()
        {
            if (!_raidsLogged && RandEventSystem.instance != null)
            {
                _raidsLogged = true;
                LogRaids();
            }
        }
#endif

        internal static void Check(CreatureTraits traits)
        {
            Character boss = traits.Character;
            Balance balance = ConfigSync.Current;
            if (balance.BossWaveCount <= 0 || balance.BossWaveStep <= 0f)
            {
                return;
            }

            float max = boss.GetMaxHealth();
            if (max <= 0f)
            {
                return;
            }

            float lost = 1f - Mathf.Clamp01(boss.GetHealth() / max);
            int due = Mathf.Min(balance.BossWaveCount, Mathf.FloorToInt(lost * 100f / balance.BossWaveStep + 0.0001f));
            ZDO zdo = boss.m_nview.GetZDO();
            int done = zdo.GetInt(WavesKey, 0);
            if (due <= done)
            {
                return;
            }

            zdo.Set(WavesKey, due);
            for (int wave = done + 1; wave <= due; wave++)
            {
                Spawn(boss, wave, balance);
            }
        }

        private static void Spawn(Character boss, int wave, Balance balance)
        {
            string raid = RaidFor(Utils.GetPrefabName(boss.gameObject), balance.BossWaves);
            RandomEvent ev = raid != null ? FindRaid(raid) : null;
            if (ev == null)
            {
                Plugin.Log.LogWarning(
                    $"{Utils.GetPrefabName(boss.gameObject)} has no raid in BossCyan.Raids (wanted '{raid}'), so it calls no wave.");
                return;
            }

            List<string> spawned = new List<string>();
            Vector3 origin = boss.transform.position;
            WaveSpawner.Spawn(ev, () => WaveSpawner.Around(origin, balance.BossWaveDistance), "boss wave", boss, spawned);

            Plugin.Log.LogInfo(
                $"{Utils.GetPrefabName(boss.gameObject)} called wave {wave} of {balance.BossWaveCount} from {raid}: " +
                $"{(spawned.Count > 0 ? string.Join(", ", spawned) : "nothing")}.");
        }

        private static string RaidFor(string boss, string mapping)
        {
            foreach (string entry in (mapping ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length == 2 && parts[0].Trim() == boss)
                {
                    return parts[1].Trim();
                }
            }

            return null;
        }

        internal static RandomEvent FindRaid(string name)
        {
            RandEventSystem system = RandEventSystem.instance;
            if (system == null)
            {
                return null;
            }

            foreach (RandomEvent ev in system.m_events)
            {
                if (string.Equals(ev.m_name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return ev;
                }
            }

            return null;
        }

#if DEBUG_TOOLS
        private static void LogRaids()
        {
            RandEventSystem system = RandEventSystem.instance;
            if (!ModConfig.LogFactions.Value || system == null)
            {
                return;
            }

            foreach (RandomEvent ev in system.m_events)
            {
                List<string> parts = new List<string>();
                foreach (SpawnSystem.SpawnData data in ev.m_spawn)
                {
                    if (data?.m_prefab != null)
                    {
                        parts.Add($"{data.m_prefab.name} x{data.m_groupSizeMin}-{data.m_groupSizeMax}");
                    }
                }

                Plugin.Log.LogInfo($"Raid {ev.m_name}: {string.Join(", ", parts)}");
            }
        }
#endif
    }
}
