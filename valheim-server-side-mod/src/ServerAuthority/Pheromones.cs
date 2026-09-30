using System.Collections.Generic;

namespace ServerAuthority
{
    internal static class Pheromones
    {
        private sealed class Pheromone
        {
            internal bool Flee;
            internal string TargetName;
        }

        private static readonly Dictionary<int, Pheromone> Known = new Dictionary<int, Pheromone>();
        private static readonly HashSet<long> Logged = new HashSet<long>();
        private static readonly HashSet<int> Unknown = new HashSet<int>();
        private static ObjectDB _knownFrom;

        internal static void Reset()
        {
            Logged.Clear();
            Unknown.Clear();
        }

        internal static bool Flees(Character creature, Character target)
        {
            if (!(target is Player player))
            {
                return false;
            }

            byte[] bytes = PlayerEffects.Published(player);
            if (bytes == null)
            {
                return false;
            }

            for (int i = 0; i + 3 < bytes.Length; i += 4)
            {
                Pheromone pheromone = Lookup(PlayerEffects.Read(bytes, i));
                if (pheromone != null && pheromone.Flee && pheromone.TargetName == creature.m_name)
                {
                    LogFirst(0, "flee", creature, player);
                    return true;
                }
            }

            return false;
        }

        internal static void Attract(Character creature)
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                byte[] bytes = PlayerEffects.Published(player);
                if (bytes == null || !ZNetScene.InActiveArea(creature.transform.position, player.transform.position))
                {
                    continue;
                }

                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    Pheromone pheromone = Lookup(PlayerEffects.Read(bytes, i));
                    if (pheromone == null || pheromone.TargetName != creature.m_name)
                    {
                        continue;
                    }

                    creature.m_pheromoneLoveEffect.Create(creature.transform.position, creature.transform.rotation);
                    if (creature.GetBaseAI() is MonsterAI monsterAI)
                    {
                        monsterAI.Alert();
                    }

                    LogFirst(1, "attraction", creature, player);
                }
            }
        }

        private static Pheromone Lookup(int hash)
        {
            ObjectDB db = ObjectDB.instance;
            if (db != _knownFrom)
            {
                _knownFrom = db;
                Known.Clear();
                if (db != null)
                {
                    foreach (StatusEffect effect in db.m_StatusEffects)
                    {
                        if (effect is SE_Stats stats && stats.m_pheromoneTarget != null)
                        {
                            Character target = stats.m_pheromoneTarget.GetComponent<Character>();
                            Known[stats.NameHash()] = new Pheromone
                            {
                                Flee = stats.m_pheromoneFlee,
                                TargetName = target != null ? target.m_name : null
                            };
                        }
                    }
                }
            }

            if (Known.TryGetValue(hash, out Pheromone pheromone))
            {
                return pheromone;
            }

            if (Unknown.Add(hash))
            {
                Plugin.Log.LogWarning(
                    $"A client published status effect {hash}, which is no pheromone effect in ObjectDB, so it is ignored. " +
                    "If a game update renamed or unregistered a pheromone mead, that mead no longer works on the server.");
            }

            return null;
        }

        private static void LogFirst(int kind, string reaction, Character creature, Player player)
        {
            long key = ((long)kind << 32) | (uint)creature.m_nview.GetZDO().GetPrefab();
            if (!Logged.Add(key))
            {
                return;
            }

            string name = Utils.GetPrefabName(creature.gameObject);
            Plugin.Log.LogInfo(
                $"{name} shows pheromone {reaction} for {player.GetPlayerName()}, read from the status effects their client " +
                $"publishes. Later {reaction} from {name} is not logged.");
        }
    }
}
