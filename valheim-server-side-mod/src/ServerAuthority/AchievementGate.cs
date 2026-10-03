using System;
using System.Collections.Generic;

namespace ServerAuthority
{
    internal static class AchievementGate
    {
        private static string _reported;

        internal static bool IgnoreModdedFlag => Integrity.IntegrityClient.OnAuthorityServer;

        internal static void Reset()
        {
            _reported = null;
        }

        internal static void Report(Player player)
        {
            try
            {
                List<string> reasons = new List<string>();
                PlayerProfile profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
                if (profile != null && profile.m_usedCheats)
                {
                    reasons.Add("the character has used devcommands");
                }

                if (player.GetInventory().AnyCheatedItem())
                {
                    reasons.Add("the inventory holds an item spawned with devcommands");
                }

                if (global::Achievements.IsWorldCheated())
                {
                    reasons.Add("the world has modifiers the game counts as cheats");
                }

                if (Game.isModded && !IgnoreModdedFlag)
                {
                    reasons.Add("BepInEx marks the game as modded, which only a Server Authority server overlooks");
                }

                string line;
                if (reasons.Count == 0)
                {
                    line = Game.isModded
                        ? "Achievements are allowed in this session. The modded flag BepInEx sets is ignored for them on a Server Authority server."
                        : "Achievements are allowed in this session.";
                }
                else
                {
                    line = $"Achievements are blocked in this session: {string.Join("; ", reasons)}.";
                }

                if (line == _reported)
                {
                    return;
                }

                _reported = line;
                Plugin.Log.LogInfo(line);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not check whether achievements are allowed: {e.Message}");
            }
        }
    }
}
