using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ValheimDyeing
{
    [BepInPlugin(Guid, "Valheim Dyeing", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "valheim.dyeing";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static Harmony Harmony;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Bind(Config);
            EffectiveSettings.Log(Log, Config, "Valheim Dyeing");
            Config.SettingChanged += (sender, args) => ConfigSync.LocalConfigChanged();
            ConfigWatch.Start(Config);

            Harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            Log.LogInfo($"Valheim Dyeing {Version} loaded.");
        }

        private void OnDestroy()
        {
            Harmony?.UnpatchSelf();
        }
    }
}
