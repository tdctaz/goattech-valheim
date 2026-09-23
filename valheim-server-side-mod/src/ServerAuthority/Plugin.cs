using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ServerAuthority
{
    [BepInPlugin(Guid, "Server Authority", ModVersion.Value)]
    [BepInProcess("valheim_server.exe")]
    [BepInProcess("valheim_server.x86_64")]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim.x86_64")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "valheim.server_authority";

        /// <summary>
        /// The game build this mod was written and verified against. A mismatch does not stop the
        /// mod, because most releases do not touch the methods we replace, but it is logged loudly
        /// so that an unexplained problem after a game update has an obvious first suspect.
        /// </summary>
        public const string VerifiedGameVersion = "1.0.15";

        internal static ManualLogSource Log;
        internal static Harmony Harmony;

        /// <summary>
        /// True once we are running inside a server that this mod should take over. Every patch
        /// checks this and falls through to vanilla behaviour when it is false, so the assembly is
        /// inert if it is ever loaded on a client.
        /// </summary>
        internal static bool ServerActive;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Bind(Config);
            EffectiveSettings.Log(Log, Config, "Server Authority");

            if (!ModConfig.Enabled.Value)
            {
                Log.LogInfo("Server Authority is disabled by configuration.");
                return;
            }

            Harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            Log.LogInfo("Server Authority patches installed, waiting for a server to start.");
        }

        private void OnDestroy()
        {
            Harmony?.UnpatchSelf();
        }

        /// <summary>
        /// Decides whether to activate, once ZNet exists and we can tell what kind of session this is.
        /// </summary>
        internal static void EvaluateSession()
        {
            ServerActive = false;

            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer())
            {
                return;
            }

            if (ModConfig.RequireDedicatedServer.Value && !znet.IsDedicated())
            {
                Log.LogInfo(
                    "This is a player-hosted session, not a dedicated server, so Server Authority " +
                    "stays inactive. The host already simulates everything here.");
                return;
            }

            ServerActive = true;
            OwnershipPolicy.Reset();
            SimulationAnchors.Reset();
            WaterQueries.EnsureLayerMask();

            string running = global::Version.CurrentVersion.ToString();
            if (running != VerifiedGameVersion)
            {
                Log.LogWarning(
                    $"Server Authority was verified against Valheim {VerifiedGameVersion} but this " +
                    $"server runs {running}. If world simulation misbehaves, re-check the patched " +
                    "methods against the current game code before looking anywhere else.");
            }

            Log.LogInfo($"Server Authority active. Ownership mode: {ModConfig.Mode.Value}.");
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    internal static class ZNet_Awake_Patch
    {
        private static void Postfix(ZNet __instance)
        {
            Integrity.IntegrityServer.Reset();
            Integrity.IntegrityClient.Reset();
            WaveSync.Reset();
            WaveField.Reset();
            ServerViewpoint.Reset();
            LocalWeather.Reset();
            LocalWind.Reset();
            CreaturesRaids.Reset();
            ServerControl.Reset();
            Patches.EnvMan_UpdateEnvironment_Patch.Forget();
            Patches.ShipEffects_CustomLateUpdate_Patch.Forget();
            Patches.Ship_CustomFixedUpdate_Patch.Forget();
            Patches.Ship_HealthWatch_Patch.Forget();
#if DEBUG_TOOLS
            Patches.Ship_CustomFixedUpdate_Diagnostic.Forget();
#endif
            Plugin.EvaluateSession();

            if (__instance.IsServer())
            {
                Plugin.Log.LogInfo(
                    $"Mod validation: {(ModConfig.ModValidationEnabled.Value ? "on" : "off")}. " +
                    $"Server side characters: {(ModConfig.CharactersEnabled.Value ? "on, stored in " + Integrity.CharacterStore.Root() : "off")}.");
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class ZNet_OnDestroy_Patch
    {
        private static void Postfix()
        {
            Plugin.ServerActive = false;
            OwnershipPolicy.Reset();
            SimulationAnchors.Reset();
            Integrity.IntegrityServer.Reset();
            Integrity.IntegrityClient.Reset();
            WaveSync.Reset();
            WaveField.Reset();
            ServerViewpoint.Reset();
            LocalWeather.Reset();
            LocalWind.Reset();
            CreaturesRaids.Reset();
            ServerControl.Reset();
            Patches.EnvMan_UpdateEnvironment_Patch.Forget();
            Patches.ShipEffects_CustomLateUpdate_Patch.Forget();
            Patches.Ship_CustomFixedUpdate_Patch.Forget();
            Patches.Ship_HealthWatch_Patch.Forget();
#if DEBUG_TOOLS
            Patches.Ship_CustomFixedUpdate_Diagnostic.Forget();
#endif
        }
    }
}
