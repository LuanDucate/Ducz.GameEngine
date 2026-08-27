using System.Text.Json;

namespace Ducz.Serialization;

/// <summary>
/// A game project descriptor ("project.duczproj.json") sitting at the root of a
/// project folder. Created by the launcher, opened by the scene editor:
///
/// <code>
/// MyGame/
///   project.duczproj.json
///   scenes/main.json
///   Assets/
/// </code>
/// </summary>
public sealed class ProjectFile
{
    /// <summary>Standard file name inside a project folder.</summary>
    public const string FileName = "project.duczproj.json";

    /// <summary>Display name of the game.</summary>
    public string Name { get; set; } = "My Game";

    /// <summary>Path of the main scene, relative to the project folder.</summary>
    public string MainScene { get; set; } = "scenes/main.json";

    /// <summary>Engine version the project was created with.</summary>
    public string EngineVersion { get; set; } = "0.1.0";

    /// <summary>Creation timestamp (informational).</summary>
    public string? Created { get; set; }

    /// <summary>The folder this project was loaded from (not serialized).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Directory { get; private set; } = "";

    /// <summary>
    /// Finds the project file for a path that may be the project folder itself,
    /// the project file, or any file inside the folder. Returns null when none exists.
    /// </summary>
    public static string? Locate(string path)
    {
        if (File.Exists(path) && Path.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(path);

        string? directory = System.IO.Directory.Exists(path)
            ? path
            : Path.GetDirectoryName(Path.GetFullPath(path));

        if (directory == null)
            return null;

        string candidate = Path.Combine(directory, FileName);
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    /// <summary>Loads a project from its folder or file path.</summary>
    public static ProjectFile Load(string path)
    {
        string? file = Locate(path) ?? throw new FileNotFoundException($"No {FileName} found at {path}.");
        var project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(file), SceneDocument.JsonOptions)
                      ?? throw new InvalidDataException($"Invalid project file: {file}");
        project.Directory = Path.GetDirectoryName(file)!;
        return project;
    }

    /// <summary>Writes the project file into a folder (creating it if needed).</summary>
    public void Save(string projectDirectory)
    {
        System.IO.Directory.CreateDirectory(projectDirectory);
        Directory = Path.GetFullPath(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, FileName),
            JsonSerializer.Serialize(this, SceneDocument.JsonOptions));
    }

    /// <summary>Absolute path of the main scene file.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string MainScenePath => Path.Combine(Directory, MainScene);
}
