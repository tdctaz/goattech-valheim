namespace ServerTool;

public static class Log
{
    private static readonly object Lock = new();
    private static StreamWriter? _file;

    public static void OpenFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);

    public static void Error(string message) => Write("ERROR", message, ConsoleColor.Red);

    public static void Good(string message) => Write("INFO", message, ConsoleColor.Green);

    private static void Write(string level, string message, ConsoleColor? color)
    {
        DateTime now = DateTime.Now;
        lock (Lock)
        {
            if (color.HasValue)
            {
                Console.ForegroundColor = color.Value;
            }

            Console.WriteLine($"[{now:HH:mm:ss}] {message}");
            if (color.HasValue)
            {
                Console.ResetColor();
            }

            _file?.WriteLine($"{now:yyyy-MM-dd HH:mm:ss} {level,-5} {message}");
        }
    }
}
