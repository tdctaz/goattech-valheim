using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority
{
    internal static class ServerControl
    {
        private const float PollIntervalSeconds = 1f;

        private static string _directory;
        private static float _nextPoll;
        private static bool _quitting;

        internal static string Directory()
        {
            string configured = ModConfig.ControlDirectory.Value;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return Path.Combine(Paths.GameRootPath, "ServerAuthority-control");
        }

        internal static void Tick()
        {
            if (!ModConfig.ControlEnabled.Value || _quitting)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextPoll)
            {
                return;
            }

            _nextPoll = now + PollIntervalSeconds;

            if (_directory == null)
            {
                _directory = Directory();
                System.IO.Directory.CreateDirectory(_directory);
                Plugin.Log.LogInfo($"Server control: watching {_directory} for commands.");
            }

            string[] files = System.IO.Directory.GetFiles(_directory, "*.cmd");
            if (files.Length == 0)
            {
                return;
            }

            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file).Trim();
                    File.Delete(file);
                }
                catch (IOException)
                {
                    continue;
                }

                Run(text);
                if (_quitting)
                {
                    return;
                }
            }
        }

        private static void Run(string text)
        {
            int space = text.IndexOf(' ');
            string verb = (space < 0 ? text : text.Substring(0, space)).ToLowerInvariant();
            string rest = space < 0 ? string.Empty : text.Substring(space + 1).Trim();

            switch (verb)
            {
                case "say":
                    Broadcast(rest);
                    break;
                case "shutdown":
                    Plugin.Log.LogInfo("Server control: shutdown requested, saving and quitting.");
                    _quitting = true;
                    Application.Quit();
                    break;
                default:
                    Plugin.Log.LogWarning($"Server control: unknown command '{text}'.");
                    break;
            }
        }

        private static void Broadcast(string message)
        {
            if (message.Length == 0 || ZRoutedRpc.instance == null)
            {
                return;
            }

            Plugin.Log.LogInfo($"Server control: broadcasting '{message}'.");
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.Everybody, "ShowMessage", (int)MessageHud.MessageType.Center, message);
        }

        internal static void Reset()
        {
            _directory = null;
            _nextPoll = 0f;
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    internal static class ZNet_Update_ServerControl_Patch
    {
        private static void Postfix(ZNet __instance)
        {
            if (!__instance.IsServer() || !__instance.IsDedicated())
            {
                return;
            }

            try
            {
                ServerControl.Tick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Server control failed: {e}");
            }
        }
    }
}
