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

configPath ??= File.Exists("servertool.json")
    ? "servertool.json"
    : Path.Combine(AppContext.BaseDirectory, "servertool.json");

string verb = words.Count > 0 ? words[0].ToLowerInvariant() : "help";

if (verb is "help" or "-h" or "--help" or "?")
{
    Console.WriteLine("Usage: ValheimServerTool [--config servertool.json] <command>");
    Console.WriteLine();
    Console.WriteLine("  init               write a default servertool.json to edit");
    Console.WriteLine("  run                start the server and keep watching it, until 'quit'");
    Console.WriteLine();
    Console.WriteLine("Sent to a running 'run', or typed into its window:");
    Console.WriteLine(Supervisor.Help);
    return 0;
}

if (verb == "init")
{
    if (File.Exists(configPath))
    {
        Console.Error.WriteLine($"{Path.GetFullPath(configPath)} already exists.");
        return 1;
    }

    ToolConfig.WriteDefault(configPath);
    Console.WriteLine($"Wrote {Path.GetFullPath(configPath)}. Edit it, then run: ValheimServerTool run");
    return 0;
}

if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"No config at {Path.GetFullPath(configPath)}. Create one with: ValheimServerTool init");
    return 1;
}

ToolConfig config;
try
{
    config = ToolConfig.Load(configPath);
}
catch (Exception e)
{
    Console.Error.WriteLine($"Could not read {Path.GetFullPath(configPath)}: {e.Message}");
    return 1;
}

if (verb == "run")
{
    if (await ControlChannel.IsRunningAsync(config))
    {
        Console.Error.WriteLine($"A server tool for instance '{config.InstanceName}' is already running. Send it commands instead, for example: ValheimServerTool status");
        return 1;
    }

    return await new Supervisor(config).RunAsync();
}

return await ControlChannel.SendAsync(config, string.Join(' ', words));
