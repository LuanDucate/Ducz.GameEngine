using System.Numerics;
using System.Text.Json;
using Ducz.Rendering;
using Ducz.Serialization;
using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// Materials & textures: the swatch palette in the sidebar, the material section of
/// the properties panel, importing image files as materials (dialog or drag & drop)
/// and per-material tiling / filtering.
/// </summary>
partial class EditorScene
{
    private const float PaletteSwatchSize = 38f;
    private const float PropsSwatchSize = 32f;
    private const float SwatchGap = 4f;
    private const int PropsMaxSwatches = 12;   // 6 columns x 2 rows fit the panel

    private Panel _materialGrid = null!;
    private Label _materialNameLabel = null!;
    private readonly List<MaterialSwatch> _paletteSwatches = new();

    private Panel _propsMaterialGrid = null!;
    private Label _propsMaterialLabel = null!;
    private Label _propsTilingLabel = null!;
    private CheckBox _propsNearest = null!;
    private Label _propsNormalLabel = null!;
    private Label _propsNormalStrengthLabel = null!;
    private readonly List<MaterialSwatch> _propsSwatches = new();
    private readonly List<UINode> _materialSectionWidgets = new();

    /// <summary>Thumbnail textures keyed by the material definition (JSON), so edits regenerate them.</summary>
    private readonly Dictionary<string, Texture2D?> _thumbnails = new();

    private static readonly string[] MaterialNodeTypes =
        { "mesh", "static", "rigid", "area", "floor", "wall", "ramp", "crate", "terrain", "model" };

    private static bool SupportsMaterial(string type) => MaterialNodeTypes.Contains(type.ToLowerInvariant());

    // ------------------------------------------------------------------
    // Sidebar palette
    // ------------------------------------------------------------------

    private void BuildMaterialPalette(VStack stack)
    {
        stack.AddChild(new Label("MATERIALS / TEXTURES") { FontSize = 13, Color = Color.White.WithAlpha(0.5f), Anchor = Anchor.TopCenter });

        _materialGrid = stack.AddChild(new Panel
        {
            Anchor = Anchor.TopCenter,
            Size = new Vector2(170, PaletteSwatchSize),
            BackgroundColor = Color.Transparent,
            BorderColor = Color.Transparent
        });
        _materialNameLabel = stack.AddChild(new Label("")
        {
            FontSize = 12, Color = Color.White.WithAlpha(0.7f), Anchor = Anchor.TopCenter
        });

        var addButton = stack.AddChild(new Button("+ Texture file...")
        {
            Size = new Vector2(170, 26), FontSize = 13, Anchor = Anchor.TopCenter
        });
        addButton.Clicked += () => FileDialogs.OpenFileAsync("Choose a texture image", FileDialogs.ImageFilter,
            TexturesDirectory, path => Post(() => ImportImage(path, null)));

        stack.AddChild(new Label("(or drop an image on an object)")
        {
            FontSize = 11, Color = Color.White.WithAlpha(0.35f), Anchor = Anchor.TopCenter
        });
    }

    /// <summary>Rebuilds both swatch grids from the document's materials.</summary>
    private void RebuildMaterialPalette()
    {
        FillSwatchGrid(_materialGrid, _paletteSwatches, 170f, PaletteSwatchSize, int.MaxValue,
            key => SelectMaterial(key), key => _materialNameLabel.Text = key);
        if (_propsMaterialGrid != null)
            FillSwatchGrid(_propsMaterialGrid, _propsSwatches, 212f, PropsSwatchSize, PropsMaxSwatches,
                ApplyMaterialToSelected, key => _propsMaterialLabel.Text = key);
        RefreshSwatchSelection();
        RefreshMaterialSection();
    }

    private void FillSwatchGrid(Panel grid, List<MaterialSwatch> list, float width, float size, int max,
        Action<string> onClick, Action<string> onHover)
    {
        grid.ClearChildren();
        list.Clear();

        int columns = Math.Max(1, (int)((width + SwatchGap) / (size + SwatchGap)));
        int index = 0;
        foreach (var (key, def) in _doc.Materials)
        {
            if (index >= max)
                break;
            int column = index % columns, row = index / columns;
            var swatch = new MaterialSwatch(key, size)
            {
                Anchor = Anchor.TopLeft,
                Position = new Vector2(column * (size + SwatchGap), row * (size + SwatchGap))
            };
            ApplyThumbnail(swatch, def);
            string captured = key;
            swatch.Clicked += () => onClick(captured);
            swatch.MouseEntered += () => onHover(captured);
            grid.AddChild(swatch);
            list.Add(swatch);
            index++;
        }

        int rows = Math.Max(1, (index + columns - 1) / columns);
        grid.Size = new Vector2(width, rows * (size + SwatchGap) - SwatchGap);
    }

    private void ApplyThumbnail(MaterialSwatch swatch, MaterialDef def)
    {
        string cacheKey = JsonSerializer.Serialize(def, SceneDocument.JsonOptions);
        if (!_thumbnails.TryGetValue(cacheKey, out var texture))
        {
            texture = null;
            if (def.Texture != null || def.Checkerboard != null)
            {
                try
                {
                    texture = SceneLoader.BuildMaterial(def).AlbedoTexture;
                }
                catch (Exception ex)
                {
                    Log.Warning($"Thumbnail for material failed: {ex.Message}");
                }
            }
            _thumbnails[cacheKey] = texture;
        }

        Color albedo;
        try { albedo = def.Albedo != null ? Color.FromHex(def.Albedo) : Color.White; }
        catch { albedo = Color.White; }

        swatch.Texture = texture;
        swatch.Tint = texture != null ? albedo.WithAlpha(1f) : albedo;
        swatch.Emissive = def.Emission != null;
    }

    private void SelectMaterial(string key)
    {
        _selectedMaterial = key;
        RefreshSwatchSelection();
        UpdateSelectionLabel();
        _materialNameLabel.Text = key;
    }

    private void RefreshSwatchSelection()
    {
        foreach (var swatch in _paletteSwatches)
            swatch.Selected = string.Equals(swatch.Key, _selectedMaterial, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Properties panel section
    // ------------------------------------------------------------------

    /// <summary>Adds the material widgets to the properties panel starting at <paramref name="y"/>; returns the next free y.</summary>
    private float BuildMaterialSection(Panel panel, float y)
    {
        _materialSectionWidgets.Clear();

        UINode Track(UINode node) { _materialSectionWidgets.Add(node); return node; }

        Track(panel.AddChild(new Label("Material") { Anchor = Anchor.TopLeft, Position = new Vector2(14, y), FontSize = 14 }));
        _propsMaterialLabel = (Label)Track(panel.AddChild(new Label("-")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(84, y), FontSize = 14, Color = Color.FromHex("#ffd75e")
        }));
        y += 20f;

        _propsMaterialGrid = (Panel)Track(panel.AddChild(new Panel
        {
            Anchor = Anchor.TopLeft,
            Position = new Vector2(14, y),
            Size = new Vector2(212, PropsSwatchSize),
            BackgroundColor = Color.Transparent,
            BorderColor = Color.Transparent
        }));
        y += 2 * (PropsSwatchSize + SwatchGap) - SwatchGap + 6f;   // room for 2 rows

        var textureButton = (Button)Track(panel.AddChild(new Button("Texture file...")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(14, y), Size = new Vector2(104, 26), FontSize = 13
        }));
        textureButton.Clicked += () => FileDialogs.OpenFileAsync("Choose a texture for this object", FileDialogs.ImageFilter,
            TexturesDirectory, path => Post(() => ImportImage(path, _selected)));

        var applyButton = (Button)Track(panel.AddChild(new Button("Use palette mat.")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(122, y), Size = new Vector2(104, 26), FontSize = 13
        }));
        applyButton.Clicked += () => ApplyMaterialToSelected(_selectedMaterial);
        y += 32f;

        Track(panel.AddChild(new Label("Tiling") { Anchor = Anchor.TopLeft, Position = new Vector2(14, y + 4), FontSize = 14 }));
        _propsTilingLabel = (Label)Track(panel.AddChild(new Label("1 /m")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(84, y + 4), FontSize = 14, Color = Color.FromHex("#ffd75e")
        }));
        var tilingMinus = (Button)Track(panel.AddChild(new Button("-")
        {
            Anchor = Anchor.TopRight, Position = new Vector2(-64, y), Size = new Vector2(34, 30), FontSize = 15
        }));
        tilingMinus.Clicked += () => AdjustTiling(1f / 1.25f);
        var tilingPlus = (Button)Track(panel.AddChild(new Button("+")
        {
            Anchor = Anchor.TopRight, Position = new Vector2(-24, y), Size = new Vector2(34, 30), FontSize = 15
        }));
        tilingPlus.Clicked += () => AdjustTiling(1.25f);
        y += 36f;

        _propsNearest = (CheckBox)Track(panel.AddChild(new CheckBox("Pixel filter (nearest)")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(14, y), Size = new Vector2(212, 24), FontSize = 14
        }));
        _propsNearest.Toggled += SetSelectedMaterialNearest;
        y += 28f;

        // Normal map row: shows whether the material has one and lets you pick / clear it.
        _propsNormalLabel = (Label)Track(panel.AddChild(new Label("Normal: -")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(14, y + 4), FontSize = 12, Color = Color.White.WithAlpha(0.75f)
        }));
        var normalButton = (Button)Track(panel.AddChild(new Button("Normal map...")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(122, y), Size = new Vector2(104, 24), FontSize = 12
        }));
        normalButton.Clicked += () => FileDialogs.OpenFileAsync("Choose a normal map", FileDialogs.ImageFilter,
            TexturesDirectory, path => Post(() => SetSelectedNormalMap(path)));
        y += 28f;

        Track(panel.AddChild(new Label("Bump") { Anchor = Anchor.TopLeft, Position = new Vector2(14, y + 4), FontSize = 13 }));
        _propsNormalStrengthLabel = (Label)Track(panel.AddChild(new Label("1")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(84, y + 4), FontSize = 13, Color = Color.FromHex("#ffd75e")
        }));
        var bumpMinus = (Button)Track(panel.AddChild(new Button("-") { Anchor = Anchor.TopLeft, Position = new Vector2(158, y), Size = new Vector2(26, 24), FontSize = 14 }));
        bumpMinus.Clicked += () => AdjustNormalStrength(-0.25f);
        var bumpPlus = (Button)Track(panel.AddChild(new Button("+") { Anchor = Anchor.TopLeft, Position = new Vector2(188, y), Size = new Vector2(26, 24), FontSize = 14 }));
        bumpPlus.Clicked += () => AdjustNormalStrength(0.25f);
        y += 30f;

        return y;
    }

    /// <summary>Assigns a normal map to the selected object's material (all objects using it change).</summary>
    private void SetSelectedNormalMap(string imagePath)
    {
        var def = SelectedMaterialDef(out var key);
        if (def == null || key == null)
        {
            SetStatus("Select an object with a palette material first");
            return;
        }
        try
        {
            PushUndo();
            def.NormalMap = StoreTexture(imagePath);
            def.AutoMaps = false;
            RebuildNodesUsingMaterial(key);
            RefreshMaterialSection();
            SetStatus($"Normal map applied to \"{key}\"");
        }
        catch (Exception ex)
        {
            Log.Error($"Normal map failed: {ex}");
            SetStatus($"Normal map failed: {ex.Message}");
        }
    }

    private void AdjustNormalStrength(float delta)
    {
        var def = SelectedMaterialDef(out var key);
        if (def == null || key == null || (def.NormalMap == null && !def.AutoMaps))
        {
            SetStatus("This material has no normal map");
            return;
        }
        PushUndo();
        def.NormalStrength = Mathf.Clamp(def.NormalStrength + delta, 0f, 4f);
        RebuildNodesUsingMaterial(key);
        RefreshMaterialSection();
        SetStatus($"Bump strength: {def.NormalStrength:0.##}");
    }

    private void RefreshMaterialSection()
    {
        if (_selectedDef == null || _propsMaterialLabel == null)
            return;

        bool supports = SupportsMaterial(_selectedDef.Type);
        foreach (var widget in _materialSectionWidgets)
            widget.Visible = supports;
        if (!supports)
            return;

        string? key = _selectedDef.Material?.Reference;
        _propsMaterialLabel.Text = key ?? (_selectedDef.Material?.Inline != null ? "(inline)" : "(none)");
        foreach (var swatch in _propsSwatches)
            swatch.Selected = key != null && string.Equals(swatch.Key, key, StringComparison.OrdinalIgnoreCase);

        var def = SelectedMaterialDef(out _);
        float tiling = def?.UvScale is { Length: > 0 } uv ? uv[0] : 1f;
        _propsTilingLabel.Text = def != null ? $"{tiling:0.##} /m" : "-";
        _propsNearest.Checked = def?.Filter?.Equals("nearest", StringComparison.OrdinalIgnoreCase) == true;
        string normalName = def?.NormalMap != null ? Path.GetFileName(def.NormalMap)
            : def is { AutoMaps: true, Texture: not null } ? "auto" : "-";
        _propsNormalLabel.Text = $"Normal: {Truncate(normalName, 14)}";
        _propsNormalStrengthLabel.Text = def != null ? def.NormalStrength.ToString("0.##") : "-";
    }

    /// <summary>The document material referenced by the selected object (null for none/inline).</summary>
    private MaterialDef? SelectedMaterialDef(out string? key)
    {
        key = _selectedDef?.Material?.Reference;
        return key != null && _doc.Materials.TryGetValue(key, out var def) ? def : null;
    }

    private void ApplyMaterialToSelected(string key)
    {
        if (_selected != null)
            ApplyMaterial(_selected, key);
    }

    /// <summary>
    /// Assigns a document material to a placed object - or to one part of a prefab, which is
    /// what you get when you paint a wall of a house that came in as a group.
    /// </summary>
    private void ApplyMaterial(Node3D node, string key)
    {
        if (!_defByPart.TryGetValue(node, out var def))
            return;
        if (!SupportsMaterial(def.Type))
        {
            SetStatus($"{def.Name ?? def.Type} has no material to change");
            return;
        }
        if (!_doc.Materials.ContainsKey(key))
        {
            SetStatus($"Material \"{key}\" not found");
            return;
        }

        PushUndo();
        def.Material = key;
        RebuildNode(_rootByPart.GetValueOrDefault(node) ?? node);
        RefreshPropertiesPanel();
        SetStatus($"Applied \"{key}\" to {def.Name ?? def.Type}");
    }

    /// <summary>Changes the tiling (tiles per meter with world UVs) of the selected object's material.</summary>
    private void AdjustTiling(float factor)
    {
        var def = SelectedMaterialDef(out var key);
        if (def == null || key == null)
        {
            SetStatus("Select an object with a palette material to change tiling");
            return;
        }

        PushUndo();
        float current = def.UvScale is { Length: > 0 } uv ? uv[0] : 1f;
        float next = Mathf.Clamp(current * factor, 0.02f, 50f);
        def.UvScale = MathF.Abs(next - 1f) < 0.001f ? null : new[] { next, next };
        RebuildNodesUsingMaterial(key);
        RefreshMaterialSection();
        SetStatus($"Tiling of \"{key}\": {next:0.##} tiles/m (all objects using it)");
    }

    private void SetSelectedMaterialNearest(bool nearest)
    {
        var def = SelectedMaterialDef(out var key);
        if (def == null || key == null)
            return;

        PushUndo();
        def.Filter = nearest ? "nearest" : null;
        RebuildNodesUsingMaterial(key);
        RebuildMaterialPalette();
        SetStatus(nearest ? $"\"{key}\": pixel-art filtering" : $"\"{key}\": smooth filtering");
    }

    // ------------------------------------------------------------------
    // Importing images as materials
    // ------------------------------------------------------------------

    /// <summary>Where texture files are copied to in project mode.</summary>
    private string? TexturesDirectory =>
        _projectDirectory != null ? Path.Combine(_projectDirectory, "Assets", "Textures") : null;

    /// <summary>
    /// Turns an image file into a document material (reusing an existing one for the
    /// same file). Applies it to <paramref name="target"/> when given, otherwise selects
    /// it in the palette for the next blocks.
    /// </summary>
    private void ImportImage(string imagePath, Node3D? target)
    {
        string key;
        try
        {
            key = AddTextureMaterial(imagePath);
        }
        catch (Exception ex)
        {
            Log.Error($"Texture import failed: {ex}");
            SetStatus($"Texture import failed: {ex.Message}");
            return;
        }

        if (target != null && _defByNode.TryGetValue(target, out var def) && SupportsMaterial(def.Type))
            ApplyMaterial(target, key);
        else
            SelectMaterial(key);
    }

    /// <summary>Creates (or finds) the material for an image file and returns its key.</summary>
    private string AddTextureMaterial(string imagePath)
    {
        string stored = StoreTexture(imagePath);
        string storedFull = Path.GetFullPath(Assets.Resolve(stored));

        foreach (var (existingKey, existing) in _doc.Materials)
        {
            if (existing.Texture != null &&
                string.Equals(Path.GetFullPath(Assets.Resolve(existing.Texture)), storedFull, StringComparison.OrdinalIgnoreCase))
                return existingKey;
        }

        string key = MakeMaterialKey(Path.GetFileNameWithoutExtension(imagePath));
        PushUndo();
        _doc.Materials[key] = new MaterialDef { Texture = stored, Specular = 0.15f };
        RebuildMaterialPalette();
        SetStatus($"Added texture material \"{key}\"");
        return key;
    }

    /// <summary>
    /// In project mode, copies the image into Assets/Textures (unless it already lives
    /// inside the project) and returns a project-relative path; otherwise returns the
    /// absolute path.
    /// </summary>
    private string StoreTexture(string imagePath)
    {
        string full = Path.GetFullPath(imagePath);
        if (_projectDirectory == null)
            return full;

        string projectFull = Path.GetFullPath(_projectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (full.StartsWith(projectFull, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(_projectDirectory, full).Replace('\\', '/');

        string directory = TexturesDirectory!;
        Directory.CreateDirectory(directory);
        string name = Path.GetFileName(full);
        string destination = Path.Combine(directory, name);
        int counter = 1;
        while (File.Exists(destination) && new FileInfo(destination).Length != new FileInfo(full).Length)
            destination = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)}_{counter++}{Path.GetExtension(name)}");
        if (!File.Exists(destination))
            File.Copy(full, destination);
        return Path.GetRelativePath(_projectDirectory, destination).Replace('\\', '/');
    }

    private string MakeMaterialKey(string baseName)
    {
        var chars = baseName.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray();
        string key = new string(chars).Trim('_');
        if (key.Length == 0)
            key = "texture";
        string unique = key;
        int counter = 2;
        while (_doc.Materials.ContainsKey(unique))
            unique = $"{key}_{counter++}";
        return unique;
    }
}
