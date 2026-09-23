using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerTool;

public sealed class ServerSettings
{
    public string Name { get; set; } = "GoatTech";
    public string World { get; set; } = "Dedicated";
    public string Password { get; set; } = "changeme123";
    public int Port { get; set; } = 2456;
    public bool Public { get; set; }
    public int SaveInterval { get; set; } = 1800;
    public int Backups { get; set; } = 4;
    public List<string> ExtraArguments { get; set; } = new();
}

public sealed class ToolConfig
{
    public string InstanceName { get; set; } = "valheim";
    public string ServerDirectory { get; set; } = "";
    public string Executable { get; set; } = "";
    public ServerSettings Server { get; set; } = new();
    public Dictionary<string, string> Environment { get; set; } = new();
    public string SaveDirectory { get; set; } = "";
    public string ControlDirectory { get; set; } = "";
    public string BackupDirectory { get; set; } = "backups";
    public List<string> ExtraBackupPaths { get; set; } = new();
    public int WorldBackupsToKeep { get; set; } = 14;
    public int ServerLogsToKeep { get; set; } = 30;
    public bool BackupOnStop { get; set; } = true;
    public string DailyRestartTime { get; set; } = "05:00";
    public List<int> WarningMinutes { get; set; } = new() { 15, 10, 5, 2, 1 };
    public string WarningMessage { get; set; } = "Server {action} in {time}";
    public string FinalMessage { get; set; } = "Server {action} now";
    public bool AutoRestartOnCrash { get; set; } = true;
    public int CrashRestartDelaySeconds { get; set; } = 10;
    public int ControlAckSeconds { get; set; } = 10;
    public int StopTimeoutSeconds { get; set; } = 180;
    public bool EchoServerOutput { get; set; }

    [JsonIgnore]
    public string ConfigPath { get; private set; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ToolConfig Load(string path)
    {
        string full = Path.GetFullPath(path);
        ToolConfig config = JsonSerializer.Deserialize<ToolConfig>(File.ReadAllText(full), Options)
            ?? throw new InvalidDataException($"{full} is empty.");
        config.ConfigPath = full;
        return config;
    }

    public static void WriteDefault(string path)
    {
        ToolConfig config = new() { ServerDirectory = DefaultServerDirectory() };
        File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
    }

    public string BaseDirectory => Path.GetDirectoryName(ConfigPath) ?? Directory.GetCurrentDirectory();

    public string Resolve(string path) => Path.GetFullPath(path, BaseDirectory);

    public string ServerDir => Resolve(ServerDirectory);

    public string BackupDir => Resolve(BackupDirectory);

    public string ExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Executable))
        {
            return Path.GetFullPath(Executable, ServerDir);
        }

        return Path.Combine(ServerDir, OperatingSystem.IsWindows() ? "valheim_server.exe" : "valheim_server.x86_64");
    }

    public string ControlDir()
    {
        if (!string.IsNullOrWhiteSpace(ControlDirectory))
        {
            return Resolve(ControlDirectory);
        }

        return Path.Combine(ServerDir, "ServerAuthority-control");
    }

    public string SaveDir()
    {
        if (!string.IsNullOrWhiteSpace(SaveDirectory))
        {
            return Resolve(SaveDirectory);
        }

        int index = Server.ExtraArguments.FindIndex(a => a.Equals("-savedir", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < Server.ExtraArguments.Count)
        {
            return Path.GetFullPath(Server.ExtraArguments[index + 1], ServerDir);
        }

        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(home, "AppData", "LocalLow", "IronGate", "Valheim");
        }

        string? xdg = Environment.TryGetValue("XDG_CONFIG_HOME", out string? configured)
            ? configured
            : System.Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string configHome = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : Path.GetFullPath(xdg, ServerDir);
        return Path.Combine(configHome, "unity3d", "IronGate", "Valheim");
    }

    public TimeOnly? DailyRestart()
    {
        if (string.IsNullOrWhiteSpace(DailyRestartTime))
        {
            return null;
        }

        return TimeOnly.Parse(DailyRestartTime, System.Globalization.CultureInfo.InvariantCulture);
    }

    public List<string> ServerArguments()
    {
        List<string> args = new()
        {
            "-nographics",
            "-batchmode",
            "-name", Server.Name,
            "-port", Server.Port.ToString(),
            "-world", Server.World,
            "-public", Server.Public ? "1" : "0",
            "-saveinterval", Server.SaveInterval.ToString(),
            "-backups", Server.Backups.ToString(),
        };
        if (!string.IsNullOrEmpty(Server.Password))
        {
            args.Add("-password");
            args.Add(Server.Password);
        }

        args.AddRange(Server.ExtraArguments);
        return args;
    }

    private static string DefaultServerDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return @"C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server";
        }

        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "Valheim dedicated server");
    }
}
