namespace ServerAuthority
{
    internal static class PlayerFlags
    {
        internal const int Ghost = 1;
        internal const int Cinematic = 2;

        private static readonly int Key = "ServerAuthority_PlayerFlags".GetStableHashCode();
        private static ZDOID _writtenTo = ZDOID.None;
        private static int _written;

        internal static void Publish(Player player, ZDO zdo)
        {
            int flags = (player.m_ghostMode ? Ghost : 0) | (CinematicsManager.IsPlaying() ? Cinematic : 0);
            if (zdo.m_uid != _writtenTo)
            {
                _writtenTo = zdo.m_uid;
                _written = 0;
            }

            if (flags == _written)
            {
                return;
            }

            LogChange(Ghost, "ghost mode", flags);
            LogChange(Cinematic, "cinematic playing", flags);
            zdo.Set(Key, flags);
            _written = flags;
        }

        private static void LogChange(int flag, string state, int flags)
        {
            if ((flags & flag) != (_written & flag))
            {
                Plugin.Log.LogInfo($"Published {state} {((flags & flag) != 0 ? "on" : "off")} for the server.");
            }
        }

        internal static bool Has(Player player, int flag)
        {
            ZNetView view = player.m_nview;
            if (view == null || !view.IsValid() || view.IsOwner())
            {
                return false;
            }

            return (view.GetZDO().GetInt(Key) & flag) != 0;
        }
    }
}
