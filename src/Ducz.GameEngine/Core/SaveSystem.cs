using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ducz;

/// <summary>
/// Simple JSON save/load for game data. Define a plain class with your data and:
/// <code>
/// var data = SaveSystem.Load&lt;SaveData&gt;("slot1") ?? new SaveData();
/// data.Coins += 10;
/// SaveSystem.Save("slot1", data);
/// </code>
/// Files live under <see cref="SaveDirectory"/> (per-user app data by default).
/// </summary>
public static class SaveSystem
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static string? _saveDirectory;

    /// <summary>Folder where saves are written. Defaults to %AppData%/DuczEngine/&lt;game title&gt;.</summary>
    public static string SaveDirectory
    {
        get
        {
            if (_saveDirectory == null)
            {
                var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                _saveDirectory = Path.Combine(appData, "DuczEngine", "Saves");
            }
            return _saveDirectory;
        }
        set => _saveDirectory = value;
    }

    private static string PathFor(string slot) => Path.Combine(SaveDirectory, slot + ".json");

    /// <summary>Serializes and writes data to a save slot.</summary>
    public static void Save<T>(string slot, T data)
    {
        Directory.CreateDirectory(SaveDirectory);
        File.WriteAllText(PathFor(slot), JsonSerializer.Serialize(data, _options));
    }

    /// <summary>Loads a save slot, or returns default when it doesn't exist or fails to parse.</summary>
    public static T? Load<T>(string slot)
    {
        var path = PathFor(slot);
        if (!File.Exists(path))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _options);
        }
        catch (Exception ex)
        {
            Log.Warning($"SaveSystem: failed to load \"{slot}\": {ex.Message}");
            return default;
        }
    }

    /// <summary>True when the slot exists on disk.</summary>
    public static bool Exists(string slot) => File.Exists(PathFor(slot));

    /// <summary>Deletes a save slot.</summary>
    public static void Delete(string slot)
    {
        var path = PathFor(slot);
        if (File.Exists(path))
            File.Delete(path);
    }
}
