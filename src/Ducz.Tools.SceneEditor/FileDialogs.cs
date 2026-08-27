using System.Diagnostics;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// Native "open file" dialog. On Windows it shells out to a small PowerShell
/// command that shows the standard OpenFileDialog (no extra dependencies); other
/// platforms return nothing and the user types/drops the path instead. The dialog
/// runs on a background task so it never blocks the game loop - the callback runs
/// on that background thread, marshal back to the game loop yourself.
/// </summary>
public static class FileDialogs
{
    public const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.gif|All files|*.*";
    public const string ModelFilter = "3D models|*.glb;*.gltf;*.fbx;*.obj;*.dae;*.stl;*.3ds;*.ply|All files|*.*";

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Shows the dialog; invokes <paramref name="onPicked"/> with the chosen file (nothing when cancelled).</summary>
    public static void OpenFileAsync(string title, string filter, string? initialDirectory, Action<string> onPicked)
    {
        if (!IsSupported)
        {
            Log.Warning("File dialogs are only available on Windows; type or drop the path instead.");
            return;
        }

        Task.Run(() =>
        {
            try
            {
                string? path = ShowWindowsDialog(title, filter, initialDirectory);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    onPicked(path);
            }
            catch (Exception ex)
            {
                Log.Warning($"File dialog failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Shows a native "save as" dialog; invokes <paramref name="onPicked"/> with the chosen path
    /// (extension defaulted from the filter) or <paramref name="onCancelled"/>.
    /// </summary>
    public static void SaveFileAsync(string title, string filter, string? initialDirectory, string defaultFileName,
        Action<string> onPicked, Action? onCancelled = null)
    {
        if (!IsSupported)
        {
            Log.Warning("File dialogs are only available on Windows.");
            onCancelled?.Invoke();
            return;
        }

        Task.Run(() =>
        {
            try
            {
                string? path = ShowWindowsSaveDialog(title, filter, initialDirectory, defaultFileName);
                if (!string.IsNullOrEmpty(path))
                    onPicked(path);
                else
                    onCancelled?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warning($"Save dialog failed: {ex.Message}");
                onCancelled?.Invoke();
            }
        });
    }

    private static string? ShowWindowsSaveDialog(string title, string filter, string? initialDirectory, string defaultFileName)
    {
        static string Escape(string s) => s.Replace("'", "''");
        string script =
            "Add-Type -AssemblyName System.Windows.Forms;" +
            "$d = New-Object System.Windows.Forms.SaveFileDialog;" +
            $"$d.Title = '{Escape(title)}';" +
            $"$d.Filter = '{Escape(filter)}';" +
            $"$d.FileName = '{Escape(defaultFileName)}';" +
            "$d.AddExtension = $true; $d.OverwritePrompt = $true;" +
            (string.IsNullOrEmpty(initialDirectory) ? "" : $"$d.InitialDirectory = '{Escape(initialDirectory)}';") +
            "$owner = New-Object System.Windows.Forms.Form; $owner.TopMost = $true;" +
            "if ($d.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.FileName) }";
        return RunDialogScript(script);
    }

    private static string? ShowWindowsDialog(string title, string filter, string? initialDirectory)
    {
        static string Escape(string s) => s.Replace("'", "''");
        string script =
            "Add-Type -AssemblyName System.Windows.Forms;" +
            "$d = New-Object System.Windows.Forms.OpenFileDialog;" +
            $"$d.Title = '{Escape(title)}';" +
            $"$d.Filter = '{Escape(filter)}';" +
            "$d.Multiselect = $false;" +
            (string.IsNullOrEmpty(initialDirectory) ? "" : $"$d.InitialDirectory = '{Escape(initialDirectory)}';") +
            "$owner = New-Object System.Windows.Forms.Form; $owner.TopMost = $true;" +
            "if ($d.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.FileName) }";
        return RunDialogScript(script);
    }

    private static string? RunDialogScript(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -STA -WindowStyle Hidden -Command \"{script}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return null;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Trim();
    }
}
