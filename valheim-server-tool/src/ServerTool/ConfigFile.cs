using System.Globalization;
using System.Text;

namespace ServerTool;

public static class ConfigFile
{
    private sealed record Setting(
        string Section,
        string Key,
        string Type,
        string? Options,
        string Description,
        Func<ToolConfig, string> Get,
        Func<ToolConfig, string, string?> Set);

    private static readonly string[] Presets = { "Default", "Normal", "Casual", "Easy", "Hard", "Hardcore", "Immersive", "Hammer" };
    private static readonly string[] CombatOptions = { "Default", "VeryEasy", "Easy", "Hard", "VeryHard" };
    private static readonly string[] DeathPenaltyOptions = { "Default", "Casual", "VeryEasy", "Easy", "Hard", "Hardcore" };
    private static readonly string[] ResourceOptions = { "Default", "MuchLess", "Less", "More", "MuchMore", "Most" };
    private static readonly string[] RaidOptions = { "Default", "None", "MuchLess", "Less", "More", "MuchMore" };
    private static readonly string[] PortalOptions = { "Default", "Casual", "Hard", "VeryHard" };

    private static readonly Setting[] Settings =
    {
        Text("Server", "Name", "The name shown in the server browser. Empty uses the World name.",
            c => c.ServerName, (c, v) => c.ServerName = v),
        Text("Server", "World", "The world to load. A new world is created on first start if it does not exist, with the world name as " +
            "its seed. Needs Server Authority, which hands the seed to the game.",
            c => c.World, (c, v) => c.World = v, required: true),
        Text("Server", "Password",
            "Password players must enter. A public server needs at least 5 characters, and the password may not " +
            "appear in the World name. A server that is not public may leave it empty.",
            c => c.Password, (c, v) => c.Password = v),
        Number("Server", "Port", "The game port. Valheim also uses the next port up, so open both in the firewall.",
            1, 65534, c => c.Port, (c, v) => c.Port = v),
        Flag("Server", "Public", "List the server in the in-game server browser. When false, players join by IP address only.",
            c => c.Public, (c, v) => c.Public = v),
        Flag("Server", "Crossplay",
            "Use the Crossplay (PlayFab) backend so players on other platforms can join. False uses Steam only.",
            c => c.Crossplay, (c, v) => c.Crossplay = v),
        Text("Server", "InstanceId",
            "Only needed with Crossplay when several servers share a port on one machine: give each a unique value.",
            c => c.InstanceId, (c, v) => c.InstanceId = v),
        Number("Server", "SaveInterval", "Seconds between world saves. A crash loses everything since the last save.",
            5, 86400, c => c.SaveInterval, (c, v) => c.SaveInterval = v),
        Number("Server", "Backups",
            "How many automatic world backups Valheim keeps in its own save folder. The first is taken after " +
            "BackupShort seconds, the rest BackupLong seconds apart.",
            0, 1000, c => c.Backups, (c, v) => c.Backups = v),
        Number("Server", "BackupShort", "Seconds before Valheim's first automatic backup.",
            5, 604800, c => c.BackupShort, (c, v) => c.BackupShort = v),
        Number("Server", "BackupLong", "Seconds between Valheim's later automatic backups.",
            5, 604800, c => c.BackupLong, (c, v) => c.BackupLong = v),
        Text("Server", "SaveDirectory",
            "Where worlds, characters and the admin, ban and permitted lists are kept. Empty uses Valheim's " +
            "default: %USERPROFILE%\\AppData\\LocalLow\\IronGate\\Valheim on Windows, ~/.config/unity3d/IronGate/Valheim on Linux.",
            c => c.SaveDirectory, (c, v) => c.SaveDirectory = v),
        Text("Server", "ExtraArguments",
            "Anything else to pass to valheim_server, separated by spaces. Put quotes around values with spaces.",
            c => c.ExtraArguments, (c, v) => c.ExtraArguments = v),

        Flag("World Modifiers", "ResetModifiers",
            "Valheim stores world modifiers in the world itself, so a modifier once set stays even after it is removed " +
            "here. True clears them on every start and applies only what this section says, making this file the one " +
            "place they are decided. False adds this section on top of whatever the world already has, such as " +
            "modifiers picked when the world was created in the game.",
            c => c.ResetModifiers, (c, v) => c.ResetModifiers = v),
        Choice("World Modifiers", "Preset",
            "Sets all the modifiers below at once, as the buttons on the world creation screen do. Modifiers set " +
            "below are applied after it and win. Default leaves the world's modifiers alone. " +
            "Normal: recommended on your first playthrough and the most balanced overall. " +
            "Casual: combat is a lot easier, enemies will not attack until provoked, there are no raids, resources " +
            "are more plentiful, and you do not drop items or lose skills on death. " +
            "Easy: combat is easier and there are fewer raids. " +
            "Hard: combat is harder and monsters raid your base more often. " +
            "Hardcore: combat is a lot harder and raids come more often; on death all skills and carried items are " +
            "lost forever; no map, and no portals or leaving boss dungeons while a boss is alive. " +
            "Immersive: no map and no portals, other settings normal. " +
            "Hammer: build pieces are free once discovered, no raids, enemies do not attack until provoked, other " +
            "settings normal.",
            Presets, c => c.Preset, (c, v) => c.Preset = v),
        Choice("World Modifiers", "Combat",
            "The difficulty. Governs how much damage you give and take, how likely you are to meet higher level " +
            "enemies, and how dangerous they are. Default leaves it as the world has it.",
            CombatOptions, c => c.Combat, (c, v) => c.Combat = v),
        Choice("World Modifiers", "DeathPenalty",
            "Governs what happens when you die, from keeping everything (Casual) to losing all skills and carried " +
            "items (Hardcore). Default leaves it as the world has it.",
            DeathPenaltyOptions, c => c.DeathPenalty, (c, v) => c.DeathPenalty = v),
        Choice("World Modifiers", "Resources",
            "Governs the amount of resources you gain from the world and from enemies. Default leaves it as the " +
            "world has it.",
            ResourceOptions, c => c.Resources, (c, v) => c.Resources = v),
        Choice("World Modifiers", "Raids",
            "Governs how often enemies may raid your base. Default leaves it as the world has it.",
            RaidOptions, c => c.Raids, (c, v) => c.Raids = v),
        Choice("World Modifiers", "Portals",
            "Changes how portals work. Casual lets you bring all items through portals, which skips the intended " +
            "progression. Hard: you cannot use portals or exit boss dungeons while a boss is active. VeryHard: no " +
            "portals at all. Default leaves it as the world has it.",
            PortalOptions, c => c.Portals, (c, v) => c.Portals = v),
        Flag("World Modifiers", "NoBuildCost",
            "Build pieces require no materials. You still need to discover recipes as usual.",
            c => c.NoBuildCost, (c, v) => c.NoBuildCost = v),
        Flag("World Modifiers", "PlayerEvents",
            "Raids are based on each player's own progress rather than on which bosses have been killed on the " +
            "server. Friendlier to players with different progress.",
            c => c.PlayerEvents, (c, v) => c.PlayerEvents = v),
        Flag("World Modifiers", "PassiveMobs", "Enemies will not attack until you provoke them.",
            c => c.PassiveMobs, (c, v) => c.PassiveMobs = v),
        Flag("World Modifiers", "NoMap", "No map or minimap. This makes the game harder than intended.",
            c => c.NoMap, (c, v) => c.NoMap = v),
        Flag("World Modifiers", "FireHazards",
            "Wood can catch fire and spread throughout the whole world, not just in the Ashlands.",
            c => c.FireHazards, (c, v) => c.FireHazards = v),

        new("Schedule", "DailyRestartTime", "Time", "HH:mm in 24 hour local time, or empty to turn the daily restart off",
            "When the server restarts every day. Players are warned beforehand, the world is backed up while the " +
            "server is down, and it starts again straight away.",
            c => c.DailyRestartTime,
            (c, v) =>
            {
                if (v.Length > 0 && !TimeOnly.TryParseExact(v, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    return "must be a time such as 05:00, or empty";
                }

                c.DailyRestartTime = v;
                return null;
            }),
        new("Schedule", "WarningMinutes", "List of whole numbers", "comma separated minutes, or empty for no warnings",
            "How many minutes before a stop or restart players are warned. The countdown is as long as the largest.",
            c => string.Join(", ", c.WarningMinutes),
            (c, v) =>
            {
                List<int> minutes = new();
                foreach (string part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) || m < 1 || m > 1440)
                    {
                        return "must be whole minutes between 1 and 1440, separated by commas";
                    }

                    minutes.Add(m);
                }

                c.WarningMinutes = minutes.Distinct().OrderByDescending(m => m).ToList();
                return null;
            }),
        Text("Schedule", "WarningMessage",
            "The warning players see. {action} becomes RestartAction or ShutdownAction, {time} becomes \"5 minutes\" " +
            "or \"1 minute\", and {minutes} the bare number.",
            c => c.WarningMessage, (c, v) => c.WarningMessage = v),
        Text("Schedule", "FinalMessage", "Sent as the server stops.",
            c => c.FinalMessage, (c, v) => c.FinalMessage = v),
        Text("Schedule", "CancelMessage", "Sent when a scheduled stop or restart is cancelled.",
            c => c.CancelMessage, (c, v) => c.CancelMessage = v),
        Text("Schedule", "RestartAction", "The word for {action} when the server will come back.",
            c => c.RestartAction, (c, v) => c.RestartAction = v),
        Text("Schedule", "ShutdownAction", "The word for {action} when the server stays down.",
            c => c.ShutdownAction, (c, v) => c.ShutdownAction = v),

        Flag("Update", "UpdateOnDailyRestart",
            "Update the server with SteamCMD during the daily restart, while it is down. Players' games update " +
            "themselves through Steam, and a server left on an older version turns them away. If SteamCMD is " +
            "missing or fails, the server starts on the version it has.",
            c => c.UpdateOnDailyRestart, (c, v) => c.UpdateOnDailyRestart = v),
        Text("Update", "SteamCmdPath",
            "Where steamcmd is. Empty looks on the PATH, then in C:\\steamcmd on Windows, or ~/steamcmd, " +
            "~/.steam/steamcmd and /usr/games on Linux.",
            c => c.SteamCmdPath, (c, v) => c.SteamCmdPath = v),
        Flag("Update", "Validate",
            "Also check every game file and repair any that differ. Slower. BepInEx, the mods and this tool are " +
            "not Steam's files and are left alone either way.",
            c => c.ValidateOnUpdate, (c, v) => c.ValidateOnUpdate = v),
        Number("Update", "UpdateTimeoutMinutes",
            "How long SteamCMD may take before it is stopped and the server starts on the version it has.",
            1, 240, c => c.UpdateTimeoutMinutes, (c, v) => c.UpdateTimeoutMinutes = v),

        Flag("Crashes", "AutoRestart",
            "Start the server again after a crash. Three crashes within a minute of starting in a row stop the " +
            "retries, since that is a startup problem restarting will not fix.",
            c => c.AutoRestartOnCrash, (c, v) => c.AutoRestartOnCrash = v),
        Number("Crashes", "RestartDelaySeconds", "Seconds to wait before starting again after a crash.",
            0, 3600, c => c.CrashRestartDelaySeconds, (c, v) => c.CrashRestartDelaySeconds = v),

        Text("Backups", "Directory",
            "Where the tool keeps world backups, server logs and crash reports. Relative to this file.",
            c => c.BackupDirectory, (c, v) => c.BackupDirectory = v, required: true),
        Number("Backups", "WorldBackupsToKeep", "Older world backups beyond this many are deleted. 0 keeps them all.",
            0, 100000, c => c.WorldBackupsToKeep, (c, v) => c.WorldBackupsToKeep = v),
        Number("Backups", "ServerLogsToKeep", "Older server logs beyond this many are deleted. 0 keeps them all. Crash reports are never deleted.",
            0, 100000, c => c.ServerLogsToKeep, (c, v) => c.ServerLogsToKeep = v),
        Flag("Backups", "BackupOnStop", "Back up the world after every clean stop. The daily restart always backs up.",
            c => c.BackupOnStop, (c, v) => c.BackupOnStop = v),
        Text("Backups", "ExtraPaths",
            "More files or folders to include in every world backup, separated by semicolons, such as adminlist.txt.",
            c => c.ExtraBackupPaths, (c, v) => c.ExtraBackupPaths = v),

        Text("Tool", "InstanceName",
            "Names the channel that commands such as 'ValheimServerTool status' use to reach this tool. Give each " +
            "server its own when running several on one machine.",
            c => c.InstanceName, (c, v) => c.InstanceName = v, required: true),
        Text("Tool", "ServerDirectory",
            "Folder holding valheim_server. Empty looks next to this file, one folder up, then in Steam's default location.",
            c => c.ServerDirectory, (c, v) => c.ServerDirectory = v),
        Text("Tool", "Executable",
            "Empty runs valheim_server directly. A start script also works if it execs the server, so signals reach it.",
            c => c.Executable, (c, v) => c.Executable = v),
        Text("Tool", "Environment",
            "Extra environment variables for the server, as NAME=value pairs separated by semicolons. On Linux the " +
            "BepInEx doorstop variables are set automatically.",
            c => c.Environment, (c, v) => c.Environment = v),
        Text("Tool", "ControlDirectory",
            "Where the tool drops commands for Server Authority. Must match Control.Directory in its config. Empty " +
            "means ServerAuthority-control next to the server.",
            c => c.ControlDirectory, (c, v) => c.ControlDirectory = v),
        Number("Tool", "ControlAckSeconds",
            "How long Server Authority gets to pick up a shutdown before the tool interrupts the server instead.",
            1, 600, c => c.ControlAckSeconds, (c, v) => c.ControlAckSeconds = v),
        Number("Tool", "StopTimeoutSeconds",
            "How long a stopping server may take to save and exit before it is killed.",
            10, 3600, c => c.StopTimeoutSeconds, (c, v) => c.StopTimeoutSeconds = v),
        Flag("Tool", "EchoServerOutput", "Also print the server's own log in the tool's window.",
            c => c.EchoServerOutput, (c, v) => c.EchoServerOutput = v),
    };

    private static readonly Dictionary<string, string> SectionHeaders = new()
    {
        ["Server"] = "Valheim's own server settings, passed to valheim_server on every start.",
        ["World Modifiers"] = "The difficulty and world modifiers from Valheim's world creation screen. The descriptions are the game's own.",
        ["Schedule"] = "The daily restart and what players are told before the server stops.",
        ["Update"] = "Keeping the server on the same Valheim version as the players, with SteamCMD.",
        ["Crashes"] = "What happens when the server dies without being asked to.",
        ["Backups"] = "The tool's own backups, logs and crash reports.",
        ["Tool"] = "Where things are and how the tool talks to the server. The defaults usually work.",
    };

    public static List<string> Read(ToolConfig config, string path)
    {
        List<string> problems = new();
        Dictionary<string, Setting> byKey = Settings.ToDictionary(s => s.Section + "\n" + s.Key, StringComparer.OrdinalIgnoreCase);
        string section = "";
        int lineNumber = 0;

        foreach (string raw in File.ReadAllLines(path))
        {
            lineNumber++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals < 0)
            {
                problems.Add($"Line {lineNumber}: expected 'Key = value', found '{line}'.");
                continue;
            }

            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();
            if (!byKey.TryGetValue(section + "\n" + key, out Setting? setting))
            {
                Console.Error.WriteLine($"Warning: line {lineNumber}: [{section}] {key} is not a known setting and will be dropped.");
                continue;
            }

            string? error = setting.Set(config, value);
            if (error != null)
            {
                problems.Add($"Line {lineNumber}: [{section}] {key} = {value}: {error}.");
            }
        }

        return problems;
    }

    public static void Write(ToolConfig config, string path)
    {
        ToolConfig defaults = new();
        StringBuilder text = new();
        text.AppendLine("## Valheim server tool settings.");
        text.AppendLine("## Edit this file, save it, and start the tool with: ValheimServerTool run");
        text.AppendLine("## Changes take effect the next time the tool starts. Lines starting with # are comments.");
        text.AppendLine("## The tool rewrites this file when it starts, keeping your values and refreshing these notes.");

        string? current = null;
        foreach (Setting setting in Settings)
        {
            if (setting.Section != current)
            {
                current = setting.Section;
                text.AppendLine();
                text.AppendLine($"[{current}]");
                if (SectionHeaders.TryGetValue(current, out string? header))
                {
                    text.AppendLine();
                    foreach (string line in Wrap(header))
                    {
                        text.AppendLine("## " + line);
                    }
                }
            }

            text.AppendLine();
            foreach (string line in Wrap(setting.Description))
            {
                text.AppendLine("## " + line);
            }

            text.AppendLine($"# Setting type: {setting.Type}");
            text.AppendLine($"# Default value: {setting.Get(defaults)}");
            if (setting.Options != null)
            {
                text.AppendLine($"# Acceptable values: {setting.Options}");
            }

            string value = setting.Get(config);
            text.AppendLine(value.Length == 0 ? $"{setting.Key} =" : $"{setting.Key} = {value}");
        }

        string content = text.ToString();
        if (File.Exists(path) && File.ReadAllText(path) == content)
        {
            return;
        }

        File.WriteAllText(path, content);
    }

    private static Setting Text(string section, string key, string description, Func<ToolConfig, string> get,
        Action<ToolConfig, string> set, bool required = false)
    {
        return new Setting(section, key, "Text", required ? "any text, not empty" : null, description, get, (c, v) =>
        {
            if (required && v.Length == 0)
            {
                return "may not be empty";
            }

            set(c, v);
            return null;
        });
    }

    private static Setting Number(string section, string key, string description, int min, int max,
        Func<ToolConfig, int> get, Action<ToolConfig, int> set)
    {
        return new Setting(section, key, "Whole number", $"{min} to {max}", description,
            c => get(c).ToString(CultureInfo.InvariantCulture),
            (c, v) =>
            {
                if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
                {
                    return $"must be a whole number from {min} to {max}";
                }

                set(c, n);
                return null;
            });
    }

    private static Setting Flag(string section, string key, string description, Func<ToolConfig, bool> get,
        Action<ToolConfig, bool> set)
    {
        return new Setting(section, key, "Boolean", "true, false", description,
            c => get(c) ? "true" : "false",
            (c, v) =>
            {
                if (!bool.TryParse(v, out bool b))
                {
                    return "must be true or false";
                }

                set(c, b);
                return null;
            });
    }

    private static Setting Choice(string section, string key, string description, string[] options,
        Func<ToolConfig, string> get, Action<ToolConfig, string> set)
    {
        return new Setting(section, key, "Choice", string.Join(", ", options), description, get, (c, v) =>
        {
            string? match = options.FirstOrDefault(o => o.Equals(v, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                return $"must be one of {string.Join(", ", options)}";
            }

            set(c, match);
            return null;
        });
    }

    private static IEnumerable<string> Wrap(string text, int width = 100)
    {
        StringBuilder line = new();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }
}
