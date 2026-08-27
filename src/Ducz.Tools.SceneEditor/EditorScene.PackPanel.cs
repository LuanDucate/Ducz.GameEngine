using System.Numerics;
using Ducz.Serialization;
using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// The pack browser. Modular kits ship as one file holding dozens of separate pieces
/// (wall, gate, tower, barrel...). Importing such a file used to give you a single
/// palette entry for the whole pack; this panel lists its pieces so you can pick one and
/// place it like any other block - ghost preview, grid snapping and all.
/// </summary>
partial class EditorScene
{
    private const int PackCols = 5;
    private const int PackRows = 8;
    private const int PackPerPage = PackCols * PackRows;

    private Panel _packPanel = null!;
    private Label _packTitle = null!;
    private Label _packPageLabel = null!;
    private readonly List<Button> _packButtons = new();
    private readonly Dictionary<Button, BlockItem> _packItems = new();

    private string? _packPath;
    private List<string> _packPieces = new();
    private int _packPage;

    private void BuildPackPanel()
    {
        _packPanel = _canvas.AddChild(new Panel
        {
            Anchor = Anchor.Center,
            Size = new Vector2(760, 470),
            BackgroundColor = Color.Black.WithAlpha(0.88f),
            BorderColor = Color.White.WithAlpha(0.2f),
            Visible = false
        });

        _packTitle = _packPanel.AddChild(new Label("Pack pieces")
        {
            Anchor = Anchor.TopCenter, Position = new Vector2(0, 12), FontSize = 18,
            Color = Color.FromHex("#7fd4ff")
        });
        _packPanel.AddChild(new Label("Click a piece, then click in the map to place it. R rotates, G changes the grid.")
        {
            Anchor = Anchor.TopCenter, Position = new Vector2(0, 36), FontSize = 12,
            Color = Color.White.WithAlpha(0.6f)
        });

        for (int row = 0; row < PackRows; row++)
        {
            for (int col = 0; col < PackCols; col++)
            {
                var button = _packPanel.AddChild(new Button("")
                {
                    Anchor = Anchor.TopLeft,
                    Position = new Vector2(18 + col * 146, 62 + row * 40),
                    Size = new Vector2(140, 34), FontSize = 12,
                    Visible = false
                });
                var captured = button;
                button.Clicked += () => ChoosePackPiece(captured);
                _packButtons.Add(button);
            }
        }

        var prev = _packPanel.AddChild(new Button("< Previous")
        {
            Anchor = Anchor.BottomLeft, Position = new Vector2(18, -14), Size = new Vector2(120, 30), FontSize = 14
        });
        prev.Clicked += () => TurnPackPage(-1);

        _packPageLabel = _packPanel.AddChild(new Label("")
        {
            Anchor = Anchor.BottomCenter, Position = new Vector2(-40, -20), FontSize = 14
        });

        var next = _packPanel.AddChild(new Button("Next >")
        {
            Anchor = Anchor.BottomRight, Position = new Vector2(-150, -14), Size = new Vector2(120, 30), FontSize = 14
        });
        next.Clicked += () => TurnPackPage(1);

        var close = _packPanel.AddChild(new Button("Close")
        {
            Anchor = Anchor.BottomRight, Position = new Vector2(-18, -14), Size = new Vector2(120, 30), FontSize = 14
        });
        close.Clicked += () => _packPanel.Visible = false;
    }

    /// <summary>
    /// P opens the browser for the selected pack (or the first imported one that has
    /// several pieces), so it is reachable even when the palette has scrolled past the
    /// bottom of the sidebar.
    /// </summary>
    private void TogglePackPanel()
    {
        if (_packPanel.Visible)
        {
            _packPanel.Visible = false;
            return;
        }
        string? path = _selectedItem?.ModelPath
                       ?? _modelItems.Select(m => m.ModelPath).FirstOrDefault(p => p != null);
        if (path == null)
        {
            SetStatus("Import a model pack first (drop a .glb here or use the MODELS box).");
            return;
        }
        OpenPackPanel(path);
    }

    /// <summary>Opens the browser for an imported model that holds several pieces.</summary>
    private void OpenPackPanel(string path)
    {
        try
        {
            _packPieces = Assets.LoadModel(path).MeshNodeNames.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            Log.Error($"Pack browser failed: {ex}");
            SetStatus($"Could not read the pack: {ex.Message}");
            return;
        }
        if (_packPieces.Count == 0)
        {
            SetStatus("This model has no separate pieces.");
            return;
        }
        _packPath = path;
        _packPage = 0;
        _packTitle.Text = $"{Path.GetFileNameWithoutExtension(path)} - {_packPieces.Count} pieces";
        _packPanel.Visible = true;
        RefreshPackPage();
    }

    private void TurnPackPage(int direction)
    {
        int pages = Math.Max(1, (_packPieces.Count + PackPerPage - 1) / PackPerPage);
        _packPage = Math.Clamp(_packPage + direction, 0, pages - 1);
        RefreshPackPage();
    }

    private void RefreshPackPage()
    {
        int pages = Math.Max(1, (_packPieces.Count + PackPerPage - 1) / PackPerPage);
        _packPageLabel.Text = $"page {_packPage + 1} / {pages}";
        _packItems.Clear();
        for (int i = 0; i < _packButtons.Count; i++)
        {
            int index = _packPage * PackPerPage + i;
            var button = _packButtons[i];
            if (index >= _packPieces.Count || _packPath == null)
            {
                button.Visible = false;
                continue;
            }
            string piece = _packPieces[index];
            button.Visible = true;
            button.Text = TruncateMiddle(piece, 19);
            _packItems[button] = MakePackItem(_packPath, piece);
        }
    }

    private void ChoosePackPiece(Button button)
    {
        if (!_packItems.TryGetValue(button, out var item))
            return;
        SelectItem(item);
        SetStatus($"Piece \"{item.SubNode}\" selected - click in the map to place it");
    }

    /// <summary>A palette entry that places one piece of a pack under the cursor.</summary>
    private BlockItem MakePackItem(string path, string piece)
    {
        (Vector3 Min, Vector3 Max) bounds;
        try
        {
            bounds = Assets.LoadModel(path).PartBounds(piece) ?? (new Vector3(-0.5f), new Vector3(0.5f));
        }
        catch
        {
            bounds = (new Vector3(-0.5f), new Vector3(0.5f));
        }

        return new BlockItem(piece, new MeshDef(), 0f, (e, p) => new NodeDef
        {
            Type = "model",
            Name = piece,
            Path = path,
            SubNode = piece,
            SubNodePivot = "base",     // land under the cursor, not where it sits in the file
            Position = ToArray(p),
            RotationDegrees = e._rotationY != 0f ? new[] { 0f, e._rotationY, 0f } : null,
            Scale = MathF.Abs(e._placeScale - 1f) > 0.001f
                ? new[] { e._placeScale, e._placeScale, e._placeScale }
                : null,
            Collider = new ColliderDef { Shape = "auto" }
        })
        {
            ModelPath = path,
            SubNode = piece,
            Bounds = bounds
        };
    }
}
