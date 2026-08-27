using System.Numerics;
using Silk.NET.OpenGL;

namespace Ducz.Rendering;

/// <summary>
/// A compiled GLSL shader program. The engine ships built-in shaders; create your own
/// with <see cref="FromSource"/> for custom materials or post effects.
/// </summary>
public sealed class Shader : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniformLocations = new();

    /// <summary>OpenGL program handle.</summary>
    public uint Handle { get; }

    private Shader(GL gl, uint handle)
    {
        _gl = gl;
        Handle = handle;
    }

    /// <summary>
    /// Compiles a shader from vertex + fragment GLSL sources.
    /// <paramref name="defines"/> are injected as "#define X" right after the #version line.
    /// </summary>
    public static Shader FromSource(GraphicsDevice device, string vertexSource, string fragmentSource, params string[] defines)
    {
        var gl = device.GL;

        uint vs = Compile(gl, ShaderType.VertexShader, Preprocess(vertexSource, defines));
        uint fs = Compile(gl, ShaderType.FragmentShader, Preprocess(fragmentSource, defines));

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        gl.LinkProgram(program);

        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            string info = gl.GetProgramInfoLog(program);
            gl.DeleteProgram(program);
            throw new InvalidOperationException($"Shader link failed: {info}");
        }

        gl.DetachShader(program, vs);
        gl.DetachShader(program, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);

        return new Shader(gl, program);
    }

    private static string Preprocess(string source, string[] defines)
    {
        if (defines.Length == 0)
            return source;

        var lines = source.Replace("\r\n", "\n").Split('\n').ToList();
        int insertAt = lines.FindIndex(l => l.TrimStart().StartsWith("#version")) + 1;
        foreach (var define in defines)
            lines.Insert(insertAt++, $"#define {define}");
        return string.Join('\n', lines);
    }

    private static uint Compile(GL gl, ShaderType type, string source)
    {
        uint handle = gl.CreateShader(type);
        gl.ShaderSource(handle, source);
        gl.CompileShader(handle);

        gl.GetShader(handle, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string info = gl.GetShaderInfoLog(handle);
            gl.DeleteShader(handle);
            throw new InvalidOperationException($"{type} compilation failed: {info}");
        }
        return handle;
    }

    /// <summary>Makes this program current.</summary>
    public void Use() => _gl.UseProgram(Handle);

    private int Location(string name)
    {
        if (!_uniformLocations.TryGetValue(name, out int location))
        {
            location = _gl.GetUniformLocation(Handle, name);
            _uniformLocations[name] = location;
        }
        return location;
    }

    public void Set(string name, int value) => _gl.Uniform1(Location(name), value);
    public void Set(string name, float value) => _gl.Uniform1(Location(name), value);
    public void Set(string name, bool value) => _gl.Uniform1(Location(name), value ? 1 : 0);
    public void Set(string name, Vector2 value) => _gl.Uniform2(Location(name), value.X, value.Y);
    public void Set(string name, Vector3 value) => _gl.Uniform3(Location(name), value.X, value.Y, value.Z);
    public void Set(string name, Vector4 value) => _gl.Uniform4(Location(name), value.X, value.Y, value.Z, value.W);
    public void Set(string name, Color value) => _gl.Uniform4(Location(name), value.R, value.G, value.B, value.A);

    public unsafe void Set(string name, Matrix4x4 value)
    {
        // System.Numerics row-major memory uploaded as-is becomes the correct
        // column-vector matrix in GLSL (no transpose needed).
        _gl.UniformMatrix4(Location(name), 1, false, (float*)&value);
    }

    public unsafe void Set(string name, ReadOnlySpan<Matrix4x4> values)
    {
        fixed (Matrix4x4* ptr = values)
            _gl.UniformMatrix4(Location(name), (uint)values.Length, false, (float*)ptr);
    }

    /// <summary>True when the program declares the uniform (after optimization).</summary>
    public bool HasUniform(string name) => Location(name) >= 0;

    public void Dispose() => _gl.DeleteProgram(Handle);
}
