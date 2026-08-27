namespace Ducz;

/// <summary>Severity of a log message.</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// Simple engine-wide logger. Writes to the console by default;
/// subscribe to <see cref="OnMessage"/> to route messages elsewhere.
/// </summary>
public static class Log
{
    /// <summary>Minimum level that gets written. Defaults to <see cref="LogLevel.Info"/>.</summary>
    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    /// <summary>Raised for every message at or above <see cref="MinLevel"/>.</summary>
    public static event Action<LogLevel, string>? OnMessage;

    private static StreamWriter? _file;
    private static readonly object FileLock = new();

    /// <summary>Path of the log file when file logging is enabled.</summary>
    public static string? FilePath { get; private set; }

    /// <summary>
    /// Also appends every message to a file - what the tools use since they run without a
    /// console window. The file is truncated at start-up; a "-- session --" line marks each run.
    /// </summary>
    public static void EnableFile(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            lock (FileLock)
            {
                _file?.Dispose();
                _file = new StreamWriter(path, append: false) { AutoFlush = true };
                _file.WriteLine($"-- session {DateTime.Now:yyyy-MM-dd HH:mm:ss} --");
            }
            FilePath = path;
        }
        catch (Exception ex)
        {
            Warning($"Could not open log file {path}: {ex.Message}");
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    private static void Write(LogLevel level, string message)
    {
        if (level < MinLevel)
            return;

        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        try
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                LogLevel.Debug => ConsoleColor.DarkGray,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => previous
            };
            Console.WriteLine(line);
            Console.ForegroundColor = previous;
        }
        catch
        {
            // No console attached (WinExe tools): the file sink below still gets the line.
        }

        if (_file != null)
        {
            lock (FileLock)
            {
                try { _file.WriteLine(line); } catch { /* disk full etc. - never crash on logging */ }
            }
        }

        OnMessage?.Invoke(level, message);
    }
}
