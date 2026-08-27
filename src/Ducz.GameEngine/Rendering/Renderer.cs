using System.Numerics;
using Silk.NET.OpenGL;

namespace Ducz.Rendering;

/// <summary>
/// The forward renderer. It walks the scene tree every frame, renders shadows,
/// opaque and transparent geometry, particles, debug lines and finally the UI.
/// Access via <c>Engine.Renderer</c>.
/// </summary>
public sealed class Renderer
{
    private const int ShadowMapSize = 2048;

    /// <summary>Low-level graphics device (state + raw GL).</summary>
    public GraphicsDevice Device { get; }

    /// <summary>World environment: sky, ambient light and fog.</summary>
    public Environment Environment { get; set; } = new();

    /// <summary>The 2D batch used for UI. Also usable for custom 2D overlays.</summary>
    public SpriteBatch SpriteBatch { get; }

    /// <summary>Framebuffer width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Framebuffer height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Width / height.</summary>
    public float Aspect => Height <= 0 ? 1f : Width / (float)Height;

    private readonly GL _gl;
    private readonly Shader _standard;
    private readonly Shader _standardSkinned;
    private readonly Shader _depth;
    private readonly Shader _depthSkinned;
    private readonly Shader _sky;
    private readonly Shader _line;
    internal readonly Shader ParticleShader;

    private readonly uint _shadowFbo;
    private readonly uint _shadowTexture;
    private readonly uint _skyVao;
    private uint _lineVao;
    private uint _lineVbo;

    // Per-frame collections (reused to avoid allocations)
    private readonly List<MeshInstance3D> _meshInstances = new();
    private readonly List<PointLight3D> _pointLights = new();
    private readonly List<SpotLight3D> _spotLights = new();
    private readonly List<ParticleSystem3D> _particles = new();
    private readonly List<UI.Canvas> _canvases = new();
    private DirectionalLight3D? _directionalLight;

    private readonly List<(Surface Surface, MeshInstance3D Instance, float Distance)> _opaque = new();
    private readonly List<(Surface Surface, MeshInstance3D Instance, float Distance)> _transparent = new();

    private readonly Vector4[] _frustumPlanes = new Vector4[6];

    internal unsafe Renderer(GL gl, int width, int height)
    {
        _gl = gl;
        Width = width;
        Height = height;
        Device = new GraphicsDevice(gl);

        _standard = Shader.FromSource(Device, BuiltinShaders.StandardVertex, BuiltinShaders.StandardFragment);
        _standardSkinned = Shader.FromSource(Device, BuiltinShaders.StandardVertex, BuiltinShaders.StandardFragment, "SKINNED");
        _depth = Shader.FromSource(Device, BuiltinShaders.DepthVertex, BuiltinShaders.DepthFragment);
        _depthSkinned = Shader.FromSource(Device, BuiltinShaders.DepthVertex, BuiltinShaders.DepthFragment, "SKINNED");
        _sky = Shader.FromSource(Device, BuiltinShaders.SkyVertex, BuiltinShaders.SkyFragment);
        _line = Shader.FromSource(Device, BuiltinShaders.LineVertex, BuiltinShaders.LineFragment);
        ParticleShader = Shader.FromSource(Device, BuiltinShaders.ParticleVertex, BuiltinShaders.ParticleFragment);

        SpriteBatch = new SpriteBatch(Device);
        _skyVao = gl.GenVertexArray();

        // Shadow map framebuffer (depth only)
        _shadowTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _shadowTexture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.DepthComponent24,
            ShadowMapSize, ShadowMapSize, 0, PixelFormat.DepthComponent, PixelType.Float, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToBorder);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToBorder);
        var border = stackalloc float[] { 1f, 1f, 1f, 1f };
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, border);

        _shadowFbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _shadowTexture, 0);
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    internal void Resize(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
    }

    // ------------------------------------------------------------------
    // Frame
    // ------------------------------------------------------------------

    internal void RenderFrame(SceneTree tree)
    {
        Device.ResetFrameStats();
        CollectNodes(tree.Root, true);

        var camera = Camera3D.CurrentCamera;
        if (camera is { IsInsideTree: true })
        {
            RenderWorld(camera);
        }
        else
        {
            Device.SetViewport(0, 0, Width, Height);
            Device.Clear(Environment.ClearColor);
        }

        RenderUI();
        ClearCollections();
    }

    private void RenderWorld(Camera3D camera)
    {
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(Aspect);
        var viewProj = view * projection;
        var cameraPos = camera.GlobalPosition;

        // Sort surfaces into passes
        _opaque.Clear();
        _transparent.Clear();
        ExtractFrustumPlanes(viewProj);

        foreach (var instance in _meshInstances)
        {
            var world = instance.Skin?.Skeleton.GlobalTransform ?? instance.GlobalTransform;
            foreach (var surface in instance.Surfaces)
            {
                if (instance.FrustumCullingEnabled && !surface.Mesh.IsSkinned && IsCulled(surface.Mesh, world))
                    continue;

                float distance = Vector3.DistanceSquared(cameraPos, world.Translation);
                if (surface.Material.Transparent)
                    _transparent.Add((surface, instance, distance));
                else
                    _opaque.Add((surface, instance, distance));
            }
        }

        // Shadow pass
        var lightSpace = Matrix4x4.Identity;
        bool shadows = false;
        if (_directionalLight is { ShadowsEnabled: true })
        {
            lightSpace = BuildLightSpaceMatrix(_directionalLight, camera);
            RenderShadowPass(lightSpace);
            shadows = true;
        }

        // Main pass
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Device.SetViewport(0, 0, Width, Height);
        Device.SetBlendMode(BlendMode.Opaque);
        Device.SetDepthTest(true);
        Device.SetDepthWrite(true);

        if (Environment.Background == BackgroundMode.SolidColor)
        {
            Device.Clear(Environment.ClearColor);
        }
        else
        {
            Device.Clear(Color.Black);
            RenderSky(viewProj);
        }

        // Frame uniforms for both standard shader variants
        foreach (var shader in new[] { _standard, _standardSkinned })
        {
            shader.Use();
            shader.Set("uViewProj", viewProj);
            shader.Set("uCameraPos", cameraPos);
            shader.Set("uAmbientColor", Environment.AmbientColor.ToVector3() * Environment.AmbientIntensity);
            shader.Set("uLightSpace", lightSpace);
            shader.Set("uShadowsEnabled", shadows);

            if (_directionalLight != null)
            {
                shader.Set("uDirLightEnabled", true);
                shader.Set("uDirLightDir", _directionalLight.GlobalForward);
                shader.Set("uDirLightColor", _directionalLight.Color.ToVector3() * _directionalLight.Energy);
            }
            else
            {
                shader.Set("uDirLightEnabled", false);
            }

            int pointCount = Math.Min(_pointLights.Count, BuiltinShaders.MaxPointLights);
            shader.Set("uPointLightCount", pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                var light = _pointLights[i];
                shader.Set($"uPointLightPos[{i}]", light.GlobalPosition);
                shader.Set($"uPointLightColor[{i}]", light.Color.ToVector3() * light.Energy);
                shader.Set($"uPointLightRange[{i}]", MathF.Max(0.01f, light.Range));
            }

            int spotCount = Math.Min(_spotLights.Count, BuiltinShaders.MaxSpotLights);
            shader.Set("uSpotLightCount", spotCount);
            for (int i = 0; i < spotCount; i++)
            {
                var light = _spotLights[i];
                shader.Set($"uSpotLightPos[{i}]", light.GlobalPosition);
                shader.Set($"uSpotLightDir[{i}]", light.GlobalForward);
                shader.Set($"uSpotLightColor[{i}]", light.Color.ToVector3() * light.Energy);
                shader.Set($"uSpotLightRange[{i}]", MathF.Max(0.01f, light.Range));
                shader.Set($"uSpotLightAngleCos[{i}]", MathF.Cos(light.AngleDegrees * 0.5f * Mathf.Deg2Rad));
                shader.Set($"uSpotLightSoftness[{i}]", light.Softness);
            }

            shader.Set("uFogEnabled", Environment.FogEnabled);
            shader.Set("uFogColor", Environment.FogColor.ToVector3());
            shader.Set("uFogStart", Environment.FogStart);
            shader.Set("uFogEnd", MathF.Max(Environment.FogEnd, Environment.FogStart + 0.01f));

            shader.Set("uAlbedoTex", 0);
            shader.Set("uShadowMap", 1);
            shader.Set("uNormalMap", 2);
            shader.Set("uRoughnessMap", 3);
        }

        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _shadowTexture);

        // Opaque front-to-back
        _opaque.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        foreach (var (surface, instance, _) in _opaque)
            DrawSurface(surface, instance);

        // Transparent back-to-front
        Device.SetBlendMode(BlendMode.Alpha);
        Device.SetDepthWrite(false);
        _transparent.Sort((a, b) => b.Distance.CompareTo(a.Distance));
        foreach (var (surface, instance, _) in _transparent)
            DrawSurface(surface, instance);

        // Particles
        foreach (var system in _particles)
            system.Render(this, viewProj, camera);

        Device.SetBlendMode(BlendMode.Opaque);
        Device.SetDepthWrite(true);

        // Debug lines
        DebugDraw.Flush(_gl, _line, viewProj, ref _lineVao, ref _lineVbo);
    }

    private void DrawSurface(Surface surface, MeshInstance3D instance)
    {
        var material = surface.Material;
        var mesh = surface.Mesh;
        bool skinned = mesh.IsSkinned && instance.Skin != null;
        var shader = skinned ? _standardSkinned : _standard;
        shader.Use();

        var world = skinned ? instance.Skin!.Skeleton.GlobalTransform : instance.GlobalTransform;
        shader.Set("uModel", world);

        Matrix4x4.Invert(world, out var invWorld);
        shader.Set("uNormalMatrix", Matrix4x4.Transpose(invWorld));

        shader.Set("uAlbedo", material.Albedo);
        shader.Set("uSpecularStrength", material.SpecularStrength);
        shader.Set("uShininess", MathF.Max(1f, material.Shininess));
        shader.Set("uEmission", material.Emission.ToVector3() * material.EmissionEnergy);
        shader.Set("uUnshaded", material.Unshaded);
        shader.Set("uAlphaCutout", material.AlphaCutout);
        shader.Set("uReceiveShadows", material.ReceiveShadows);
        shader.Set("uUvScale", material.UvScale);
        shader.Set("uUvOffset", material.UvOffset);

        (material.AlbedoTexture ?? Device.WhiteTexture).Bind(0);

        bool hasNormal = material.NormalMap != null;
        shader.Set("uHasNormalMap", hasNormal);
        if (hasNormal)
        {
            shader.Set("uNormalStrength", material.NormalStrength);
            material.NormalMap!.Bind(2);
        }
        bool hasRoughness = material.RoughnessMap != null;
        shader.Set("uHasRoughnessMap", hasRoughness);
        if (hasRoughness)
            material.RoughnessMap!.Bind(3);

        if (skinned)
            shader.Set("uBones", instance.Skin!.GetSkinningMatrices());

        Device.SetBackfaceCulling(!material.DoubleSided);
        mesh.Draw();
    }

    private void RenderShadowPass(Matrix4x4 lightSpace)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        Device.SetViewport(0, 0, ShadowMapSize, ShadowMapSize);
        Device.SetDepthTest(true);
        Device.SetDepthWrite(true);
        _gl.Clear((uint)ClearBufferMask.DepthBufferBit);

        foreach (var list in new[] { _opaque, _transparent })
        {
            foreach (var (surface, instance, _) in list)
            {
                if (!surface.Material.CastShadows)
                    continue;

                bool skinned = surface.Mesh.IsSkinned && instance.Skin != null;
                var shader = skinned ? _depthSkinned : _depth;
                shader.Use();
                shader.Set("uLightSpace", lightSpace);
                shader.Set("uModel", skinned ? instance.Skin!.Skeleton.GlobalTransform : instance.GlobalTransform);
                if (skinned)
                    shader.Set("uBones", instance.Skin!.GetSkinningMatrices());

                Device.SetBackfaceCulling(!surface.Material.DoubleSided);
                surface.Mesh.Draw();
            }
        }
    }

    private Matrix4x4 BuildLightSpaceMatrix(DirectionalLight3D light, Camera3D camera)
    {
        var lightDir = light.GlobalForward;
        float size = light.ShadowOrthoSize;
        float depth = light.ShadowDepthRange;

        // Center the shadow volume ahead of the camera.
        var focus = camera.GlobalPosition + camera.GlobalForward * size * 0.5f;
        var lightPos = focus - lightDir * depth * 0.5f;

        var up = MathF.Abs(lightDir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        var lightView = Matrix4x4.CreateLookAt(lightPos, focus, up);
        var lightProj = Mathf.OrthographicGl(size * 2f, size * 2f, 0.05f, depth);
        return lightView * lightProj;
    }

    private unsafe void RenderSky(Matrix4x4 viewProj)
    {
        Matrix4x4.Invert(viewProj, out var invViewProj);

        Device.SetDepthTest(false);
        Device.SetDepthWrite(false);

        _sky.Use();
        _sky.Set("uInvViewProj", invViewProj);
        _sky.Set("uTopColor", Environment.SkyTopColor.ToVector3());
        _sky.Set("uHorizonColor", Environment.SkyHorizonColor.ToVector3());
        _sky.Set("uGroundColor", Environment.SkyGroundColor.ToVector3());
        _sky.Set("uSunEnabled", Environment.SkySunEnabled && _directionalLight != null);
        if (_directionalLight != null)
        {
            _sky.Set("uSunDir", _directionalLight.GlobalForward);
            _sky.Set("uSunColor", _directionalLight.Color.ToVector3() * _directionalLight.Energy);
        }

        _gl.BindVertexArray(_skyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
        Device.DrawCalls++;

        Device.SetDepthTest(true);
        Device.SetDepthWrite(true);
    }

    private void RenderUI()
    {
        if (_canvases.Count == 0)
            return;

        Device.SetDepthTest(false);
        Device.SetBlendMode(BlendMode.Alpha);
        Device.SetBackfaceCulling(false);

        SpriteBatch.Begin(Width, Height);
        foreach (var canvas in _canvases)
            canvas.InternalRender(SpriteBatch);
        SpriteBatch.End();

        Device.SetBackfaceCulling(true);
        Device.SetBlendMode(BlendMode.Opaque);
        Device.SetDepthTest(true);
    }

    // ------------------------------------------------------------------
    // Collection & culling
    // ------------------------------------------------------------------

    private void CollectNodes(Node node, bool parentVisible)
    {
        bool visible = parentVisible;
        if (node is Node3D node3D)
        {
            visible = parentVisible && node3D.Visible;
        }

        switch (node)
        {
            case MeshInstance3D mesh when visible && mesh.Surfaces.Count > 0:
                _meshInstances.Add(mesh);
                break;
            case DirectionalLight3D dir when visible:
                _directionalLight ??= dir;
                break;
            case PointLight3D point when visible:
                _pointLights.Add(point);
                break;
            case SpotLight3D spot when visible:
                _spotLights.Add(spot);
                break;
            case ParticleSystem3D particles when visible:
                _particles.Add(particles);
                break;
            case UI.Canvas canvas when canvas.Visible:
                _canvases.Add(canvas);
                break;
        }

        foreach (var child in node.Children)
            CollectNodes(child, visible);
    }

    private void ClearCollections()
    {
        _meshInstances.Clear();
        _pointLights.Clear();
        _spotLights.Clear();
        _particles.Clear();
        _canvases.Clear();
        _directionalLight = null;
    }

    private void ExtractFrustumPlanes(Matrix4x4 vp)
    {
        // Row-vector convention: planes come from the matrix columns.
        var col1 = new Vector4(vp.M11, vp.M21, vp.M31, vp.M41);
        var col2 = new Vector4(vp.M12, vp.M22, vp.M32, vp.M42);
        var col3 = new Vector4(vp.M13, vp.M23, vp.M33, vp.M43);
        var col4 = new Vector4(vp.M14, vp.M24, vp.M34, vp.M44);

        _frustumPlanes[0] = col4 + col1; // left
        _frustumPlanes[1] = col4 - col1; // right
        _frustumPlanes[2] = col4 + col2; // bottom
        _frustumPlanes[3] = col4 - col2; // top
        _frustumPlanes[4] = col4 + col3; // near
        _frustumPlanes[5] = col4 - col3; // far

        for (int i = 0; i < 6; i++)
        {
            float length = new Vector3(_frustumPlanes[i].X, _frustumPlanes[i].Y, _frustumPlanes[i].Z).Length();
            if (length > Mathf.Epsilon)
                _frustumPlanes[i] /= length;
        }
    }

    private bool IsCulled(Mesh mesh, Matrix4x4 world)
    {
        var center = world.TransformPoint(mesh.BoundsCenter);
        float maxScale = MathF.Max(
            new Vector3(world.M11, world.M12, world.M13).Length(),
            MathF.Max(
                new Vector3(world.M21, world.M22, world.M23).Length(),
                new Vector3(world.M31, world.M32, world.M33).Length()));
        float radius = mesh.BoundsRadius * maxScale;

        for (int i = 0; i < 6; i++)
        {
            var plane = _frustumPlanes[i];
            float distance = plane.X * center.X + plane.Y * center.Y + plane.Z * center.Z + plane.W;
            if (distance < -radius)
                return true;
        }
        return false;
    }
}
