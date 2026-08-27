// Ducz Map Builder Launcher - the front door.
//
// Lists your map projects, creates new ones from templates and opens them in the
// Map Builder. The project list lives in %AppData%/DuczEngine/launcher.json.
// Runs without a console window; messages go to %AppData%/DuczEngine/logs/launcher.log.

using Ducz;
using Ducz.Tools.Launcher;

Log.EnableFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuczEngine", "logs", "launcher.log"));
CrashReporter.Install("Ducz Map Builder");

var game = new Game(new GameSettings
{
    Title = "Ducz Map Builder",
    Width = 1160,
    Height = 680,
    Resizable = false,
    IconPath = Path.Combine(AppContext.BaseDirectory, "Branding", "ducz-icon-256.png")
});

// A folder passed on the command line (or dropped on the .exe) is adopted and opened,
// so an existing project on disk can be reached without hunting for it in the dialog.
string? startFolder = args.FirstOrDefault(a => !a.StartsWith('-') && Directory.Exists(a));

game.Run(() => new LauncherScene { StartFolder = startFolder });
