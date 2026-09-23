using System;
using System.IO;
using BepInEx.Configuration;

namespace ValheimDyeing
{
    internal static class ConfigWatch
    {
        private static FileSystemWatcher _watcher;
        private static ConfigFile _config;
        private static volatile bool _dirty;
        private static DateTime _lastSeen = DateTime.MinValue;

        internal static void Start(ConfigFile config)
        {
            if (_watcher != null || config == null)
            {
                return;
            }

            _config = config;
            string path = config.ConfigFilePath;
            string folder = Path.GetDirectoryName(path);
            if (folder == null || !Directory.Exists(folder))
            {
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(folder, Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += (sender, args) => _dirty = true;
                _watcher.Created += (sender, args) => _dirty = true;
            }
            catch (Exception error)
            {
                Plugin.Log.LogWarning($"Could not watch {path} for changes: {error.Message}");
                _watcher = null;
            }
        }

        internal static void Poll()
        {
            if (!_dirty || _config == null)
            {
                return;
            }

            _dirty = false;

            DateTime written;
            try
            {
                written = File.GetLastWriteTimeUtc(_config.ConfigFilePath);
            }
            catch (IOException)
            {
                return;
            }

            if (written == _lastSeen)
            {
                return;
            }

            _lastSeen = written;

            try
            {
                _config.Reload();
                Plugin.Log.LogInfo("Re-read the configuration file after it changed on disk.");
            }
            catch (Exception error)
            {
                Plugin.Log.LogWarning($"Could not re-read the configuration file: {error.Message}");
            }
        }
    }
}
