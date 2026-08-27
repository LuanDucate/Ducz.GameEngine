using System.Numerics;
using System.Text.Json;
using Ducz.Serialization;
using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// The prefab browser (key <b>B</b>): ready-made houses, streets, trees, cars. Picking one
/// selects it like any other block - ghost preview, grid snapping, R to rotate - and clicking
/// drops the whole assembly into the map as a single object you can move, duplicate or delete.
/// </summary>
partial class EditorScene
{
    private const int PrefabCols = 4;
    private const int PrefabRows = 7;
    private const int PrefabPerPage = PrefabCols * PrefabRows;

    private PrefabLibrary _prefabs = new();
    private Panel _prefabPanel = null!;
    private Label _prefabHint = null!;
    private Label _prefabPageLabel = null!;
    private readonly List<Button> _prefabButtons = new();
    private readonly List<Button> _prefabTabs = new();
    private readonly Dictionary<Button, PrefabDef> _prefabByButton = new();
    private string _prefabCategory = "";
    private int _prefabPage;
    private VStack _prefabSectionStack = null!;
    private readonly List<UINode> _prefabSectionWidgets = new();

    /// <summary>
    /// The PREFABS section of the sidebar: every piece of the library, grouped by category.
    /// The sidebar scrolls, so the whole catalogue can live there and a piece is one click
    /// away without opening a panel.
    /// </summary>
    private void BuildPrefabSection(VStack stack)
    {
        _prefabs = PrefabLibrary.Load(_projectDirectory);
        _prefabSectionStack = stack;

        stack.AddChild(new Label("PREFABS")
        {
            FontSize = 13, Color = Color.White.WithAlpha(0.5f), Anchor = Anchor.TopCenter
        });
        var open = stack.AddChild(new Button("Browse prefabs (B)")
        {
            Size = new Vector2(170, 24), FontSize = 12, Anchor = Anchor.TopCenter,
            NormalColor = Color.FromHex("#2b4a5e")
        });
        open.Clicked += TogglePrefabPanel;

        FillPrefabSection();
    }

    private void FillPrefabSection()
    {
        foreach (var widget in _prefabSectionWidgets)
            widget.RemoveFromParent();
        _prefabSectionWidgets.Clear();

        foreach (string category in _prefabs.Categories())
        {
            var header = _prefabSectionStack.AddChild(new Label("  " + category)
            {
                FontSize = 11, Color = Color.FromHex("#7fd4ff").WithAlpha(0.75f), Anchor = Anchor.TopLeft
            });
            _prefabSectionWidgets.Add(header);

            foreach (var prefab in _prefabs.InCategory(category))
            {
                var button = _prefabSectionStack.AddChild(new Button(Truncate(prefab.Name, 22))
                {
                    Size = new Vector2(170, 22), FontSize = 11, Anchor = Anchor.TopCenter
                });
                var captured = prefab;
                button.Clicked += () =>
                {
                    SelectItem(MakePrefabItem(captured));
                    SetStatus($"\"{captured.Name}\" selected - click in the map to place it");
                };
                _prefabSectionWidgets.Add(button);
            }
        }
    }

    /// <summary>Re-scans the prefab folders and refreshes both the sidebar and the browser.</summary>
    private void ReloadPrefabs()
    {
        _prefabs = PrefabLibrary.Load(_projectDirectory);
        FillPrefabSection();
        if (_prefabPanel.Visible)
            ShowPrefabCategory(_prefabCategory);
        SetStatus($"{_prefabs.Prefabs.Count} prefabs loaded");
    }

    private void BuildPrefabPanel()
    {

        _prefabPanel = _canvas.AddChild(new Panel
        {
            Anchor = Anchor.Center,
            Size = new Vector2(700, 500),
            BackgroundColor = Color.Black.WithAlpha(0.9f),
            BorderColor = Color.White.WithAlpha(0.2f),
            Visible = false
        });

        _prefabPanel.AddChild(new Label("Prefabs - ready-made pieces")
        {
            Anchor = Anchor.TopCenter, Position = new Vector2(0, 12), FontSize = 18,
            Color = Color.FromHex("#7fd4ff")
        });

        for (int i = 0; i < 8; i++)
        {
            var tab = _prefabPanel.AddChild(new Button("")
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(16 + i * 84, 44),
                Size = new Vector2(80, 26), FontSize = 12, Visible = false
            });
            var captured = tab;
            tab.Clicked += () => ShowPrefabCategory(captured.Text);
            _prefabTabs.Add(tab);
        }

        for (int row = 0; row < PrefabRows; row++)
        {
            for (int col = 0; col < PrefabCols; col++)
            {
                var button = _prefabPanel.AddChild(new Button("")
                {
                    Anchor = Anchor.TopLeft,
                    Position = new Vector2(16 + col * 168, 82 + row * 42),
                    Size = new Vector2(162, 36), FontSize = 12, Visible = false
                });
                var captured = button;
                button.Clicked += () => ChoosePrefab(captured);
                _prefabButtons.Add(button);
            }
        }

        _prefabHint = _prefabPanel.AddChild(new Label("")
        {
            Anchor = Anchor.BottomLeft, Position = new Vector2(16, -52), FontSize = 12,
            Color = Color.White.WithAlpha(0.65f)
        });

        var prev = _prefabPanel.AddChild(new Button("< Previous")
        {
            Anchor = Anchor.BottomLeft, Position = new Vector2(16, -14), Size = new Vector2(110, 30), FontSize = 13
        });
        prev.Clicked += () => TurnPrefabPage(-1);
        _prefabPageLabel = _prefabPanel.AddChild(new Label("")
        {
            Anchor = Anchor.BottomCenter, Position = new Vector2(-30, -20), FontSize = 13
        });
        var next = _prefabPanel.AddChild(new Button("Next >")
        {
            Anchor = Anchor.BottomRight, Position = new Vector2(-140, -14), Size = new Vector2(110, 30), FontSize = 13
        });
        next.Clicked += () => TurnPrefabPage(1);
        var close = _prefabPanel.AddChild(new Button("Close")
        {
            Anchor = Anchor.BottomRight, Position = new Vector2(-16, -14), Size = new Vector2(110, 30), FontSize = 13
        });
        close.Clicked += () => _prefabPanel.Visible = false;
    }

    private void TogglePrefabPanel()
    {
        if (_prefabPanel.Visible)
        {
            _prefabPanel.Visible = false;
            return;
        }
        if (_prefabs.Prefabs.Count == 0)
        {
            _prefabs = PrefabLibrary.Load(_projectDirectory);
            if (_prefabs.Prefabs.Count == 0)
            {
                SetStatus($"No prefabs found. Put .duczprefab.json files in {PrefabLibrary.UserFolder}");
                return;
            }
        }
        _prefabPanel.Visible = true;
        var categories = _prefabs.Categories();
        ShowPrefabCategory(_prefabCategory.Length > 0 && categories.Contains(_prefabCategory)
            ? _prefabCategory
            : categories.FirstOrDefault() ?? "");
    }

    private void ShowPrefabCategory(string category)
    {
        _prefabCategory = category;
        _prefabPage = 0;
        var categories = _prefabs.Categories();
        for (int i = 0; i < _prefabTabs.Count; i++)
        {
            var tab = _prefabTabs[i];
            if (i >= categories.Count)
            {
                tab.Visible = false;
                continue;
            }
            tab.Visible = true;
            tab.Text = categories[i];
            tab.NormalColor = string.Equals(categories[i], category, StringComparison.OrdinalIgnoreCase)
                ? UITheme.AccentColor.Darkened(0.3f)
                : UITheme.PanelColor;
        }
        RefreshPrefabPage();
    }

    private void TurnPrefabPage(int direction)
    {
        int count = _prefabs.InCategory(_prefabCategory).Count;
        int pages = Math.Max(1, (count + PrefabPerPage - 1) / PrefabPerPage);
        _prefabPage = Math.Clamp(_prefabPage + direction, 0, pages - 1);
        RefreshPrefabPage();
    }

    private void RefreshPrefabPage()
    {
        var items = _prefabs.InCategory(_prefabCategory);
        int pages = Math.Max(1, (items.Count + PrefabPerPage - 1) / PrefabPerPage);
        _prefabPageLabel.Text = $"{items.Count} pieces - page {_prefabPage + 1}/{pages}";
        _prefabByButton.Clear();
        for (int i = 0; i < _prefabButtons.Count; i++)
        {
            int index = _prefabPage * PrefabPerPage + i;
            var button = _prefabButtons[i];
            if (index >= items.Count)
            {
                button.Visible = false;
                continue;
            }
            button.Visible = true;
            button.Text = Truncate(items[index].Name, 20);
            _prefabByButton[button] = items[index];
        }
    }

    private void ChoosePrefab(Button button)
    {
        if (!_prefabByButton.TryGetValue(button, out var prefab))
            return;
        SelectItem(MakePrefabItem(prefab));
        _prefabHint.Text = prefab.Description ?? "";
        SetStatus($"\"{prefab.Name}\" selected - click in the map to place it");
    }

    /// <summary>A palette entry that drops the whole prefab as one object.</summary>
    private BlockItem MakePrefabItem(PrefabDef prefab)
    {
        var bounds = PrefabBounds(prefab);
        return new BlockItem(prefab.Name, new MeshDef(), 0f, (e, p) =>
        {
            e.MergePrefabMaterials(prefab);
            var def = ClonePrefabNode(prefab);
            def.Name = UniqueName(prefab.Name);
            def.Position = ToArray(p);
            if (e._rotationY != 0f)
                def.RotationDegrees = new[] { 0f, e._rotationY, 0f };
            if (MathF.Abs(e._placeScale - 1f) > 0.001f)
                def.Scale = new[] { e._placeScale, e._placeScale, e._placeScale };
            return def;
        })
        {
            Prefab = prefab,
            Bounds = bounds
        };
    }

    private static NodeDef ClonePrefabNode(PrefabDef prefab) =>
        JsonSerializer.Deserialize<NodeDef>(
            JsonSerializer.Serialize(prefab.Node, SceneDocument.JsonOptions), SceneDocument.JsonOptions)!;

    /// <summary>Adds the materials a prefab needs, without touching ones the map already has.</summary>
    private void MergePrefabMaterials(PrefabDef prefab)
    {
        if (prefab.Materials == null)
            return;
        foreach (var (key, material) in prefab.Materials)
            if (!_doc.Materials.ContainsKey(key))
                _doc.Materials[key] = material;
    }

    private string UniqueName(string baseName)
    {
        string safe = new string(baseName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        int n = 1;
        while (_doc.Nodes.Any(node => string.Equals(node.Name, $"{safe}_{n}", StringComparison.OrdinalIgnoreCase)))
            n++;
        return $"{safe}_{n}";
    }

    private (Vector3 Min, Vector3 Max) PrefabBounds(PrefabDef prefab)
    {
        try
        {
            MergePrefabMaterials(prefab);
            var node = SceneLoader.InstantiateNode(_doc, prefab.Node);
            if (node?.ComputeVisualBounds() is { } bounds)
                return bounds;
        }
        catch (Exception ex)
        {
            Log.Warning($"Prefab \"{prefab.Name}\" preview failed: {ex.Message}");
        }
        return (new Vector3(-1f, 0f, -1f), new Vector3(1f, 2f, 1f));
    }

    /// <summary>Writes the selected object (with everything under it) to the user library.</summary>
    private void SaveSelectionAsPrefab()
    {
        if (_selectedDef == null)
        {
            SetStatus("Select an object first (right-click it), then save it as a prefab.");
            return;
        }
        try
        {
            var node = JsonSerializer.Deserialize<NodeDef>(
                JsonSerializer.Serialize(_selectedDef, SceneDocument.JsonOptions), SceneDocument.JsonOptions)!;
            node.Position = null;          // a prefab is placed by the cursor
            node.Name ??= "Prefab";

            var used = new Dictionary<string, MaterialDef>();
            CollectMaterials(node, used);

            string name = node.Name;
            var prefab = new PrefabDef
            {
                Name = name,
                Category = "Mine",
                Description = $"Saved from {Path.GetFileName(_savePath)}",
                Materials = used.Count > 0 ? used : null,
                Node = node
            };
            string file = Path.Combine(PrefabLibrary.UserFolder, "Mine",
                new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()) + PrefabDef.Extension);
            prefab.Save(file);
            ReloadPrefabs();
            SetStatus($"Saved as prefab: {name}  (it is in the PREFABS list on the left)");
        }
        catch (Exception ex)
        {
            Log.Error($"Save as prefab failed: {ex}");
            SetStatus($"Could not save the prefab: {ex.Message}");
        }
    }

    private void CollectMaterials(NodeDef def, Dictionary<string, MaterialDef> into)
    {
        if (def.Material?.Reference is { } name && _doc.Materials.TryGetValue(name, out var material))
            into[name] = material;
        if (def.FaceMaterials != null)
        {
            foreach (var reference in def.FaceMaterials.Values)
                if (reference.Reference is { } key && _doc.Materials.TryGetValue(key, out var faceMaterial))
                    into[key] = faceMaterial;
        }
        if (def.Children != null)
            foreach (var child in def.Children)
                CollectMaterials(child, into);
    }
}
