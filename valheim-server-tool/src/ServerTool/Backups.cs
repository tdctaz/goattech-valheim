using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ServerTool;

public static class Backups
{
    private static readonly Regex Interesting = new(
        "Exception|Assertion|Caught fatal signal|stack frames|Crash|Server Authority",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

    public static string LogsDir(ToolConfig config) => Path.Combine(config.BackupDir, "logs");

    public static string BackupWorld(ToolConfig config)
    {
        string saveDir = config.SaveDir();
        string worlds = Path.Combine(saveDir, "worlds_local");
        string world = config.Server.World;
        string worldDir = Path.Combine(worlds, world);
        string[] files =
        {
            Path.Combine(worlds, world + ".db"),
            Path.Combine(worlds, world + ".fwl"),
        };

        if (!Directory.Exists(worldDir) && !files.Any(File.Exists))
        {
            throw new FileNotFoundException($"No save for world '{world}' in {worlds}");
        }

        string target = Path.Combine(config.BackupDir, "worlds");
        Directory.CreateDirectory(target);
        string zipPath = Path.Combine(target, $"{world}-{Stamp()}.zip");
        string partial = zipPath + ".partial";

        using (ZipArchive zip = ZipFile.Open(partial, ZipArchiveMode.Create))
        {
            AddDirectory(zip, worldDir, Path.Combine("worlds_local", world));
            foreach (string file in files.Where(File.Exists))
            {
                AddFile(zip, file, Path.Combine("worlds_local", Path.GetFileName(file)));
            }

            AddDirectory(zip, Path.Combine(saveDir, "characters_serverauthority"), "characters_serverauthority");

            foreach (string extra in config.ExtraBackupPaths)
            {
                string full = config.Resolve(extra);
                string name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (Directory.Exists(full))
                {
                    AddDirectory(zip, full, Path.Combine("extra", name));
                }
                else if (File.Exists(full))
                {
                    AddFile(zip, full, Path.Combine("extra", name));
                }
            }
        }

        File.Move(partial, zipPath);
        Prune(target, $"{world}-*.zip", config.WorldBackupsToKeep);
        return zipPath;
    }

    public static string SaveCrash(ToolConfig config, ServerProcess server, int exitCode, TimeSpan ranFor)
    {
        string dir = Path.Combine(config.BackupDir, "crashes", $"{Stamp()}-exit{exitCode}");
        Directory.CreateDirectory(dir);

        TryCopy(server.LogPath, Path.Combine(dir, "server.log"));
        TryCopy(Path.Combine(config.ServerDir, "BepInEx", "LogOutput.log"), Path.Combine(dir, "BepInEx-LogOutput.log"));

        string configDir = Path.Combine(config.ServerDir, "BepInEx", "config");
        if (Directory.Exists(configDir))
        {
            foreach (string cfg in Directory.GetFiles(configDir, "*.cfg"))
            {
                TryCopy(cfg, Path.Combine(dir, "config", Path.GetFileName(cfg)));
            }
        }

        CopyUnityCrashReports(server.StartedAt, dir);
        WriteSummary(server.LogPath, Path.Combine(dir, "summary.txt"), exitCode, ranFor);
        return dir;
    }

    public static void PruneServerLogs(ToolConfig config)
    {
        Prune(LogsDir(config), "server-*.log", config.ServerLogsToKeep);
    }

    private static void WriteSummary(string logPath, string summaryPath, int exitCode, TimeSpan ranFor)
    {
        List<string> lines = new()
        {
            $"=== Server died with exit code {exitCode} after {ranFor:d\\.hh\\:mm\\:ss}, at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===",
            "",
        };

        string[] log = ReadShared(logPath);
        lines.Add("Interesting lines:");
        lines.AddRange(log.Where(l => Interesting.IsMatch(l)).TakeLast(60));
        lines.Add("");
        lines.Add("Last 40 lines:");
        lines.AddRange(log.TakeLast(40));
        File.WriteAllLines(summaryPath, lines);
    }

    private static void CopyUnityCrashReports(DateTime since, string dir)
    {
        string crashes = Path.Combine(Path.GetTempPath(), "IronGate", "Valheim", "Crashes");
        if (!Directory.Exists(crashes))
        {
            return;
        }

        foreach (string report in Directory.GetDirectories(crashes))
        {
            if (Directory.GetLastWriteTime(report) < since)
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(report, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(crashes, file);
                TryCopy(file, Path.Combine(dir, "unity-crash", relative));
            }
        }
    }

    private static string[] ReadShared(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new(stream);
            return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static void TryCopy(string from, string to)
    {
        if (!File.Exists(from))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            using FileStream source = new(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using FileStream target = new(to, FileMode.Create, FileAccess.Write);
            source.CopyTo(target);
        }
        catch (IOException e)
        {
            Log.Warn($"Could not copy {from}: {e.Message}");
        }
    }

    private static void AddFile(ZipArchive zip, string file, string entryName)
    {
        using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        ZipArchiveEntry entry = zip.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
        entry.LastWriteTime = File.GetLastWriteTime(file);
        using Stream target = entry.Open();
        source.CopyTo(target);
    }

    private static void AddDirectory(ZipArchive zip, string dir, string entryPrefix)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            AddFile(zip, file, Path.Combine(entryPrefix, Path.GetRelativePath(dir, file)));
        }
    }

    private static void Prune(string dir, string pattern, int keep)
    {
        if (keep <= 0 || !Directory.Exists(dir))
        {
            return;
        }

        foreach (FileInfo old in new DirectoryInfo(dir).GetFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
        {
            try
            {
                old.Delete();
            }
            catch (IOException e)
            {
                Log.Warn($"Could not delete old file {old.FullName}: {e.Message}");
            }
        }
    }
}
