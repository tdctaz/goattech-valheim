using System.Collections.Generic;

namespace ServerAuthority
{
    internal static class PlayerEffects
    {
        private static readonly int Key = "ServerAuthority_StatusEffects".GetStableHashCode();

        private static readonly List<int> Current = new List<int>();
        private static readonly List<int> Written = new List<int>();
        private static ZDOID _writtenTo = ZDOID.None;

        internal static void Publish(SEMan seman, ZDO zdo)
        {
            Current.Clear();
            foreach (StatusEffect effect in seman.m_statusEffects)
            {
                if (effect is SE_Stats stats && stats.m_pheromoneTarget != null)
                {
                    Current.Add(effect.NameHash());
                }
            }

            if (zdo.m_uid != _writtenTo)
            {
                _writtenTo = zdo.m_uid;
                Written.Clear();
            }

            if (SameAsWritten())
            {
                return;
            }

            LogChanges();
            byte[] bytes = new byte[Current.Count * 4];
            for (int i = 0; i < Current.Count; i++)
            {
                int hash = Current[i];
                bytes[i * 4] = (byte)hash;
                bytes[i * 4 + 1] = (byte)(hash >> 8);
                bytes[i * 4 + 2] = (byte)(hash >> 16);
                bytes[i * 4 + 3] = (byte)(hash >> 24);
            }

            zdo.Set(Key, bytes);
            Written.Clear();
            Written.AddRange(Current);
        }

        internal static byte[] Published(Player player)
        {
            ZNetView view = player != null ? player.m_nview : null;
            if (view == null || !view.IsValid() || view.IsOwner())
            {
                return null;
            }

            byte[] bytes = view.GetZDO().GetByteArray(Key);
            return bytes != null && bytes.Length >= 4 ? bytes : null;
        }

        internal static int Read(byte[] bytes, int offset)
        {
            return bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);
        }

        private static bool SameAsWritten()
        {
            if (Current.Count != Written.Count)
            {
                return false;
            }

            for (int i = 0; i < Current.Count; i++)
            {
                if (Current[i] != Written[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void LogChanges()
        {
            foreach (int hash in Current)
            {
                if (!Written.Contains(hash))
                {
                    Plugin.Log.LogInfo($"Pheromone effect {Name(hash)} now published for the server.");
                }
            }

            foreach (int hash in Written)
            {
                if (!Current.Contains(hash))
                {
                    Plugin.Log.LogInfo($"Pheromone effect {Name(hash)} no longer published.");
                }
            }
        }

        private static string Name(int hash)
        {
            StatusEffect effect = ObjectDB.instance != null ? ObjectDB.instance.GetStatusEffect(hash) : null;
            return effect != null ? effect.name : hash.ToString();
        }
    }
}
