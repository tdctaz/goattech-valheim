using ServerTool;

string? configPath = null;
List<string> words = new();
for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--config" || args[i] == "-c") && i + 1 < args.Length)
    {
        configPath = args[++i];
    }
    else
    {
        words.Add(args[i]);
    }
}

configPath ??= File.Exists("servertool.cfg")
    ? "servertool.cfg"
    : Path.Combine(AppContext.BaseDirectory, "servertool.cfg");
string fullConfigPath = Path.GetFullPath(configPath);

string verb = words.Count > 0 ? words[0].ToLowerInvariant() : "help";

if (verb is "help" or "-h" or "--help" or "?")
{
    Console.WriteLine("Usage: ValheimServerTool [--config servertool.cfg] <command>");
    Console.WriteLine();
    Console.WriteLine("  run                start the server and keep watching it, until 'quit'");
    Console.WriteLine("                     the first run writes servertool.cfg for you to fill in");
    Console.WriteLine();
    Console.WriteLine("Sent to a running 'run', or typed into its window:");
    Console.WriteLine(Supervisor.Help);
    return 0;
}

if (!File.Exists(fullConfigPath))
{
    if (verb != "run")
    {
        Console.Error.WriteLine($"No config at {fullConfigPath}. Start the tool with 'ValheimServerTool run' to create one.");
        return 1;
    }

    ToolConfig.WriteDefault(fullConfigPath);
    Console.WriteLine($"Wrote a new config to {fullConfigPath}");
    Console.WriteLine();
    Console.WriteLine("Every setting in it is explained, with its default and the values it accepts.");
    Console.WriteLine("At the least, set Name, World and Password under [Server], then run the tool again.");
    return 0;
}

ToolConfig config;
try
{
    config = ToolConfig.Load(fullConfigPath);
}
catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"{fullConfigPath} has problems:");
    Console.Error.WriteLine(e.Message);
    return 1;
}

if (verb == "run")
{
    List<string> problems = config.StartupProblems().ToList();
    if (problems.Count > 0)
    {
        Console.Error.WriteLine($"Not starting. Fix these in {fullConfigPath}:");
        foreach (string problem in problems)
        {
            Console.Error.WriteLine("  " + problem);
        }

        return 1;
    }

    if (await ControlChannel.IsRunningAsync(config))
    {
        Console.Error.WriteLine($"A server tool for instance '{config.InstanceName}' is already running. Send it commands instead, for example: ValheimServerTool status");
        return 1;
    }

    return await new Supervisor(config).RunAsync();
}

return await ControlChannel.SendAsync(config, string.Join(' ', words));
