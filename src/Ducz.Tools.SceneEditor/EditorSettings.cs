using System.Text.Json;

namespace Ducz.Tools.SceneEditor;

/// <summary>Per-user map builder preferences, stored in %AppData%/DuczEngine/mapbuilder.json.</summary>
public sealed class EditorSettings
{
    /// <summary>Folder chosen in the last "Export GLB" dialog.</summary>
    public string? LastGlbExportDirectory { get; set; }

    /// <summary>Export scale chosen last time (1 = meters).</summary>
    public float GlbScale { get; set; } = 1f;
    public bool GlbMergeByMaterial { get; set; }
    public bool GlbGodotSuffixes { get; set; } = true;
    public bool GlbIncludeModels { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuczEngine", "mapbuilder.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static EditorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath), Options) ?? new EditorSettings();
        }
        catch (Exception ex)
        {
            Log.Warning($"Editor settings unreadable, using defaults: {ex.Message}");
        }
        return new EditorSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Log.Warning($"Could not save editor settings: {ex.Message}");
        }
    }
}
