using System.Numerics;
using Ducz.Rendering;
using Silk.NET.OpenGL;

namespace Ducz;

/// <summary>Emission volume shapes for <see cref="ParticleSystem3D"/>.</summary>
public enum EmissionShape
{
    Point,
    Sphere,
    Box
}

/// <summary>
/// CPU-simulated billboard particle system (smoke, fire, sparks, pickups...).
///
/// <code>
/// var fire = AddChild(new ParticleSystem3D
/// {
///     Amount = 80,
///     Lifetime = 1.2f,
///     Speed = 2f,
///     Direction = Vector3.UnitY,
///     SpreadDegrees = 20f,
///     StartColor = new Color(1f, 0.6f, 0.1f),
///     EndColor = new Color(1f, 0.1f, 0f, 0f),
///     Additive = true
/// });
/// </code>
/// </summary>
public class ParticleSystem3D : Node3D
{
    private struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Age;
        public float Lifetime;
        public float BaseSize;
        public float Rotation;
        public float RotationSpeed;
    }

    private static Texture2D? _defaultTexture;

    private Particle[] _particles = Array.Empty<Particle>();
    private int _aliveCount;
    private float _emitAccumulator;
    private uint _vao, _vbo, _ebo;
    private int _bufferCapacity;
    private float[] _vertexData = Array.Empty<float>();

    // ---- Emission ----

    /// <summary>Maximum simultaneous particles.</summary>
    public int Amount { get; set; } = 64;

    /// <summary>When false, no new particles spawn (existing ones finish).</summary>
    public bool Emitting { get; set; } = true;

    /// <summary>When true, particles are only spawned via <see cref="EmitBurst"/>.</summary>
    public bool OneShot { get; set; }

    /// <summary>Seconds each particle lives.</summary>
    public float Lifetime { get; set; } = 1.5f;

    /// <summary>Random extra lifetime (0..this) added per particle.</summary>
    public float LifetimeRandomness { get; set; } = 0.3f;

    /// <summary>Shape particles spawn in.</summary>
    public EmissionShape Shape { get; set; } = EmissionShape.Point;

    /// <summary>Radius for <see cref="EmissionShape.Sphere"/>.</summary>
    public float ShapeRadius { get; set; } = 0.5f;

    /// <summary>Half-extents for <see cref="EmissionShape.Box"/>.</summary>
    public Vector3 ShapeExtents { get; set; } = new(0.5f);

    // ---- Motion ----

    /// <summary>Base emission direction (local space).</summary>
    public Vector3 Direction { get; set; } = Vector3.UnitY;

    /// <summary>Cone spread around <see cref="Direction"/> in degrees (0..180).</summary>
    public float SpreadDegrees { get; set; } = 25f;

    /// <summary>Initial speed.</summary>
    public float Speed { get; set; } = 3f;

    /// <summary>Random speed variation (0..1 fraction of <see cref="Speed"/>).</summary>
    public float SpeedRandomness { get; set; } = 0.3f;

    /// <summary>Constant acceleration (world space), e.g. gravity.</summary>
    public Vector3 Gravity { get; set; } = new(0, -3f, 0);

    /// <summary>Velocity damping per second (0 = none).</summary>
    public float Damping { get; set; }

    /// <summary>Max rotation speed in radians/second (randomized per particle, both directions).</summary>
    public float RotationSpeed { get; set; } = 1f;

    // ---- Look ----

    /// <summary>Particle size at spawn (world units).</summary>
    public float StartSize { get; set; } = 0.25f;

    /// <summary>Particle size at death.</summary>
    public float EndSize { get; set; } = 0.05f;

    /// <summary>Color at spawn.</summary>
    public Color StartColor { get; set; } = Color.White;

    /// <summary>Color at death (use alpha 0 to fade out).</summary>
    public Color EndColor { get; set; } = Color.White.WithAlpha(0f);

    /// <summary>Optional texture (defaults to a soft round dot).</summary>
    public Texture2D? Texture { get; set; }

    /// <summary>Additive blending (fire, magic) instead of alpha blending (smoke, dust).</summary>
    public bool Additive { get; set; }

    /// <summary>Number of currently alive particles.</summary>
    public int AliveCount => _aliveCount;

    public ParticleSystem3D(string? name = null) : base(name) { }

    /// <summary>Spawns a burst of particles immediately (works with <see cref="OneShot"/>).</summary>
    public void EmitBurst(int count)
    {
        EnsureCapacity();
        for (int i = 0; i < count && _aliveCount < _particles.Length; i++)
            SpawnParticle();
    }

    /// <summary>Removes all alive particles.</summary>
    public void Clear() => _aliveCount = 0;

    protected override void OnUpdate(float dt)
    {
        EnsureCapacity();

        // Spawn
        if (Emitting && !OneShot && Lifetime > 0f)
        {
            float rate = Amount / Lifetime;
            _emitAccumulator += rate * dt;
            while (_emitAccumulator >= 1f && _aliveCount < _particles.Length)
            {
                _emitAccumulator -= 1f;
                SpawnParticle();
            }
            if (_aliveCount >= _particles.Length)
                _emitAccumulator = 0f;
        }

        // Simulate (world space)
        float damp = MathF.Max(0f, 1f - Damping * dt);
        for (int i = _aliveCount - 1; i >= 0; i--)
        {
            ref var p = ref _particles[i];
            p.Age += dt;
            if (p.Age >= p.Lifetime)
            {
                _particles[i] = _particles[--_aliveCount];
                continue;
            }
            p.Velocity = p.Velocity * damp + Gravity * dt;
            p.Position += p.Velocity * dt;
            p.Rotation += p.RotationSpeed * dt;
        }
    }

    private void EnsureCapacity()
    {
        if (_particles.Length != Amount)
        {
            Array.Resize(ref _particles, Math.Max(1, Amount));
            _aliveCount = Math.Min(_aliveCount, _particles.Length);
        }
    }

    private void SpawnParticle()
    {
        var transform = GlobalTransform;
        var origin = Shape switch
        {
            EmissionShape.Sphere => transform.TransformPoint(Rng.InsideUnitSphere() * ShapeRadius),
            EmissionShape.Box => transform.TransformPoint(new Vector3(
                Rng.Range(-ShapeExtents.X, ShapeExtents.X),
                Rng.Range(-ShapeExtents.Y, ShapeExtents.Y),
                Rng.Range(-ShapeExtents.Z, ShapeExtents.Z))),
            _ => transform.Translation
        };

        // Random direction within the spread cone.
        var baseDir = Mathf.NormalizeSafe(transform.TransformDirection(Direction));
        if (baseDir == Vector3.Zero)
            baseDir = Vector3.UnitY;
        float spread = Mathf.Clamp(SpreadDegrees, 0f, 180f) * Mathf.Deg2Rad;
        var dir = Mathf.NormalizeSafe(Vector3.Lerp(baseDir, Rng.OnUnitSphere(), spread / MathF.PI));
        if (dir == Vector3.Zero)
            dir = baseDir;

        float speed = Speed * (1f + Rng.Range(-SpeedRandomness, SpeedRandomness));

        _particles[_aliveCount++] = new Particle
        {
            Position = origin,
            Velocity = dir * speed,
            Age = 0f,
            Lifetime = Lifetime + Rng.Range(0f, LifetimeRandomness),
            BaseSize = 1f + Rng.Range(-0.15f, 0.15f),
            Rotation = Rng.Range(0f, Mathf.Tau),
            RotationSpeed = Rng.Range(-RotationSpeed, RotationSpeed)
        };
    }

    // ------------------------------------------------------------------
    // Rendering (called by the Renderer during the transparent pass)
    // ------------------------------------------------------------------

    internal unsafe void Render(Renderer renderer, Matrix4x4 viewProj, Camera3D camera)
    {
        if (_aliveCount == 0)
            return;

        var gl = renderer.Device.GL;
        const int floatsPerVertex = 13; // center3 corner2 uv2 color4 size1 rotation1

        if (_vao == 0)
        {
            _vao = gl.GenVertexArray();
            _vbo = gl.GenBuffer();
            _ebo = gl.GenBuffer();
            gl.BindVertexArray(_vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            uint stride = floatsPerVertex * sizeof(float);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)12);
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)20);
            gl.EnableVertexAttribArray(3);
            gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, (void*)28);
            gl.EnableVertexAttribArray(4);
            gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, (void*)44);
            gl.EnableVertexAttribArray(5);
            gl.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, (void*)48);
            gl.BindVertexArray(0);
        }

        if (_bufferCapacity < _particles.Length)
        {
            _bufferCapacity = _particles.Length;
            _vertexData = new float[_bufferCapacity * 4 * floatsPerVertex];

            var indices = new uint[_bufferCapacity * 6];
            for (int i = 0; i < _bufferCapacity; i++)
            {
                uint v = (uint)(i * 4);
                int o = i * 6;
                indices[o] = v; indices[o + 1] = v + 1; indices[o + 2] = v + 2;
                indices[o + 3] = v; indices[o + 4] = v + 2; indices[o + 5] = v + 3;
            }
            gl.BindVertexArray(_vao);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            fixed (uint* ptr = indices)
            {
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
            }
            gl.BindVertexArray(0);

            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (float* ptr = _vertexData)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertexData.Length * sizeof(float)), null, BufferUsageARB.StreamDraw);
            }
        }

        // Build vertex data
        int offset = 0;
        Span<Vector2> corners = stackalloc Vector2[]
        {
            new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f)
        };
        Span<Vector2> uvs = stackalloc Vector2[]
        {
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0)
        };

        for (int i = 0; i < _aliveCount; i++)
        {
            ref var p = ref _particles[i];
            float t = Mathf.Clamp01(p.Age / p.Lifetime);
            var color = Color.Lerp(StartColor, EndColor, t);
            float size = Mathf.Lerp(StartSize, EndSize, t) * p.BaseSize;

            for (int c = 0; c < 4; c++)
            {
                _vertexData[offset++] = p.Position.X;
                _vertexData[offset++] = p.Position.Y;
                _vertexData[offset++] = p.Position.Z;
                _vertexData[offset++] = corners[c].X;
                _vertexData[offset++] = corners[c].Y;
                _vertexData[offset++] = uvs[c].X;
                _vertexData[offset++] = uvs[c].Y;
                _vertexData[offset++] = color.R;
                _vertexData[offset++] = color.G;
                _vertexData[offset++] = color.B;
                _vertexData[offset++] = color.A;
                _vertexData[offset++] = size;
                _vertexData[offset++] = p.Rotation;
            }
        }

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* ptr = _vertexData)
        {
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(offset * sizeof(float)), ptr);
        }

        var shader = renderer.ParticleShader;
        shader.Use();
        shader.Set("uViewProj", viewProj);
        shader.Set("uCameraRight", camera.GlobalRight);
        shader.Set("uCameraUp", camera.GlobalUp);
        shader.Set("uTexture", 0);
        (Texture ?? GetDefaultTexture()).Bind(0);

        renderer.Device.SetBlendMode(Additive ? BlendMode.Additive : BlendMode.Alpha);
        renderer.Device.SetDepthWrite(false);
        renderer.Device.SetBackfaceCulling(false);

        gl.BindVertexArray(_vao);
        gl.DrawElements(PrimitiveType.Triangles, (uint)(_aliveCount * 6), DrawElementsType.UnsignedInt, (void*)0);
        gl.BindVertexArray(0);
        renderer.Device.DrawCalls++;

        renderer.Device.SetBackfaceCulling(true);
    }

    private static Texture2D GetDefaultTexture()
    {
        if (_defaultTexture != null)
            return _defaultTexture;

        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy) * 2f;
                float alpha = Mathf.Clamp01(1f - d);
                alpha *= alpha;
                int i = (y * size + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
                pixels[i + 3] = (byte)(alpha * 255);
            }
        }
        _defaultTexture = Texture2D.FromPixels(size, size, pixels, TextureFilter.Linear, false);
        return _defaultTexture;
    }
}
