using System.Collections.Concurrent;
using System.Numerics;
using Ducz.Export;
using Ducz.Physics;
using Ducz.Rendering;
using Ducz.Serialization;
using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// The Ducz Map Builder: a visual editor that produces the engine's JSON scene
/// format and exports maps as GLB for Godot / Blender.
///
/// Controls:
///  - LMB: place the selected block (hold and drag to paint a row of blocks)
///  - Shift + LMB drag: fill a rectangle with the selected block
///  - Ctrl + LMB on an object: paint the selected material onto it
///  - RMB click on an object: properties panel (material/texture, tiling, scale, rotation, collision)
///  - X / Delete: delete the hovered object
///  - R: rotate placement 90 degrees, G: cycle grid size, T: top-down / fly view
///  - Ctrl+Z / Ctrl+Y: undo / redo, Ctrl+D: duplicate selection, arrows / PgUp / PgDn: nudge selection
///  - WASD + E/Q + hold RMB: fly camera
///  - Tab: play mode, Ctrl+S / Ctrl+O / Ctrl+N: save / load / new, Ctrl+E: export GLB
///  - Drop image files (textures) or model files onto the window
/// </summary>
partial class EditorScene : Node
{
    private readonly string _savePath;
    private readonly string? _projectDirectory;

    private SceneDocument _doc = null!;
    private FlyCamera _camera = null!;
    private TopDownCamera _topCamera = null!;
    private bool _topDown;
    private Node3D _world = null!;
    private Node3D? _ghost;
    private Node3D? _playRoot;
    private bool _playing;

    private readonly Dictionary<Node3D, NodeDef> _defByNode = new();

    // Placement state
    private BlockItem _selectedItem = null!;
    private string _selectedMaterial = "stone";
    private float _rotationY;
    private float _placeScale = 1f;
    private float _gridSize = 1f;
    /// <summary>Vertical snap: how finely stacking rounds in Y (grid sizes below 0.25 use the same step).</summary>
    private float VerticalSnap => MathF.Min(_gridSize, 0.05f);
    private static readonly float[] GridSizes = { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };
    private Vector3? _placeTarget;
    private Node3D? _hoveredNode;

    // Drag painting / rectangle fill
    private bool _painting;
    private float _paintPlaneY;
    private Vector3? _lastPaintCell;
    private Vector3? _rectStart;

    // Imported models (FBX/OBJ/glTF props usable as palette items)
    private readonly List<BlockItem> _modelItems = new();
    private static readonly string[] ModelExtensions =
        { ".fbx", ".obj", ".gltf", ".glb", ".dae", ".stl", ".3ds", ".ply" };
    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };

    // Selection / properties panel
    private Node3D? _selected;
    private NodeDef? _selectedDef;
    private Vector2 _rmbDownPosition;
    private float _rmbHeld;
    /// <summary>A right button held shorter than this is a click (properties), longer is camera look / pan.</summary>
    private const float RmbClickTime = 0.2f;
    private Panel _propsPanel = null!;
    private Label _propsTitle = null!;
    private Label _propsScaleLabel = null!;
    private CheckBox _propsCollision = null!;
    private TextBox[] _propsPositionBoxes = Array.Empty<TextBox>();
    private TextBox[] _propsRotationBoxes = Array.Empty<TextBox>();
    private Button _propsSplitButton = null!;
    private Button? _freeMouseButton;

    // Export
    private bool _exporting;

    // Work posted from background threads (dialogs, export) runs on the game loop.
    private readonly ConcurrentQueue<Action> _pending = new();

    // UI
    private Canvas _canvas = null!;
    private ScrollPanel _sidebar = null!;
    private VStack _paletteStack = null!;
    private TextBox _importBox = null!;
    private Label _statusLabel = null!;
    private Label _modeLabel = null!;
    private Label _selectionLabel = null!;
    private Button _viewButton = null!;
    private readonly Dictionary<Button, BlockItem> _itemButtons = new();

    /// <summary>Optional initial fly-camera position / look-at (from the --camera / --look command line options).</summary>
    public Vector3? StartCameraPosition { get; init; }
    public Vector3? StartCameraLookAt { get; init; }

    public EditorScene(string savePath, string? projectDirectory = null)
    {
        _savePath = savePath;
        _projectDirectory = projectDirectory;
    }

    // ------------------------------------------------------------------
    // Block palette
    // ------------------------------------------------------------------

    private sealed record BlockItem(
        string Label,
        MeshDef GhostMesh,
        float YOffset,
        Func<EditorScene, Vector3, NodeDef> CreateDef)
    {
        /// <summary>Set for imported model items (FBX/OBJ/glTF props).</summary>
        public string? ModelPath { get; init; }

        /// <summary>Local-space bounds of the imported model (for base placement).</summary>
        public (Vector3 Min, Vector3 Max)? Bounds { get; init; }

        /// <summary>Set when this entry places a single piece of a modular pack.</summary>
        public string? SubNode { get; init; }

        /// <summary>Set when this entry drops a ready-made prefab.</summary>
        public PrefabDef? Prefab { get; init; }
    }

    private static readonly List<BlockItem> Palette = new()
    {
        new("Cube 1m", new MeshDef { Primitive = "box", Size = new[] { 1f, 1f, 1f } }, 0.5f,
            (e, p) => e.StaticBlockDef(p, new[] { 1f, 1f, 1f })),
        new("Block 2m", new MeshDef { Primitive = "box", Size = new[] { 2f, 2f, 2f } }, 1f,
            (e, p) => e.StaticBlockDef(p, new[] { 2f, 2f, 2f })),
        new("Slab 1x0.5", new MeshDef { Primitive = "box", Size = new[] { 1f, 0.5f, 1f } }, 0.25f,
            (e, p) => e.StaticBlockDef(p, new[] { 1f, 0.5f, 1f })),
        new("Floor 4x4", new MeshDef { Primitive = "box", Size = new[] { 4f, 0.3f, 4f } }, 0.15f,
            (e, p) => e.StaticBlockDef(p, new[] { 4f, 0.3f, 4f })),
        new("Wall 4x3", new MeshDef { Primitive = "box", Size = new[] { 4f, 3f, 0.3f } }, 1.5f,
            (e, p) => e.StaticBlockDef(p, new[] { 4f, 3f, 0.3f })),
        new("Wall 1x3", new MeshDef { Primitive = "box", Size = new[] { 1f, 3f, 0.3f } }, 1.5f,
            (e, p) => e.StaticBlockDef(p, new[] { 1f, 3f, 0.3f })),
        new("Ramp", new MeshDef { Primitive = "box", Size = new[] { 2f, 1f, 3f } }, 0f,
            (e, p) => new NodeDef
            {
                Type = "ramp", Size = new[] { 2f, 1f, 3f },
                Material = e._selectedMaterial,
                Position = ToArray(p),
                RotationDegrees = new[] { 0f, e._rotationY, 0f },
                WorldUv = true
            }),
        new("Pillar", new MeshDef { Primitive = "cylinder", Radius = 0.4f, Height = 3f }, 1.5f,
            (e, p) => new NodeDef
            {
                Type = "static",
                Mesh = new MeshDef { Primitive = "cylinder", Radius = 0.4f, Height = 3f },
                Material = e._selectedMaterial,
                Position = ToArray(p),
                RotationDegrees = new[] { 0f, e._rotationY, 0f },
                WorldUv = true
            }),
        new("Wedge / ramp", new MeshDef { Primitive = "wedge", Size = new[] { 2f, 1f, 3f } }, 0.5f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "wedge", Size = new[] { 2f, 1f, 3f } })),
        new("Stairs", new MeshDef { Primitive = "stairs", Size = new[] { 2f, 2f, 3f }, Steps = 8 }, 1f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "stairs", Size = new[] { 2f, 2f, 3f }, Steps = 8 })),
        new("Roof (gable)", new MeshDef { Primitive = "roofGable", Size = new[] { 4f, 1.6f, 5f }, Overhang = 0.25f }, 0.8f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "roofGable", Size = new[] { 4f, 1.6f, 5f }, Overhang = 0.25f })),
        new("Roof (hip)", new MeshDef { Primitive = "roofHip", Size = new[] { 4f, 1.6f, 5f }, RidgeLength = 2f, Overhang = 0.25f }, 0.8f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "roofHip", Size = new[] { 4f, 1.6f, 5f }, RidgeLength = 2f, Overhang = 0.25f })),
        new("Roof (shed)", new MeshDef { Primitive = "roofShed", Size = new[] { 4f, 1.2f, 4f }, Thickness = 0.15f }, 0.68f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "roofShed", Size = new[] { 4f, 1.2f, 4f }, Thickness = 0.15f })),
        new("Arch", new MeshDef { Primitive = "arch", Size = new[] { 3f, 3.5f, 0f }, Thickness = 0.4f, OpeningWidth = 1.6f, OpeningHeight = 1.6f }, 1.75f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "arch", Size = new[] { 3f, 3.5f, 0f }, Thickness = 0.4f, OpeningWidth = 1.6f, OpeningHeight = 1.6f })),
        new("Curved wall", new MeshDef { Primitive = "curvedWall", Radius = 3f, Height = 3f, Thickness = 0.3f, ArcDegrees = 90f }, 1.5f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "curvedWall", Radius = 3f, Height = 3f, Thickness = 0.3f, ArcDegrees = 90f })),
        new("Tube", new MeshDef { Primitive = "tube", Radius = 1f, Height = 3f, Thickness = 0.15f }, 1.5f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "tube", Radius = 1f, Height = 3f, Thickness = 0.15f })),
        new("Prism (hex)", new MeshDef { Primitive = "prism", Radius = 0.6f, Height = 2f, Sides = 6 }, 1f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "prism", Radius = 0.6f, Height = 2f, Sides = 6 })),
        new("Pyramid", new MeshDef { Primitive = "pyramid", Size = new[] { 2f, 2f, 2f } }, 1f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "pyramid", Size = new[] { 2f, 2f, 2f } })),
        new("Rounded box", new MeshDef { Primitive = "roundedBox", Size = new[] { 1f, 1f, 1f }, Bevel = 0.1f }, 0.5f,
            (e, p) => e.ShapeDef(p, new MeshDef { Primitive = "roundedBox", Size = new[] { 1f, 1f, 1f }, Bevel = 0.1f })),
        new("Sphere", new MeshDef { Primitive = "sphere", Radius = 0.5f }, 0.5f,
            (e, p) => new NodeDef
            {
                Type = "static",
                Mesh = new MeshDef { Primitive = "sphere", Radius = 0.5f },
                Material = e._selectedMaterial,
                Position = ToArray(p)
            }),
        new("Crate (physics)", new MeshDef { Primitive = "box", Size = new[] { 1f, 1f, 1f } }, 0.5f,
            (e, p) => new NodeDef
            {
                Type = "crate", Size = new[] { 1f },
                Material = e._selectedMaterial,
                Position = ToArray(p),
                Mass = 2f,
                WorldUv = true
            }),
        new("Point Light", new MeshDef { Primitive = "sphere", Radius = 0.25f }, 1.6f,
            (e, p) => new NodeDef
            {
                Type = "pointLight",
                Color = "#ffd9a0", Energy = 2f, Range = 9f,
                Position = ToArray(p)
            }),
        new("Spawn Point", new MeshDef { Primitive = "capsule", Radius = 0.35f, Height = 1.7f }, 0.9f,
            (e, p) => new NodeDef { Type = "spawn", Name = "SpawnPoint", Position = ToArray(p) })
    };

    /// <summary>A solid object using any primitive/shape, with the palette's current material and rotation.</summary>
    private NodeDef ShapeDef(Vector3 position, MeshDef mesh) => new()
    {
        Type = "static",
        Mesh = mesh,
        Material = _selectedMaterial,
        Position = ToArray(position),
        RotationDegrees = _rotationY != 0f ? new[] { 0f, _rotationY, 0f } : null,
        WorldUv = true
    };

    private NodeDef StaticBlockDef(Vector3 position, float[] size) => new()
    {
        Type = "static",
        Mesh = new MeshDef { Primitive = "box", Size = size },
        Material = _selectedMaterial,
        Position = ToArray(position),
        RotationDegrees = _rotationY != 0f ? new[] { 0f, _rotationY, 0f } : null,
        WorldUv = true
    };

    private static float[] ToArray(Vector3 v) => new[] { v.X, v.Y, v.Z };

    /// <summary>XZ footprint of the selected item at the current rotation/scale (for rectangle fills and duplicates).</summary>
    private Vector2 Footprint(BlockItem item)
    {
        float x = 1f, z = 1f;
        if (item is { ModelPath: not null, Bounds: { } bounds })
        {
            var size = (bounds.Max - bounds.Min) * _placeScale;
            x = MathF.Max(0.25f, size.X);
            z = MathF.Max(0.25f, size.Z);
        }
        else
        {
            var mesh = item.GhostMesh;
            switch (mesh.Primitive.ToLowerInvariant())
            {
                case "box":
                    x = mesh.Size is { Length: > 0 } ? mesh.Size[0] : 1f;
                    z = mesh.Size is { Length: > 2 } ? mesh.Size[2] : x;
                    break;
                case "cylinder" or "sphere" or "capsule" or "cone":
                    x = z = mesh.Radius * 2f;
                    break;
            }
        }
        bool turned = MathF.Abs(MathF.IEEERemainder(_rotationY, 180f)) > 45f;
        return turned ? new Vector2(z, x) : new Vector2(x, z);
    }

    // ------------------------------------------------------------------
    // Setup
    // ------------------------------------------------------------------

    protected override void OnReady()
    {
        _selectedItem = Palette[0];

        _doc = File.Exists(_savePath) ? SceneDocument.Load(_savePath) : CreateDefaultDocument();

        // If the editor ever dies with unsaved work, dump the document next to the scene.
        CrashReporter.RescueHandler = RescueDocument;

        // Show the map's own sky/ambient/fog while editing (not just in play mode).
        ApplyDocumentEnvironment();

        _camera = AddChild(new FlyCamera { MoveSpeed = 12f, LookHoldDelay = RmbClickTime });
        _camera.Position = StartCameraPosition ?? new Vector3(8f, 9f, 14f);
        _camera.LookAt(StartCameraLookAt ?? (StartCameraPosition.HasValue ? StartCameraPosition.Value + new Vector3(0f, -0.4f, -1f) : Vector3.Zero));
        _camera.MakeCurrent();

        _topCamera = AddChild(new TopDownCamera { Active = false });

        _world = AddChild(new Node3D("EditorWorld"));
        RebuildWorld();

        BuildUi();
        RebuildMaterialPalette();
        RefreshModelPaletteFromDoc();
        SelectItem(Palette[0]);
        SelectMaterial(_doc.Materials.ContainsKey(_selectedMaterial) ? _selectedMaterial : _doc.Materials.Keys.FirstOrDefault() ?? "stone");
        SetStatus($"Editing {Path.GetFileName(_savePath)} - {_doc.Nodes.Count} nodes");

        // Drag & drop model / image files straight onto the window.
        Engine.Game.FileDropped += OnFilesDropped;
    }

    /// <summary>
    /// Applies the document's environment (sky, ambient, fog) so the editor viewport looks like
    /// the real scene. Fog is pushed far away while editing so distant geometry stays visible.
    /// </summary>
    private void ApplyDocumentEnvironment()
    {
        SceneLoader.ApplyEnvironment(_doc.Environment);
        var env = Engine.Renderer.Environment;
        if (env.FogEnabled)
        {
            env.FogStart = MathF.Max(env.FogStart, 150f);
            env.FogEnd = MathF.Max(env.FogEnd, 600f);
        }
        // Never let the map go pitch black in the editor.
        env.AmbientIntensity = MathF.Max(env.AmbientIntensity, 0.35f);
    }

    protected override void OnExitTree()
    {
        Engine.Game.FileDropped -= OnFilesDropped;
        base.OnExitTree();
    }

    private void OnFilesDropped(string[] paths)
    {
        if (_playing)
            return;
        var target = PickAt(Input.MousePosition);   // object under the cursor at drop time
        foreach (var path in paths)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ModelExtensions.Contains(ext))
                ImportModel(path);
            else if (ImageExtensions.Contains(ext))
                ImportImage(path, target);
            else
                SetStatus($"Unsupported file type: {Path.GetFileName(path)}");
        }
    }

    /// <summary>Runs an action on the game loop (safe to call from any thread).</summary>
    private void Post(Action action) => _pending.Enqueue(action);

    /// <summary>
    /// A ready-to-play starter map: sun, textured ground, spawn, player and a
    /// third-person camera. Materials use world-space UVs (uvScale = tiles per meter).
    /// </summary>
    internal static SceneDocument CreateDefaultDocument()
    {
        var doc = new SceneDocument
        {
            Name = "NewMap",
            Environment = new EnvironmentDef
            {
                SkyTop = "#2a4a8f",
                SkyHorizon = "#b8cfe8",
                AmbientIntensity = 0.3f,
                Fog = new FogDef { Color = "#b8cfe8", Start = 40f, End = 160f }
            },
            Materials = DefaultMaterials.Create()
        };

        doc.Nodes.Add(new NodeDef { Type = "directionalLight", Name = "Sun", RotationDegrees = new[] { -48f, 35f, 0f }, Energy = 1.1f });
        doc.Nodes.Add(new NodeDef { Type = "floor", Name = "Ground", Size = new[] { 40f, 40f }, Material = "grass", WorldUv = true });
        doc.Nodes.Add(new NodeDef { Type = "spawn", Name = "SpawnPoint", Position = new[] { 0f, 0f, 0f } });
        doc.Nodes.Add(new NodeDef { Type = "player", Name = "Player", Position = new[] { 0f, 1.2f, 0f } });
        doc.Nodes.Add(new NodeDef { Type = "thirdPersonCamera", Name = "MainCamera", Target = "Player", Distance = 6.5f, TargetHeight = 1.4f, Current = true });
        return doc;
    }

    // ------------------------------------------------------------------
    // Editor world building
    // ------------------------------------------------------------------

    private void RebuildWorld()
    {
        _world.ClearChildren();
        _defByNode.Clear();

        foreach (var def in _doc.Nodes)
        {
            var visual = BuildEditorVisual(def);
            if (visual != null)
            {
                _world.AddChild(visual);
                _defByNode[visual] = def;
                RegisterParts(visual, def);
            }
        }

        // The selection may point at a node that no longer exists.
        if (_selectedDef != null)
        {
            _selected = _defByNode.FirstOrDefault(kv => kv.Value == _selectedDef).Key;
            if (_selected == null)
                CloseProperties();
        }
    }

    /// <summary>Editor representation of a def: real geometry for blocks, gizmos for logical nodes.</summary>
    private Node3D? BuildEditorVisual(NodeDef def)
    {
        switch (def.Type.ToLowerInvariant())
        {
            // Cameras and the player are not simulated in edit mode.
            case "camera" or "flycamera" or "thirdpersoncamera" or "player":
                return null;

            case "spawn":
            {
                var marker = new StaticBody3D("SpawnMarker") { Shape = new SphereShape(0.45f) };
                var visual = marker.AddChild(new MeshInstance3D(
                    MeshFactory.Capsule(0.35f, 1.7f),
                    new Material { Albedo = Color.FromHex("#3ec46d88"), Transparent = true, Unshaded = true }));
                visual.Position = new Vector3(0f, 0.85f, 0f);
                if (def.Position is { Length: >= 3 })
                    marker.Position = new Vector3(def.Position[0], def.Position[1], def.Position[2]);
                return marker;
            }

            case "pointlight":
            {
                var marker = new StaticBody3D("LightMarker") { Shape = new SphereShape(0.3f) };
                var color = def.Color != null ? Color.FromHex(def.Color) : Color.White;
                marker.AddChild(new PointLight3D { Color = color, Energy = def.Energy, Range = def.Range });
                marker.AddChild(new MeshInstance3D(MeshFactory.Sphere(0.22f, 10, 14),
                    Material.Emissive(color, 1.2f)));
                if (def.Position is { Length: >= 3 })
                    marker.Position = new Vector3(def.Position[0], def.Position[1], def.Position[2]);
                return marker;
            }

            default:
            {
                var node = SceneLoader.InstantiateNode(_doc, def);
                if (node == null)
                    return null;
                // Physics props must not tumble around while editing.
                if (node is RigidBody3D rigid)
                    rigid.Freeze = true;
                foreach (var descendant in node.Descendants().OfType<RigidBody3D>())
                    descendant.Freeze = true;
                return node;
            }
        }
    }

    /// <summary>Rebuilds one placed node's editor visual after its definition changed. Returns the new node.</summary>
    private Node3D? RebuildNode(Node3D node)
    {
        if (!_defByNode.TryGetValue(node, out var def))
            return null;

        bool wasSelected = _selected == node || (_selected != null && _rootByPart.GetValueOrDefault(_selected) == node);
        _defByNode.Remove(node);
        ForgetParts(node);
        node.RemoveFromParent();

        var visual = BuildEditorVisual(def);
        if (visual != null)
        {
            _world.AddChild(visual);
            _defByNode[visual] = def;
            RegisterParts(visual, def);
        }
        if (wasSelected)
        {
            _selected = visual;
            RefreshPropertiesPanel();
        }
        return visual;
    }

    /// <summary>Rebuilds every node that uses a material (after the material itself changed).</summary>
    private void RebuildNodesUsingMaterial(string key)
    {
        // A material can be used by a part deep inside a prefab, so check the whole tree and
        // rebuild the placed object that owns it.
        var roots = new HashSet<Node3D>();
        foreach (var (node, def) in _defByPart.ToList())
            if (string.Equals(def.Material?.Reference, key, StringComparison.OrdinalIgnoreCase))
                roots.Add(_rootByPart.GetValueOrDefault(node) ?? node);
        foreach (var root in roots)
            RebuildNode(root);
    }

    // ------------------------------------------------------------------
    // UI
    // ------------------------------------------------------------------

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

        // Top bar
        var topBar = _canvas.AddChild(new Panel
        {
            Anchor = Anchor.TopLeft,
            Position = new Vector2(0, 0),
            Size = new Vector2(4000, 46),
            BackgroundColor = Color.Black.WithAlpha(0.55f),
            BorderColor = Color.Transparent
        });
        var iconTexture = LoadBranding("ducz-icon-64.png");
        if (iconTexture != null)
            topBar.AddChild(new ImageBox(iconTexture) { Anchor = Anchor.MiddleLeft, Position = new Vector2(10, 0), Size = new Vector2(32, 32) });
        topBar.AddChild(new Label($"Ducz Map Builder - {Path.GetFileName(_savePath)}")
        {
            Anchor = Anchor.MiddleLeft, Position = new Vector2(iconTexture != null ? 50 : 14, 0), FontSize = 18,
            Color = Color.FromHex("#7fd4ff")
        });

        var buttons = topBar.AddChild(new HStack
        {
            Anchor = Anchor.MiddleLeft, Position = new Vector2(360, 0), Spacing = 6
        });
        AddTopButton(buttons, "New", NewDocument, 70);
        AddTopButton(buttons, "Load", LoadDocument, 70);
        AddTopButton(buttons, "Save", SaveDocument, 70);
        AddTopButton(buttons, "Undo", Undo, 70);
        AddTopButton(buttons, "Redo", Redo, 70);
        _viewButton = AddTopButton(buttons, "Top view (T)", ToggleTopDown, 120);
        AddTopButton(buttons, "Play (Tab)", TogglePlay, 110);
        AddTopButton(buttons, "Export GLB", ExportGlb, 120);

        _modeLabel = topBar.AddChild(new Label("EDIT MODE")
        {
            Anchor = Anchor.MiddleRight, Position = new Vector2(-16, 0), FontSize = 16,
            Color = Color.FromHex("#8fd48f")
        });

        // Sidebar - scrolls, because blocks + materials + models are taller than any window.
        _sidebar = _canvas.AddChild(new ScrollPanel
        {
            Anchor = Anchor.TopLeft,
            Position = new Vector2(0, 46),
            Size = new Vector2(190, 830),
            BackgroundColor = Color.Black.WithAlpha(0.45f),
            BorderColor = Color.Transparent
        });

        var stack = _sidebar.AddChild(new VStack
        {
            Anchor = Anchor.TopCenter, Position = new Vector2(0, 8), Spacing = 3
        });

        stack.AddChild(new Label("BLOCKS") { FontSize = 13, Color = Color.White.WithAlpha(0.5f), Anchor = Anchor.TopCenter });

        // Free mouse comes first: it is how you stop placing and go back to just looking.
        _freeMouseButton = stack.AddChild(new Button("Free mouse (Esc)")
        {
            Size = new Vector2(170, 25), FontSize = 13, Anchor = Anchor.TopCenter,
            NormalColor = Color.FromHex("#2b4a5e")
        });
        _freeMouseButton.Clicked += () => EnterFreeMouse();

        foreach (var item in Palette)
        {
            var button = stack.AddChild(new Button(item.Label)
            {
                Size = new Vector2(170, 25), FontSize = 13, Anchor = Anchor.TopCenter
            });
            _itemButtons[button] = item;
            button.Clicked += () => SelectItem(item);
        }

        _selectionLabel = stack.AddChild(new Label("")
        {
            FontSize = 12, Color = Color.FromHex("#ffd75e"), Anchor = Anchor.TopCenter
        });

        BuildMaterialPalette(stack);

        // ---- Imported models (FBX / OBJ / glTF ... or drag & drop onto the window) ----
        stack.AddChild(new Label("MODELS") { FontSize = 13, Color = Color.White.WithAlpha(0.5f), Anchor = Anchor.TopCenter });
        _importBox = stack.AddChild(new TextBox
        {
            Placeholder = "path to .fbx / .glb + Enter",
            Size = new Vector2(170, 26),
            FontSize = 12,
            Anchor = Anchor.TopCenter
        });
        _importBox.Submitted += ImportModel;
        var browseButton = stack.AddChild(new Button("Browse model file...")
        {
            Size = new Vector2(170, 26), FontSize = 13, Anchor = Anchor.TopCenter
        });
        browseButton.Clicked += () => FileDialogs.OpenFileAsync("Import 3D model", FileDialogs.ModelFilter,
            _projectDirectory, path => Post(() => ImportModel(path)));

        // ---- Prefabs (ready-made houses, streets, trees... - see EditorScene.PrefabPanel.cs)
        BuildPrefabSection(stack);

        _paletteStack = stack;

        BuildPropertiesPanel();
        BuildExportPanel();
        BuildPackPanel();
        BuildPrefabPanel();
        BuildBoxSelectPanel();

        // Help + status
        _canvas.AddChild(new Label(
            "LMB place | Esc free mouse | Ctrl+C/V copy | Alt+RMB part | R rotate | G grid | T top | B prefabs | P pack | F reachability")
        {
            Anchor = Anchor.BottomCenter, Position = new Vector2(0, -8), FontSize = 13,
            Color = Color.White.WithAlpha(0.55f)
        });
        _canvas.AddChild(new Label(
            "WASD/E/Q fly (hold RMB to look) | Ctrl+Z/Y undo/redo | Ctrl+D duplicate | arrows/PgUp/PgDn nudge | Ctrl+E export GLB | Tab play | drop images or models here")
        {
            Anchor = Anchor.BottomCenter, Position = new Vector2(0, -26), FontSize = 13,
            Color = Color.White.WithAlpha(0.55f)
        });

        _statusLabel = _canvas.AddChild(new Label("")
        {
            Anchor = Anchor.BottomCenter, Position = new Vector2(0, -50), FontSize = 16,
            Color = Color.FromHex("#ffd75e")
        });
    }

    private Button AddTopButton(HStack bar, string text, Action action, float width = 130f)
    {
        var button = bar.AddChild(new Button(text) { Size = new Vector2(width, 32), FontSize = 14 });
        button.Clicked += action;
        return button;
    }

    // ------------------------------------------------------------------
    // Properties panel (right-click an object to open)
    // ------------------------------------------------------------------

    private void BuildPropertiesPanel()
    {
        _propsPanel = _canvas.AddChild(new Panel
        {
            Anchor = Anchor.TopRight,
            Position = new Vector2(-12, 52),
            Size = new Vector2(240, 836),
            BackgroundColor = Color.Black.WithAlpha(0.7f),
            BorderColor = Color.White.WithAlpha(0.15f),
            Visible = false
        });

        _propsTitle = _propsPanel.AddChild(new Label("Object")
        {
            Anchor = Anchor.TopCenter, Position = new Vector2(0, 10), FontSize = 17,
            Color = Color.FromHex("#7fd4ff")
        });

        // Position: one row per axis - type a number (Enter) or hold the - / + buttons and
        // watch the object move. Stepping uses the current grid size (Shift = 4x).
        _propsPanel.AddChild(new Label("POSITION")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(14, 38), FontSize = 12, Color = Color.White.WithAlpha(0.5f)
        });
        _propsPositionBoxes = new TextBox[3];
        string[] axes = { "X", "Y", "Z" };
        float py = 54f;
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            _propsPanel.AddChild(new Label(axes[i]) { Anchor = Anchor.TopLeft, Position = new Vector2(16, py + 4), FontSize = 13 });
            var box = _propsPanel.AddChild(new TextBox
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(32, py),
                Size = new Vector2(72, 22), FontSize = 13, Placeholder = axes[i]
            });
            box.Submitted += text => SetSelectedPositionAxis(axis, text);
            _propsPositionBoxes[i] = box;

            var minus = _propsPanel.AddChild(new Button("-")
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(110, py), Size = new Vector2(52, 22), FontSize = 15
            });
            minus.Clicked += () => StepSelectedPositionAxis(axis, -1, true);
            AddRepeat(minus, first => StepSelectedPositionAxis(axis, -1, first));
            var plus = _propsPanel.AddChild(new Button("+")
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(166, py), Size = new Vector2(52, 22), FontSize = 15
            });
            plus.Clicked += () => StepSelectedPositionAxis(axis, +1, true);
            AddRepeat(plus, first => StepSelectedPositionAxis(axis, +1, first));
            py += 24f;
        }
        AddPropsSmallButton("To origin", new Vector2(14, py + 2), 66, MoveSelectedToOrigin);
        AddPropsSmallButton("To spawn", new Vector2(84, py + 2), 66, MoveSelectedToSpawn);
        AddPropsSmallButton("Ground", new Vector2(154, py + 2), 72, GroundSelected);

        // Shape parameters (size, slope, steps... - built in EditorScene.Shape.cs)
        float y = BuildShapeSection(_propsPanel, py + 32f);

        // Material / texture section (built in EditorScene.Materials.cs)
        y = BuildMaterialSection(_propsPanel, y);

        // Rotation: one box per axis, plus quick 90-degree buttons for yaw.
        _propsPanel.AddChild(new Label("Rotation") { Anchor = Anchor.TopLeft, Position = new Vector2(14, y + 4), FontSize = 14 });
        _propsRotationBoxes = new TextBox[3];
        for (int axis = 0; axis < 3; axis++)
        {
            int captured = axis;
            var box = _propsPanel.AddChild(new TextBox
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(84 + axis * 48, y), Size = new Vector2(44, 24), FontSize = 13,
                Placeholder = captured == 0 ? "X" : captured == 1 ? "Y" : "Z"
            });
            box.Submitted += text => SetSelectedRotationAxis(captured, text);
            _propsRotationBoxes[axis] = box;
        }
        y += 28f;
        AddPropsSmallButton("Yaw -90", new Vector2(14, y), 66, () => AdjustSelectedRotation(-90f));
        AddPropsSmallButton("Yaw +90", new Vector2(84, y), 66, () => AdjustSelectedRotation(90f));
        AddPropsSmallButton("Reset", new Vector2(154, y), 72, ResetSelectedRotation);
        y += 30f;

        // Uniform scale (on top of the shape size)
        _propsPanel.AddChild(new Label("Scale") { Anchor = Anchor.TopLeft, Position = new Vector2(14, y + 4), FontSize = 14 });
        _propsScaleLabel = _propsPanel.AddChild(new Label("1.00")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(80, y + 4), FontSize = 14, Color = Color.FromHex("#ffd75e")
        });
        AddPropsButton("-", new Vector2(-64, y), () => AdjustSelectedScale(1f / 1.25f));
        AddPropsButton("+", new Vector2(-24, y), () => AdjustSelectedScale(1.25f));
        y += 32f;

        // Collision toggle
        _propsCollision = _propsPanel.AddChild(new CheckBox("Collision")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(14, y), Size = new Vector2(200, 24)
        });
        _propsCollision.Toggled += enabled =>
        {
            if (_selectedDef == null) return;
            PushUndo();
            _selectedDef.Collider = enabled ? null : new ColliderDef { Shape = "none" };
            if (enabled && _selectedDef.Type.Equals("model", StringComparison.OrdinalIgnoreCase))
                _selectedDef.Collider = new ColliderDef { Shape = "auto" };
            RebuildSelected();
            SetStatus(enabled ? "Collision enabled" : "Collision disabled");
        };
        y += 32f;

        var duplicateButton = _propsPanel.AddChild(new Button("Duplicate")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(14, y),
            Size = new Vector2(98, 30), FontSize = 14
        });
        duplicateButton.Clicked += DuplicateSelected;

        _propsSplitButton = _propsPanel.AddChild(new Button("Split model")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(118, y),
            Size = new Vector2(108, 30), FontSize = 14
        });
        _propsSplitButton.Clicked += () =>
        {
            if (_selectedDef is { Children.Count: > 0 })
                UngroupSelected();
            else
                SplitSelectedModel();
        };

        var deleteButton = _propsPanel.AddChild(new Button("Delete")
        {
            Anchor = Anchor.BottomLeft, Position = new Vector2(14, -10),
            Size = new Vector2(96, 30), FontSize = 14,
            NormalColor = Color.FromHex("#5a2020")
        });
        deleteButton.Clicked += () =>
        {
            if (DeleteSelection())
                return;                      // removed the whole multi-selection
            if (DeleteSelectedPart())
                return;                      // removed one part; keep the panel on the group
            if (_selected != null)
                DeleteBlock(_selected);
            CloseProperties();
        };

        var closeButton = _propsPanel.AddChild(new Button("Close")
        {
            Anchor = Anchor.BottomLeft, Position = new Vector2(116, -10),
            Size = new Vector2(110, 30), FontSize = 14
        });
        closeButton.Clicked += CloseProperties;
    }

    private void AddPropsButton(string text, Vector2 position, Action action)
    {
        var button = _propsPanel.AddChild(new Button(text)
        {
            Anchor = Anchor.TopRight, Position = position, Size = new Vector2(34, 30), FontSize = 15
        });
        button.Clicked += action;
    }

    private void AddPropsSmallButton(string text, Vector2 position, float width, Action action)
    {
        var button = _propsPanel.AddChild(new Button(text)
        {
            Anchor = Anchor.TopLeft, Position = position, Size = new Vector2(width, 24), FontSize = 12
        });
        button.Clicked += action;
    }

    // ---- Position editing ----

    private void RefreshPositionBoxes()
    {
        if (_selectedDef == null || _propsPositionBoxes.Length < 3)
            return;
        var p = _selectedDef.Position is { Length: >= 3 } existing ? existing : new float[3];
        for (int i = 0; i < 3; i++)
        {
            var box = _propsPositionBoxes[i];
            if (_canvas.FocusedElement != box)   // don't overwrite while the user is typing
                box.Text = p[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Applies one typed coordinate (accepts "1.5" and "1,5").</summary>
    private void SetSelectedPositionAxis(int axis, string text)
    {
        if (_selectedDef == null)
            return;
        if (!float.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            SetStatus($"\"{text}\" is not a number");
            RefreshPositionBoxes();
            return;
        }
        var p = _selectedDef.Position is { Length: >= 3 } existing ? (float[])existing.Clone() : new float[3];
        if (MathF.Abs(p[axis] - value) < 1e-6f)
            return;
        PushUndo();
        p[axis] = value;
        _selectedDef.Position = p;
        RebuildSelected();
        SetStatus($"{_selectedDef.Name ?? _selectedDef.Type} moved to ({p[0]:0.##}, {p[1]:0.##}, {p[2]:0.##})");
    }

    /// <summary>
    /// Nudges one coordinate by the current grid step (Shift = 4x) from the - / + buttons.
    /// Holding a button repeats this; only the first step of a hold enters the undo history.
    /// </summary>
    private void StepSelectedPositionAxis(int axis, int direction, bool pushUndo)
    {
        if (_selectedDef == null)
            return;
        float step = _gridSize * (Input.IsKeyDown(Key.LeftShift) || Input.IsKeyDown(Key.RightShift) ? 4f : 1f);

        // With several objects picked, the whole set moves by the same amount.
        var delta = axis switch
        {
            0 => new Vector3(step * direction, 0f, 0f),
            1 => new Vector3(0f, step * direction, 0f),
            _ => new Vector3(0f, 0f, step * direction),
        };
        if (EditSelection(def => MoveDef(def, delta), "moved"))
            return;

        var p = _selectedDef.Position is { Length: >= 3 } existing ? (float[])existing.Clone() : new float[3];
        if (pushUndo)
            PushUndo();
        p[axis] = MathF.Round((p[axis] + step * direction) * 1000f) / 1000f;
        _selectedDef.Position = p;
        RebuildSelected();
        RefreshPositionBoxes();
        SetStatus($"{"XYZ"[axis]} = {p[axis]:0.###}  (step {step:0.###} m)");
    }

    private void SetSelectedPosition(Vector3 position, string message)
    {
        if (_selectedDef == null)
            return;
        PushUndo();
        _selectedDef.Position = ToArray(position);
        RebuildSelected();
        SetStatus(message);
    }

    private void MoveSelectedToOrigin() => SetSelectedPosition(Vector3.Zero, "Moved to origin (0, 0, 0)");

    private void MoveSelectedToSpawn()
    {
        var spawn = _doc.Nodes.FirstOrDefault(n => n.Type.Equals("spawn", StringComparison.OrdinalIgnoreCase));
        var target = spawn?.Position is { Length: >= 3 } s ? new Vector3(s[0], s[1], s[2]) : Vector3.Zero;
        SetSelectedPosition(target, $"Moved to spawn point ({target.X:0.##}, {target.Y:0.##}, {target.Z:0.##})");
    }

    /// <summary>
    /// Shifts the object so its visual bounds sit centered on its X/Z position with the
    /// bottom on the ground (Y of the current position). Handy for imported maps/props
    /// whose pivot is far from their geometry.
    /// </summary>
    private void GroundSelected()
    {
        if (_selectedDef == null || _selected == null)
            return;
        var bounds = _selected.ComputeVisualBounds();
        if (bounds == null)
        {
            SetStatus("This object has no visual bounds to ground");
            return;
        }

        // Bounds are local to the node: transform their corners to world space.
        var transform = _selected.GlobalTransform;
        var (bMin, bMax) = bounds.Value;
        var worldMin = new Vector3(float.MaxValue);
        var worldMax = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? bMin.X : bMax.X, (i & 2) == 0 ? bMin.Y : bMax.Y, (i & 4) == 0 ? bMin.Z : bMax.Z);
            var world = transform.TransformPoint(corner);
            worldMin = Vector3.Min(worldMin, world);
            worldMax = Vector3.Max(worldMax, world);
        }

        var current = _selectedDef.Position is { Length: >= 3 } p ? new Vector3(p[0], p[1], p[2]) : Vector3.Zero;
        var center = (worldMin + worldMax) * 0.5f;
        // Move so that the geometry's XZ center lands on the current XZ position and its bottom on current Y.
        var offset = new Vector3(current.X - center.X, current.Y - worldMin.Y, current.Z - center.Z);
        SetSelectedPosition(current + offset,
            $"Grounded: geometry centered at ({current.X:0.##}, {current.Z:0.##}), bottom at Y={current.Y:0.##}");
    }

    private void OpenProperties(Node3D node)
    {
        if (!_defByNode.TryGetValue(node, out var def))
            return;

        _selected = node;
        _selectedDef = def;
        _selectedPath = new List<int>();
        _propsPanel.Visible = true;
        RefreshPropertiesPanel();
    }

    private void CloseProperties()
    {
        _selected = null;
        _selectedDef = null;
        _propsPanel.Visible = false;
    }

    private void RefreshPropertiesPanel()
    {
        if (_selectedDef == null)
            return;

        int selected = SelectionCount();
        _propsTitle.Text = selected > 1
            ? $"{selected} objects selected"
            : _selectedDef.Name ?? _selectedDef.Type;
        float scale = _selectedDef.Scale is { Length: > 0 } s ? s[0] : 1f;
        _propsScaleLabel.Text = scale.ToString("0.##");
        _propsCollision.Checked = !(_selectedDef.Collider?.Shape.Equals("none", StringComparison.OrdinalIgnoreCase) ?? false);
        RefreshPositionBoxes();
        RefreshRotationBoxes();
        RefreshShapeSection();
        bool isModel = _selectedDef.Type.Equals("model", StringComparison.OrdinalIgnoreCase) && _selectedDef.SubNode == null;
        bool isGroup = _selectedDef.Children is { Count: > 0 };
        _propsSplitButton.Visible = isModel || isGroup;
        _propsSplitButton.Text = isGroup ? "Ungroup parts" : "Split model";
        RefreshMaterialSection();
    }

    /// <summary>Applies one typed rotation angle (degrees) to the selection.</summary>
    private void SetSelectedRotationAxis(int axis, string text)
    {
        if (_selectedDef == null)
            return;
        if (!float.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            SetStatus($"\"{text}\" is not a number");
            RefreshPropertiesPanel();
            return;
        }
        var r = _selectedDef.RotationDegrees is { Length: >= 3 } existing ? (float[])existing.Clone() : new float[3];
        value = (value % 360f + 360f) % 360f;
        if (MathF.Abs(r[axis] - value) < 1e-4f)
            return;
        PushUndo();
        r[axis] = value;
        _selectedDef.RotationDegrees = r[0] == 0f && r[1] == 0f && r[2] == 0f ? null : r;
        RebuildSelected();
        SetStatus($"Rotation: {r[0]:0.#}, {r[1]:0.#}, {r[2]:0.#}");
    }

    private void ResetSelectedRotation()
    {
        if (_selectedDef == null) return;
        PushUndo();
        _selectedDef.RotationDegrees = null;
        RebuildSelected();
        SetStatus("Rotation reset");
    }

    private void RefreshRotationBoxes()
    {
        if (_selectedDef == null || _propsRotationBoxes.Length < 3)
            return;
        var r = _selectedDef.RotationDegrees is { Length: >= 3 } existing ? existing : new float[3];
        for (int i = 0; i < 3; i++)
            if (_canvas.FocusedElement != _propsRotationBoxes[i])
                _propsRotationBoxes[i].Text = r[i].ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void AdjustSelectedScale(float factor)
    {
        if (_selectedDef == null) return;
        if (EditSelection(def => ScaleDef(def, factor), $"scaled x{factor:0.##}")) return;
        PushUndo();
        float current = _selectedDef.Scale is { Length: > 0 } s ? s[0] : 1f;
        float next = Mathf.Clamp(current * factor, 0.05f, 20f);
        _selectedDef.Scale = MathF.Abs(next - 1f) < 0.001f ? null : new[] { next, next, next };
        RebuildSelected();
    }

    private void AdjustSelectedRotation(float deltaDegrees)
    {
        if (_selectedDef == null) return;
        if (EditSelection(def => RotateDef(def, deltaDegrees), $"rotated {deltaDegrees:0}deg")) return;
        PushUndo();
        var r = _selectedDef.RotationDegrees is { Length: >= 3 } existing
            ? (float[])existing.Clone()
            : new float[3];
        r[1] = (r[1] + deltaDegrees + 360f) % 360f;
        _selectedDef.RotationDegrees = r[0] == 0f && r[1] == 0f && r[2] == 0f ? null : r;
        RebuildSelected();
    }

    /// <summary>Moves the selected object by a world offset (arrow keys / PgUp / PgDn).</summary>
    private void NudgeSelected(Vector3 offset)
    {
        if (_selectedDef == null) return;
        if (EditSelection(def => MoveDef(def, offset), "moved")) return;
        PushUndo();
        var p = _selectedDef.Position is { Length: >= 3 } existing ? (float[])existing.Clone() : new float[3];
        p[0] += offset.X; p[1] += offset.Y; p[2] += offset.Z;
        _selectedDef.Position = p;
        RebuildSelected();
    }

    /// <summary>Clones the selected object one footprint further along +X and selects the copy.</summary>
    /// <summary>
    /// Replaces the selected model with one object per mesh node of its file (each a
    /// "model" def with <see cref="NodeDef.SubNode"/>), so parts of an imported map can be
    /// moved, re-textured or deleted individually.
    /// </summary>
    private void SplitSelectedModel()
    {
        if (_selectedDef == null || !_selectedDef.Type.Equals("model", StringComparison.OrdinalIgnoreCase)
            || _selectedDef.SubNode != null || _selectedDef.Path == null)
            return;

        List<string> parts;
        try
        {
            parts = Assets.LoadModel(_selectedDef.Path).MeshNodeNames.ToList();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not read model parts: {ex.Message}");
            return;
        }
        if (parts.Count <= 1)
        {
            SetStatus("This model has a single mesh - nothing to split");
            return;
        }

        PushUndo();
        var source = _selectedDef;
        string sourceJson = System.Text.Json.JsonSerializer.Serialize(source, SceneDocument.JsonOptions);
        int insertAt = _doc.Nodes.IndexOf(source);
        _doc.Nodes.Remove(source);
        CloseProperties();

        var pieces = new List<NodeDef>();
        foreach (var part in parts)
        {
            var piece = System.Text.Json.JsonSerializer.Deserialize<NodeDef>(sourceJson, SceneDocument.JsonOptions)!;
            piece.SubNode = part;
            piece.Name = $"{source.Name ?? "Model"}.{part}";
            pieces.Add(piece);
        }
        _doc.Nodes.InsertRange(insertAt < 0 ? _doc.Nodes.Count : insertAt, pieces);
        RebuildWorld();
        SetStatus($"Split {source.Name ?? source.Type} into {pieces.Count} pieces - right-click any of them to edit it");
    }

    private void DuplicateSelected()
    {
        if (_selectedDef == null) return;
        if (DuplicateSelection())
            return;
        if (DuplicateSelectedPart())
            return;
        PushUndo();

        var copy = System.Text.Json.JsonSerializer.Deserialize<NodeDef>(
            System.Text.Json.JsonSerializer.Serialize(_selectedDef, SceneDocument.JsonOptions), SceneDocument.JsonOptions)!;
        var p = copy.Position is { Length: >= 3 } existing ? (float[])existing.Clone() : new float[3];
        float step = MathF.Max(_gridSize, _selected?.ComputeVisualBounds() is { } b ? MathF.Round(b.Max.X - b.Min.X) : 1f);
        p[0] += step;
        copy.Position = p;
        if (copy.Name != null)
            copy.Name = $"{copy.Name.Split('_')[0]}_{_doc.Nodes.Count}";

        _doc.Nodes.Add(copy);
        var visual = BuildEditorVisual(copy);
        if (visual != null)
        {
            _world.AddChild(visual);
            _defByNode[visual] = copy;
            RegisterParts(visual, copy);
            OpenProperties(visual);
        }
        SetStatus($"Duplicated {copy.Name ?? copy.Type}");
    }

    /// <summary>Rebuilds the selected object's editor visual after its definition changed.</summary>
    private void RebuildSelected()
    {
        if (_selected != null)
            RebuildSelectedPart();
        RefreshPropertiesPanel();
    }

    private void SelectItem(BlockItem item)
    {
        LeaveFreeMouse();
        _selectedItem = item;
        foreach (var (button, blockItem) in _itemButtons)
            button.NormalColor = blockItem == item ? UITheme.AccentColor.Darkened(0.3f) : UITheme.PanelColor;
        RebuildGhost();
        UpdateSelectionLabel();
    }

    private void UpdateSelectionLabel()
    {
        string extra = _selectedItem.ModelPath != null
            ? $"scale {_placeScale:0.##}  rot {_rotationY:0}"
            : $"[{_selectedMaterial}]  rot {_rotationY:0}";
        _selectionLabel.Text = $"{_selectedItem.Label}\n{extra}  grid {_gridSize:0.##}m";
    }

    // ------------------------------------------------------------------
    // Model import (FBX / OBJ / glTF props)
    // ------------------------------------------------------------------

    private void ImportModel(string rawPath)
    {
        var path = rawPath.Trim().Trim('"');
        if (path.Length == 0)
            return;

        if (!File.Exists(path))
        {
            SetStatus($"File not found: {path}");
            return;
        }
        if (!ModelExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            SetStatus($"Unsupported model format: {Path.GetExtension(path)}");
            return;
        }

        // Already imported? Just select it.
        var existing = _modelItems.FirstOrDefault(m =>
            string.Equals(m.ModelPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectItem(existing);
            return;
        }

        try
        {
            var model = Assets.LoadModel(path);
            var probe = model.Instantiate();
            var bounds = probe.ComputeVisualBounds() ?? (new Vector3(-0.5f), new Vector3(0.5f));
            var size = bounds.Item2 - bounds.Item1;

            var item = MakeModelItem(path, bounds);
            _modelItems.Add(item);
            AddModelButton(item);
            SelectItem(item);
            _importBox.Text = "";

            int pieces = model.MeshNodeNames.Distinct().Count();
            if (pieces > 1)
            {
                AddPackButton(path, pieces);
                SetStatus($"Imported {Path.GetFileName(path)} - {pieces} pieces. Use \"Pieces...\" to place them one by one.");
            }
            else
            {
                SetStatus($"Imported {Path.GetFileName(path)}  ({size.X:0.#} x {size.Y:0.#} x {size.Z:0.#} m)");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Model import failed: {ex}");
            SetStatus($"Import failed: {ex.Message}");
        }
    }

    private BlockItem MakeModelItem(string path, (Vector3 Min, Vector3 Max) bounds) => new(
        Path.GetFileNameWithoutExtension(path),
        new MeshDef(),   // ghost comes from the real model, not from a primitive
        0f,
        (e, p) => new NodeDef
        {
            Type = "model",
            Path = path,
            Position = ToArray(p),
            RotationDegrees = e._rotationY != 0f ? new[] { 0f, e._rotationY, 0f } : null,
            Scale = MathF.Abs(e._placeScale - 1f) > 0.001f
                ? new[] { e._placeScale, e._placeScale, e._placeScale }
                : null,
            Collider = new ColliderDef { Shape = "auto" }
        })
    {
        ModelPath = path,
        Bounds = bounds
    };

    /// <summary>Modular kits get a second button that opens the piece browser.</summary>
    private void AddPackButton(string path, int pieces)
    {
        var button = _paletteStack.AddChild(new Button($"  Pieces... ({pieces})")
        {
            Size = new Vector2(170, 22), FontSize = 11, Anchor = Anchor.TopCenter,
            NormalColor = Color.FromHex("#2b4a5e")
        });
        button.Clicked += () => OpenPackPanel(path);
    }

    private void AddModelButton(BlockItem item)
    {
        var button = _paletteStack.AddChild(new Button(Truncate(item.Label, 18))
        {
            Size = new Vector2(170, 24), FontSize = 12, Anchor = Anchor.TopCenter
        });
        _itemButtons[button] = item;
        button.Clicked += () => SelectItem(item);
    }

    // The UI font has no ellipsis glyph, so ".." it is.
    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 2)] + "..";

    /// <summary>
    /// Shortens from the middle. Pack pieces are named like "modern_suv.050_low.003_gl_0":
    /// cutting the tail would leave every button reading the same thing.
    /// </summary>
    private static string TruncateMiddle(string text, int max)
    {
        if (text.Length <= max)
            return text;
        int tail = Math.Max(4, (max - 2) / 2);
        int head = Math.Max(1, max - 2 - tail);
        return text[..head] + ".." + text[^tail..];
    }

    /// <summary>Re-imports every model referenced by the loaded document into the palette.</summary>
    private void RefreshModelPaletteFromDoc()
    {
        var paths = CollectModelPaths(_doc.Nodes).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (File.Exists(path))
                ImportModel(path);
            else
                SetStatus($"Model in scene not found on disk: {path}");
        }
    }

    private static IEnumerable<string> CollectModelPaths(List<NodeDef> defs)
    {
        foreach (var def in defs)
        {
            if (def.Type.Equals("model", StringComparison.OrdinalIgnoreCase) && def.Path != null)
                yield return def.Path;
            if (def.Children != null)
                foreach (var nested in CollectModelPaths(def.Children))
                    yield return nested;
        }
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
        var color = Color.FromHex("#ffd75e");
        _statusLabel.Color = color;
        Tree!.CreateTween().Wait(2.5f).To(c => _statusLabel.Color = c, color, color.WithAlpha(0.3f), 0.5f);
    }

    // ------------------------------------------------------------------
    // Ghost preview
    // ------------------------------------------------------------------

    private static readonly Material GhostMaterial = new()
    {
        Albedo = Color.FromHex("#4f8fea66"),
        Transparent = true,
        Unshaded = true,
        CastShadows = false
    };

    private void RebuildGhost()
    {
        if (_ghost != null)
        {
            _ghost.RemoveFromParent();
            _ghost = null;
        }

        if (_selectedItem.Prefab is { } prefab)
        {
            // Preview the whole assembly, tinted translucent blue.
            var instance = SceneLoader.InstantiateNode(_doc, prefab.Node);
            if (instance != null)
            {
                // The preview must be invisible to physics: a prefab is made of static bodies,
                // and the placement ray would otherwise hit the ghost that follows the cursor.
                foreach (var body in instance.Descendants().OfType<PhysicsBody3D>()
                             .Concat(instance is PhysicsBody3D root ? new[] { root } : Array.Empty<PhysicsBody3D>()))
                    body.Active = false;
                foreach (var meshInstance in instance.Descendants().OfType<MeshInstance3D>()
                             .Concat(instance is MeshInstance3D self ? new[] { self } : Array.Empty<MeshInstance3D>()))
                {
                    meshInstance.FrustumCullingEnabled = false;
                    foreach (var surface in meshInstance.Surfaces)
                        surface.Material = GhostMaterial;
                }
                _ghost = AddChild(instance);
                _ghost.Visible = false;
                return;
            }
        }

        if (_selectedItem.ModelPath != null)
        {
            // Preview the actual imported model (or the chosen pack piece), tinted blue.
            var model = Assets.LoadModel(_selectedItem.ModelPath);
            var instance = _selectedItem.SubNode != null
                ? model.InstantiatePart(_selectedItem.SubNode, recenter: true) ?? model.Instantiate()
                : model.Instantiate();
            var meshInstances = instance.Descendants().OfType<MeshInstance3D>().ToList();
            if (instance is MeshInstance3D self)
                meshInstances.Add(self);
            foreach (var meshInstance in meshInstances)
            {
                meshInstance.FrustumCullingEnabled = false;
                foreach (var surface in meshInstance.Surfaces)
                    surface.Material = GhostMaterial;
            }
            _ghost = AddChild(instance);
        }
        else
        {
            _ghost = AddChild(new MeshInstance3D(
                SceneLoader.BuildMesh(_selectedItem.GhostMesh), GhostMaterial)
            {
                FrustumCullingEnabled = false
            });
        }

        _ghost.Visible = false;
    }

    /// <summary>Vertical offset from the placement surface to the node origin.</summary>
    private float EffectiveYOffset()
    {
        if (_selectedItem.Bounds is { } bounds && (_selectedItem.ModelPath != null || _selectedItem.Prefab != null))
            return -bounds.Min.Y * _placeScale;   // rest the base on the surface
        return _selectedItem.YOffset;
    }

    // ------------------------------------------------------------------
    // Frame update: picking, placing, deleting, shortcuts
    // ------------------------------------------------------------------

    private Camera3D ActiveCamera => _topDown ? _topCamera : _camera;

    protected override void OnUpdate(float dt)
    {
        while (_pending.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Log.Error($"Editor action failed: {ex}"); SetStatus($"Error: {ex.Message}"); }
        }

        // Shortcuts that work in both modes
        if (Input.IsKeyPressed(Key.Tab))
        {
            TogglePlay();
            return;
        }

        if (_playing)
            return;

        // Keep the sidebar as tall as the window and let the wheel scroll it.
        _sidebar.Size = new Vector2(_sidebar.Size.X, MathF.Max(200f, Engine.WindowSize.Y - 46f - 34f));
        _sidebar.HandleWheel(Input.MousePosition, Input.ScrollDelta.Y);

        UpdateSteppers(dt);
        UpdateAutosave(dt);

        DebugDraw.Grid(24, 1f, Color.White.WithAlpha(0.08f));
        DebugDraw.Axes(Matrix4x4.Identity, 1.5f);
        DrawReachability();

        _rmbHeld = Input.IsMouseButtonDown(MouseButton.Right) ? _rmbHeld + dt : 0f;

        bool typing = _canvas.FocusedElement is TextBox;
        _camera.Active = !_topDown && !typing;
        _topCamera.Active = _topDown && !typing;

        if (typing)
        {
            UpdatePlacement();
            return;
        }

        bool ctrl = Input.IsKeyDown(Key.LeftControl) || Input.IsKeyDown(Key.RightControl);
        bool shift = Input.IsKeyDown(Key.LeftShift) || Input.IsKeyDown(Key.RightShift);

        if (ctrl && Input.IsKeyPressed(Key.S)) { SaveDocument(); return; }
        if (ctrl && Input.IsKeyPressed(Key.O)) { LoadDocument(); return; }
        if (ctrl && Input.IsKeyPressed(Key.N)) { NewDocument(); return; }
        if (ctrl && Input.IsKeyPressed(Key.E)) { ExportGlb(); return; }
        if (ctrl && Input.IsKeyPressed(Key.D)) { DuplicateSelected(); return; }
        if (ctrl && Input.IsKeyPressed(Key.Z)) { if (shift) Redo(); else Undo(); return; }
        if (ctrl && Input.IsKeyPressed(Key.Y)) { Redo(); return; }
        if (ctrl && Input.IsKeyPressed(Key.C)) { CopySelection(); return; }
        if (ctrl && Input.IsKeyPressed(Key.V)) { PasteClipboard(); return; }
        if (ctrl && Input.IsKeyPressed(Key.X)) { CutSelection(); return; }

        if (Input.IsKeyPressed(Key.R))
        {
            _rotationY = (_rotationY + 90f) % 360f;
            UpdateSelectionLabel();
        }
        if (Input.IsKeyPressed(Key.G))
        {
            int index = Array.IndexOf(GridSizes, _gridSize);
            _gridSize = GridSizes[(index + 1) % GridSizes.Length];
            UpdateSelectionLabel();
            SetStatus($"Grid: {_gridSize:0.##} m");
        }
        if (Input.IsKeyPressed(Key.T))
            ToggleTopDown();
        if (Input.IsKeyPressed(Key.Escape))
            HandleEscape();
        if (Input.IsKeyPressed(Key.P))
            TogglePackPanel();
        if (Input.IsKeyPressed(Key.F))
            ToggleReachability();
        if (Input.IsKeyPressed(Key.B))
        {
            if (ctrl)
                SaveSelectionAsPrefab();
            else
                TogglePrefabPanel();
        }

        // = / - adjust the placement scale of imported models.
        if (Input.IsKeyPressed(Key.Equal) || Input.IsKeyPressed(Key.KeypadAdd))
        {
            _placeScale = MathF.Min(10f, _placeScale * 1.25f);
            UpdateSelectionLabel();
        }
        if (Input.IsKeyPressed(Key.Minus) || Input.IsKeyPressed(Key.KeypadSubtract))
        {
            _placeScale = MathF.Max(0.05f, _placeScale / 1.25f);
            UpdateSelectionLabel();
        }

        // Nudge the selected object.
        if (_selectedDef != null)
        {
            float step = _gridSize;
            if (Input.IsKeyPressed(Key.Left)) NudgeSelected(new Vector3(-step, 0f, 0f));
            if (Input.IsKeyPressed(Key.Right)) NudgeSelected(new Vector3(step, 0f, 0f));
            if (Input.IsKeyPressed(Key.Up)) NudgeSelected(new Vector3(0f, 0f, -step));
            if (Input.IsKeyPressed(Key.Down)) NudgeSelected(new Vector3(0f, 0f, step));
            if (Input.IsKeyPressed(Key.PageUp)) NudgeSelected(new Vector3(0f, 0.25f, 0f));
            if (Input.IsKeyPressed(Key.PageDown)) NudgeSelected(new Vector3(0f, -0.25f, 0f));
        }

        UpdateBoxSelect();
        UpdatePlacement();
    }

    private void ToggleTopDown()
    {
        _topDown = !_topDown;
        if (_topDown)
        {
            // Look over the point the fly camera is aimed at (roughly).
            var focus = _camera.GlobalPosition + _camera.GlobalForward * 12f;
            _topCamera.LookOver(new Vector3(focus.X, 0f, focus.Z));
            _topCamera.MakeCurrent();
            Input.SetMouseMode(MouseMode.Visible);
        }
        else
        {
            _camera.MakeCurrent();
        }
        _viewButton.Text = _topDown ? "Fly view (T)" : "Top view (T)";
        SetStatus(_topDown ? "Top-down view: WASD pan, wheel zoom, RMB-drag pan" : "Fly view");
    }

    private void UpdatePlacement()
    {
        _placeTarget = null;
        _hoveredNode = null;
        bool uiHovered = Canvas.IsMouseOverUI;
        bool looking = !_topDown && Input.IsMouseButtonDown(MouseButton.Right) && _rmbHeld >= RmbClickTime;
        bool ctrl = Input.IsKeyDown(Key.LeftControl) || Input.IsKeyDown(Key.RightControl);
        bool shift = Input.IsKeyDown(Key.LeftShift) || Input.IsKeyDown(Key.RightShift);

        // The move gizmo gets first dibs on the mouse when an object is selected.
        bool gizmoOwns = UpdateGizmo(uiHovered);

        if (!uiHovered && !_freeMouse && !gizmoOwns)
        {
            var (origin, direction) = ActiveCamera.ScreenPointToRay(Input.MousePosition);

            if (Engine.Physics.Raycast(origin, direction, 1000f, out var hit))
            {
                _hoveredNode = FindPlacedRoot(hit.Body);
                if (!looking && hit.Normal.Y > 0.5f)
                    _placeTarget = SnapToGrid(hit.Point);
            }
            else if (!looking && MathF.Abs(direction.Y) > 1e-4f)
            {
                // Fall back to the infinite ground plane (y = 0).
                float t = -origin.Y / direction.Y;
                if (t > 0f && t < 1000f)
                    _placeTarget = SnapToGrid(origin + direction * t);
            }

            // While painting or filling, stay on the plane where the stroke started
            // so newly placed blocks don't push the cursor upwards.
            if ((_painting || _rectStart != null) && MathF.Abs(direction.Y) > 1e-4f)
            {
                float planeY = _painting ? _paintPlaneY : _rectStart!.Value.Y;
                float t = (planeY - origin.Y) / direction.Y;
                _placeTarget = t > 0f ? SnapToGrid(origin + direction * t) with { Y = planeY } : null;
            }
        }

        // Right-CLICK (press + release without dragging) opens the properties panel;
        // holding the right button flies the camera / pans the map.
        if (Input.IsMouseButtonPressed(MouseButton.Right))
            _rmbDownPosition = Input.MousePosition;
        if (Input.IsMouseButtonReleased(MouseButton.Right) && !uiHovered
            && _rmbHeld < RmbClickTime && Vector2.Distance(_rmbDownPosition, Input.MousePosition) < 6f)
        {
            var part = AltDown ? PickPartAt(Input.MousePosition) : null;
            if (part != null)
                SelectPart(part);
            else if (_hoveredNode != null)
                OpenProperties(_hoveredNode);
            else
                CloseProperties();
        }

        // Highlight the selected object (and the rest of a multi-selection).
        if (_selected is { IsInsideTree: true } && _selected.ComputeVisualBounds() is { } bounds)
            DrawWorldBounds(_selected, bounds, Color.FromHex("#ffd75e"));
        DrawSelectionExtras();
        DrawGizmo();

        // Ghost follows the target (hidden while painting materials with Ctrl).
        if (_ghost != null)
        {
            _ghost.Visible = _placeTarget != null && !ctrl && _rectStart == null;
            if (_placeTarget is { } target)
            {
                _ghost.Position = target + new Vector3(0f, EffectiveYOffset(), 0f);
                _ghost.RotationDegrees = new Vector3(0f, _rotationY, 0f);
                _ghost.Scale = _selectedItem.ModelPath != null
                    ? new Vector3(_placeScale)
                    : Vector3.One;
            }
        }

        // Rectangle fill preview.
        if (_rectStart is { } start && _placeTarget is { } current)
        {
            var footprint = Footprint(_selectedItem);
            var min = Vector3.Min(start, current) - new Vector3(footprint.X * 0.5f, 0f, footprint.Y * 0.5f);
            var max = Vector3.Max(start, current) + new Vector3(footprint.X * 0.5f, 0.15f, footprint.Y * 0.5f);
            DebugDraw.Aabb(min, max, Color.FromHex("#4f8fea"));
        }

        // ---- Left mouse: paint material / place / paint-drag / rectangle fill ----
        if (_freeMouse && Input.IsMouseButtonReleased(MouseButton.Left) && !uiHovered
            && !_boxSelecting && !_boxJustSelected && !_gizmoConsumed)
        {
            var picked = AltDown ? PickPartAt(Input.MousePosition) : PickAt(Input.MousePosition);
            if (picked == null)
            {
                ClearMultiSelection();
                CloseProperties();
            }
            else if (AltDown)
            {
                ClearMultiSelection();
                SelectPart(picked);
            }
            else if (shift)
            {
                ToggleInSelection(picked);
            }
            else
            {
                ClearMultiSelection();
                OpenProperties(picked);
            }
        }
        else if (Input.IsMouseButtonPressed(MouseButton.Left) && !uiHovered && !gizmoOwns)
        {
            if (ctrl)
            {
                // Paint the exact part under the cursor - a group itself has no material.
                var target = PickPartAt(Input.MousePosition) ?? _hoveredNode;
                if (target != null)
                    ApplyMaterial(target, _selectedMaterial);
            }
            else if (_placeTarget is { } placePosition)
            {
                if (shift)
                {
                    _rectStart = placePosition;
                }
                else
                {
                    PushUndo();
                    PlaceBlock(placePosition);
                    _painting = true;
                    _paintPlaneY = placePosition.Y;
                    _lastPaintCell = placePosition;
                }
            }
        }
        else if (Input.IsMouseButtonDown(MouseButton.Left) && _painting && !ctrl
                 && _placeTarget is { } paintPosition && paintPosition != _lastPaintCell)
        {
            var footprint = Footprint(_selectedItem);
            var last = _lastPaintCell ?? paintPosition;
            // Only stamp when the cursor moved a whole footprint away (avoids overlapping blocks).
            if (MathF.Abs(paintPosition.X - last.X) >= footprint.X - 0.01f ||
                MathF.Abs(paintPosition.Z - last.Z) >= footprint.Y - 0.01f)
            {
                PlaceBlock(paintPosition);
                _lastPaintCell = paintPosition;
            }
        }

        if (Input.IsMouseButtonReleased(MouseButton.Left))
        {
            if (_rectStart is { } rectStart && _placeTarget is { } rectEnd)
                FillRectangle(rectStart, rectEnd);
            _rectStart = null;
            _painting = false;
            _lastPaintCell = null;
        }

        // Delete (not while typing a path in the import box)
        if (_canvas.FocusedElement is not TextBox && (Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Delete)))
        {
            if (AltDown && DeleteSelectedPart())
            {
                // removed one part of a group
            }
            else if (DeleteSelection())
            {
                // removed every selected object
            }
            else if (_hoveredNode != null)
                DeleteBlock(_hoveredNode);
            else if (_selected != null)
                DeleteBlock(_selected);
        }
    }

    /// <summary>The placed object under a screen position, or null.</summary>
    /// <summary>Alt reaches inside a prefab: the wall, the window band, the pavement itself.</summary>
    private static bool AltDown => Input.IsKeyDown(Key.LeftAlt) || Input.IsKeyDown(Key.RightAlt);

    /// <summary>Outlines a node's visual bounds in world space.</summary>
    private static void DrawWorldBounds(Node3D node, (Vector3 Min, Vector3 Max) bounds, Color color)
    {
        var transform = node.GlobalTransform;
        var (bMin, bMax) = bounds;
        var worldMin = new Vector3(float.MaxValue);
        var worldMax = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? bMin.X : bMax.X,
                (i & 2) == 0 ? bMin.Y : bMax.Y,
                (i & 4) == 0 ? bMin.Z : bMax.Z);
            var world = transform.TransformPoint(corner);
            worldMin = Vector3.Min(worldMin, world);
            worldMax = Vector3.Max(worldMax, world);
        }
        DebugDraw.Aabb(worldMin, worldMax, color);
    }

    private Node3D? PickAt(Vector2 screenPosition)
    {
        var (origin, direction) = ActiveCamera.ScreenPointToRay(screenPosition);
        return Engine.Physics.Raycast(origin, direction, 1000f, out var hit) ? FindPlacedRoot(hit.Body) : null;
    }

    /// <summary>The individual part under the cursor (a wall of a prefab, not the whole prefab).</summary>
    private Node3D? PickPartAt(Vector2 screenPosition)
    {
        var (origin, direction) = ActiveCamera.ScreenPointToRay(screenPosition);
        return Engine.Physics.Raycast(origin, direction, 1000f, out var hit) ? FindPart(hit.Body) : null;
    }

    private Vector3 SnapToGrid(Vector3 point) => new(
        MathF.Round(point.X / _gridSize) * _gridSize,
        MathF.Round(point.Y / VerticalSnap) * VerticalSnap,   // fine vertical snap so stacking is exact
        MathF.Round(point.Z / _gridSize) * _gridSize);

    private Node3D? FindPlacedRoot(Node node)
    {
        Node? current = node;
        while (current != null && current.Parent != _world)
            current = current.Parent;
        return current as Node3D;
    }

    private void PlaceBlock(Vector3 basePosition)
    {
        var position = basePosition + new Vector3(0f, EffectiveYOffset(), 0f);
        var def = _selectedItem.CreateDef(this, position);
        def.Name ??= $"{_selectedItem.Label.Split(' ')[0]}_{_doc.Nodes.Count}";

        // Only one spawn point: replace the previous one and keep the player on it.
        if (def.Type.Equals("spawn", StringComparison.OrdinalIgnoreCase))
        {
            _doc.Nodes.RemoveAll(n => n.Type.Equals("spawn", StringComparison.OrdinalIgnoreCase));
            var player = _doc.Nodes.FirstOrDefault(n => n.Type.Equals("player", StringComparison.OrdinalIgnoreCase));
            if (player != null && def.Position != null)
                player.Position = new[] { def.Position[0], def.Position[1] + 1.2f, def.Position[2] };
            _doc.Nodes.Add(def);
            RebuildWorld();
            SetStatus("Spawn point moved");
            return;
        }

        _doc.Nodes.Add(def);
        var visual = BuildEditorVisual(def);
        if (visual != null)
        {
            _world.AddChild(visual);
            _defByNode[visual] = def;
            RegisterParts(visual, def);
        }
        SetStatus($"Placed {def.Name}  ({_doc.Nodes.Count} nodes)");
    }

    /// <summary>Fills the XZ rectangle between two snapped points with the selected block (Shift+drag).</summary>
    private void FillRectangle(Vector3 a, Vector3 b)
    {
        var footprint = Footprint(_selectedItem);
        float stepX = MathF.Max(_gridSize, footprint.X);
        float stepZ = MathF.Max(_gridSize, footprint.Y);
        float minX = MathF.Min(a.X, b.X), maxX = MathF.Max(a.X, b.X);
        float minZ = MathF.Min(a.Z, b.Z), maxZ = MathF.Max(a.Z, b.Z);

        int countX = (int)MathF.Floor((maxX - minX) / stepX + 0.001f) + 1;
        int countZ = (int)MathF.Floor((maxZ - minZ) / stepZ + 0.001f) + 1;
        const int limit = 600;
        if (countX * countZ > limit)
        {
            SetStatus($"Rectangle too large ({countX * countZ} blocks, limit {limit}) - zoom in or use bigger blocks");
            return;
        }

        PushUndo();
        for (int ix = 0; ix < countX; ix++)
            for (int iz = 0; iz < countZ; iz++)
                PlaceBlock(new Vector3(minX + ix * stepX, a.Y, minZ + iz * stepZ));
        SetStatus($"Filled {countX * countZ} x {_selectedItem.Label}  ({_doc.Nodes.Count} nodes)");
    }

    private void DeleteBlock(Node3D placedRoot)
    {
        if (!_defByNode.TryGetValue(placedRoot, out var def))
            return;

        PushUndo();
        if (_selected == placedRoot)
            CloseProperties();

        _doc.Nodes.Remove(def);
        _defByNode.Remove(placedRoot);
        ForgetParts(placedRoot);
        placedRoot.QueueFree();
        SetStatus($"Deleted {def.Name ?? def.Type}");
    }

    // ------------------------------------------------------------------
    // Export GLB (the map as a glTF binary for Godot / Blender / any DCC)
    // ------------------------------------------------------------------

    /// <summary>Folder where exports land: &lt;project&gt;/Export or next to the scene file.</summary>
    private string ExportRoot => Path.Combine(
        _projectDirectory ?? Path.GetDirectoryName(Path.GetFullPath(_savePath)) ?? ".", "Export");

    private static string MakeFileSafe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return safe.Length == 0 ? "Map" : safe;
    }

    // ------------------------------------------------------------------
    // File actions
    // ------------------------------------------------------------------

    /// <summary>
    /// Last-chance save used by <see cref="CrashReporter"/>: writes the in-memory document to
    /// "&lt;scene&gt;.recovered.json" so a crash never costs the user their work.
    /// </summary>
    private string? RescueDocument()
    {
        try
        {
            string path = Path.ChangeExtension(Path.GetFullPath(_savePath), null) + ".recovered.json";
            File.WriteAllText(path, _doc.ToJson());
            return $"Your unsaved map was rescued to:\n{path}";
        }
        catch (Exception ex)
        {
            Log.Error($"Could not rescue the document: {ex}");
            return null;
        }
    }

    private void SaveDocument()
    {
        _doc.Save(_savePath);
        _dirty = false;
        _idleSinceChange = 0f;
        _sinceLastSave = 0f;
        SetStatus($"Saved {Path.GetFullPath(_savePath)}");
    }

    private void LoadDocument()
    {
        if (!File.Exists(_savePath))
        {
            SetStatus($"File not found: {_savePath}");
            return;
        }
        PushUndo();
        _doc = SceneDocument.Load(_savePath);
        AfterDocumentReplaced();
        RefreshModelPaletteFromDoc();
        SetStatus($"Loaded {_savePath} - {_doc.Nodes.Count} nodes");
    }

    private void NewDocument()
    {
        PushUndo();
        _doc = CreateDefaultDocument();
        AfterDocumentReplaced();
        SetStatus("New map");
    }

    /// <summary>Refreshes everything derived from the document (world, palettes, selection).</summary>
    private void AfterDocumentReplaced()
    {
        CloseProperties();
        ApplyDocumentEnvironment();
        RebuildWorld();
        RebuildMaterialPalette();
        if (!_doc.Materials.ContainsKey(_selectedMaterial))
            SelectMaterial(_doc.Materials.Keys.FirstOrDefault() ?? "stone");
        UpdateSelectionLabel();
    }

    // ------------------------------------------------------------------
    // Play mode
    // ------------------------------------------------------------------

    private void TogglePlay()
    {
        if (_playing)
            ExitPlayMode();
        else
            EnterPlayMode();
    }

    private void EnterPlayMode()
    {
        _playing = true;
        _painting = false;
        _rectStart = null;

        // Remove the editor world so its colliders and visuals disappear,
        // then instantiate the real scene from the document.
        RemoveChild(_world);
        if (_ghost != null)
            _ghost.Visible = false;
        _camera.Active = false;
        _topCamera.Active = false;
        _sidebar.Visible = false;
        _propsPanel.Visible = false;

        _playRoot = SceneLoader.Instantiate(_doc);
        AddChild(_playRoot);

        _modeLabel.Text = "PLAY MODE - Tab to return";
        _modeLabel.Color = Color.FromHex("#ffd75e");
        SetStatus("Playing...");
    }

    private void ExitPlayMode()
    {
        _playing = false;

        if (_playRoot != null)
        {
            RemoveChild(_playRoot);
            _playRoot = null;
        }

        AddChild(_world);
        ActiveCamera.MakeCurrent();
        _sidebar.Visible = true;
        _propsPanel.Visible = _selectedDef != null;
        Input.SetMouseMode(MouseMode.Visible);

        _modeLabel.Text = "EDIT MODE";
        _modeLabel.Color = Color.FromHex("#8fd48f");
        SetStatus("Back to editing");
    }
}
