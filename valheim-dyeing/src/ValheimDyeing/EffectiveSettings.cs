using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace ValheimDyeing
{
    /// <summary>
    /// Writes every setting the mod is actually running with to the log at startup, on a server and
    /// on a client alike, marking the ones that differ from the code's own default.
    ///
    /// This exists because of a day lost to guessing. A boat was destroyed on the live server, and
    /// the whole investigation was reasoned about on the assumption that the server owned it, which
    /// the code's default says it does. The config file on disk said otherwise: it predated the
    /// commit that flipped that default, and BepInEx keeps whatever the file already contains, so
    /// the server had been handing boats to clients the entire time. The README stated the old
    /// default too, so the code, the documentation and the file all disagreed, and nothing on
    /// startup said which one was winning. It was eventually settled by noticing that one log line
    /// is only reachable when the setting is false, which is not a reasonable way to find out.
    ///
    /// A config file also cannot answer the question on its own: keys removed from the code linger
    /// in it and read as real, and keys never written take the code default silently. Only the
    /// running mod knows, so the running mod is what should say.
    /// </summary>
    internal static class EffectiveSettings
    {
#if DEBUG_TOOLS
        private const string Build = "debug tools included";
#else
        private const string Build = "debug tools left out";
#endif

        internal static void Log(ManualLogSource log, ConfigFile config, string what)
        {
            if (log == null || config == null)
            {
                return;
            }

            List<string> lines = new List<string>();
            int changed = 0;

            foreach (ConfigDefinition definition in config.Keys)
            {
                ConfigEntryBase entry = config[definition];
                if (entry == null)
                {
                    continue;
                }

                string value = Describe(entry.BoxedValue);
                string fallback = Describe(entry.DefaultValue);
                bool differs = value != fallback;
                if (differs)
                {
                    changed++;
                }

                lines.Add(
                    $"  {definition.Section}.{definition.Key} = {value}" +
                    (differs ? $"    <- changed, default is {fallback}" : string.Empty));
            }

            lines.Sort(System.StringComparer.Ordinal);

            StringBuilder message = new StringBuilder();
            message.Append($"{what} effective configuration, {Build}: {lines.Count} setting(s), ")
                .Append($"{changed} changed from the default.");
            for (int i = 0; i < lines.Count; i++)
            {
                message.Append('\n').Append(lines[i]);
            }

            log.LogInfo(message.ToString());
        }

        /// <summary>
        /// An empty string is indistinguishable from an unset one in a log line, and several of
        /// these settings treat empty as meaningful, so it is spelled out.
        /// </summary>
        private static string Describe(object value)
        {
            if (value == null)
            {
                return "(null)";
            }

            string text = value.ToString();
            return text.Length == 0 ? "(empty)" : text;
        }
    }
}
