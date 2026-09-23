using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ValheimRebalanced
{
    [BepInPlugin(Guid, "Valheim Rebalanced", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "valheim.rebalanced";
        public const string Version = ModVersion.Value;

        internal static ManualLogSource Log;
        internal static Harmony Harmony;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Bind(Config);
            EffectiveSettings.Log(Log, Config, "Valheim Rebalanced");
            Config.SettingChanged += (sender, args) => ConfigSync.LocalConfigChanged();

            Harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            Log.LogInfo($"Valheim Rebalanced {Version} loaded.");
        }

        private void OnDestroy()
        {
            Harmony?.UnpatchSelf();
        }
    }
}
