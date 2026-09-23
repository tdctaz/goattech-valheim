using System.IO.Pipes;
using System.Threading.Channels;

namespace ServerTool;

public sealed record Request(string Line, TaskCompletionSource<string> Reply);

public static class ControlChannel
{
    private const string EndMarker = "\u0004";

    public static string PipeName(ToolConfig config) => "ValheimServerTool." + config.InstanceName;

    public static async Task ServeAsync(ToolConfig config, ChannelWriter<Request> requests, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream pipe = new(
                PipeName(config), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(token);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                return;
            }

            _ = HandleAsync(pipe, requests, token);
        }
    }

    private static async Task HandleAsync(NamedPipeServerStream pipe, ChannelWriter<Request> requests, CancellationToken token)
    {
        await using (pipe)
        {
            try
            {
                using StreamReader reader = new(pipe, leaveOpen: true);
                await using StreamWriter writer = new(pipe, leaveOpen: true) { AutoFlush = true };
                string? line = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                Request request = new(line, new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
                await requests.WriteAsync(request, token);
                string reply = await request.Reply.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
                await writer.WriteLineAsync(reply);
                await writer.WriteLineAsync(EndMarker);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or TimeoutException)
            {
            }
        }
    }

    public static async Task<bool> IsRunningAsync(ToolConfig config)
    {
        await using NamedPipeClientStream pipe = new(".", PipeName(config), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(500);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            return false;
        }
    }

    public static async Task<int> SendAsync(ToolConfig config, string line)
    {
        await using NamedPipeClientStream pipe = new(".", PipeName(config), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(3000);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine($"No server tool is running for instance '{config.InstanceName}'. Start it with: ValheimServerTool run");
            return 2;
        }

        using StreamReader reader = new(pipe, leaveOpen: true);
        await using StreamWriter writer = new(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(line);

        string? reply;
        while ((reply = await reader.ReadLineAsync()) != null && reply != EndMarker)
        {
            Console.WriteLine(reply);
        }

        return 0;
    }
}
