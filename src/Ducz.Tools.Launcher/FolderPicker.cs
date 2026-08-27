using System.Diagnostics;

namespace Ducz.Tools.Launcher;

/// <summary>
/// Opens a native "choose folder" dialog. On Windows it shells out to a small
/// PowerShell command that shows the standard folder browser (no extra project
/// dependencies); other platforms return null and the user types the path.
/// The dialog runs on a background task so it never blocks the game loop.
/// </summary>
public static class FolderPicker
{
    /// <summary>
    /// Shows the folder dialog and invokes <paramref name="onPicked"/> with the
    /// chosen path (or does nothing if cancelled). The callback runs on the
    /// background task thread - marshal back to the game loop yourself.
    /// </summary>
    public static void PickAsync(string? initialDirectory, Action<string> onPicked)
    {
        if (!OperatingSystem.IsWindows())
        {
            Log.Warning("Folder picker is only available on Windows; type the location instead.");
            return;
        }

        Task.Run(() =>
        {
            try
            {
                string? path = ShowWindowsDialog(initialDirectory);
                if (!string.IsNullOrEmpty(path))
                    onPicked(path);
            }
            catch (Exception ex)
            {
                Log.Warning($"Folder picker failed: {ex.Message}");
            }
        });
    }

    private static string? ShowWindowsDialog(string? initialDirectory)
    {
        // A self-contained PowerShell script: shows FolderBrowserDialog, prints the path.
        string start = (initialDirectory ?? "").Replace("'", "''");
        string script =
            "Add-Type -AssemblyName System.Windows.Forms;" +
            "$d = New-Object System.Windows.Forms.FolderBrowserDialog;" +
            "$d.Description = 'Choose where to create the project';" +
            "$d.ShowNewFolderButton = $true;" +
            $"$d.SelectedPath = '{start}';" +
            "$owner = New-Object System.Windows.Forms.Form; $owner.TopMost = $true;" +
            "if ($d.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.SelectedPath) }";

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
