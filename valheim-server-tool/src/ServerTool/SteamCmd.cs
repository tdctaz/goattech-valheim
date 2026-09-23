using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ServerTool;

public sealed record UpdateResult(bool Success, string Summary);

public static class SteamCmd
{
    public const string AppId = "896660";

    private static readonly Regex BuildIdPattern = new("\"buildid\"\\s+\"(\\d+)\"", RegexOptions.Compiled);

    public static string? Find(ToolConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.SteamCmdPath))
        {
            string configured = config.Resolve(config.SteamCmdPath);
            return File.Exists(configured) ? configured : null;
        }

        string[] names = OperatingSystem.IsWindows()
            ? new[] { "steamcmd.exe" }
            : new[] { "steamcmd", "steamcmd.sh" };

        IEnumerable<string> folders = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        folders = folders.Concat(OperatingSystem.IsWindows()
            ? new[] { @"C:\steamcmd", @"C:\SteamCMD", Path.Combine(home, "steamcmd") }
            : new[] { Path.Combine(home, "steamcmd"), Path.Combine(home, ".steam", "steamcmd"), "/usr/games", "/usr/lib/games/steam" });

        foreach (string folder in folders)
        {
            foreach (string name in names)
            {
                string candidate = Path.Combine(folder, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static UpdateResult Update(ToolConfig config, string steamCmd, string logPath)
    {
        string? before = BuildId(config.ServerDir);

        ProcessStartInfo info = new(steamCmd)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        info.ArgumentList.Add("+force_install_dir");
        info.ArgumentList.Add(config.ServerDir);
        info.ArgumentList.Add("+login");
        info.ArgumentList.Add("anonymous");
        info.ArgumentList.Add("+app_update");
        info.ArgumentList.Add(AppId);
        if (config.ValidateOnUpdate)
        {
            info.ArgumentList.Add("validate");
        }

        info.ArgumentList.Add("+quit");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        object gate = new();
        bool succeeded = false;
        string? lastError = null;

        using StreamWriter log = new(logPath) { AutoFlush = true };
        using Process process = new() { StartInfo = info };

        void OnLine(string? line)
        {
            if (line == null)
            {
                return;
            }

            lock (gate)
            {
                log.WriteLine(line);
                if (line.Contains("Success! App", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("already up to date", StringComparison.OrdinalIgnoreCase))
                {
                    succeeded = true;
                }
                else if (line.Contains("ERROR", StringComparison.Ordinal) || line.Contains("FAILED", StringComparison.Ordinal))
                {
                    lastError = line.Trim();
                }
            }
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        try
        {
            process.Start();
        }
        catch (Exception e)
        {
            return new UpdateResult(false, $"SteamCMD could not be started: {e.Message}");
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(TimeSpan.FromMinutes(config.UpdateTimeoutMinutes)))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            process.WaitForExit();
            return new UpdateResult(false, $"SteamCMD did not finish within {config.UpdateTimeoutMinutes} minutes and was stopped.");
        }

        process.WaitForExit();
        int code = process.ExitCode;
        string? after = BuildId(config.ServerDir);

        lock (gate)
        {
            if (!succeeded || code != 0)
            {
                return new UpdateResult(false,
                    $"SteamCMD failed with exit code {code}{(lastError != null ? $": {lastError}" : "")}.");
            }
        }

        if (before != null && after != null && before != after)
        {
            return new UpdateResult(true, $"Server updated from build {before} to build {after}.");
        }

        return new UpdateResult(true, after != null ? $"Server is up to date, build {after}." : "Server is up to date.");
    }

    public static string? BuildId(string serverDir)
    {
        string manifest = "appmanifest_" + AppId + ".acf";
        string[] candidates =
        {
            Path.Combine(serverDir, "steamapps", manifest),
            Path.GetFullPath(Path.Combine(serverDir, "..", "..", manifest)),
        };

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            Match match = BuildIdPattern.Match(File.ReadAllText(path));
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }
}
