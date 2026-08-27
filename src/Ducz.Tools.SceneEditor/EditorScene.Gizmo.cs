using System.Numerics;
using Ducz.Rendering;
using Ducz.Serialization;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// The move gizmo: three axis handles on the selected object that you grab and drag to move it
/// with the mouse - the interaction a hand-builder expects, instead of typing coordinates.
/// Dragging snaps to the current grid, pushes one undo per drag, and moves the whole selection
/// when several objects are picked.
/// </summary>
partial class EditorScene
{
    private static readonly Vector3[] GizmoAxes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
    private static readonly string[] GizmoColors = { "#ff5a5a", "#5aff6e", "#5a9dff" };

    private int _gizmoHover = -1;
    private bool _gizmoDrag;
    private float _gizmoStartParam;
    private Vector3 _gizmoDragOrigin;   // axis line stays anchored here during a drag
    private readonly Dictionary<NodeDef, float[]> _gizmoStartPos = new();
    private bool _gizmoConsumed;

    /// <summary>True while dragging a handle - callers must not place, paint or box-select.</summary>
    private bool GizmoBusy => _gizmoDrag;

    /// <summary>Screen-constant handle length, so the gizmo looks the same size at any distance.</summary>
    private float GizmoLength(Vector3 origin) =>
        MathF.Max(1.2f, Vector3.Distance(ActiveCamera.GlobalPosition, origin) * 0.13f);

    /// <summary>
    /// Runs the gizmo for the frame. Returns true when it owns the mouse (hovering a handle or
    /// dragging), so the placement code stands down.
    /// </summary>
    private bool UpdateGizmo(bool uiHovered)
    {
        _gizmoConsumed = false;
        if (_selected is not { IsInsideTree: true } || _selectedDef == null)
        {
            _gizmoDrag = false;
            _gizmoHover = -1;
            return false;
        }

        var origin = _selected.GlobalPosition;
        float len = GizmoLength(origin);

        if (_gizmoDrag)
        {
            if (Input.IsMouseButtonDown(MouseButton.Left))
                DragGizmo(_gizmoDragOrigin);
            else
                EndGizmoDrag();
            _gizmoConsumed = true;
            return true;
        }

        // Hover test (only when not busy elsewhere).
        _gizmoHover = uiHovered ? -1 : PickGizmoAxis(origin, len);

        if (_gizmoHover >= 0 && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            BeginGizmoDrag(origin);
            _gizmoConsumed = true;
            return true;
        }
        return _gizmoHover >= 0;   // hovering a handle: don't let a click fall through to placement
    }

    private void BeginGizmoDrag(Vector3 origin)
    {
        PushUndo();
        _gizmoDrag = true;
        _gizmoDragOrigin = origin;
        _gizmoStartParam = AxisParamUnderMouse(origin, GizmoAxes[_gizmoHover]);
        _gizmoStartPos.Clear();
        foreach (var def in SelectionDefsForGizmo())
            _gizmoStartPos[def] = def.Position is { Length: >= 3 } p ? (float[])p.Clone() : new float[3];
    }

    private void DragGizmo(Vector3 origin)
    {
        var axis = GizmoAxes[_gizmoHover];
        float param = AxisParamUnderMouse(origin, axis);
        float delta = param - _gizmoStartParam;

        // Snap the travelled distance to the grid (fine snap on the vertical axis).
        float step = _gizmoHover == 1 ? VerticalSnap : _gridSize;
        delta = MathF.Round(delta / step) * step;

        var offset = axis * delta;
        foreach (var (def, start) in _gizmoStartPos)
            def.Position = new[] { start[0] + offset.X, start[1] + offset.Y, start[2] + offset.Z };

        RebuildGizmoSelection();
        RefreshPositionBoxes();
        SetStatus($"Move {"XYZ"[_gizmoHover]} {delta:+0.##;-0.##;0} m");
    }

    private void EndGizmoDrag()
    {
        _gizmoDrag = false;
        _gizmoStartPos.Clear();
    }

    private List<NodeDef> SelectionDefsForGizmo()
    {
        var defs = SelectionDefs();
        if (defs.Count == 0 && _selectedDef != null)
            defs.Add(_selectedDef);
        return defs;
    }

    private void RebuildGizmoSelection()
    {
        // Rebuild every moved object, keeping the selection on the same nodes.
        foreach (var node in SelectionNodes().ToList())
            RebuildNode(node);
        _selected = _defByNode.FirstOrDefault(kv => kv.Value == _selectedDef).Key ?? _selected;
    }

    /// <summary>Position along an axis (in metres) where the mouse ray is closest to it.</summary>
    private float AxisParamUnderMouse(Vector3 origin, Vector3 axis)
    {
        var (ro, rd) = ActiveCamera.ScreenPointToRay(Input.MousePosition);
        // Closest point between the axis line (origin + t*axis) and the mouse ray.
        var w0 = origin - ro;
        float b = Vector3.Dot(axis, rd);
        float c = Vector3.Dot(rd, rd);
        float dd = Vector3.Dot(axis, w0);
        float e = Vector3.Dot(rd, w0);
        float denom = c - b * b;               // a = axis·axis = 1
        if (MathF.Abs(denom) < 1e-5f)
            return 0f;                         // ray parallel to axis
        return (b * e - c * dd) / denom;
    }

    /// <summary>The axis whose handle the mouse is over (screen-space), or -1.</summary>
    private int PickGizmoAxis(Vector3 origin, float len)
    {
        var o = ActiveCamera.WorldToScreenPoint(origin);
        if (o == null)
            return -1;
        var mouse = Input.MousePosition;
        int best = -1;
        float bestDist = 14f;                  // pixel threshold
        for (int i = 0; i < 3; i++)
        {
            var tip = ActiveCamera.WorldToScreenPoint(origin + GizmoAxes[i] * len);
            if (tip == null)
                continue;
            float d = DistanceToSegment(mouse, new Vector2(o.Value.X, o.Value.Y), new Vector2(tip.Value.X, tip.Value.Y));
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-4f)
            return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>Draws the three axis handles, the hovered/active one highlighted.</summary>
    private void DrawGizmo()
    {
        if (_selected is not { IsInsideTree: true } || _selectedDef == null)
            return;
        var origin = _selected.GlobalPosition;
        float len = GizmoLength(origin);
        int active = _gizmoDrag ? _gizmoHover : _gizmoHover;
        for (int i = 0; i < 3; i++)
        {
            var color = i == active ? Color.FromHex("#ffe14a") : Color.FromHex(GizmoColors[i]);
            var tip = origin + GizmoAxes[i] * len;
            DebugDraw.Line(origin, tip, color);
            // a small arrowhead: two short lines back from the tip
            var back = tip - GizmoAxes[i] * (len * 0.18f);
            var perp = (GizmoAxes[i] == Vector3.UnitY ? Vector3.UnitX : Vector3.UnitY) * (len * 0.06f);
            var perp2 = Vector3.Cross(GizmoAxes[i], perp == Vector3.Zero ? Vector3.UnitZ : Vector3.Normalize(perp)) * (len * 0.06f);
            DebugDraw.Line(tip, back + perp, color);
            DebugDraw.Line(tip, back - perp, color);
            DebugDraw.Line(tip, back + perp2, color);
            DebugDraw.Line(tip, back - perp2, color);
        }
    }
}
