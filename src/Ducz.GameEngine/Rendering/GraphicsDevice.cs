using Silk.NET.OpenGL;

namespace Ducz.Rendering;

/// <summary>Blend modes supported by the renderer.</summary>
public enum BlendMode
{
    Opaque,
    Alpha,
    Additive
}

/// <summary>
/// Thin wrapper around the OpenGL context: state changes, defaults and statistics.
/// Advanced users can reach the raw API via <see cref="GL"/>.
/// </summary>
public sealed class GraphicsDevice
{
    /// <summary>The raw Silk.NET OpenGL API.</summary>
    public GL GL { get; }

    /// <summary>OpenGL version string reported by the driver.</summary>
    public string GlVersion { get; }

    /// <summary>GPU name reported by the driver.</summary>
    public string GlRenderer { get; }

    /// <summary>Draw calls issued during the current frame (reset by the renderer).</summary>
    public int DrawCalls { get; internal set; }

    /// <summary>Triangles submitted during the current frame.</summary>
    public int Triangles { get; internal set; }

    /// <summary>A 1x1 white texture, useful as a default.</summary>
    public Texture2D WhiteTexture { get; }

    private BlendMode _blendMode = BlendMode.Opaque;
    private bool _depthTest = true;
    private bool _depthWrite = true;
    private bool _cullBack = true;

    internal GraphicsDevice(GL gl)
    {
        GL = gl;
        unsafe
        {
            GlVersion = gl.GetStringS(StringName.Version) ?? "?";
            GlRenderer = gl.GetStringS(StringName.Renderer) ?? "?";
        }

        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.Enable(EnableCap.CullFace);
        gl.CullFace(GLEnum.Back);
        gl.FrontFace(FrontFaceDirection.Ccw);
        gl.Enable(EnableCap.Multisample);

        WhiteTexture = Texture2D.FromPixels(this, 1, 1, new byte[] { 255, 255, 255, 255 }, generateMipmaps: false);
    }

    public void SetViewport(int x, int y, int width, int height) =>
        GL.Viewport(x, y, (uint)width, (uint)height);

    public void Clear(Color color, bool depth = true)
    {
        GL.ClearColor(color.R, color.G, color.B, color.A);
        var mask = (uint)ClearBufferMask.ColorBufferBit;
        if (depth)
        {
            SetDepthWrite(true); // glClear respects the depth mask
            mask |= (uint)ClearBufferMask.DepthBufferBit;
        }
        GL.Clear(mask);
    }

    public void SetBlendMode(BlendMode mode)
    {
        if (_blendMode == mode)
            return;
        _blendMode = mode;

        switch (mode)
        {
            case BlendMode.Opaque:
                GL.Disable(EnableCap.Blend);
                break;
            case BlendMode.Alpha:
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                break;
            case BlendMode.Additive:
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                break;
        }
    }

    public void SetDepthTest(bool enabled)
    {
        if (_depthTest == enabled)
            return;
        _depthTest = enabled;
        if (enabled) GL.Enable(EnableCap.DepthTest);
        else GL.Disable(EnableCap.DepthTest);
    }

    public void SetDepthWrite(bool enabled)
    {
        if (_depthWrite == enabled)
            return;
        _depthWrite = enabled;
        GL.DepthMask(enabled);
    }

    public void SetBackfaceCulling(bool enabled)
    {
        if (_cullBack == enabled)
            return;
        _cullBack = enabled;
        if (enabled) GL.Enable(EnableCap.CullFace);
        else GL.Disable(EnableCap.CullFace);
    }

    internal void ResetFrameStats()
    {
        DrawCalls = 0;
        Triangles = 0;
    }
}
