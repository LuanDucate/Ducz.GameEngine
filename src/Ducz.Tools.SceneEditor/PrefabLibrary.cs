using System.Text.Json;
using System.Text.Json.Serialization;
using Ducz.Serialization;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// A ready-made piece of map: a whole house, a street segment, a tree, a car - stored as one
/// group node plus the materials it needs. Dropping one in places dozens of blocks at once,
/// already assembled and in proportion, which is what makes a hand-built map look good.
/// </summary>
public sealed class PrefabDef
{
    /// <summary>Shown on the palette button.</summary>
    public string Name { get; set; } = "Prefab";

    /// <summary>Groups the browser by tab: "Houses", "Streets", "Nature"...</summary>
    public string Category { get; set; } = "Geral";

    /// <summary>One line of help shown under the grid.</summary>
    public string? Description { get; set; }

    /// <summary>Materials the prefab needs; merged into the map when it is placed.</summary>
    public Dictionary<string, MaterialDef>? Materials { get; set; }

    /// <summary>The prefab itself: normally a "node" with children.</summary>
    public NodeDef Node { get; set; } = new() { Type = "node" };

    /// <summary>Where the file came from (not serialized).</summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    public const string Extension = ".duczprefab.json";

    public static PrefabDef Load(string path)
    {
        var prefab = JsonSerializer.Deserialize<PrefabDef>(File.ReadAllText(path), SceneDocument.JsonOptions)
                     ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not a prefab.");
        prefab.SourcePath = path;
        if (string.IsNullOrWhiteSpace(prefab.Name))
            prefab.Name = Path.GetFileName(path).Replace(Extension, "");
        return prefab;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, SceneDocument.JsonOptions));
    }
}

/// <summary>
/// Finds prefabs in three places: the library that ships with the editor, the user's own
/// folder in %AppData%, and a "Prefabs" folder inside the open project. Later sources win,
/// so a project can override a stock prefab with its own version.
/// </summary>
public sealed class PrefabLibrary
{
    public List<PrefabDef> Prefabs { get; } = new();

    /// <summary>Where "Save as prefab" writes, and where the user can drop their own files.</summary>
    public static string UserFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuczEngine", "prefabs");

    public static string ShippedFolder => Path.Combine(AppContext.BaseDirectory, "Prefabs");

    public static PrefabLibrary Load(string? projectDirectory)
    {
        var library = new PrefabLibrary();
        library.AddFolder(ShippedFolder);
        library.AddFolder(UserFolder);
        if (projectDirectory != null)
            library.AddFolder(Path.Combine(projectDirectory, "Prefabs"));
        return library;
    }

    public void AddFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (var file in Directory.EnumerateFiles(folder, "*" + PrefabDef.Extension, SearchOption.AllDirectories)
                                      .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var prefab = PrefabDef.Load(file);
                Prefabs.RemoveAll(p => string.Equals(p.Name, prefab.Name, StringComparison.OrdinalIgnoreCase)
                                       && string.Equals(p.Category, prefab.Category, StringComparison.OrdinalIgnoreCase));
                Prefabs.Add(prefab);
            }
            catch (Exception ex)
            {
                Log.Warning($"Prefab \"{Path.GetFileName(file)}\" could not be read: {ex.Message}");
            }
        }
    }

    /// <summary>Category names in a stable order, with the stock ones first.</summary>
    public List<string> Categories()
    {
        string[] preferred = { "Streets", "Houses", "Buildings", "Structures", "Nature", "Urban", "Mine" };
        var found = Prefabs.Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return found.OrderBy(c => Array.FindIndex(preferred, p => string.Equals(p, c, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0 ? i : 99)
                    .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }

    public List<PrefabDef> InCategory(string category) =>
        Prefabs.Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
}
