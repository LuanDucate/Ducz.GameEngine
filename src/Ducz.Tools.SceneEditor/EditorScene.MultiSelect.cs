using System.Numerics;
using Ducz.Serialization;
using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// Working with several objects at once. In free-mouse mode (Esc) a left-drag rubber-bands a
/// box over the map and everything inside it is selected; Shift+click adds or removes one.
/// Moving, scaling, rotating, duplicating, copying and deleting then apply to the whole set,
/// which is what you need to shift a block of houses or make a row of trees bigger.
/// </summary>
partial class EditorScene
{
    /// <summary>Extra objects picked besides <see cref="_selected"/> (the primary one).</summary>
    private readonly List<Node3D> _multiSelection = new();

    private Vector2? _boxSelectStart;
    private bool _boxSelecting;

    /// <summary>
    /// Set for the rest of the frame in which a rubber-band selection finished, so the
    /// mouse-release handler that normally selects a single object leaves it alone.
    /// </summary>
    private bool _boxJustSelected;
    private Panel _boxSelectPanel = null!;

    /// <summary>The translucent rectangle drawn while rubber-band selecting.</summary>
    private void BuildBoxSelectPanel()
    {
        _boxSelectPanel = _canvas.AddChild(new Panel
        {
            Anchor = Anchor.TopLeft,
            BackgroundColor = Color.FromHex("#4f8fea").WithAlpha(0.16f),
            BorderColor = Color.FromHex("#7fd4ff"),
            Visible = false
        });
    }

    /// <summary>The whole selection: the primary object first, then the rest.</summary>
    private IEnumerable<Node3D> SelectionNodes()
    {
        if (_selected != null)
            yield return _selected;
        foreach (var node in _multiSelection)
            if (node != _selected && node.IsInsideTree)
                yield return node;
    }

    /// <summary>Definitions of everything selected (top-level objects only).</summary>
    private List<NodeDef> SelectionDefs() =>
        SelectionNodes().Select(node => _defByNode.GetValueOrDefault(node))
                        .Where(def => def != null).Select(def => def!).Distinct().ToList();

    private int SelectionCount() => SelectionNodes().Count();

    private void ClearMultiSelection() => _multiSelection.Clear();

    /// <summary>Adds or removes one object from the selection (Shift+click).</summary>
    private void ToggleInSelection(Node3D node)
    {
        if (!_defByNode.ContainsKey(node))
            return;

        if (node == _selected)
        {
            // Un-picking the primary promotes the next one, or clears everything.
            _multiSelection.Remove(node);
            var next = _multiSelection.FirstOrDefault();
            if (next == null)
            {
                CloseProperties();
                return;
            }
            _multiSelection.Remove(next);
            OpenProperties(next);
        }
        else if (_multiSelection.Contains(node))
        {
            _multiSelection.Remove(node);
        }
        else
        {
            if (_selected != null && !_multiSelection.Contains(_selected))
                _multiSelection.Add(_selected);
            OpenProperties(node);
        }
        RefreshPropertiesPanel();
        SetStatus(SelectionCount() > 1 ? $"{SelectionCount()} objects selected" : "1 object selected");
    }

    // ------------------------------------------------------------------
    // Rubber-band box select (free mouse only)
    // ------------------------------------------------------------------

    private void UpdateBoxSelect()
    {
        _boxJustSelected = false;
        if (!_freeMouse)
        {
            _boxSelectStart = null;
            _boxSelecting = false;
            return;
        }

        if (Input.IsMouseButtonPressed(MouseButton.Left) && !Canvas.IsMouseOverUI && !_gizmoConsumed)
            _boxSelectStart = Input.MousePosition;

        if (_boxSelectStart is { } start && Input.IsMouseButtonDown(MouseButton.Left) &&
            Vector2.Distance(start, Input.MousePosition) > 6f)
            _boxSelecting = true;

        if (Input.IsMouseButtonReleased(MouseButton.Left))
        {
            if (_boxSelecting && _boxSelectStart is { } from)
            {
                SelectInsideBox(from, Input.MousePosition);
                _boxJustSelected = true;
            }
            _boxSelectStart = null;
            _boxSelecting = false;
        }
    }

    /// <summary>Selects every placed object whose centre falls inside the screen rectangle.</summary>
    private void SelectInsideBox(Vector2 a, Vector2 b)
    {
        var min = Vector2.Min(a, b);
        var max = Vector2.Max(a, b);
        var camera = ActiveCamera;
        var found = new List<Node3D>();

        foreach (var (node, _) in _defByNode)
        {
            if (!node.IsInsideTree || node.ComputeVisualBounds() is not { } bounds)
                continue;
            var centre = Vector3.Transform((bounds.Min + bounds.Max) * 0.5f, node.GlobalTransform);
            var screen = camera.WorldToScreenPoint(centre);
            if (screen is { } point && point.X >= min.X && point.X <= max.X &&
                point.Y >= min.Y && point.Y <= max.Y)
                found.Add(node);
        }

        if (found.Count == 0)
        {
            CloseProperties();
            SetStatus("Nothing inside the box");
            return;
        }

        ClearMultiSelection();
        OpenProperties(found[0]);
        for (int i = 1; i < found.Count; i++)
            _multiSelection.Add(found[i]);
        RefreshPropertiesPanel();
        SetStatus($"{found.Count} objects selected - move, scale, duplicate or delete them together");
    }

    /// <summary>Shows the rubber band and outlines every extra object in the selection.</summary>
    private void DrawSelectionExtras()
    {
        if (_boxSelecting && _boxSelectStart is { } start)
        {
            var min = Vector2.Min(start, Input.MousePosition);
            var max = Vector2.Max(start, Input.MousePosition);
            _boxSelectPanel.Visible = true;
            _boxSelectPanel.Position = min;
            _boxSelectPanel.Size = Vector2.Max(max - min, new Vector2(1f, 1f));
        }
        else
        {
            _boxSelectPanel.Visible = false;
        }

        foreach (var node in SelectionNodes().Skip(1))
            if (node.IsInsideTree && node.ComputeVisualBounds() is { } bounds)
                DrawWorldBounds(node, bounds, Color.FromHex("#7fd4ff"));
    }

    // ------------------------------------------------------------------
    // Operations that apply to the whole selection
    // ------------------------------------------------------------------

    /// <summary>Runs an edit on every selected definition and rebuilds them all.</summary>
    private bool EditSelection(Action<NodeDef> edit, string what)
    {
        var defs = SelectionDefs();
        if (defs.Count < 2)
            return false;

        PushUndo();
        foreach (var def in defs)
            edit(def);

        var nodes = SelectionNodes().ToList();
        var rebuilt = new List<Node3D>();
        foreach (var node in nodes)
            if (RebuildNode(node) is { } newNode)
                rebuilt.Add(newNode);

        _multiSelection.Clear();
        if (rebuilt.Count > 0)
        {
            _selected = rebuilt[0];
            _selectedDef = _defByNode.GetValueOrDefault(rebuilt[0]);
            _selectedPath = new List<int>();
            for (int i = 1; i < rebuilt.Count; i++)
                _multiSelection.Add(rebuilt[i]);
        }
        RefreshPropertiesPanel();
        SetStatus($"{defs.Count} objects: {what}");
        return true;
    }

    private static void MoveDef(NodeDef def, Vector3 offset)
    {
        var p = def.Position is { Length: >= 3 } existing ? (float[])existing.Clone() : new float[3];
        p[0] += offset.X;
        p[1] += offset.Y;
        p[2] += offset.Z;
        def.Position = p;
    }

    private static void ScaleDef(NodeDef def, float factor)
    {
        float current = def.Scale is { Length: > 0 } s ? s[0] : 1f;
        float next = Mathf.Clamp(current * factor, 0.05f, 20f);
        def.Scale = MathF.Abs(next - 1f) < 0.001f ? null : new[] { next, next, next };
    }

    private static void RotateDef(NodeDef def, float deltaDegrees)
    {
        var r = def.RotationDegrees is { Length: >= 3 } existing
            ? (float[])existing.Clone()
            : new float[3];
        r[1] = (r[1] + deltaDegrees) % 360f;
        def.RotationDegrees = MathF.Abs(r[0]) < 0.01f && MathF.Abs(r[1]) < 0.01f && MathF.Abs(r[2]) < 0.01f
            ? null : r;
    }

    /// <summary>Deletes every selected object. Returns false when only one is selected.</summary>
    private bool DeleteSelection()
    {
        var nodes = SelectionNodes().ToList();
        if (nodes.Count < 2)
            return false;

        PushUndo();
        foreach (var node in nodes)
        {
            if (!_defByNode.TryGetValue(node, out var def))
                continue;
            _doc.Nodes.Remove(def);
            _defByNode.Remove(node);
            ForgetParts(node);
            node.QueueFree();
        }
        ClearMultiSelection();
        CloseProperties();
        SetStatus($"Deleted {nodes.Count} objects");
        return true;
    }

    /// <summary>Duplicates every selected object one grid step across.</summary>
    private bool DuplicateSelection()
    {
        var defs = SelectionDefs();
        if (defs.Count < 2)
            return false;

        PushUndo();
        var copies = new List<NodeDef>();
        foreach (var def in defs)
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<NodeDef>(
                System.Text.Json.JsonSerializer.Serialize(def, SceneDocument.JsonOptions),
                SceneDocument.JsonOptions)!;
            MoveDef(copy, new Vector3(_gridSize, 0f, 0f));
            copy.Name = UniqueName(copy.Name ?? copy.Type);
            copies.Add(copy);
        }

        ClearMultiSelection();
        Node3D? first = null;
        foreach (var copy in copies)
        {
            _doc.Nodes.Add(copy);
            var visual = BuildEditorVisual(copy);
            if (visual == null)
                continue;
            _world.AddChild(visual);
            _defByNode[visual] = copy;
            RegisterParts(visual, copy);
            if (first == null)
                first = visual;
            else
                _multiSelection.Add(visual);
        }
        if (first != null)
            OpenProperties(first);
        SetStatus($"Duplicated {copies.Count} objects");
        return true;
    }
}
