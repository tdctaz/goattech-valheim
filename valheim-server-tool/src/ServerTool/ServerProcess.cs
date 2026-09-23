using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ServerTool;

public sealed class ServerProcess : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _log;
    private readonly object _logLock = new();
    private readonly bool _echo;

    public string LogPath { get; }
    public DateTime StartedAt { get; }
    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;
    public int ExitCode { get; private set; }

    private ServerProcess(Process process, StreamWriter log, string logPath, bool echo)
    {
        _process = process;
        _log = log;
        _echo = echo;
        LogPath = logPath;
        StartedAt = DateTime.Now;
    }

    public static ServerProcess Start(ToolConfig config, string logPath)
    {
        string exe = config.ExecutablePath();
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"Server executable not found: {exe}");
        }

        ProcessStartInfo info = new(exe)
        {
            WorkingDirectory = config.ServerDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };

        foreach (string arg in config.ServerArguments())
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["SteamAppId"] = "892970";
        if (!OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(config.Executable))
        {
            AddDoorstop(info, config.ServerDir);
        }

        foreach ((string key, string value) in config.EnvironmentVariables())
        {
            info.Environment[key] = value;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        StreamWriter log = new(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };

        Process process = new() { StartInfo = info, EnableRaisingEvents = true };
        ServerProcess server = new(process, log, logPath, config.EchoServerOutput);
        process.OutputDataReceived += (_, e) => server.Write(e.Data);
        process.ErrorDataReceived += (_, e) => server.Write(e.Data);

        try
        {
            process.Start();
        }
        catch
        {
            log.Dispose();
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return server;
    }

    private static void AddDoorstop(ProcessStartInfo info, string serverDir)
    {
        string libs = Path.Combine(serverDir, "doorstop_libs");
        if (!Directory.Exists(libs))
        {
            return;
        }

        string Existing(string name) => info.Environment.TryGetValue(name, out string? v) && !string.IsNullOrEmpty(v) ? ":" + v : "";

        info.Environment["DOORSTOP_ENABLED"] = "1";
        info.Environment["DOORSTOP_TARGET_ASSEMBLY"] = Path.Combine(serverDir, "BepInEx", "core", "BepInEx.Preloader.dll");
        info.Environment["LD_LIBRARY_PATH"] = libs + ":" + Path.Combine(serverDir, "linux64") + Existing("LD_LIBRARY_PATH");
        info.Environment["LD_PRELOAD"] = "libdoorstop_x64.so" + Existing("LD_PRELOAD");
    }

    private void Write(string? line)
    {
        if (line == null)
        {
            return;
        }

        lock (_logLock)
        {
            try
            {
                _log.WriteLine(line);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (_echo)
        {
            Console.WriteLine(line);
        }
    }

    public bool Interrupt()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return Native.GenerateConsoleCtrlEvent(0, 0);
            }

            return Native.kill(_process.Id, 2) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            return;
        }

        _process.WaitForExit();
        ExitCode = _process.ExitCode;
        lock (_logLock)
        {
            _log.Dispose();
        }

        _process.Dispose();
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

        [DllImport("libc", SetLastError = true)]
        public static extern int kill(int pid, int sig);
    }
}
