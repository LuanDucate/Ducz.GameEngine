using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ducz.Serialization;
using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// Copy / paste and the free-mouse mode.
///
/// Copying puts the object (and the materials it uses) on the system clipboard as JSON, so it
/// travels between two open maps - and can be pasted into a text editor if you want to look at
/// it. Pasting drops it under the cursor rather than on top of the original.
/// </summary>
partial class EditorScene
{
    private const string ClipboardMarker = "duczClipboard";

    /// <summary>Fallback when the platform has no clipboard.</summary>
    private string? _internalClipboard;

    /// <summary>True when no block is armed: clicking selects instead of placing.</summary>
    private bool _freeMouse;

    /// <summary>Copies the selection - a whole object, or one part of a prefab.</summary>
    private void CopySelection()
    {
        if (_selectedDef == null)
        {
            SetStatus("Nothing selected to copy (right-click an object first).");
            return;
        }

        var defs = SelectionDefs();
        if (defs.Count < 2)
            defs = new List<NodeDef> { _selectedDef };

        var nodes = new JsonArray();
        var used = new Dictionary<string, MaterialDef>();
        foreach (var def in defs)
        {
            nodes.Add(JsonNode.Parse(JsonSerializer.Serialize(def, SceneDocument.JsonOptions)));
            CollectMaterials(def, used);
        }

        var payload = new JsonObject
        {
            [ClipboardMarker] = 1,
            ["node"] = JsonNode.Parse(JsonSerializer.Serialize(defs[0], SceneDocument.JsonOptions)),
            ["nodes"] = nodes,
        };
        if (used.Count > 0)
            payload["materials"] = JsonNode.Parse(JsonSerializer.Serialize(used, SceneDocument.JsonOptions));

        string text = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        _internalClipboard = text;
        Input.ClipboardText = text;
        SetStatus(defs.Count > 1
            ? $"Copied {defs.Count} objects - Ctrl+V to paste them under the cursor"
            : $"Copied \"{_selectedDef.Name ?? _selectedDef.Type}\" - Ctrl+V to paste under the cursor");
    }

    /// <summary>Cut = copy, then delete.</summary>
    private void CutSelection()
    {
        if (_selectedDef == null)
            return;
        CopySelection();
        if (DeleteSelectedPart())
            return;
        if (_selected != null)
            DeleteBlock(_selected);
        CloseProperties();
    }

    /// <summary>Pastes the clipboard object at the cursor (or beside the original).</summary>
    private void PasteClipboard()
    {
        string text = Input.ClipboardText;
        if (!text.Contains(ClipboardMarker, StringComparison.Ordinal))
            text = _internalClipboard ?? "";
        if (text.Length == 0)
        {
            SetStatus("Clipboard is empty - select an object and press Ctrl+C first.");
            return;
        }

        List<NodeDef> defs = new();
        Dictionary<string, MaterialDef>? materials = null;
        try
        {
            var payload = JsonNode.Parse(text)?.AsObject();
            if (payload?["nodes"] is JsonArray array && array.Count > 0)
            {
                foreach (var item in array)
                    if (item != null && JsonSerializer.Deserialize<NodeDef>(
                            item.ToJsonString(), SceneDocument.JsonOptions) is { } parsed)
                        defs.Add(parsed);
            }
            else if (payload?["node"] is { } single &&
                     JsonSerializer.Deserialize<NodeDef>(single.ToJsonString(), SceneDocument.JsonOptions) is { } one)
            {
                defs.Add(one);
            }

            if (payload?["materials"] is { } materialsJson)
                materials = JsonSerializer.Deserialize<Dictionary<string, MaterialDef>>(
                    materialsJson.ToJsonString(), SceneDocument.JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warning($"Paste failed: {ex.Message}");
            SetStatus("The clipboard does not hold a Ducz object.");
            return;
        }
        if (defs.Count == 0)
        {
            SetStatus("The clipboard does not hold a Ducz object.");
            return;
        }

        PushUndo();

        if (materials != null)
            foreach (var (key, material) in materials)
                if (!_doc.Materials.ContainsKey(key))
                    _doc.Materials[key] = material;

        // Anchor the group on the cursor, keeping the objects' relative layout.
        var origin = Origin(defs[0]);
        foreach (var d in defs.Skip(1))
            origin = Vector3.Min(origin, Origin(d));

        Vector3 shift;
        if ((_placeTarget ?? CursorGroundPoint()) is { } target)
        {
            float lift = 0f;
            if (SceneLoader.InstantiateNode(_doc, defs[0])?.ComputeVisualBounds() is { } bounds)
                lift = -bounds.Min.Y;
            shift = new Vector3(target.X, target.Y + lift, target.Z) - Origin(defs[0]);
        }
        else
        {
            shift = new Vector3(_gridSize, 0f, 0f);
        }

        ClearMultiSelection();
        Node3D? first = null;
        foreach (var d in defs)
        {
            var moved = Origin(d) + shift;
            d.Position = new[] { moved.X, moved.Y, moved.Z };
            d.Name = UniqueName(d.Name ?? d.Type);

            _doc.Nodes.Add(d);
            var visual = BuildEditorVisual(d);
            if (visual == null)
                continue;
            _world.AddChild(visual);
            _defByNode[visual] = d;
            RegisterParts(visual, d);
            if (first == null)
                first = visual;
            else
                _multiSelection.Add(visual);
        }
        if (first != null)
            OpenProperties(first);

        RebuildMaterialPalette();
        SetStatus(defs.Count > 1
            ? $"Pasted {defs.Count} objects  ({_doc.Nodes.Count} nodes)"
            : $"Pasted {defs[0].Name}  ({_doc.Nodes.Count} nodes)");
    }

    private static Vector3 Origin(NodeDef def) =>
        def.Position is { Length: >= 3 } p ? new Vector3(p[0], p[1], p[2]) : Vector3.Zero;

    /// <summary>
    /// Where the cursor points at the map, even in free-mouse mode (where no placement target
    /// is tracked) - so pasting always lands where you are looking.
    /// </summary>
    private Vector3? CursorGroundPoint()
    {
        if (Canvas.IsMouseOverUI)
            return null;
        var (origin, direction) = ActiveCamera.ScreenPointToRay(Input.MousePosition);
        if (Engine.Physics.Raycast(origin, direction, 1000f, out var hit) && hit.Normal.Y > 0.5f)
            return SnapToGrid(hit.Point);
        if (MathF.Abs(direction.Y) < 1e-4f)
            return null;
        float t = -origin.Y / direction.Y;
        return t > 0f && t < 1000f ? SnapToGrid(origin + direction * t) : null;
    }

    // ------------------------------------------------------------------
    // Free mouse: no block armed, clicks select instead of placing
    // ------------------------------------------------------------------

    /// <summary>Puts the cursor back to "just looking": nothing is placed until you pick a block.</summary>
    private void EnterFreeMouse(bool announce = true)
    {
        _freeMouse = true;
        _painting = false;
        _rectStart = null;
        if (_ghost != null)
            _ghost.Visible = false;
        foreach (var (button, _) in _itemButtons)
            button.NormalColor = UITheme.PanelColor;
        if (_freeMouseButton != null)
            _freeMouseButton.NormalColor = UITheme.AccentColor.Darkened(0.3f);
        UpdateSelectionLabel();
        if (announce)
            SetStatus("Free mouse: click to select, nothing is placed. Pick a block to build again.");
    }

    private void LeaveFreeMouse()
    {
        _freeMouse = false;
        if (_freeMouseButton != null)
            _freeMouseButton.NormalColor = UITheme.PanelColor;
    }

    /// <summary>Escape: close whatever is open, then release the cursor.</summary>
    private void HandleEscape()
    {
        if (_packPanel.Visible || _prefabPanel.Visible || _exportPanel.Visible)
        {
            _packPanel.Visible = false;
            _prefabPanel.Visible = false;
            _exportPanel.Visible = false;
            return;
        }
        if (!_freeMouse)
        {
            EnterFreeMouse();
            return;
        }
        CloseProperties();
    }
}
