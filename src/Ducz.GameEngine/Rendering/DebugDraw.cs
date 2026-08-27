using System.Numerics;
using Silk.NET.OpenGL;

namespace Ducz.Rendering;

/// <summary>
/// Immediate-mode debug line drawing. Call from anywhere each frame:
/// <code>DebugDraw.Line(a, b, Color.Red);</code>
/// Lines are flushed and cleared by the renderer every frame (unless given a duration).
/// </summary>
public static class DebugDraw
{
    private struct TimedLine
    {
        public Vector3 A, B;
        public Color Color;
        public float TimeLeft;
    }

    private static readonly List<float> _vertexData = new();
    private static readonly List<TimedLine> _timedLines = new();
    private static int _lineCount;

    /// <summary>Draws a line for one frame (or <paramref name="duration"/> seconds).</summary>
    public static void Line(Vector3 from, Vector3 to, Color color, float duration = 0f)
    {
        if (duration > 0f)
        {
            _timedLines.Add(new TimedLine { A = from, B = to, Color = color, TimeLeft = duration });
            return;
        }
        Push(from, to, color);
    }

    /// <summary>Draws a ray from an origin along a direction.</summary>
    public static void Ray(Vector3 origin, Vector3 direction, Color color, float duration = 0f) =>
        Line(origin, origin + direction, color, duration);

    /// <summary>Draws an axis-aligned box given min/max corners.</summary>
    public static void Aabb(Vector3 min, Vector3 max, Color color, float duration = 0f)
    {
        Vector3 a = min, b = max;
        var corners = new[]
        {
            new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, a.Y, a.Z),
            new Vector3(b.X, a.Y, b.Z), new Vector3(a.X, a.Y, b.Z),
            new Vector3(a.X, b.Y, a.Z), new Vector3(b.X, b.Y, a.Z),
            new Vector3(b.X, b.Y, b.Z), new Vector3(a.X, b.Y, b.Z)
        };
        int[][] edges =
        {
            new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 },
            new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 4 },
            new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 }
        };
        foreach (var e in edges)
            Line(corners[e[0]], corners[e[1]], color, duration);
    }

    /// <summary>Draws a wire sphere (3 axis-aligned circles).</summary>
    public static void Sphere(Vector3 center, float radius, Color color, float duration = 0f, int segments = 24)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 prev = default;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.Tau;
                var point = axis switch
                {
                    0 => center + new Vector3(0, MathF.Cos(t), MathF.Sin(t)) * radius,
                    1 => center + new Vector3(MathF.Cos(t), 0, MathF.Sin(t)) * radius,
                    _ => center + new Vector3(MathF.Cos(t), MathF.Sin(t), 0) * radius
                };
                if (i > 0)
                    Line(prev, point, color, duration);
                prev = point;
            }
        }
    }

    /// <summary>Draws XYZ axes (X=red, Y=green, Z=blue) at a transform.</summary>
    public static void Axes(Matrix4x4 transform, float size = 1f, float duration = 0f)
    {
        var origin = transform.Translation;
        Line(origin, origin + transform.TransformDirection(Vector3.UnitX) * size, Color.Red, duration);
        Line(origin, origin + transform.TransformDirection(Vector3.UnitY) * size, Color.Green, duration);
        Line(origin, origin + transform.TransformDirection(Vector3.UnitZ) * size, Color.Blue, duration);
    }

    /// <summary>Draws a flat grid on the XZ plane, centered at the origin.</summary>
    public static void Grid(int halfCells = 10, float cellSize = 1f, Color? color = null)
    {
        var c = color ?? Color.Gray.WithAlpha(0.5f);
        float extent = halfCells * cellSize;
        for (int i = -halfCells; i <= halfCells; i++)
        {
            Line(new Vector3(i * cellSize, 0, -extent), new Vector3(i * cellSize, 0, extent), c);
            Line(new Vector3(-extent, 0, i * cellSize), new Vector3(extent, 0, i * cellSize), c);
        }
    }

    private static void Push(Vector3 a, Vector3 b, Color color)
    {
        _vertexData.Add(a.X); _vertexData.Add(a.Y); _vertexData.Add(a.Z);
        _vertexData.Add(color.R); _vertexData.Add(color.G); _vertexData.Add(color.B); _vertexData.Add(color.A);
        _vertexData.Add(b.X); _vertexData.Add(b.Y); _vertexData.Add(b.Z);
        _vertexData.Add(color.R); _vertexData.Add(color.G); _vertexData.Add(color.B); _vertexData.Add(color.A);
        _lineCount++;
    }

    // ---- Renderer internals ----

    internal static unsafe void Flush(GL gl, Shader lineShader, Matrix4x4 viewProj, ref uint vao, ref uint vbo)
    {
        // Age timed lines into the buffer.
        for (int i = _timedLines.Count - 1; i >= 0; i--)
        {
            var line = _timedLines[i];
            Push(line.A, line.B, line.Color);
            line.TimeLeft -= Time.UnscaledDeltaTime;
            if (line.TimeLeft <= 0f)
                _timedLines.RemoveAt(i);
            else
                _timedLines[i] = line;
        }

        if (_lineCount == 0)
            return;

        if (vao == 0)
        {
            vao = gl.GenVertexArray();
            vbo = gl.GenBuffer();
            gl.BindVertexArray(vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, (void*)12);
            gl.BindVertexArray(0);
        }

        var data = _vertexData.ToArray();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (float* ptr = data)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, BufferUsageARB.StreamDraw);
        }

        lineShader.Use();
        lineShader.Set("uViewProj", viewProj);
        gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(_lineCount * 2));
        gl.BindVertexArray(0);

        _vertexData.Clear();
        _lineCount = 0;
    }
}
