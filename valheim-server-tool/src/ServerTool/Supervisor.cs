using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace ServerTool;

public enum StopKind
{
    Stop,
    Restart,
    Quit,
}

public sealed class Supervisor
{
    private enum State
    {
        Stopped,
        Running,
        Stopping,
    }

    private sealed class Countdown
    {
        public required DateTime At { get; init; }
        public required StopKind Kind { get; init; }
        public required string Reason { get; init; }
        public HashSet<int> Sent { get; } = new();
    }

    private const int FastFailureSeconds = 60;
    private const int FastFailureLimit = 3;

    private readonly ToolConfig _config;
    private readonly Channel<Request> _requests = Channel.CreateUnbounded<Request>();

    private State _state = State.Stopped;
    private ServerProcess? _server;
    private Countdown? _countdown;
    private StopKind _stopKind;
    private DateTime _stopStarted;
    private string? _shutdownCommand;
    private bool _interruptSent;
    private bool _killSent;
    private DateTime? _nextDaily;
    private DateTime? _crashRestartAt;
    private int _fastFailures;
    private string? _lastCrash;
    private int _commandSequence;
    private bool _exit;
    private volatile bool _userInterrupted;
    private long _ignoreCtrlCUntil;

    public Supervisor(ToolConfig config)
    {
        _config = config;
    }

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_config.BackupDir);
        Log.OpenFile(Path.Combine(_config.BackupDir, "servertool.log"));
        Log.Info($"Valheim server tool, config {_config.ConfigPath}");
        Log.Info($"  server  : {_config.ExecutablePath()}");
        Log.Info($"  world   : {_config.Server.World} in {_config.SaveDir()}");
        Log.Info($"  backups : {_config.BackupDir}");

        foreach (string problem in Validate())
        {
            Log.Warn(problem);
        }

        using CancellationTokenSource shutdown = new();
        Task pipe = ControlChannel.ServeAsync(_config, _requests.Writer, shutdown.Token);
        Console.CancelKeyPress += OnCancelKeyPress;
        using PosixSignalRegistration? term = OperatingSystem.IsWindows()
            ? null
            : PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnTerminate);
        StartConsoleReader();

        ScheduleNextDaily(DateTime.Now + MaxWarning());
        Log.Info(_nextDaily.HasValue ? $"Daily restart at {_nextDaily:HH:mm}, next {_nextDaily:yyyy-MM-dd HH:mm}." : "Daily restart is off.");
        Log.Info("Type 'help' for commands.");
        StartServer();

        while (!_exit)
        {
            while (_requests.Reader.TryRead(out Request? request))
            {
                string reply;
                try
                {
                    reply = Handle(request.Line);
                }
                catch (Exception e)
                {
                    reply = $"Failed: {e.Message}";
                    Log.Error($"Command '{request.Line}' failed: {e}");
                }

                request.Reply.TrySetResult(reply);
            }

            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Log.Error($"Supervisor tick failed: {e}");
            }

            if (_exit)
            {
                break;
            }

            using CancellationTokenSource wait = new(TimeSpan.FromSeconds(1));
            try
            {
                await _requests.Reader.WaitToReadAsync(wait.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        shutdown.Cancel();
        await pipe;
        Console.CancelKeyPress -= OnCancelKeyPress;
        Log.Info("Server tool stopped.");
        return 0;
    }

    private IEnumerable<string> Validate()
    {
        if (!File.Exists(_config.ExecutablePath()))
        {
            yield return $"Server executable not found: {_config.ExecutablePath()}";
        }

        if (_config.Server.Password.Length is > 0 and < 5)
        {
            yield return "The server password must be at least 5 characters, or Valheim refuses to start.";
        }

        if (_config.Server.Password.Length > 0 && _config.Server.Name.Contains(_config.Server.Password, StringComparison.OrdinalIgnoreCase))
        {
            yield return "The server password may not appear in the server name, or Valheim refuses to start.";
        }

        if (!File.Exists(Path.Combine(_config.ServerDir, "BepInEx", "plugins", "ServerAuthority.dll")))
        {
            yield return "ServerAuthority.dll is not installed, so player warnings cannot be sent and stopping falls back to an interrupt signal.";
        }
    }

    private void Tick()
    {
        DateTime now = DateTime.Now;

        if (_server != null && _server.HasExited)
        {
            OnServerExited(now);
        }

        CheckDailyRestart(now);

        switch (_state)
        {
            case State.Running:
                TickCountdown(now);
                break;
            case State.Stopping:
                TickStopping(now);
                break;
            case State.Stopped:
                if (_crashRestartAt.HasValue && now >= _crashRestartAt.Value)
                {
                    _crashRestartAt = null;
                    Log.Info("Restarting after crash.");
                    StartServer();
                }

                break;
        }
    }

    private string StartServer()
    {
        if (_server != null)
        {
            return $"The server is already {(_state == State.Stopping ? "stopping" : "running")}.";
        }

        ClearControlDirectory();
        string logPath = Path.Combine(Backups.LogsDir(_config), $"server-{Backups.Stamp()}.log");
        try
        {
            _server = ServerProcess.Start(_config, logPath);
        }
        catch (Exception e)
        {
            Log.Error($"Could not start the server: {e.Message}");
            _state = State.Stopped;
            return $"Could not start the server: {e.Message}";
        }

        _state = State.Running;
        _userInterrupted = false;
        _countdown = null;
        Log.Good($"Server started, pid {_server.Id}, logging to {logPath}");
        return $"Server started, pid {_server.Id}.";
    }

    private void OnServerExited(DateTime now)
    {
        ServerProcess server = _server!;
        _server = null;
        server.Dispose();

        int code = server.ExitCode;
        TimeSpan ranFor = now - server.StartedAt;
        bool expected = _state == State.Stopping || _userInterrupted;
        if (_userInterrupted && _state != State.Stopping)
        {
            _stopKind = StopKind.Quit;
        }

        _state = State.Stopped;
        _countdown = null;
        DeleteShutdownCommand();

        if (expected)
        {
            Log.Good($"Server stopped with exit code {code} after {Format(ranFor)}.");
            if (_config.BackupOnStop || _stopKind == StopKind.Restart)
            {
                TryBackup();
            }

            Backups.PruneServerLogs(_config);

            if (_stopKind == StopKind.Restart)
            {
                StartServer();
            }
            else if (_stopKind == StopKind.Quit)
            {
                _exit = true;
            }

            return;
        }

        Log.Error($"SERVER CRASHED: exit code {code} after {Format(ranFor)}.");
        try
        {
            string dir = Backups.SaveCrash(_config, server, code, ranFor);
            _lastCrash = $"{now:yyyy-MM-dd HH:mm:ss}, exit code {code}, evidence in {dir}";
            Log.Error($"Crash evidence saved to {dir}");
        }
        catch (Exception e)
        {
            _lastCrash = $"{now:yyyy-MM-dd HH:mm:ss}, exit code {code}, evidence not saved";
            Log.Error($"Saving the crash evidence failed: {e}");
        }

        Backups.PruneServerLogs(_config);

        _fastFailures = ranFor.TotalSeconds < FastFailureSeconds ? _fastFailures + 1 : 0;
        if (!_config.AutoRestartOnCrash)
        {
            Log.Warn("Automatic restart is off. Use 'start' to start the server again.");
            return;
        }

        if (_fastFailures >= FastFailureLimit)
        {
            Log.Error(
                $"The server died within {FastFailureSeconds}s of starting {FastFailureLimit} times in a row, so it is failing " +
                "to start rather than crashing in play. Not restarting. Check the newest log, fix the cause, then use 'start'.");
            return;
        }

        _crashRestartAt = now.AddSeconds(_config.CrashRestartDelaySeconds);
        Log.Info($"Restarting in {_config.CrashRestartDelaySeconds} seconds.");
    }

    private void CheckDailyRestart(DateTime now)
    {
        if (!_nextDaily.HasValue || now < _nextDaily.Value - MaxWarning())
        {
            return;
        }

        DateTime at = _nextDaily.Value;
        ScheduleNextDaily(at);

        if (_state != State.Running)
        {
            Log.Info($"Skipping the daily restart at {at:HH:mm}, the server is not running.");
            return;
        }

        if (_countdown != null)
        {
            Log.Info($"Skipping the daily restart at {at:HH:mm}, a {Describe(_countdown.Kind)} is already scheduled.");
            return;
        }

        _countdown = new Countdown { At = at, Kind = StopKind.Restart, Reason = "daily restart" };
        Log.Info($"Daily restart at {at:HH:mm}, warning players.");
    }

    private void ScheduleNextDaily(DateTime after)
    {
        TimeOnly? time = _config.DailyRestart();
        if (!time.HasValue)
        {
            _nextDaily = null;
            return;
        }

        DateTime candidate = after.Date + time.Value.ToTimeSpan();
        while (candidate <= after)
        {
            candidate = candidate.AddDays(1);
        }

        _nextDaily = candidate;
    }

    private void TickCountdown(DateTime now)
    {
        if (_countdown == null)
        {
            return;
        }

        TimeSpan remaining = _countdown.At - now;
        if (remaining <= TimeSpan.Zero)
        {
            StopKind kind = _countdown.Kind;
            _countdown = null;
            Say(Render(_config.FinalMessage, kind, 0));
            BeginStop(kind);
            return;
        }

        List<int> due = _config.WarningMinutes
            .Where(m => m > 0 && !_countdown.Sent.Contains(m) && remaining <= TimeSpan.FromMinutes(m))
            .ToList();
        if (due.Count == 0)
        {
            return;
        }

        _countdown.Sent.UnionWith(due);
        Say(Render(_config.WarningMessage, _countdown.Kind, due.Min()));
    }

    private void BeginStop(StopKind kind)
    {
        _state = State.Stopping;
        _stopKind = kind;
        _stopStarted = DateTime.Now;
        _interruptSent = false;
        _killSent = false;
        Log.Info($"Stopping the server ({Describe(kind)}), saving the world.");
        _shutdownCommand = WriteCommand("shutdown");
    }

    private void TickStopping(DateTime now)
    {
        if (_server == null)
        {
            return;
        }

        TimeSpan elapsed = now - _stopStarted;
        bool unanswered = _shutdownCommand == null
            || (File.Exists(_shutdownCommand) && elapsed.TotalSeconds >= _config.ControlAckSeconds);
        if (!_interruptSent && unanswered)
        {
            Log.Warn("Server Authority did not pick up the shutdown command, sending an interrupt instead.");
            DeleteShutdownCommand();
            Interlocked.Exchange(ref _ignoreCtrlCUntil, DateTime.UtcNow.AddSeconds(10).Ticks);
            if (!_server.Interrupt())
            {
                Log.Warn("Sending the interrupt failed.");
            }

            _interruptSent = true;
        }

        if (!_killSent && elapsed.TotalSeconds >= _config.StopTimeoutSeconds)
        {
            Log.Error($"The server did not exit within {_config.StopTimeoutSeconds}s, killing it. The world may not have been saved.");
            _server.Kill();
            _killSent = true;
        }
    }

    private string Handle(string line)
    {
        string[] words = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "";
        }

        string verb = words[0].ToLowerInvariant();
        string rest = words.Length > 1 ? words[1].Trim() : "";
        bool now = rest.Equals("--now", StringComparison.OrdinalIgnoreCase) || rest.Equals("now", StringComparison.OrdinalIgnoreCase);

        switch (verb)
        {
            case "start":
                _crashRestartAt = null;
                _fastFailures = 0;
                return StartServer();
            case "stop":
                return RequestStop(StopKind.Stop, now);
            case "restart":
                return RequestStop(StopKind.Restart, now);
            case "quit":
            case "exit":
                return RequestStop(StopKind.Quit, now);
            case "cancel":
                return CancelCountdown();
            case "say":
                if (rest.Length == 0)
                {
                    return "Usage: say <message>";
                }

                if (_state != State.Running)
                {
                    return "The server is not running.";
                }

                Say(rest);
                return $"Sent: {rest}";
            case "backup":
                return TryBackup() is string path ? $"Backup written to {path}" : "Backup failed, see the log.";
            case "status":
                return Status();
            case "help":
            case "?":
                return Help;
            default:
                return $"Unknown command '{verb}'. Type 'help'.";
        }
    }

    private string RequestStop(StopKind kind, bool now)
    {
        switch (_state)
        {
            case State.Stopped:
                _crashRestartAt = null;
                if (kind == StopKind.Quit)
                {
                    _exit = true;
                    return "Server tool exiting.";
                }

                if (kind == StopKind.Restart)
                {
                    _fastFailures = 0;
                    return StartServer();
                }

                return "The server is already stopped.";
            case State.Stopping:
                if (kind == StopKind.Quit)
                {
                    _stopKind = StopKind.Quit;
                    return "The server is already stopping, the tool will exit afterwards.";
                }

                return "The server is already stopping.";
        }

        int minutes = _config.WarningMinutes.Where(m => m > 0).DefaultIfEmpty(0).Max();
        if (now || minutes == 0)
        {
            _countdown = null;
            Say(Render(_config.FinalMessage, kind, 0));
            BeginStop(kind);
            return $"Server {Describe(kind)} now.";
        }

        Countdown? existing = _countdown;
        DateTime at = DateTime.Now.AddMinutes(minutes);
        if (existing != null && existing.At < at)
        {
            at = existing.At;
        }

        _countdown = new Countdown { At = at, Kind = kind, Reason = "requested" };
        if (existing != null && existing.At == at)
        {
            _countdown.Sent.UnionWith(existing.Sent);
        }

        Log.Info($"Scheduled {Describe(kind)} at {at:HH:mm:ss}.");
        return $"Server {Describe(kind)} at {at:HH:mm:ss}, warning players. Use 'cancel' to abort or add --now to skip the warnings.";
    }

    private string CancelCountdown()
    {
        if (_countdown == null)
        {
            return "Nothing is scheduled.";
        }

        StopKind kind = _countdown.Kind;
        _countdown = null;
        Say($"Server {Describe(kind)} cancelled");
        Log.Info($"Cancelled the scheduled {Describe(kind)}.");
        return $"Cancelled the {Describe(kind)}.";
    }

    private string? TryBackup()
    {
        try
        {
            string path = Backups.BackupWorld(_config);
            Log.Good($"World backup written to {path}{(_state == State.Running ? ", from the last world save" : "")}.");
            return path;
        }
        catch (Exception e)
        {
            Log.Error($"World backup failed: {e.Message}");
            return null;
        }
    }

    private void Say(string message)
    {
        if (_state != State.Running || message.Length == 0)
        {
            return;
        }

        Log.Info($"Telling players: {message}");
        WriteCommand("say " + message.Replace('\n', ' ').Replace('\r', ' '));
    }

    private string? WriteCommand(string command)
    {
        try
        {
            string dir = _config.ControlDir();
            Directory.CreateDirectory(dir);
            string name = $"{DateTime.UtcNow.Ticks:D19}-{++_commandSequence:D6}";
            string temp = Path.Combine(dir, name + ".tmp");
            string path = Path.Combine(dir, name + ".cmd");
            File.WriteAllText(temp, command);
            File.Move(temp, path);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not write the control command '{command}': {e.Message}");
            return null;
        }
    }

    private void ClearControlDirectory()
    {
        string dir = _config.ControlDir();
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (string file in Directory.GetFiles(dir, "*.cmd").Concat(Directory.GetFiles(dir, "*.tmp")))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    private void DeleteShutdownCommand()
    {
        if (_shutdownCommand == null)
        {
            return;
        }

        try
        {
            File.Delete(_shutdownCommand);
        }
        catch (IOException)
        {
        }

        _shutdownCommand = null;
    }

    private string Status()
    {
        List<string> lines = new();
        DateTime now = DateTime.Now;
        switch (_state)
        {
            case State.Running:
                lines.Add($"Server running, pid {_server!.Id}, up {Format(now - _server.StartedAt)}.");
                break;
            case State.Stopping:
                lines.Add($"Server stopping ({Describe(_stopKind)}) for {Format(now - _stopStarted)}.");
                break;
            default:
                lines.Add(_crashRestartAt.HasValue
                    ? $"Server stopped, restarting after a crash at {_crashRestartAt:HH:mm:ss}."
                    : "Server stopped.");
                break;
        }

        if (_countdown != null)
        {
            lines.Add($"Scheduled {Describe(_countdown.Kind)} ({_countdown.Reason}) at {_countdown.At:HH:mm:ss}.");
        }

        lines.Add(_nextDaily.HasValue ? $"Next daily restart {_nextDaily:yyyy-MM-dd HH:mm}." : "Daily restart is off.");
        lines.Add($"Last crash: {_lastCrash ?? "none since the tool started"}.");
        return string.Join(System.Environment.NewLine, lines);
    }

    private string Render(string template, StopKind kind, int minutes)
    {
        return template
            .Replace("{action}", ActionWord(kind))
            .Replace("{minutes}", minutes.ToString())
            .Replace("{time}", minutes == 1 ? "1 minute" : $"{minutes} minutes");
    }

    private static string ActionWord(StopKind kind) => kind == StopKind.Restart ? "restarting" : "shutting down";

    private static string Describe(StopKind kind) => kind switch
    {
        StopKind.Restart => "restart",
        StopKind.Quit => "shutdown and exit",
        _ => "shutdown",
    };

    private TimeSpan MaxWarning() => TimeSpan.FromMinutes(_config.WarningMinutes.Where(m => m > 0).DefaultIfEmpty(0).Max());

    private static string Format(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span:hh\\:mm\\:ss}" : span.ToString(@"hh\:mm\:ss");

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _ignoreCtrlCUntil))
        {
            return;
        }

        _userInterrupted = true;
        Log.Warn("Interrupted, stopping the server now and exiting.");
        _requests.Writer.TryWrite(new Request("quit --now", new TaskCompletionSource<string>()));
    }

    private void OnTerminate(PosixSignalContext context)
    {
        context.Cancel = true;
        Log.Warn("Terminated, stopping the server now and exiting.");
        _requests.Writer.TryWrite(new Request("quit --now", new TaskCompletionSource<string>()));
    }

    private void StartConsoleReader()
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        Thread reader = new(() =>
        {
            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Request request = new(line, new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
                _requests.Writer.TryWrite(request);
                Console.WriteLine(request.Reply.Task.GetAwaiter().GetResult());
            }
        })
        {
            IsBackground = true,
            Name = "console",
        };
        reader.Start();
    }

    public const string Help =
        "Commands:\n" +
        "  start              start the server\n" +
        "  stop [--now]       warn players, stop the server and back up the world\n" +
        "  restart [--now]    warn players, stop, back up and start again\n" +
        "  quit [--now]       like stop, then exit the tool\n" +
        "  cancel             cancel a scheduled stop or restart\n" +
        "  say <message>      show a message to every player\n" +
        "  backup             back up the world as last saved\n" +
        "  status             show the server state and schedule";
}
