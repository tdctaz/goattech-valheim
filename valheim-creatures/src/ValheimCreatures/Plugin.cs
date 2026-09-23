using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ValheimCreatures
{
    [BepInPlugin(Guid, "Valheim Creatures", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "valheim.creatures";
        public const string Version = ModVersion.Value;

        internal static ManualLogSource Log;
        internal static Harmony Harmony;
        internal static BepInEx.Configuration.ConfigFile ConfigFile;

        private void Awake()
        {
            Log = Logger;
            ConfigFile = Config;
            ModConfig.Bind(Config);
            EffectiveSettings.Log(Log, Config, "Valheim Creatures");
            Config.SettingChanged += (sender, args) => ConfigSync.LocalConfigChanged();

            Harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            Log.LogInfo($"Valheim Creatures {Version} loaded.");
        }

        private void OnDestroy()
        {
            Harmony?.UnpatchSelf();
        }
    }
}
