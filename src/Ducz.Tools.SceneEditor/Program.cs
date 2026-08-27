// Ducz Map Builder - build maps visually, export them as GLB (Godot / Blender) or JSON scenes.
//
// Usage:
//   dotnet run --project src/Ducz.Tools.SceneEditor                     (edits ./level.json)
//   dotnet run --project src/Ducz.Tools.SceneEditor -- my.json          (edits a scene file)
//   dotnet run --project src/Ducz.Tools.SceneEditor -- <project folder> (opens a project)
//
// Projects (created by the Ducz Launcher) contain project.duczproj.json; the
// editor then saves into the project's main scene and exports under the
// project folder. Press Tab to play-test instantly.

using Ducz;
using Ducz.Serialization;
using Ducz.Tools.SceneEditor;

// The tools run without a console window: keep a log file for diagnostics.
Log.EnableFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuczEngine", "logs", "mapbuilder.log"));
CrashReporter.Install("Ducz Map Builder");

// Optional: --camera x,y,z --look x,y,z  (start the fly camera somewhere specific, e.g. to review an area)
System.Numerics.Vector3? startCamera = null, startLook = null;
var positional = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--camera" || args[i] == "--look") && i + 1 < args.Length)
    {
        var parts = args[i + 1].Split(',');
        if (parts.Length == 3 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vx)
            && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vy)
            && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vz))
        {
            if (args[i] == "--camera") startCamera = new System.Numerics.Vector3(vx, vy, vz); else startLook = new System.Numerics.Vector3(vx, vy, vz);
        }
        i++;
        continue;
    }
    positional.Add(args[i]);
}

string argument = positional.Count > 0 ? positional[0] : "level.json";

string savePath;
string? projectDirectory = null;
string title = "Ducz Map Builder";

// A project folder / project file / anything inside a project opens project mode.
if (ProjectFile.Locate(argument) != null)
{
    var project = ProjectFile.Load(argument);
    projectDirectory = project.Directory;
    savePath = project.MainScenePath;
    Assets.BasePath = project.Directory;   // relative asset paths resolve inside the project
    title = $"Ducz Map Builder - {project.Name}";
}
else
{
    savePath = argument;
}

var game = new Game(new GameSettings
{
    Title = title,
    Width = 1600,
    Height = 900,
    IconPath = Path.Combine(AppContext.BaseDirectory, "Branding", "ducz-icon-256.png")
});

game.Run(() => new EditorScene(savePath, projectDirectory) { StartCameraPosition = startCamera, StartCameraLookAt = startLook });
