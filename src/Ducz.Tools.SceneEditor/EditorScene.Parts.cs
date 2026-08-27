using System.Numerics;
using System.Text.Json;
using Ducz.Serialization;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// Editing inside a group. A prefab drops in as one object so it can be moved and duplicated
/// as a unit, but its parts must stay reachable: hold <b>Alt</b> and click to select the wall,
/// the window band or the pavement itself, then change its material, move it or delete it.
/// <b>Ungroup</b> breaks the whole thing into loose objects when that is what you want.
/// </summary>
partial class EditorScene
{
    /// <summary>Every editor visual (a placed root and everything under it) to its definition.</summary>
    private readonly Dictionary<Node3D, NodeDef> _defByPart = new();

    /// <summary>Any part visual to the placed root it belongs to.</summary>
    private readonly Dictionary<Node3D, Node3D> _rootByPart = new();

    /// <summary>Child indices from the placed root's definition down to the selection.</summary>
    private List<int> _selectedPath = new();

    /// <summary>Records a placed root and all of its parts so they can be picked and edited.</summary>
    private void RegisterParts(Node3D visual, NodeDef def)
    {
        _defByPart[visual] = def;
        _rootByPart[visual] = visual;
        RegisterChildren(visual, def, visual);
    }

    private void RegisterChildren(Node3D visual, NodeDef def, Node3D root)
    {
        if (def.Children is not { Count: > 0 } children)
            return;
        // BuildNode adds one child node per child definition, in order.
        var childNodes = visual.Children.OfType<Node3D>().ToList();
        for (int i = 0; i < children.Count && i < childNodes.Count; i++)
        {
            _defByPart[childNodes[i]] = children[i];
            _rootByPart[childNodes[i]] = root;
            RegisterChildren(childNodes[i], children[i], root);
        }
    }

    private void ForgetParts(Node3D visual)
    {
        foreach (var node in _rootByPart.Where(kv => kv.Value == visual).Select(kv => kv.Key).ToList())
        {
            _rootByPart.Remove(node);
            _defByPart.Remove(node);
        }
    }

    /// <summary>The nearest ancestor (or the node itself) that the editor knows as a part.</summary>
    private Node3D? FindPart(Node node)
    {
        for (Node? current = node; current != null; current = current.Parent)
            if (current is Node3D node3D && _defByPart.ContainsKey(node3D))
                return node3D;
        return null;
    }

    /// <summary>Selects one part inside a placed object and opens the properties panel on it.</summary>
    private void SelectPart(Node3D part)
    {
        if (!_defByPart.TryGetValue(part, out var def) || !_rootByPart.TryGetValue(part, out var root))
            return;

        _selected = part;
        _selectedDef = def;
        _selectedPath = PathTo(root, part);
        _propsPanel.Visible = true;
        RefreshPropertiesPanel();

        if (_selectedPath.Count > 0 && _defByPart.TryGetValue(root, out var rootDef))
            SetStatus($"Part \"{def.Name ?? def.Type}\" of {rootDef.Name ?? rootDef.Type} - edit it like any object");
    }

    private List<int> PathTo(Node3D root, Node3D part)
    {
        var path = new List<int>();
        for (Node3D? current = part; current != null && current != root; current = current.Parent as Node3D)
        {
            if (current.Parent is not Node3D parent)
                break;
            path.Insert(0, parent.Children.OfType<Node3D>().ToList().IndexOf(current));
        }
        return path;
    }

    /// <summary>Walks a child-index path through a definition tree.</summary>
    private static NodeDef? DefAtPath(NodeDef root, List<int> path)
    {
        var def = root;
        foreach (int index in path)
        {
            if (def.Children is not { } children || index < 0 || index >= children.Count)
                return null;
            def = children[index];
        }
        return def;
    }

    private static Node3D? NodeAtPath(Node3D root, List<int> path)
    {
        var node = root;
        foreach (int index in path)
        {
            var children = node.Children.OfType<Node3D>().ToList();
            if (index < 0 || index >= children.Count)
                return null;
            node = children[index];
        }
        return node;
    }

    /// <summary>The placed root that owns the current selection (the selection itself when top level).</summary>
    private Node3D? SelectedRoot() =>
        _selected != null && _rootByPart.TryGetValue(_selected, out var root) ? root : _selected;

    /// <summary>
    /// Rebuilds the placed object the selection belongs to and restores the selection on the
    /// same part - editing a window must not drop you back to the whole building.
    /// </summary>
    private void RebuildSelectedPart()
    {
        var root = SelectedRoot();
        if (root == null)
            return;
        var path = _selectedPath;
        var rebuilt = RebuildNode(root);
        if (rebuilt == null || path.Count == 0)
            return;

        var part = NodeAtPath(rebuilt, path);
        if (part != null && _defByPart.TryGetValue(part, out var def))
        {
            _selected = part;
            _selectedDef = def;
            _selectedPath = path;
        }
    }

    /// <summary>Deletes the selected part from its parent (or the whole object at top level).</summary>
    private bool DeleteSelectedPart()
    {
        var root = SelectedRoot();
        if (root == null || _selectedPath.Count == 0 || !_defByPart.TryGetValue(root, out var rootDef))
            return false;

        var parentDef = DefAtPath(rootDef, _selectedPath.Take(_selectedPath.Count - 1).ToList());
        if (parentDef?.Children == null)
            return false;

        PushUndo();
        string name = _selectedDef?.Name ?? "part";
        parentDef.Children.RemoveAt(_selectedPath[^1]);
        _selectedPath = new List<int>();
        var rebuilt = RebuildNode(root);
        _selected = rebuilt;
        _selectedDef = rootDef;
        RefreshPropertiesPanel();
        SetStatus($"Deleted part \"{name}\" from {rootDef.Name ?? rootDef.Type}");
        return true;
    }

    /// <summary>Copies the selected part inside its own group, offset by one grid step.</summary>
    private bool DuplicateSelectedPart()
    {
        var root = SelectedRoot();
        if (root == null || _selectedPath.Count == 0 || !_defByPart.TryGetValue(root, out var rootDef))
            return false;

        var parentDef = DefAtPath(rootDef, _selectedPath.Take(_selectedPath.Count - 1).ToList());
        if (parentDef?.Children == null || _selectedDef == null)
            return false;

        PushUndo();
        var copy = JsonSerializer.Deserialize<NodeDef>(
            JsonSerializer.Serialize(_selectedDef, SceneDocument.JsonOptions), SceneDocument.JsonOptions)!;
        var position = copy.Position is { Length: >= 3 } existing ? (float[])existing.Clone() : new float[3];
        position[0] += _gridSize;
        copy.Position = position;
        if (copy.Name != null)
            copy.Name += "_copia";

        int index = _selectedPath[^1] + 1;
        parentDef.Children.Insert(index, copy);
        _selectedPath = _selectedPath.Take(_selectedPath.Count - 1).Append(index).ToList();
        var rebuilt = RebuildNode(root);
        if (rebuilt != null && NodeAtPath(rebuilt, _selectedPath) is { } node)
        {
            _selected = node;
            _selectedDef = copy;
        }
        RefreshPropertiesPanel();
        SetStatus($"Duplicated part \"{copy.Name ?? copy.Type}\"");
        return true;
    }

    /// <summary>
    /// Breaks a group into loose objects: every child becomes a top-level node with the
    /// group's transform baked in, so each one can be edited independently.
    /// </summary>
    private void UngroupSelected()
    {
        if (_selectedDef is not { Children.Count: > 0 } group)
        {
            SetStatus("Select a prefab (or a group) to ungroup it.");
            return;
        }

        PushUndo();
        var origin = group.Position is { Length: >= 3 } p ? new Vector3(p[0], p[1], p[2]) : Vector3.Zero;
        float yaw = group.RotationDegrees is { Length: >= 2 } r ? r[1] : 0f;
        float scale = group.Scale is { Length: > 0 } s ? s[0] : 1f;
        float rad = MathF.PI / 180f * yaw;
        float cos = MathF.Cos(rad), sin = MathF.Sin(rad);

        int index = _doc.Nodes.IndexOf(group);
        if (index < 0)
            index = _doc.Nodes.Count;
        _doc.Nodes.Remove(group);

        var loose = new List<NodeDef>();
        foreach (var child in group.Children)
        {
            var copy = JsonSerializer.Deserialize<NodeDef>(
                JsonSerializer.Serialize(child, SceneDocument.JsonOptions), SceneDocument.JsonOptions)!;

            var local = copy.Position is { Length: >= 3 } cp ? new Vector3(cp[0], cp[1], cp[2]) : Vector3.Zero;
            local *= scale;
            var rotated = new Vector3(local.X * cos + local.Z * sin, local.Y, -local.X * sin + local.Z * cos);
            copy.Position = new[] { origin.X + rotated.X, origin.Y + rotated.Y, origin.Z + rotated.Z };

            if (yaw != 0f)
            {
                float childYaw = copy.RotationDegrees is { Length: >= 2 } cr ? cr[1] : 0f;
                float childPitch = copy.RotationDegrees is { Length: >= 1 } cr2 ? cr2[0] : 0f;
                float childRoll = copy.RotationDegrees is { Length: >= 3 } cr3 ? cr3[2] : 0f;
                copy.RotationDegrees = new[] { childPitch, childYaw + yaw, childRoll };
            }
            if (MathF.Abs(scale - 1f) > 0.001f)
            {
                float childScale = copy.Scale is { Length: > 0 } cs ? cs[0] : 1f;
                copy.Scale = new[] { childScale * scale, childScale * scale, childScale * scale };
            }
            copy.Name = UniqueName(copy.Name ?? copy.Type);
            loose.Add(copy);
        }

        _doc.Nodes.InsertRange(Math.Min(index, _doc.Nodes.Count), loose);
        CloseProperties();
        RebuildWorld();
        SetStatus($"Ungrouped into {loose.Count} separate objects");
    }
}
