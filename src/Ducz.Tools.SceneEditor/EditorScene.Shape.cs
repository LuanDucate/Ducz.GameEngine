using System.Globalization;
using System.Numerics;
using Ducz.Serialization;
using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// The "Shape" section of the properties panel: the numeric parameters of the selected object
/// (size, radius, slope, steps, arc...). The rows are generic slots reconfigured for whatever
/// is selected, so every primitive exposes exactly the fields that make sense for it.
/// </summary>
partial class EditorScene
{
    /// <summary>One editable number of the selected object.</summary>
    private sealed record ShapeParam(string Label, Func<float> Get, Action<float> Set, string Suffix = "", float Step = 0.25f, float Min = 0.01f, float Max = 500f)
    {
        /// <summary>Integer parameters (steps, sides, segments) snap to whole numbers.</summary>
        public bool Integer { get; init; }
    }

    private const int ShapeRows = 7;
    private Label[] _shapeLabels = Array.Empty<Label>();
    private TextBox[] _shapeBoxes = Array.Empty<TextBox>();
    private Button[] _shapeMinus = Array.Empty<Button>();
    private Button[] _shapePlus = Array.Empty<Button>();
    private Label _shapeHint = null!;
    private float _shapeRowTop;
    private readonly List<ShapeParam> _shapeParams = new();

    /// <summary>Builds the shape rows; returns the y where the section ends.</summary>
    private float BuildShapeSection(Panel panel, float y)
    {
        panel.AddChild(new Label("SHAPE") { Anchor = Anchor.TopLeft, Position = new Vector2(14, y), FontSize = 12, Color = Color.White.WithAlpha(0.5f) });
        y += 18f;

        _shapeLabels = new Label[ShapeRows];
        _shapeBoxes = new TextBox[ShapeRows];
        _shapeMinus = new Button[ShapeRows];
        _shapePlus = new Button[ShapeRows];

        for (int i = 0; i < ShapeRows; i++)
        {
            int row = i;
            _shapeLabels[i] = panel.AddChild(new Label("") { Anchor = Anchor.TopLeft, Position = new Vector2(14, y + 5), FontSize = 13 });
            _shapeBoxes[i] = panel.AddChild(new TextBox
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(96, y), Size = new Vector2(58, 24), FontSize = 13
            });
            _shapeBoxes[i].Submitted += text => ApplyShapeParam(row, text);
            _shapeMinus[i] = panel.AddChild(new Button("-") { Anchor = Anchor.TopLeft, Position = new Vector2(158, y), Size = new Vector2(26, 24), FontSize = 14 });
            _shapeMinus[i].Clicked += () => StepShapeParam(row, -1, true);
            AddRepeat(_shapeMinus[i], first => StepShapeParam(row, -1, first));
            _shapePlus[i] = panel.AddChild(new Button("+") { Anchor = Anchor.TopLeft, Position = new Vector2(188, y), Size = new Vector2(26, 24), FontSize = 14 });
            _shapePlus[i].Clicked += () => StepShapeParam(row, +1, true);
            AddRepeat(_shapePlus[i], first => StepShapeParam(row, +1, first));
            y += 27f;
        }

        _shapeHint = panel.AddChild(new Label("") { Anchor = Anchor.TopLeft, Position = new Vector2(14, y), FontSize = 11, Color = Color.White.WithAlpha(0.55f) });
        _shapeRowTop = y - ShapeRows * 27f;
        return y + 12f;
    }

    private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryNum(string text, out float value) =>
        float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void ApplyShapeParam(int row, string text)
    {
        if (row >= _shapeParams.Count || _selectedDef == null)
            return;
        var p = _shapeParams[row];
        if (!TryNum(text, out float value))
        {
            SetStatus($"\"{text}\" is not a number");
            RefreshShapeSection();
            return;
        }
        value = Mathf.Clamp(p.Integer ? MathF.Round(value) : value, p.Min, p.Max);
        if (MathF.Abs(p.Get() - value) < 1e-5f)
            return;
        PushUndo();
        p.Set(value);
        RebuildSelected();
        SetStatus($"{p.Label}: {Num(value)}{p.Suffix}");
    }

    private void StepShapeParam(int row, int direction, bool pushUndo)
    {
        if (row >= _shapeParams.Count || _selectedDef == null)
            return;
        var p = _shapeParams[row];
        float step = p.Integer ? 1f : (Input.IsKeyDown(Key.LeftShift) ? p.Step * 4f : p.Step);
        float value = Mathf.Clamp(p.Get() + step * direction, p.Min, p.Max);
        if (p.Integer)
            value = MathF.Round(value);
        if (MathF.Abs(value - p.Get()) < 1e-6f)
            return;
        if (pushUndo)
            PushUndo();
        p.Set(value);
        RebuildSelected();
        RefreshShapeSection();
        SetStatus($"{p.Label}: {Num(value)}{p.Suffix}");
    }

    /// <summary>Rebuilds the parameter list for the current selection and fills the rows.</summary>
    private void RefreshShapeSection()
    {
        _shapeParams.Clear();
        if (_selectedDef != null)
            CollectShapeParams(_selectedDef, _shapeParams);

        for (int i = 0; i < ShapeRows; i++)
        {
            bool used = i < _shapeParams.Count;
            _shapeLabels[i].Visible = _shapeBoxes[i].Visible = _shapeMinus[i].Visible = _shapePlus[i].Visible = used;
            if (!used)
                continue;
            var p = _shapeParams[i];
            _shapeLabels[i].Text = p.Label;
            if (_canvas.FocusedElement != _shapeBoxes[i])
                _shapeBoxes[i].Text = Num(p.Get());
        }
        _shapeHint.Text = _selectedDef == null ? "" : ShapeHint(_selectedDef);
        _shapeHint.Position = new Vector2(14, _shapeRowTop + Math.Max(1, _shapeParams.Count) * 27f + 2f);
    }

    /// <summary>A one-line explanation of the selected shape (slope, step size, arc length...).</summary>
    private static string ShapeHint(NodeDef def)
    {
        var mesh = def.Mesh;
        string primitive = (mesh?.Primitive ?? def.Type).ToLowerInvariant();
        float[]? size = mesh?.Size ?? def.Size;
        float W(int i, float fallback) => size is { } s && s.Length > i ? s[i] : fallback;

        switch (primitive)
        {
            case "wedge" or "ramp":
            {
                float h = W(1, 1f), len = W(2, 3f);
                float deg = MathF.Atan2(h, len) * 180f / MathF.PI;
                return $"slope {deg:0.#}° ({h / MathF.Max(len, 0.01f) * 100f:0}%)  rise {Num(h)} over {Num(len)} m";
            }
            case "stairs":
            {
                float h = W(1, 2f), len = W(2, 3f);
                int steps = mesh?.Steps > 0 ? mesh.Steps : 8;
                return $"{steps} steps of {h / steps:0.##} m rise / {len / steps:0.##} m tread";
            }
            case "curvedwall":
            {
                float r = mesh?.Radius ?? 4f, arc = mesh?.ArcDegrees > 0 ? mesh.ArcDegrees : 90f;
                return $"arc length {r * arc * MathF.PI / 180f:0.##} m at radius {Num(r)}";
            }
            case "arch":
                return "opening = clear span x height to the spring line";
            case "roofgable" or "roofhip" or "roofshed":
                return "eaves at the bottom of the box; ridge at the top";
            default:
                return "";
        }
    }

    /// <summary>Builds the list of numeric parameters that make sense for a node definition.</summary>
    private static void CollectShapeParams(NodeDef def, List<ShapeParam> list)
    {
        string type = def.Type.ToLowerInvariant();

        // Prefab types carry their dimensions in NodeDef.Size.
        switch (type)
        {
            case "floor":
                AddSizeParams(def, list, () => def.Size ??= new[] { 20f, 20f }, "Width", "Depth");
                return;
            case "wall":
                AddSizeParams(def, list, () => def.Size ??= new[] { 4f, 3f, 0.3f }, "Length", "Height", "Thickness");
                return;
            case "ramp":
                AddSizeParams(def, list, () => def.Size ??= new[] { 2f, 1f, 3f }, "Width", "Rise", "Run");
                list.Add(SlopeParam(() => def.Size!, "Slope"));
                return;
            case "crate":
                AddSizeParams(def, list, () => def.Size ??= new[] { 1f }, "Size");
                list.Add(new ShapeParam("Mass", () => def.Mass, v => def.Mass = v, " kg", 0.5f, 0.1f, 500f));
                return;
            case "pointlight" or "spotlight":
                list.Add(new ShapeParam("Energy", () => def.Energy, v => def.Energy = v, "", 0.25f, 0f, 20f));
                list.Add(new ShapeParam("Range", () => def.Range, v => def.Range = v, " m", 1f, 0.5f, 200f));
                if (type == "spotlight")
                {
                    list.Add(new ShapeParam("Angle", () => def.Angle, v => def.Angle = v, "°", 5f, 1f, 179f));
                    list.Add(new ShapeParam("Softness", () => def.Softness, v => def.Softness = v, "", 0.05f, 0f, 1f));
                }
                return;
            case "terrain":
            {
                var t = def.Terrain ??= new TerrainDef();
                list.Add(new ShapeParam("Size X", () => t.SizeX, v => t.SizeX = v, " m", 5f, 1f, 500f));
                list.Add(new ShapeParam("Size Z", () => t.SizeZ, v => t.SizeZ = v, " m", 5f, 1f, 500f));
                list.Add(new ShapeParam("Amplitude", () => t.Amplitude, v => t.Amplitude = v, " m", 0.5f, 0f, 100f));
                list.Add(new ShapeParam("Frequency", () => t.Frequency, v => t.Frequency = v, "", 0.01f, 0.001f, 2f));
                list.Add(new ShapeParam("Resolution", () => t.Resolution, v => t.Resolution = (int)v, "", 1f, 2f, 400f) { Integer = true });
                return;
            }
            case "model":
                list.Add(new ShapeParam("Scale", () => def.Scale is { Length: > 0 } s ? s[0] : 1f,
                    v => def.Scale = MathF.Abs(v - 1f) < 0.001f ? null : new[] { v, v, v }, "x", 0.1f, 0.01f, 100f));
                return;
        }

        // Mesh-based types (static / mesh / rigid / area): parameters depend on the primitive.
        var m = def.Mesh;
        if (m == null)
            return;

        switch (m.Primitive.ToLowerInvariant())
        {
            case "box" or "roundedbox" or "pyramid":
                AddMeshSizeParams(m, list, "Width", "Height", "Depth");
                if (m.Primitive.Equals("roundedBox", StringComparison.OrdinalIgnoreCase))
                    list.Add(new ShapeParam("Bevel", () => m.Bevel > 0 ? m.Bevel : 0.08f, v => m.Bevel = v, " m", 0.02f, 0.005f, 5f));
                break;
            case "cube":
                list.Add(new ShapeParam("Size", () => m.Size is { Length: > 0 } s ? s[0] : 1f, v => m.Size = new[] { v }, " m"));
                break;
            case "wedge":
                AddMeshSizeParams(m, list, "Width", "Rise", "Run");
                list.Add(SlopeParam(() => m.Size ??= new[] { 2f, 1f, 3f }, "Slope"));
                break;
            case "stairs":
                AddMeshSizeParams(m, list, "Width", "Height", "Depth");
                list.Add(new ShapeParam("Steps", () => m.Steps > 0 ? m.Steps : 8, v => m.Steps = (int)v, "", 1f, 1f, 60f) { Integer = true });
                break;
            case "roofgable" or "roofhip":
                AddMeshSizeParams(m, list, "Width", "Height", "Depth");
                list.Add(new ShapeParam("Overhang", () => m.Overhang, v => m.Overhang = v, " m", 0.1f, 0f, 5f));
                if (m.Primitive.Equals("roofHip", StringComparison.OrdinalIgnoreCase))
                    list.Add(new ShapeParam("Ridge", () => m.RidgeLength >= 0 ? m.RidgeLength : 3f, v => m.RidgeLength = v, " m", 0.5f, 0f, 100f));
                break;
            case "roofshed":
                AddMeshSizeParams(m, list, "Width", "Rise", "Depth");
                list.Add(new ShapeParam("Thickness", () => m.Thickness, v => m.Thickness = v, " m", 0.05f, 0.02f, 3f));
                break;
            case "arch":
                AddMeshSizeParams(m, list, "Width", "Height", null);
                list.Add(new ShapeParam("Thickness", () => m.Thickness <= 0.15f ? 0.4f : m.Thickness, v => m.Thickness = v, " m", 0.05f, 0.05f, 5f));
                list.Add(new ShapeParam("Opening W", () => m.OpeningWidth >= 0 ? m.OpeningWidth : 2f, v => m.OpeningWidth = v, " m", 0.1f, 0.1f, 50f));
                list.Add(new ShapeParam("Opening H", () => m.OpeningHeight >= 0 ? m.OpeningHeight : 2f, v => m.OpeningHeight = v, " m", 0.1f, 0.1f, 50f));
                list.Add(new ShapeParam("Segments", () => m.Segments > 0 ? m.Segments : 16, v => m.Segments = (int)v, "", 2f, 3f, 48f) { Integer = true });
                break;
            case "curvedwall":
                list.Add(new ShapeParam("Radius", () => m.Radius <= 0.5f ? 4f : m.Radius, v => m.Radius = v, " m", 0.25f, 0.2f, 200f));
                list.Add(new ShapeParam("Height", () => m.Height, v => m.Height = v, " m"));
                list.Add(new ShapeParam("Thickness", () => m.Thickness <= 0.15f ? 0.3f : m.Thickness, v => m.Thickness = v, " m", 0.05f, 0.02f, 10f));
                list.Add(new ShapeParam("Arc", () => m.ArcDegrees > 0 ? m.ArcDegrees : 90f, v => m.ArcDegrees = v, "°", 15f, 1f, 360f));
                list.Add(new ShapeParam("Segments", () => m.Segments > 0 ? m.Segments : 16, v => m.Segments = (int)v, "", 2f, 2f, 96f) { Integer = true });
                break;
            case "tube":
                list.Add(new ShapeParam("Radius", () => m.Radius, v => m.Radius = v, " m", 0.1f));
                list.Add(new ShapeParam("Height", () => m.Height, v => m.Height = v, " m"));
                list.Add(new ShapeParam("Thickness", () => m.Thickness, v => m.Thickness = v, " m", 0.02f, 0.01f, 10f));
                list.Add(new ShapeParam("Segments", () => m.Segments > 0 ? m.Segments : 24, v => m.Segments = (int)v, "", 2f, 3f, 96f) { Integer = true });
                break;
            case "prism":
                list.Add(new ShapeParam("Radius", () => m.Radius, v => m.Radius = v, " m", 0.1f));
                list.Add(new ShapeParam("Height", () => m.Height, v => m.Height = v, " m"));
                list.Add(new ShapeParam("Sides", () => m.Sides > 0 ? m.Sides : 6, v => m.Sides = (int)v, "", 1f, 3f, 32f) { Integer = true });
                break;
            case "cylinder" or "cone":
                list.Add(new ShapeParam("Radius", () => m.Radius, v => m.Radius = v, " m", 0.1f));
                list.Add(new ShapeParam("Height", () => m.Height, v => m.Height = v, " m"));
                list.Add(new ShapeParam("Segments", () => m.Segments > 0 ? m.Segments : 32, v => m.Segments = (int)v, "", 4f, 3f, 96f) { Integer = true });
                break;
            case "sphere":
                list.Add(new ShapeParam("Radius", () => m.Radius, v => m.Radius = v, " m", 0.1f));
                list.Add(new ShapeParam("Segments", () => m.Segments > 0 ? m.Segments : 24, v => m.Segments = (int)v, "", 4f, 4f, 64f) { Integer = true });
                break;
            case "capsule":
                list.Add(new ShapeParam("Radius", () => m.Radius, v => m.Radius = v, " m", 0.05f));
                list.Add(new ShapeParam("Height", () => m.Height, v => m.Height = v, " m"));
                break;
            case "torus":
                list.Add(new ShapeParam("Radius", () => m.Radius, v => m.Radius = v, " m", 0.1f));
                list.Add(new ShapeParam("Thickness", () => m.Thickness, v => m.Thickness = v, " m", 0.02f, 0.01f, 10f));
                break;
            case "plane" or "quad":
                AddMeshSizeParams(m, list, "Width", "Depth", null);
                break;
            default:
                AddMeshSizeParams(m, list, "Width", "Height", "Depth");
                break;
        }
    }

    /// <summary>Slope parameter: reading it computes the angle, setting it adjusts the rise.</summary>
    private static ShapeParam SlopeParam(Func<float[]> size, string label) => new(
        label,
        () =>
        {
            var s = size();
            float h = s.Length > 1 ? s[1] : 1f, len = s.Length > 2 ? s[2] : 3f;
            return MathF.Atan2(h, MathF.Max(len, 0.01f)) * 180f / MathF.PI;
        },
        deg =>
        {
            var s = size();
            if (s.Length < 3) return;
            float len = s[2];
            s[1] = MathF.Max(0.01f, MathF.Tan(Mathf.Clamp(deg, 0.5f, 80f) * MathF.PI / 180f) * len);
        },
        "°", 2.5f, 0.5f, 80f);

    private static void AddSizeParams(NodeDef def, List<ShapeParam> list, Func<float[]> ensure, params string[] labels)
    {
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            list.Add(new ShapeParam(labels[i],
                () => { var s = ensure(); return s.Length > index ? s[index] : 1f; },
                v =>
                {
                    var s = ensure();
                    if (s.Length <= index)
                    {
                        var grown = new float[index + 1];
                        Array.Copy(s, grown, s.Length);
                        for (int k = s.Length; k < grown.Length; k++) grown[k] = 1f;
                        def.Size = grown;
                        s = grown;
                    }
                    s[index] = v;
                }, " m"));
        }
    }

    private static void AddMeshSizeParams(MeshDef mesh, List<ShapeParam> list, string? labelX, string? labelY, string? labelZ)
    {
        var labels = new[] { labelX, labelY, labelZ };
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] == null)
                continue;
            int index = i;
            list.Add(new ShapeParam(labels[i]!,
                () => mesh.Size is { } s && s.Length > index ? s[index] : 1f,
                v =>
                {
                    var s = mesh.Size;
                    if (s == null || s.Length < 3)
                    {
                        var grown = new float[3];
                        for (int k = 0; k < 3; k++) grown[k] = s != null && s.Length > k ? s[k] : 1f;
                        mesh.Size = grown;
                        s = grown;
                    }
                    s[index] = v;
                }, " m"));
        }
    }
}
