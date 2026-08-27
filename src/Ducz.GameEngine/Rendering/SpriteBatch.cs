using System.Numerics;
using Silk.NET.OpenGL;

namespace Ducz.Rendering;

/// <summary>
/// Batched 2D quad renderer used for UI and text. Coordinates are in window pixels,
/// origin at the top-left. Used internally by the UI system; you can also use it
/// directly for custom 2D drawing via <c>Engine.Renderer.SpriteBatch</c> inside UI draws.
/// </summary>
public sealed class SpriteBatch
{
    private const int MaxQuads = 2048;
    private const int FloatsPerVertex = 8; // pos2, uv2, color4

    private readonly GL _gl;
    private readonly GraphicsDevice _device;
    private readonly Shader _shader;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly float[] _vertices = new float[MaxQuads * 4 * FloatsPerVertex];

    private int _quadCount;
    private readonly Stack<Vector4> _clipStack = new();
    private float _windowHeight;
    private Texture2D? _currentTexture;
    private Matrix4x4 _projection;
    private bool _begun;

    internal unsafe SpriteBatch(GraphicsDevice device)
    {
        _device = device;
        _gl = device.GL;
        _shader = Shader.FromSource(device, BuiltinShaders.SpriteVertex, BuiltinShaders.SpriteFragment);

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertices.Length * sizeof(float)), null, BufferUsageARB.StreamDraw);

        uint stride = FloatsPerVertex * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)8);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)16);

        // Static index buffer: 6 indices per quad.
        var indices = new uint[MaxQuads * 6];
        for (int i = 0; i < MaxQuads; i++)
        {
            uint v = (uint)(i * 4);
            int o = i * 6;
            indices[o] = v; indices[o + 1] = v + 1; indices[o + 2] = v + 2;
            indices[o + 3] = v; indices[o + 4] = v + 2; indices[o + 5] = v + 3;
        }
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* ptr = indices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
        }
        _gl.BindVertexArray(0);
    }

    /// <summary>Starts a batch. Pixel coordinates map to the given window size.</summary>
    public void Begin(int windowWidth, int windowHeight)
    {
        _projection = Matrix4x4.CreateOrthographicOffCenter(0, windowWidth, windowHeight, 0, -1, 1);
        _begun = true;
        _quadCount = 0;
        _currentTexture = null;
        _windowHeight = windowHeight;
        _clipStack.Clear();
        _gl.Disable(EnableCap.ScissorTest);
    }

    /// <summary>
    /// Limits drawing to a rectangle (in window pixels) until <see cref="PopClip"/>. Used by
    /// scrolling panels so their contents stop at the panel edge instead of spilling over the
    /// rest of the screen. Nesting is supported; the inner rectangle is intersected.
    /// </summary>
    public void PushClip(Vector2 position, Vector2 size)
    {
        Flush();
        var rect = new Vector4(position.X, position.Y, MathF.Max(0f, size.X), MathF.Max(0f, size.Y));
        if (_clipStack.Count > 0)
        {
            var outer = _clipStack.Peek();
            float x0 = MathF.Max(outer.X, rect.X);
            float y0 = MathF.Max(outer.Y, rect.Y);
            float x1 = MathF.Min(outer.X + outer.Z, rect.X + rect.Z);
            float y1 = MathF.Min(outer.Y + outer.W, rect.Y + rect.W);
            rect = new Vector4(x0, y0, MathF.Max(0f, x1 - x0), MathF.Max(0f, y1 - y0));
        }
        _clipStack.Push(rect);
        ApplyClip();
    }

    /// <summary>Restores the clip rectangle in force before the matching <see cref="PushClip"/>.</summary>
    public void PopClip()
    {
        if (_clipStack.Count == 0)
            return;
        Flush();
        _clipStack.Pop();
        ApplyClip();
    }

    private void ApplyClip()
    {
        if (_clipStack.Count == 0)
        {
            _gl.Disable(EnableCap.ScissorTest);
            return;
        }
        var rect = _clipStack.Peek();
        // GL scissor counts from the bottom of the window; the UI counts from the top.
        int height = (int)MathF.Round(_windowHeight);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor((int)MathF.Round(rect.X), height - (int)MathF.Round(rect.Y + rect.W),
                    (uint)MathF.Round(rect.Z), (uint)MathF.Round(rect.W));
    }

    /// <summary>Draws a solid colored rectangle.</summary>
    public void DrawRect(Vector2 position, Vector2 size, Color color) =>
        DrawTexture(_device.WhiteTexture, position, size, color);

    /// <summary>Draws a rectangle border with the given thickness.</summary>
    public void DrawRectOutline(Vector2 position, Vector2 size, Color color, float thickness = 1f)
    {
        DrawRect(position, new Vector2(size.X, thickness), color);
        DrawRect(position + new Vector2(0, size.Y - thickness), new Vector2(size.X, thickness), color);
        DrawRect(position + new Vector2(0, thickness), new Vector2(thickness, size.Y - thickness * 2), color);
        DrawRect(position + new Vector2(size.X - thickness, thickness), new Vector2(thickness, size.Y - thickness * 2), color);
    }

    /// <summary>Draws a texture stretched to a rectangle.</summary>
    public void DrawTexture(Texture2D texture, Vector2 position, Vector2 size, Color? tint = null) =>
        DrawTexture(texture, position, size, Vector2.Zero, Vector2.One, tint ?? Color.White);

    /// <summary>Draws a sub-region (UV rectangle, 0..1) of a texture.</summary>
    public void DrawTexture(Texture2D texture, Vector2 position, Vector2 size, Vector2 uvMin, Vector2 uvMax, Color tint)
    {
        if (!_begun)
            throw new InvalidOperationException("Call Begin before drawing.");

        if (_currentTexture != null && _currentTexture != texture)
            Flush();
        if (_quadCount >= MaxQuads)
            Flush();
        _currentTexture = texture;

        int o = _quadCount * 4 * FloatsPerVertex;
        WriteVertex(ref o, position.X, position.Y, uvMin.X, uvMin.Y, tint);
        WriteVertex(ref o, position.X + size.X, position.Y, uvMax.X, uvMin.Y, tint);
        WriteVertex(ref o, position.X + size.X, position.Y + size.Y, uvMax.X, uvMax.Y, tint);
        WriteVertex(ref o, position.X, position.Y + size.Y, uvMin.X, uvMax.Y, tint);
        _quadCount++;
    }

    private void WriteVertex(ref int offset, float x, float y, float u, float v, Color color)
    {
        _vertices[offset++] = x;
        _vertices[offset++] = y;
        _vertices[offset++] = u;
        _vertices[offset++] = v;
        _vertices[offset++] = color.R;
        _vertices[offset++] = color.G;
        _vertices[offset++] = color.B;
        _vertices[offset++] = color.A;
    }

    /// <summary>Draws text with a font. Returns the pixel width drawn.</summary>
    public float DrawText(UI.Font font, string text, Vector2 position, Color color, float scale = 1f)
    {
        return font.Draw(this, text, position, color, scale);
    }

    /// <summary>Ends the batch, flushing pending quads.</summary>
    public void End()
    {
        Flush();
        _clipStack.Clear();
        _gl.Disable(EnableCap.ScissorTest);
        _begun = false;
    }

    private unsafe void Flush()
    {
        if (_quadCount == 0 || _currentTexture == null)
        {
            _quadCount = 0;
            return;
        }

        _shader.Use();
        _shader.Set("uProjection", _projection);
        _shader.Set("uTexture", 0);
        _currentTexture.Bind(0);

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* ptr = _vertices)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0,
                (nuint)(_quadCount * 4 * FloatsPerVertex * sizeof(float)), ptr);
        }
        _gl.DrawElements(PrimitiveType.Triangles, (uint)(_quadCount * 6), DrawElementsType.UnsignedInt, (void*)0);
        _gl.BindVertexArray(0);

        _device.DrawCalls++;
        _quadCount = 0;
        _currentTexture = null;
    }
}
