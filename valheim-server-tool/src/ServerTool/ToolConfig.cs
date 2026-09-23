using System.Globalization;

namespace ServerTool;

public sealed class ToolConfig
{
    public string ServerName { get; set; } = "";
    public string World { get; set; } = "Dedicated";
    public string Password { get; set; } = "";
    public int Port { get; set; } = 2456;
    public bool Public { get; set; } = true;
    public bool Crossplay { get; set; }
    public string InstanceId { get; set; } = "";
    public int SaveInterval { get; set; } = 1800;
    public int Backups { get; set; } = 4;
    public int BackupShort { get; set; } = 7200;
    public int BackupLong { get; set; } = 43200;
    public string SaveDirectory { get; set; } = "";
    public string ExtraArguments { get; set; } = "";

    public bool ResetModifiers { get; set; }
    public string Preset { get; set; } = "Default";
    public string Combat { get; set; } = "Default";
    public string DeathPenalty { get; set; } = "Default";
    public string Resources { get; set; } = "Default";
    public string Raids { get; set; } = "Default";
    public string Portals { get; set; } = "Default";
    public bool NoBuildCost { get; set; }
    public bool PlayerEvents { get; set; }
    public bool PassiveMobs { get; set; }
    public bool NoMap { get; set; }
    public bool FireHazards { get; set; }

    public string DailyRestartTime { get; set; } = "05:00";
    public List<int> WarningMinutes { get; set; } = new() { 15, 10, 5, 2, 1 };
    public string WarningMessage { get; set; } = "Server {action} in {time}";
    public string FinalMessage { get; set; } = "Server {action} now";
    public string CancelMessage { get; set; } = "Server {action} cancelled";
    public string RestartAction { get; set; } = "restarting";
    public string ShutdownAction { get; set; } = "shutting down";

    public bool UpdateOnDailyRestart { get; set; } = true;
    public string SteamCmdPath { get; set; } = "";
    public bool ValidateOnUpdate { get; set; }
    public int UpdateTimeoutMinutes { get; set; } = 30;

    public bool AutoRestartOnCrash { get; set; } = true;
    public int CrashRestartDelaySeconds { get; set; } = 10;

    public string BackupDirectory { get; set; } = "backups";
    public int WorldBackupsToKeep { get; set; } = 14;
    public int ServerLogsToKeep { get; set; } = 30;
    public bool BackupOnStop { get; set; } = true;
    public string ExtraBackupPaths { get; set; } = "";

    public string InstanceName { get; set; } = "valheim";
    public string ServerDirectory { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Environment { get; set; } = "";
    public string ControlDirectory { get; set; } = "";
    public int ControlAckSeconds { get; set; } = 10;
    public int StopTimeoutSeconds { get; set; } = 180;
    public bool EchoServerOutput { get; set; }

    public string ConfigPath { get; private set; } = "";

    public static ToolConfig Load(string path)
    {
        ToolConfig config = new() { ConfigPath = Path.GetFullPath(path) };
        List<string> problems = ConfigFile.Read(config, config.ConfigPath);
        if (problems.Count > 0)
        {
            throw new InvalidDataException(string.Join(System.Environment.NewLine, problems));
        }

        ConfigFile.Write(config, config.ConfigPath);
        return config;
    }

    public static void WriteDefault(string path)
    {
        ConfigFile.Write(new ToolConfig(), Path.GetFullPath(path));
    }

    public string BaseDirectory => Path.GetDirectoryName(ConfigPath) ?? Directory.GetCurrentDirectory();

    public string Resolve(string path) => Path.GetFullPath(path, BaseDirectory);

    public string ServerDir => string.IsNullOrWhiteSpace(ServerDirectory) ? DetectServerDirectory() : Resolve(ServerDirectory);

    public string BackupDir => Resolve(BackupDirectory);

    public string ExecutableName => OperatingSystem.IsWindows() ? "valheim_server.exe" : "valheim_server.x86_64";

    public string ExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Executable))
        {
            return Path.GetFullPath(Executable, ServerDir);
        }

        return Path.Combine(ServerDir, ExecutableName);
    }

    public string ControlDir()
    {
        if (!string.IsNullOrWhiteSpace(ControlDirectory))
        {
            return Resolve(ControlDirectory);
        }

        return Path.Combine(ServerDir, "ServerAuthority-control");
    }

    public Dictionary<string, string> EnvironmentVariables()
    {
        Dictionary<string, string> result = new();
        foreach (string pair in Environment.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0)
            {
                result[pair[..equals].Trim()] = pair[(equals + 1)..].Trim();
            }
        }

        return result;
    }

    public IEnumerable<string> ExtraBackupPathList() =>
        ExtraBackupPaths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string SaveDir()
    {
        if (!string.IsNullOrWhiteSpace(SaveDirectory))
        {
            return Path.GetFullPath(SaveDirectory, ServerDir);
        }

        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(home, "AppData", "LocalLow", "IronGate", "Valheim");
        }

        string? xdg = EnvironmentVariables().TryGetValue("XDG_CONFIG_HOME", out string? configured)
            ? configured
            : System.Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string configHome = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : Path.GetFullPath(xdg, ServerDir);
        return Path.Combine(configHome, "unity3d", "IronGate", "Valheim");
    }

    public bool WorldExists()
    {
        string worlds = Path.Combine(SaveDir(), "worlds_local");
        return Directory.Exists(Path.Combine(worlds, World)) || File.Exists(Path.Combine(worlds, World + ".fwl"));
    }

    public TimeOnly? DailyRestart()
    {
        if (string.IsNullOrWhiteSpace(DailyRestartTime))
        {
            return null;
        }

        return TimeOnly.ParseExact(DailyRestartTime, "HH:mm", CultureInfo.InvariantCulture);
    }

    public List<string> ServerArguments()
    {
        List<string> args = new()
        {
            "-nographics",
            "-batchmode",
            "-name", string.IsNullOrWhiteSpace(ServerName) ? World : ServerName,
            "-port", Port.ToString(CultureInfo.InvariantCulture),
            "-world", World,
            "-public", Public ? "1" : "0",
            "-saveinterval", SaveInterval.ToString(CultureInfo.InvariantCulture),
            "-backups", Backups.ToString(CultureInfo.InvariantCulture),
            "-backupshort", BackupShort.ToString(CultureInfo.InvariantCulture),
            "-backuplong", BackupLong.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrEmpty(Password))
        {
            args.Add("-password");
            args.Add(Password);
        }

        if (!string.IsNullOrWhiteSpace(SaveDirectory))
        {
            args.Add("-savedir");
            args.Add(SaveDir());
        }

        args.Add("-seed");
        args.Add(World);

        if (Crossplay)
        {
            args.Add("-crossplay");
        }

        if (!string.IsNullOrWhiteSpace(InstanceId))
        {
            args.Add("-instanceid");
            args.Add(InstanceId);
        }

        if (ResetModifiers)
        {
            args.Add("-resetmodifiers");
        }

        if (!IsDefault(Preset))
        {
            args.Add("-preset");
            args.Add(Preset.ToLowerInvariant());
        }

        foreach ((string modifier, string value) in new[]
        {
            ("combat", Combat), ("deathpenalty", DeathPenalty), ("resources", Resources), ("raids", Raids), ("portals", Portals),
        })
        {
            if (!IsDefault(value))
            {
                args.Add("-modifier");
                args.Add(modifier);
                args.Add(value.ToLowerInvariant());
            }
        }

        foreach ((string key, bool on) in new[]
        {
            ("nobuildcost", NoBuildCost), ("playerevents", PlayerEvents), ("passivemobs", PassiveMobs), ("nomap", NoMap), ("fire", FireHazards),
        })
        {
            if (on)
            {
                args.Add("-setkey");
                args.Add(key);
            }
        }

        args.AddRange(SplitArguments(ExtraArguments));
        return args;
    }

    public IEnumerable<string> StartupProblems()
    {
        if (!File.Exists(ExecutablePath()))
        {
            yield return $"The server executable was not found at {ExecutablePath()}. Set ServerDirectory in [Tool].";
        }

        if (Public && Password.Length < 5)
        {
            yield return "A public server needs a Password of at least 5 characters, or Valheim refuses to start. Set Password, or set Public = false.";
        }

        if (Public && Password.Length > 0 && World.Contains(Password, StringComparison.Ordinal))
        {
            yield return "The Password may not appear in the World name, or Valheim refuses to start.";
        }
    }

    private static bool IsDefault(string value) => string.IsNullOrWhiteSpace(value) || value.Equals("Default", StringComparison.OrdinalIgnoreCase);

    private string DetectServerDirectory()
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        List<string> candidates = new()
        {
            BaseDirectory,
            Path.GetDirectoryName(BaseDirectory) ?? BaseDirectory,
            AppContext.BaseDirectory,
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) ?? AppContext.BaseDirectory,
        };

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server");
        }
        else
        {
            candidates.Add(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "Valheim dedicated server"));
            candidates.Add(Path.Combine(home, ".steam", "steam", "steamapps", "common", "Valheim dedicated server"));
        }

        return candidates.FirstOrDefault(dir => File.Exists(Path.Combine(dir, ExecutableName))) ?? BaseDirectory;
    }

    private static IEnumerable<string> SplitArguments(string text)
    {
        List<string> result = new();
        System.Text.StringBuilder current = new();
        bool quoted = false;
        bool any = false;
        foreach (char c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
