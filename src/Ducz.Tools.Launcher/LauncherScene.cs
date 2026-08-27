using System.Numerics;
using Ducz.Rendering;
using Ducz.Serialization;
using Ducz.UI;

namespace Ducz.Tools.Launcher;

/// <summary>
/// The launcher screen: project list on the left, "new project" form on the
/// right (name, location, template) - all built with the engine's own UI.
/// </summary>
class LauncherScene : Node
{
    private LauncherRegistry _registry = null!;
    private ProjectTemplate _selectedTemplate = null!;

    private Canvas _canvas = null!;
    private Panel _projectListPanel = null!;
    private VStack _projectStack = null!;
    private TextBox _nameBox = null!;
    private TextBox _locationBox = null!;
    private Label _templateDescription = null!;
    private Label _statusLabel = null!;
    private readonly Dictionary<Button, ProjectTemplate> _templateButtons = new();
    private volatile string? _pickedLocation;
    private volatile string? _pickedProject;

    /// <summary>Folder given on the command line: adopted and opened on the first frame.</summary>
    public string? StartFolder { get; init; }

    protected override void OnReady()
    {
        _registry = LauncherRegistry.Load();
        _selectedTemplate = Templates.All[0];

        Engine.Renderer.Environment.Background = BackgroundMode.SolidColor;
        Engine.Renderer.Environment.ClearColor = Color.FromHex("#14181f");

        BuildBackdrop();
        BuildUi();
        RebuildProjectList();

        if (StartFolder != null)
            _pickedProject = StartFolder;
    }

    protected override void OnUpdate(float dt)
    {
        // Apply a folder chosen in the (background-thread) picker dialog.
        if (_pickedLocation is { } picked)
        {
            _pickedLocation = null;
            _locationBox.Text = picked;
            SetStatus($"Location: {picked}");
        }

        if (_pickedProject is { } folder)
        {
            _pickedProject = null;
            AdoptProjectFolder(folder);
        }
    }

    /// <summary>A slowly drifting field of glowing cubes behind the UI.</summary>
    private void BuildBackdrop()
    {
        var world = AddChild(new Node3D("Backdrop"));
        var camera = world.AddChild(new Camera3D());
        camera.Position = new Vector3(0f, 0f, 16f);
        world.AddChild(new DirectionalLight3D { Energy = 0.9f }.WithDirection(-40f, 30f));

        var cubeMesh = MeshFactory.Cube(1f);
        string[] tints = { "#2e4a66", "#3a6ea5", "#24564b", "#4a3a66" };
        for (int i = 0; i < 26; i++)
        {
            var cube = world.AddChild(new MeshInstance3D(cubeMesh,
                Material.FromColor(Color.FromHex(Rng.Pick(tints)).WithAlpha(0.85f))));
            cube.Position = new Vector3(Rng.Range(-14f, 14f), Rng.Range(-8f, 8f), Rng.Range(-6f, 4f));
            cube.RotationDegrees = new Vector3(Rng.Range(0f, 360f), Rng.Range(0f, 360f), 0f);
            cube.Scale = new Vector3(Rng.Range(0.4f, 1.6f));
            world.AddChild(new Spinner(cube, Rng.Range(0.05f, 0.25f)));
        }
    }

    private sealed class Spinner : Node
    {
        private readonly Node3D _target;
        private readonly float _speed;
        public Spinner(Node3D target, float speed) { _target = target; _speed = speed; }
        protected override void OnUpdate(float dt)
        {
            _target.RotateY(_speed * dt);
            _target.RotateAxis(Vector3.UnitX, _speed * 0.6f * dt);
        }
    }

    // ------------------------------------------------------------------
    // UI
    // ------------------------------------------------------------------

    /// <summary>Product version shown in the UI (from the assembly).</summary>
    private static string AppVersion =>
        typeof(LauncherScene).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Loads a branding image shipped next to the executable (null when missing).</summary>
    private static Texture2D? LoadBranding(string fileName)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Branding", fileName);
            return File.Exists(path) ? Texture2D.FromFile(path, repeat: false) : null;
        }
        catch (Exception ex)
        {
            Log.Warning($"Branding image {fileName} not loaded: {ex.Message}");
            return null;
        }
    }

    private void BuildUi()
    {
        _canvas = AddChild(new Canvas());

        // Header: icon + title, and the full logo on a light card at the right
        var iconTexture = LoadBranding("ducz-icon-256.png");
        if (iconTexture != null)
            _canvas.AddChild(new ImageBox(iconTexture) { Anchor = Anchor.TopLeft, Position = new Vector2(24, 14), Size = new Vector2(78, 78) });
        float titleX = iconTexture != null ? 116 : 28;
        _canvas.AddChild(new Label("DUCZ MAP BUILDER")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(titleX, 20), FontSize = 42,
            Color = Color.FromHex("#7fd4ff")
        });
        _canvas.AddChild(new Label($"build simple maps, export GLB to Godot / Blender  |  v{AppVersion}")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(titleX + 2, 72), FontSize = 15,
            Color = Color.White.WithAlpha(0.55f)
        });

        // ---- Project list (left) ----
        _projectListPanel = _canvas.AddChild(new Panel
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(24, 108),
            Size = new Vector2(560, 520),
            BackgroundColor = Color.Black.WithAlpha(0.45f),
            BorderColor = Color.White.WithAlpha(0.1f)
        });
        _projectListPanel.AddChild(new Label("PROJECTS")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(16, 12), FontSize = 14,
            Color = Color.White.WithAlpha(0.5f)
        });
        _projectStack = _projectListPanel.AddChild(new VStack
        {
            Anchor = Anchor.TopCenter, Position = new Vector2(0, 40), Spacing = 8
        });

        // ---- New project (right) ----
        var form = _canvas.AddChild(new Panel
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(600, 108),
            Size = new Vector2(536, 520),
            BackgroundColor = Color.Black.WithAlpha(0.45f),
            BorderColor = Color.White.WithAlpha(0.1f)
        });

        form.AddChild(new Label("NEW MAP PROJECT")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(16, 12), FontSize = 14,
            Color = Color.White.WithAlpha(0.5f)
        });

        form.AddChild(new Label("Name")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(20, 44), FontSize = 15
        });
        _nameBox = form.AddChild(new TextBox
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(20, 68),
            Size = new Vector2(496, 34), Text = "MyMap"
        });

        form.AddChild(new Label("Location")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(20, 114), FontSize = 15
        });
        _locationBox = form.AddChild(new TextBox
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(20, 138),
            Size = new Vector2(384, 34), Text = _registry.ResolveDefaultLocation()
        });
        var browseButton = form.AddChild(new Button("Browse...")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(412, 138),
            Size = new Vector2(104, 34), FontSize = 14
        });
        browseButton.Clicked += () =>
        {
            SetStatus("Choose a folder in the dialog...");
            FolderPicker.PickAsync(_locationBox.Text, path => _pickedLocation = path);
        };

        form.AddChild(new Label("Template")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(20, 186), FontSize = 15
        });

        float x = 20f;
        foreach (var template in Templates.All)
        {
            var button = form.AddChild(new Button(template.Name)
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(x, 212),
                Size = new Vector2(160, 40), FontSize = 14
            });
            _templateButtons[button] = template;
            button.Clicked += () => SelectTemplate(template);
            x += 168f;
        }

        _templateDescription = form.AddChild(new Label("")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(22, 264), FontSize = 13,
            Color = Color.White.WithAlpha(0.6f)
        });

        var createButton = form.AddChild(new Button("Create Project")
        {
            Anchor = Anchor.BottomLeft, Position = new Vector2(20, -20),
            Size = new Vector2(270, 52), FontSize = 20,
            NormalColor = Color.FromHex("#1f4a6e")
        });
        createButton.Clicked += CreateProject;

        // Projects made outside the launcher (copied from a drive, cloned from git,
        // generated by a script) are not in the registry - this finds them.
        var openFolderButton = form.AddChild(new Button("Open from folder...")
        {
            Anchor = Anchor.BottomRight, Position = new Vector2(-20, -20),
            Size = new Vector2(226, 52), FontSize = 17
        });
        openFolderButton.Clicked += () =>
        {
            SetStatus("Choose the project folder in the dialog...");
            FolderPicker.PickAsync(_locationBox.Text, path => _pickedProject = path);
        };

        _statusLabel = _canvas.AddChild(new Label("")
        {
            Anchor = Anchor.BottomCenter, Position = new Vector2(0, -14), FontSize = 15,
            Color = Color.FromHex("#ffd75e")
        });

        SelectTemplate(_selectedTemplate);
    }

    private void SelectTemplate(ProjectTemplate template)
    {
        _selectedTemplate = template;
        foreach (var (button, t) in _templateButtons)
            button.NormalColor = t == template ? UITheme.AccentColor.Darkened(0.3f) : UITheme.PanelColor;
        _templateDescription.Text = template.Description;
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
        var color = Color.FromHex("#ffd75e");
        _statusLabel.Color = color;
        Tree!.CreateTween().Wait(4f).To(c => _statusLabel.Color = c, color, color.WithAlpha(0.3f), 0.6f);
    }

    // ------------------------------------------------------------------
    // Project list
    // ------------------------------------------------------------------

    private void RebuildProjectList()
    {
        _projectStack.ClearChildren();

        // Drop entries whose folder disappeared.
        _registry.Projects.RemoveAll(p => !Directory.Exists(p.Path));

        if (_registry.Projects.Count == 0)
        {
            _projectStack.AddChild(new Label("No projects yet - create one on the right!")
            {
                FontSize = 15, Color = Color.White.WithAlpha(0.5f), Anchor = Anchor.TopCenter
            });
            return;
        }

        foreach (var entry in _registry.Projects.OrderByDescending(p => p.LastOpened).Take(7))
        {
            var row = _projectStack.AddChild(new Panel
            {
                Size = new Vector2(528, 58),
                BackgroundColor = Color.White.WithAlpha(0.06f),
                BorderColor = Color.White.WithAlpha(0.1f),
                Anchor = Anchor.TopCenter
            });
            row.AddChild(new Label(entry.Name)
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(14, 8), FontSize = 18
            });
            row.AddChild(new Label(Truncate(entry.Path, 52))
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(14, 32), FontSize = 12,
                Color = Color.White.WithAlpha(0.45f)
            });

            var openButton = row.AddChild(new Button("Open")
            {
                Anchor = Anchor.MiddleRight, Position = new Vector2(-64, 0),
                Size = new Vector2(74, 36), FontSize = 14,
                NormalColor = Color.FromHex("#1f4a6e")
            });
            openButton.Clicked += () => OpenProject(entry);

            var removeButton = row.AddChild(new Button("X")
            {
                Anchor = Anchor.MiddleRight, Position = new Vector2(-16, 0),
                Size = new Vector2(36, 36), FontSize = 14
            });
            removeButton.Clicked += () =>
            {
                _registry.Projects.Remove(entry);
                _registry.Save();
                RebuildProjectList();
                SetStatus($"Removed {entry.Name} from the list (files kept on disk).");
            };
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : "..." + text[^(max - 3)..];

    // ------------------------------------------------------------------
    // Create / open
    // ------------------------------------------------------------------

    private void CreateProject()
    {
        string name = _nameBox.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus("Give the project a name first.");
            return;
        }

        string baseLocation = _locationBox.Text.Trim();
        if (baseLocation.Length == 0)
        {
            SetStatus("Choose a location for the project.");
            return;
        }

        string safeName = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        string projectDir = Path.Combine(baseLocation, safeName);

        if (File.Exists(Path.Combine(projectDir, ProjectFile.FileName)))
        {
            SetStatus($"A project already exists at {projectDir}.");
            return;
        }

        try
        {
            // Folder skeleton
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(Path.Combine(projectDir, "scenes"));
            Directory.CreateDirectory(Path.Combine(projectDir, "Assets"));

            // Project + main scene from the template
            var project = new ProjectFile
            {
                Name = name,
                MainScene = "scenes/main.json",
                Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
            project.Save(projectDir);

            var scene = _selectedTemplate.BuildScene(name);
            scene.Save(Path.Combine(projectDir, "scenes", "main.json"));

            // Register + remember location
            _registry.Projects.Add(new ProjectEntry { Name = name, Path = projectDir, LastOpened = DateTime.Now });
            _registry.DefaultLocation = baseLocation;
            _registry.Save();
            RebuildProjectList();

            SetStatus($"Created {name} ({_selectedTemplate.Name}) - opening editor...");
            LaunchEditor(projectDir);
        }
        catch (Exception ex)
        {
            Log.Error($"Project creation failed: {ex}");
            SetStatus($"Failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a project folder that the launcher has never seen to the list and opens it.
    /// Accepts a folder that already has a project file, one of its sub-folders (handy when
    /// the user picks the parent by mistake), or a bare folder with a "scenes" directory -
    /// in that case the missing project file is written so the folder becomes a real project.
    /// </summary>
    private void AdoptProjectFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            SetStatus($"Folder not found: {folder}");
            return;
        }

        string? projectDir = FindProjectDirectory(folder);
        if (projectDir == null)
        {
            SetStatus($"No project in \"{Path.GetFileName(folder)}\" (looked for {ProjectFile.FileName} or a scenes folder).");
            return;
        }

        string name;
        try
        {
            string projectFile = Path.Combine(projectDir, ProjectFile.FileName);
            if (File.Exists(projectFile))
            {
                name = ProjectFile.Load(projectDir).Name;
            }
            else
            {
                // A folder with scenes but no project file: adopt it.
                name = Path.GetFileName(projectDir.TrimEnd(Path.DirectorySeparatorChar));
                string mainScene = File.Exists(Path.Combine(projectDir, "scenes", "main.json"))
                    ? "scenes/main.json"
                    : Directory.EnumerateFiles(Path.Combine(projectDir, "scenes"), "*.json").Select(
                          f => "scenes/" + Path.GetFileName(f)).FirstOrDefault() ?? "scenes/main.json";
                new ProjectFile
                {
                    Name = name,
                    MainScene = mainScene,
                    Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                }.Save(projectDir);
                SetStatus($"Adopted \"{name}\" - project file created.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Could not read the project at {projectDir}: {ex}");
            SetStatus($"Could not read that project: {ex.Message}");
            return;
        }

        var existing = _registry.Projects.FirstOrDefault(
            p => string.Equals(Path.GetFullPath(p.Path), Path.GetFullPath(projectDir), StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new ProjectEntry { Name = name, Path = projectDir };
            _registry.Projects.Add(existing);
        }
        else
        {
            existing.Name = name;
        }
        _registry.Save();
        RebuildProjectList();
        OpenProject(existing);
    }

    /// <summary>The folder itself if it is a project, otherwise the first sub-folder that is.</summary>
    private static string? FindProjectDirectory(string folder)
    {
        if (IsProjectFolder(folder))
            return folder;
        try
        {
            return Directory.EnumerateDirectories(folder).FirstOrDefault(IsProjectFolder);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsProjectFolder(string folder) =>
        File.Exists(Path.Combine(folder, ProjectFile.FileName)) ||
        Directory.Exists(Path.Combine(folder, "scenes"));

    private void OpenProject(ProjectEntry entry)
    {
        entry.LastOpened = DateTime.Now;
        _registry.Save();
        SetStatus($"Opening {entry.Name}...");
        LaunchEditor(entry.Path);
    }

    private void LaunchEditor(string projectDir)
    {
        try
        {
            var (fileName, arguments) = FindEditorCommand(projectDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true   // the "dotnet run" fallback must not pop a console either
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not start the editor: {ex}");
            SetStatus($"Could not start the editor: {ex.Message}");
        }
    }

    /// <summary>Locates the Scene Editor: built exe first, "dotnet run" as fallback.</summary>
    private static (string FileName, string Arguments) FindEditorCommand(string projectDir)
    {
        // Shipped builds: the map builder executable sits next to the launcher.
        foreach (var sibling in new[] { "Ducz.Tools.SceneEditor.exe", "Ducz.Tools.SceneEditor" })
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, sibling);
            if (File.Exists(candidate))
                return (candidate, $"\"{projectDir}\"");
        }

        // Developer builds: walk up to the source tree.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? editorRoot = null;
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            string[] roots =
            {
                Path.Combine(dir.FullName, "src", "Ducz.Tools.SceneEditor"),
                Path.Combine(dir.FullName, "Ducz.Tools.SceneEditor")
            };
            editorRoot = roots.FirstOrDefault(Directory.Exists);
            if (editorRoot != null)
                break;
        }

        if (editorRoot == null)
            throw new FileNotFoundException("Ducz.Tools.SceneEditor not found near the launcher.");

        string[] exeCandidates =
        {
            Path.Combine(editorRoot, "bin", "Debug", "net8.0", "Ducz.Tools.SceneEditor.exe"),
            Path.Combine(editorRoot, "bin", "Release", "net8.0", "Ducz.Tools.SceneEditor.exe")
        };
        var exe = exeCandidates.FirstOrDefault(File.Exists);
        if (exe != null)
            return (exe, $"\"{projectDir}\"");

        string csproj = Path.Combine(editorRoot, "Ducz.Tools.SceneEditor.csproj");
        return ("dotnet", $"run --project \"{csproj}\" -- \"{projectDir}\"");
    }
}
