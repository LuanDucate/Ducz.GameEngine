using System.Runtime.InteropServices;

namespace Ducz;

/// <summary>
/// Last-resort handling for unhandled exceptions in shipped tools/games (which run without a
/// console): writes the crash to the log and shows a native message box on Windows pointing
/// to the log file. Call <see cref="Install"/> once at startup.
/// </summary>
public static class CrashReporter
{
    private static bool _installed;

    /// <summary>
    /// Runs just before the crash message is shown, so a tool can save whatever the user had
    /// open. Return a line to append to the message (e.g. where the rescued file went).
    /// </summary>
    public static Func<string?>? RescueHandler { get; set; }

    /// <summary>Hooks AppDomain / task unhandled exceptions.</summary>
    /// <param name="productName">Shown in the message box title.</param>
    public static void Install(string productName)
    {
        if (_installed)
            return;
        _installed = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(productName, e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error"));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };
    }

    /// <summary>Logs the exception and shows the message box (Windows) - does not exit.</summary>
    public static void Report(string productName, Exception exception)
    {
        Log.Error($"FATAL: {exception}");

        string rescued = "";
        try
        {
            if (RescueHandler?.Invoke() is { Length: > 0 } note)
            {
                rescued = $"\n\n{note}";
                Log.Info(note);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Rescue handler failed: {ex}");
        }

        string logHint = Log.FilePath != null ? $"\n\nDetails were written to:\n{Log.FilePath}" : "";
        string message = $"{productName} ran into a problem and needs to close.\n\n{exception.GetType().Name}: {exception.Message}{rescued}{logHint}";
        if (OperatingSystem.IsWindows())
        {
            try { MessageBoxW(IntPtr.Zero, message, productName, 0x00000010 /* MB_ICONERROR */ | 0x00040000 /* MB_TOPMOST */); }
            catch { /* no UI available */ }
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }

    /// <summary>Shows a native message box (Windows) or logs the text elsewhere. Use sparingly - for failures the user must not miss.</summary>
    public static void ShowMessage(string title, string text, bool isError = true)
    {
        if (OperatingSystem.IsWindows())
        {
            try { MessageBoxW(IntPtr.Zero, text, title, (isError ? 0x00000010u : 0x00000040u) | 0x00040000u); return; }
            catch { /* fall through */ }
        }
        if (isError) Log.Error($"{title}: {text}"); else Log.Info($"{title}: {text}");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
