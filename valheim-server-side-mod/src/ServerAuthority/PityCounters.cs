using System;
using System.Collections.Generic;
using System.IO;

namespace ServerAuthority
{
    internal static class PityCounters
    {
        private const string FileName = "pity_counters.dat";
        private const int FormatVersion = 1;

        private static string _path;
        private static readonly Dictionary<int, int> LastSaved = new Dictionary<int, int>();

        internal static void Reset()
        {
            _path = null;
            LastSaved.Clear();
        }

        internal static void Load()
        {
            Reset();
            if (!Plugin.ServerActive || !ModConfig.KeepPityCounters.Value || ZNet.instance == null)
            {
                return;
            }

            string path = Integrity.CharacterStore.WorldFilePath(ZNet.instance.GetWorldName(), FileName);
            Dictionary<int, int> counters = CharacterDrop.s_pseudoCounter;

            byte[] data = Read(path, out string source, out bool found);
            if (data == null)
            {
                _path = path;
                if (found)
                {
                    Plugin.Log.LogWarning(
                        $"Pity counters: no saved copy of {path} reads, so they start fresh and the next world save replaces it.");
                }
                else
                {
                    Plugin.Log.LogInfo(
                        $"Pity counters: none saved for this world yet, so they start fresh and are kept in {path} from the next world save.");
                }

                return;
            }

            Dictionary<int, int> loaded = new Dictionary<int, int>();
            try
            {
                ZPackage package = new ZPackage(data);
                int version = package.ReadInt();
                if (version != FormatVersion)
                {
                    Plugin.Log.LogWarning(
                        $"Pity counters: {source} has format {version}, which this build does not read. They start fresh " +
                        "this session and are not saved, so the file is left as it is.");
                    return;
                }

                int count = package.ReadInt();
                for (int i = 0; i < count; i++)
                {
                    int key = package.ReadInt();
                    loaded[key] = package.ReadInt();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Pity counters: {source} does not read ({e.Message}). They start fresh this session and are not saved, " +
                    "so the file is left as it is.");
                return;
            }

            counters.Clear();
            foreach (KeyValuePair<int, int> entry in loaded)
            {
                counters[entry.Key] = entry.Value;
                LastSaved[entry.Key] = entry.Value;
            }

            _path = path;
            Plugin.Log.LogInfo(
                $"Pity counters: loaded {loaded.Count} from {source}, written {File.GetLastWriteTimeUtc(source):yyyy-MM-dd HH:mm:ss} UTC.");
        }

        internal static void Save()
        {
            if (_path == null)
            {
                return;
            }

            Dictionary<int, int> counters = CharacterDrop.s_pseudoCounter;
            ZPackage package = new ZPackage();
            package.Write(FormatVersion);
            package.Write(counters.Count);
            int changed = 0;
            foreach (KeyValuePair<int, int> entry in counters)
            {
                package.Write(entry.Key);
                package.Write(entry.Value);
                if (!LastSaved.TryGetValue(entry.Key, out int saved) || saved != entry.Value)
                {
                    changed++;
                }
            }

            try
            {
                Integrity.CharacterStore.Save(_path, package.GetArray());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Pity counters: could not save {counters.Count} to {_path}: {e.Message}");
                return;
            }

            LastSaved.Clear();
            foreach (KeyValuePair<int, int> entry in counters)
            {
                LastSaved[entry.Key] = entry.Value;
            }

            Plugin.Log.LogInfo($"Pity counters: saved {counters.Count}, {changed} changed since the last save.");
        }

        private static byte[] Read(string path, out string source, out bool found)
        {
            source = path;
            found = false;
            string[] candidates = { path, path + ".new", path + ".old" };
            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                found = true;
                try
                {
                    byte[] data = Integrity.CharacterStore.ReadFramed(candidate, true);
                    if (candidate != path)
                    {
                        Plugin.Log.LogWarning(
                            $"Pity counters: {path} is missing or unreadable, probably from a crash while saving. Read {candidate} instead.");
                    }

                    source = candidate;
                    return data;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Pity counters: cannot read {candidate}: {e.Message}");
                }
            }

            return null;
        }
    }
}
