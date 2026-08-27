using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ducz.Serialization;

/// <summary>
/// A complete scene described as data. Load one from JSON with <see cref="Load"/>,
/// turn it into live nodes with <see cref="SceneLoader.Instantiate"/>, or build/edit
/// it in code (the scene editor does exactly that) and <see cref="Save"/> it.
/// </summary>
public sealed class SceneDocument
{
    /// <summary>Scene name (becomes the root node's name).</summary>
    public string Name { get; set; } = "Scene";

    /// <summary>Sky, ambient light and fog.</summary>
    public EnvironmentDef? Environment { get; set; }

    /// <summary>Named materials that nodes reference by key.</summary>
    public Dictionary<string, MaterialDef> Materials { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Input actions registered when the scene loads.</summary>
    public InputDef? Input { get; set; }

    /// <summary>The node hierarchy.</summary>
    public List<NodeDef> Nodes { get; set; } = new();

    /// <summary>Shared serializer options for scene JSON (camelCase, enums as strings, nulls omitted).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Reads a scene document from a JSON file (or the mounted asset pack).</summary>
    public static SceneDocument Load(string path)
    {
        var json = System.Text.Encoding.UTF8.GetString(Assets.ReadBytes(path));
        return FromJson(json);
    }

    /// <summary>Parses a scene document from a JSON string.</summary>
    public static SceneDocument FromJson(string json) =>
        JsonSerializer.Deserialize<SceneDocument>(json, JsonOptions)
        ?? throw new InvalidDataException("Scene JSON is empty or invalid.");

    /// <summary>Writes the document to a JSON file.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, ToJson());
    }

    /// <summary>Serializes the document to a JSON string.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>Sky, ambient light and fog settings.</summary>
public sealed class EnvironmentDef
{
    /// <summary>"proceduralSky" (default) or "solidColor".</summary>
    public string Background { get; set; } = "proceduralSky";

    public string? ClearColor { get; set; }
    public string? SkyTop { get; set; }
    public string? SkyHorizon { get; set; }
    public string? SkyGround { get; set; }
    public bool SunDisk { get; set; } = true;

    public string? AmbientColor { get; set; }
    public float AmbientIntensity { get; set; } = 0.25f;

    public FogDef? Fog { get; set; }
}

/// <summary>Linear distance fog. Its presence enables fog unless <see cref="Enabled"/> is false.</summary>
public sealed class FogDef
{
    public bool Enabled { get; set; } = true;
    public string? Color { get; set; }
    public float Start { get; set; } = 30f;
    public float End { get; set; } = 150f;
}

/// <summary>Input actions to register when the scene loads.</summary>
public sealed class InputDef
{
    /// <summary>Registers WASD/arrows + jump + sprint (needed by the built-in player).</summary>
    public bool DefaultMovement { get; set; } = true;

    /// <summary>Extra actions: name -> bindings like "Space", "F", "MouseLeft".</summary>
    public Dictionary<string, string[]>? Actions { get; set; }
}

/// <summary>A material described as data. All colors are hex strings ("#rrggbb" or "#rrggbbaa").</summary>
public sealed class MaterialDef
{
    public string? Albedo { get; set; }

    /// <summary>Texture file path (png/jpg/...).</summary>
    public string? Texture { get; set; }

    /// <summary>
    /// Tangent-space normal map. Leave null to auto-detect a sibling file named like the albedo
    /// with a _normal / _nrm / _n suffix (set <see cref="AutoMaps"/> to false to opt out).
    /// </summary>
    public string? NormalMap { get; set; }

    /// <summary>Strength of the normal map (1 = as authored).</summary>
    public float NormalStrength { get; set; } = 1f;

    /// <summary>Grayscale roughness map (white = matte). Auto-detected like <see cref="NormalMap"/>.</summary>
    public string? RoughnessMap { get; set; }

    /// <summary>Look for sibling _normal / _roughness maps next to the albedo texture (default true).</summary>
    public bool AutoMaps { get; set; } = true;

    /// <summary>"linear" (default) or "nearest" (pixel art).</summary>
    public string? Filter { get; set; }

    /// <summary>Procedural checkerboard texture instead of a file.</summary>
    public CheckerboardDef? Checkerboard { get; set; }

    public float Specular { get; set; } = 0.4f;
    public float Shininess { get; set; } = 32f;
    public string? Emission { get; set; }
    public float EmissionEnergy { get; set; } = 1f;
    public bool Transparent { get; set; }
    public bool Unshaded { get; set; }
    public bool DoubleSided { get; set; }
    public float AlphaCutout { get; set; }
    public float[]? UvScale { get; set; }
    public float[]? UvOffset { get; set; }
    public bool CastShadows { get; set; } = true;
    public bool ReceiveShadows { get; set; } = true;
}

/// <summary>Procedural checkerboard parameters.</summary>
public sealed class CheckerboardDef
{
    public string ColorA { get; set; } = "#ffffff";
    public string ColorB { get; set; } = "#c0c0c0";
    public int Size { get; set; } = 256;
    public int Cells { get; set; } = 8;
}

/// <summary>
/// A material slot on a node: either a string key referencing
/// <see cref="SceneDocument.Materials"/>, or an inline material object.
/// </summary>
[JsonConverter(typeof(MaterialRefConverter))]
public sealed class MaterialRef
{
    /// <summary>Key into the document's materials dictionary.</summary>
    public string? Reference { get; set; }

    /// <summary>Inline material definition.</summary>
    public MaterialDef? Inline { get; set; }

    public static implicit operator MaterialRef(string key) => new() { Reference = key };
    public static implicit operator MaterialRef(MaterialDef inline) => new() { Inline = inline };
}

internal sealed class MaterialRefConverter : JsonConverter<MaterialRef>
{
    public override MaterialRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new MaterialRef { Reference = reader.GetString() };
        if (reader.TokenType == JsonTokenType.StartObject)
            return new MaterialRef { Inline = JsonSerializer.Deserialize<MaterialDef>(ref reader, options) };
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, MaterialRef value, JsonSerializerOptions options)
    {
        if (value.Reference != null)
            writer.WriteStringValue(value.Reference);
        else if (value.Inline != null)
            JsonSerializer.Serialize(writer, value.Inline, options);
        else
            writer.WriteNullValue();
    }
}

/// <summary>A mesh described as data (one of the engine primitives).</summary>
public sealed class MeshDef
{
    /// <summary>
    /// Basic: cube, box, sphere, plane, quad, cylinder, capsule, cone, torus.
    /// Building shapes: wedge, roofGable, roofHip, roofShed, stairs, arch, curvedWall, tube,
    /// prism, pyramid, roundedBox.
    /// </summary>
    public string Primitive { get; set; } = "cube";

    /// <summary>Full size: [x,y,z] for box/wedge/roofs/stairs/pyramid, [x,z] for plane, [w,h] for quad, scalar in Size[0] for cube.</summary>
    public float[]? Size { get; set; }

    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 1f;
    public float Thickness { get; set; } = 0.15f;   // torus, tube, curvedWall, arch, roofShed
    public int Segments { get; set; }               // 0 = primitive default
    public float UvTiling { get; set; } = 1f;       // plane

    /// <summary>stairs: number of steps (default 8).</summary>
    public int Steps { get; set; }

    /// <summary>curvedWall: arc in degrees (default 90; 360 = full ring).</summary>
    public float ArcDegrees { get; set; }

    /// <summary>roofGable / roofHip: eave overhang in meters.</summary>
    public float Overhang { get; set; }

    /// <summary>roofHip: length of the flat ridge (0 = pyramid roof).</summary>
    public float RidgeLength { get; set; } = -1f;

    /// <summary>arch: opening width / height to the spring line.</summary>
    public float OpeningWidth { get; set; } = -1f;
    public float OpeningHeight { get; set; } = -1f;

    /// <summary>prism: number of sides (default 6).</summary>
    public int Sides { get; set; }

    /// <summary>roundedBox: corner bevel in meters.</summary>
    public float Bevel { get; set; }

    /// <summary>
    /// Footprint of the <c>polygon</c> primitive: flat (x, z) pairs in metres, e.g.
    /// <c>[0,0, 8,0, 8,5, 0,5]</c>. Any winding order; the ring is closed automatically.
    /// </summary>
    public float[]? Points { get; set; }

    /// <summary>stairs: build the closed sides/underside (default true).</summary>
    public bool SolidSide { get; set; } = true;
}

/// <summary>A collider described as data.</summary>
public sealed class ColliderDef
{
    /// <summary>
    /// "auto" (derive from the mesh primitive; for model nodes: exact triangle mesh),
    /// "mesh" (triangle mesh of a model), "box", "sphere", "capsule", "none".
    /// </summary>
    public string Shape { get; set; } = "auto";

    /// <summary>Full size for box colliders.</summary>
    public float[]? Size { get; set; }

    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 1.8f;
    public uint Layer { get; set; } = 1;
    public uint Mask { get; set; } = 1;
}

/// <summary>Terrain described as data.</summary>
public sealed class TerrainDef
{
    /// <summary>"flat", "hills" (procedural sin/cos), or "heightmap" (grayscale image).</summary>
    public string Mode { get; set; } = "flat";

    public float SizeX { get; set; } = 100f;
    public float SizeZ { get; set; } = 100f;
    public int Resolution { get; set; } = 128;

    /// <summary>Heightmap image path (mode "heightmap").</summary>
    public string? Heightmap { get; set; }

    /// <summary>Peak height (mode "heightmap").</summary>
    public float MaxHeight { get; set; } = 10f;

    /// <summary>Hill height (mode "hills").</summary>
    public float Amplitude { get; set; } = 3f;

    /// <summary>Hill density (mode "hills").</summary>
    public float Frequency { get; set; } = 0.07f;
}

/// <summary>
/// Visual model + animations for a "player" node. The model replaces the default
/// capsule; animation files are loaded as named clips and switched automatically
/// by movement speed (idle / walk / run).
/// </summary>
public sealed class PlayerVisualDef
{
    /// <summary>Character model file (FBX/glTF/OBJ...).</summary>
    public string? Path { get; set; }

    /// <summary>Uniform scale (Unreal-style FBX in centimeters needs ~0.01).</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Offset from the capsule center (e.g. [0, -0.9, 0] to put feet on the ground).</summary>
    public float[]? Offset { get; set; }

    /// <summary>Orientation fix for models not authored facing -Z.</summary>
    public float[]? RotationDegrees { get; set; }

    /// <summary>
    /// Animation clips from separate files: clip name -> file path.
    /// Names "idle", "walk" and "run" drive the automatic locomotion.
    /// </summary>
    public Dictionary<string, string>? Animations { get; set; }
}

/// <summary>Particle system described as data.</summary>
public sealed class ParticlesDef
{
    public int Amount { get; set; } = 64;
    public float Lifetime { get; set; } = 1.5f;
    public float Speed { get; set; } = 3f;
    public float[]? Direction { get; set; }
    public float Spread { get; set; } = 25f;
    public float[]? Gravity { get; set; }
    public float StartSize { get; set; } = 0.25f;
    public float EndSize { get; set; } = 0.05f;
    public string StartColor { get; set; } = "#ffffff";
    public string EndColor { get; set; } = "#ffffff00";
    public bool Additive { get; set; }
    public bool Emitting { get; set; } = true;

    /// <summary>"point", "sphere" or "box".</summary>
    public string Shape { get; set; } = "point";
    public float ShapeRadius { get; set; } = 0.5f;
}

/// <summary>
/// One node in the scene hierarchy. <see cref="Type"/> picks what gets created;
/// the other fields apply where they make sense (unused fields are ignored).
///
/// Types: node, mesh, static, rigid, area, floor, wall, ramp, crate, terrain,
/// model, player, spawn, camera, flyCamera, thirdPersonCamera, directionalLight,
/// pointLight, spotLight, particles, audio, audio3d.
/// </summary>
public sealed class NodeDef
{
    public string Type { get; set; } = "node";
    public string? Name { get; set; }

    // ---- Transform ----
    public float[]? Position { get; set; }
    public float[]? RotationDegrees { get; set; }
    public float[]? Scale { get; set; }
    public bool Visible { get; set; } = true;
    public string[]? Groups { get; set; }

    // ---- Visuals & physics ----
    public MeshDef? Mesh { get; set; }
    public MaterialRef? Material { get; set; }
    public ColliderDef? Collider { get; set; }

    /// <summary>Prefab dimensions: floor [x,z], wall [length,height,thickness], ramp [w,h,len], crate [size].</summary>
    public float[]? Size { get; set; }

    /// <summary>
    /// UV-map box/plane/cylinder geometry in meters instead of 0..1 per face, so a
    /// material uvScale means "tiles per meter" and textures keep the same density
    /// on every block. The map builder sets this on everything it places.
    /// </summary>
    public bool WorldUv { get; set; }

    /// <summary>
    /// Per-face materials for box meshes: keys "top", "bottom", "front", "back", "left",
    /// "right", plus the shortcuts "sides" (the four vertical faces) and "all". Later keys win,
    /// so <c>{ "all": "dirt", "top": "grass" }</c> is grass on top and dirt everywhere else.
    /// Falls back to <see cref="Material"/> for faces that are not listed.
    /// </summary>
    public Dictionary<string, MaterialRef>? FaceMaterials { get; set; }

    public float Mass { get; set; } = 1f;
    public float Restitution { get; set; } = 0.1f;
    public float Friction { get; set; } = 0.8f;

    // ---- Lights ----
    public string? Color { get; set; }
    public float Energy { get; set; } = 1f;
    public float Range { get; set; } = 10f;
    public float Angle { get; set; } = 45f;
    public float Softness { get; set; } = 0.1f;
    public bool Shadows { get; set; } = true;

    // ---- Cameras ----
    public float Fov { get; set; } = 60f;
    public float Near { get; set; } = 0.05f;
    public float Far { get; set; } = 500f;
    public bool Current { get; set; }

    /// <summary>Third-person camera: name of the node to follow.</summary>
    public string? Target { get; set; }
    public float Distance { get; set; } = 6f;
    public float TargetHeight { get; set; } = 1.3f;

    /// <summary>Third-person camera: mouse sensitivity (radians per pixel).</summary>
    public float Sensitivity { get; set; } = 0.0035f;

    /// <summary>Third-person camera: follow smoothing (0 = rigidly locked to the target).</summary>
    public float Smoothing { get; set; }

    // ---- Files (model / audio / texture-ish types) ----
    public string? Path { get; set; }

    /// <summary>Model: animation clip to auto-play.</summary>
    public string? Animation { get; set; }

    /// <summary>
    /// Model: instantiate only this node of the file (with its children), placed where it
    /// sits inside the model. Used by "import as pieces" so parts of a big GLB become
    /// separately editable objects.
    /// </summary>
    public string? SubNode { get; set; }

    /// <summary>
    /// Where a <see cref="SubNode"/> piece sits: "file" (default) keeps the place it has
    /// inside the model, "base" puts its footprint centre on this node's position with its
    /// base at y = 0 - the natural choice when building with a modular pack.
    /// </summary>
    public string? SubNodePivot { get; set; }

    public bool Loop { get; set; }
    public float Volume { get; set; } = 1f;
    public bool Autoplay { get; set; }

    // ---- Player ----
    public float MoveSpeed { get; set; } = 7f;
    public float JumpSpeed { get; set; } = 8.5f;
    public float Gravity { get; set; } = 22f;

    /// <summary>Player: custom character model + animation files.</summary>
    public PlayerVisualDef? Visual { get; set; }

    // ---- Sub-objects ----
    public TerrainDef? Terrain { get; set; }
    public ParticlesDef? Particles { get; set; }
    public List<NodeDef>? Children { get; set; }
}
