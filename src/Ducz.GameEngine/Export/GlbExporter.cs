using System.Numerics;
using System.Text.Json.Nodes;
using Ducz.Rendering;
using Ducz.Serialization;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using GltfMaterial = SharpGLTF.Schema2.Material;
using GltfMesh = SharpGLTF.Schema2.Mesh;
using GltfNode = SharpGLTF.Schema2.Node;
using Mesh = Ducz.Rendering.Mesh;

namespace Ducz.Export;

/// <summary>Options for <see cref="GlbExporter"/>.</summary>
public sealed class GlbExportOptions
{
    /// <summary>Embed the geometry of external model files (<c>"type": "model"</c>) into the GLB. When false, models become empty marker nodes.</summary>
    public bool IncludeModels { get; set; } = true;

    /// <summary>Export point/spot/directional lights (KHR_lights_punctual).</summary>
    public bool IncludeLights { get; set; } = true;

    /// <summary>Export logical nodes (spawn, player, cameras, particles, audio) as empty nodes with <c>extras</c> metadata.</summary>
    public bool IncludeMarkers { get; set; } = true;

    /// <summary>
    /// Append Godot import suffixes to node names: <c>-col</c> for solid geometry,
    /// <c>-rigid</c> for physics props, <c>-colonly</c> for trigger volumes. Godot creates
    /// the matching bodies/collision shapes on import; other tools ignore the suffix.
    /// </summary>
    public bool GodotSuffixes { get; set; } = true;

    /// <summary>
    /// Merge all static geometry into one mesh with one primitive per material
    /// (fewer draw calls, not editable per object). Physics props, models, lights and
    /// markers stay separate.
    /// </summary>
    public bool MergeByMaterial { get; set; }

    /// <summary>Also export nodes marked <c>"visible": false</c>.</summary>
    public bool IncludeHiddenNodes { get; set; }

    /// <summary>
    /// Uniform scale applied to the whole export (geometry, positions, light ranges).
    /// The engine works in meters, like glTF/Godot/Blender (1 = unchanged); use 100 for a
    /// tool that expects centimeters, 0.01 to shrink a centimeter-authored map, etc.
    /// </summary>
    public float Scale { get; set; } = 1f;
}

/// <summary>Summary of an export run.</summary>
public sealed class GlbExportResult
{
    public string OutputPath { get; init; } = "";
    public int NodeCount { get; internal set; }
    public int MeshCount { get; internal set; }
    public int MaterialCount { get; internal set; }
    public int TriangleCount { get; internal set; }
    public List<string> Warnings { get; } = new();

    public override string ToString() =>
        $"{NodeCount} nodes, {MeshCount} meshes, {MaterialCount} materials, {TriangleCount} triangles" +
        (Warnings.Count > 0 ? $", {Warnings.Count} warnings" : "");
}

/// <summary>
/// Writes a <see cref="SceneDocument"/> as a binary glTF (.glb) file that Godot,
/// Blender, Unity or any glTF viewer can open. Geometry is generated on the CPU
/// from the same definitions the engine renders, so the export matches what the
/// scene editor shows - no OpenGL context needed.
///
/// <code>
/// var doc = SceneDocument.Load("scenes/main.json");
/// GlbExporter.Export(doc, "Export/main.glb");
/// </code>
/// </summary>
public static class GlbExporter
{
    /// <summary>Exports the document to <paramref name="outputPath"/> (a .glb file).</summary>
    public static GlbExportResult Export(SceneDocument document, string outputPath, GlbExportOptions? options = null)
    {
        var session = new Session(document, options ?? new GlbExportOptions(), outputPath);
        var scene = session.Build();

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var model = scene.ToGltf2();
        model.SaveGLB(outputPath);

        session.Result.NodeCount = model.LogicalNodes.Count;
        session.Result.MeshCount = model.LogicalMeshes.Count;
        session.Result.MaterialCount = model.LogicalMaterials.Count;
        Log.Info($"GLB exported: {outputPath} ({session.Result})");
        return session.Result;
    }

    // ------------------------------------------------------------------
    // Export session
    // ------------------------------------------------------------------

    private sealed class Session
    {
        private readonly SceneDocument _doc;
        private readonly GlbExportOptions _options;
        private readonly SceneBuilder _scene;
        private readonly Dictionary<object, MaterialBuilder> _materials = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, MaterialBuilder> _materialsByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(MeshData Data, MaterialBuilder Material)> _mergedParts = new();
        private MaterialBuilder? _defaultMaterial;
        private int _autoNameCounter;
        private readonly float _scale;

        public GlbExportResult Result { get; }

        public Session(SceneDocument doc, GlbExportOptions options, string outputPath)
        {
            _doc = doc;
            _options = options;
            _scale = options.Scale > 0f ? options.Scale : 1f;
            _scene = new SceneBuilder(doc.Name);
            Result = new GlbExportResult { OutputPath = outputPath };
        }

        public SceneBuilder Build()
        {
            foreach (var def in _doc.Nodes)
                ExportNode(def, null);

            if (_options.MergeByMaterial && _mergedParts.Count > 0)
            {
                var node = new NodeBuilder(WithSuffix("Map", "-col"));
                _scene.AddRigidMesh(BuildMesh("Map", _mergedParts), node);
            }
            return _scene;
        }

        // ---- Nodes ---------------------------------------------------

        private void ExportNode(NodeDef def, NodeBuilder? parent)
        {
            if (!def.Visible && !_options.IncludeHiddenNodes)
                return;

            string type = def.Type.ToLowerInvariant();
            string baseName = def.Name ?? $"{def.Type}_{_autoNameCounter++}";
            bool solid = !(def.Collider?.Shape.Equals("none", StringComparison.OrdinalIgnoreCase) ?? false);

            NodeBuilder? node = null;
            bool hasContent = false;

            NodeBuilder GetNode(string name, JsonNode? extras = null)
            {
                if (node != null)
                    return node;
                node = parent != null ? parent.CreateNode(name) : new NodeBuilder(name);
                if (extras != null)
                    node.Extras = extras;
                ApplyTransform(node, def);
                return node;
            }

            // Per-face materials: export the box as six primitives so the GLB keeps the look.
            bool FaceGeometry(string? suffix)
            {
                if (def.FaceMaterials is not { Count: > 0 } faces || def.Mesh == null)
                    return false;
                string primitive = def.Mesh.Primitive;
                if (!primitive.Equals("box", StringComparison.OrdinalIgnoreCase) &&
                    !primitive.Equals("cube", StringComparison.OrdinalIgnoreCase))
                    return false;

                var size = def.Mesh.Size;
                float sx = size is { Length: > 0 } ? size[0] : 1f;
                float sy = size is { Length: > 1 } ? size[1] : sx;
                float sz = size is { Length: > 2 } ? size[2] : sx;
                var parts = MeshFactory.BoxFacesData(sx, sy, sz, def.WorldUv);
                var pieces = new List<(MeshData Data, MaterialBuilder Material)>();
                for (int f = 0; f < parts.Length; f++)
                {
                    var reference = FaceMaterialRef(faces, (MeshFactory.BoxFace)f) ?? def.Material;
                    var data = _scale != 1f ? parts[f].Transformed(Matrix4x4.CreateScale(_scale)) : parts[f];
                    pieces.Add((data, ResolveMaterial(reference)));
                }

                if (_options.MergeByMaterial)
                {
                    var world = LocalMatrix(def) * (parent?.WorldMatrix ?? Matrix4x4.Identity);
                    foreach (var (data, material) in pieces)
                    {
                        _mergedParts.Add((data.Transformed(world), material));
                        Result.TriangleCount += data.TriangleCount;
                    }
                    if (def.Children != null)
                        GetNode(baseName);
                    return true;
                }

                var node2 = GetNode(WithSuffix(baseName, suffix));
                _scene.AddRigidMesh(BuildMesh(baseName, pieces), node2);
                Result.TriangleCount += pieces.Sum(p => p.Data.TriangleCount);
                hasContent = true;
                return true;
            }

            // Solid/static geometry: merged into one mesh in MergeByMaterial mode, otherwise its own node.
            void StaticGeometry(MeshData data, Matrix4x4 localOffset, string? suffix)
            {
                if (_options.MergeByMaterial)
                {
                    var world = Matrix4x4.CreateScale(_scale) * ScaleTranslation(localOffset) * LocalMatrix(def) * (parent?.WorldMatrix ?? Matrix4x4.Identity);
                    _mergedParts.Add((data.Transformed(world), ResolveMaterial(def.Material)));
                    Result.TriangleCount += data.TriangleCount;
                    if (def.Children != null)
                        GetNode(baseName);   // keep the group node for its children
                    return;
                }
                hasContent = AddGeometry(GetNode(WithSuffix(baseName, suffix)), baseName, data, def.Material, localOffset);
            }

            switch (type)
            {
                case "node" or "group":
                    GetNode(baseName);
                    break;

                case "mesh":
                    StaticGeometry(SceneLoader.BuildMeshData(def.Mesh ?? new MeshDef(), def.WorldUv), Matrix4x4.Identity, null);
                    break;

                case "static":
                    if (def.Mesh != null)
                    {
                        if (!FaceGeometry(solid ? "-col" : null))
                            StaticGeometry(SceneLoader.BuildMeshData(def.Mesh, def.WorldUv), Matrix4x4.Identity, solid ? "-col" : null);
                    }
                    else
                        GetNode(baseName);
                    break;

                case "rigid":
                    if (def.Mesh != null)
                        hasContent = AddGeometry(GetNode(WithSuffix(baseName, solid ? "-rigid" : null)), baseName,
                            SceneLoader.BuildMeshData(def.Mesh, def.WorldUv), def.Material, Matrix4x4.Identity);
                    else
                        GetNode(baseName);
                    break;

                case "area":
                    if (def.Mesh != null)
                        hasContent = AddGeometry(GetNode(baseName), baseName,
                            SceneLoader.BuildMeshData(def.Mesh, def.WorldUv), def.Material, Matrix4x4.Identity);
                    else
                        GetNode(WithSuffix(baseName, "-colonly"), Extras("area"));
                    break;

                case "floor":
                {
                    var size = def.Size ?? new[] { 20f, 20f };
                    float sizeX = size[0], sizeZ = size.Length > 1 ? size[1] : size[0];
                    const float thickness = 0.5f;
                    var data = MeshFactory.BoxData(sizeX, thickness, sizeZ, def.WorldUv);
                    StaticGeometry(data, Matrix4x4.CreateTranslation(0f, -thickness * 0.5f, 0f), solid ? "-col" : null);
                    break;
                }

                case "wall":
                {
                    var size = def.Size ?? new[] { 4f, 3f, 0.3f };
                    var data = MeshFactory.BoxData(size[0], size.Length > 1 ? size[1] : 3f, size.Length > 2 ? size[2] : 0.3f, def.WorldUv);
                    StaticGeometry(data, Matrix4x4.Identity, solid ? "-col" : null);
                    break;
                }

                case "ramp":
                {
                    var size = def.Size ?? new[] { 2f, 1f, 3f };
                    float width = size[0], height = size.Length > 1 ? size[1] : 1f, length = size.Length > 2 ? size[2] : 3f;
                    float angle = MathF.Atan2(height, length);
                    float slopeLength = MathF.Sqrt(height * height + length * length);
                    const float thickness = 0.3f;
                    var data = MeshFactory.BoxData(width, thickness, slopeLength, def.WorldUv);
                    var local = Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, -angle) *
                                Matrix4x4.CreateTranslation(0f, height * 0.5f, 0f);
                    StaticGeometry(data, local, solid ? "-col" : null);
                    break;
                }

                case "crate":
                {
                    float size = def.Size is { Length: > 0 } ? def.Size[0] : 1f;
                    hasContent = AddGeometry(GetNode(WithSuffix(baseName, solid ? "-rigid" : null)), baseName,
                        MeshFactory.CubeData(size, def.WorldUv), def.Material, Matrix4x4.Identity);
                    break;
                }

                case "terrain":
                    hasContent = AddTerrain(def, GetNode(WithSuffix(baseName, solid ? "-col" : null)), baseName);
                    break;

                case "model":
                    hasContent = AddModel(def, baseName, parent, out node);
                    break;

                case "directionallight" or "pointlight" or "spotlight":
                    if (_options.IncludeLights)
                        hasContent = AddLight(def, type, GetNode(baseName));
                    break;

                case "camera" or "flycamera":
                    if (_options.IncludeMarkers)
                    {
                        var extras = Extras(def.Type);
                        extras["current"] = def.Current;
                        _scene.AddCamera(new CameraBuilder.Perspective(null, def.Fov * Mathf.Deg2Rad, def.Near * _scale, def.Far * _scale),
                            GetNode(baseName, extras));
                        hasContent = true;
                    }
                    break;

                case "player" or "spawn" or "thirdpersoncamera" or "particles" or "audio" or "audio3d":
                    if (_options.IncludeMarkers)
                    {
                        var extras = Extras(def.Type);
                        if (def.Path != null) extras["path"] = def.Path;
                        if (def.Target != null) extras["target"] = def.Target;
                        GetNode(baseName, extras);
                    }
                    break;

                default:
                    Warn($"Unknown node type \"{def.Type}\" exported as an empty node.");
                    GetNode(baseName);
                    break;
            }

            if (node == null)
                return;

            if (def.Groups is { Length: > 0 })
            {
                node.Extras ??= new JsonObject();
                if (node.Extras is JsonObject obj)
                    obj["groups"] = new JsonArray(def.Groups.Select(g => (JsonNode)g!).ToArray());
            }

            if (def.Children != null)
                foreach (var child in def.Children)
                    ExportNode(child, node);

            // Empty nodes (groups, markers) still need to be registered with the scene.
            if (!hasContent && parent == null)
                _scene.AddNode(node);
        }

        /// <summary>Same face-key precedence as the scene loader (exact face > sides > all).</summary>
        private static MaterialRef? FaceMaterialRef(Dictionary<string, MaterialRef> faces, MeshFactory.BoxFace face)
        {
            string[] keys = face switch
            {
                MeshFactory.BoxFace.Top => new[] { "top", "all" },
                MeshFactory.BoxFace.Bottom => new[] { "bottom", "all" },
                MeshFactory.BoxFace.Front => new[] { "front", "sides", "all" },
                MeshFactory.BoxFace.Back => new[] { "back", "sides", "all" },
                MeshFactory.BoxFace.Left => new[] { "left", "sides", "all" },
                _ => new[] { "right", "sides", "all" }
            };
            foreach (var key in keys)
                if (faces.TryGetValue(key, out var reference))
                    return reference;
            return null;
        }

        private void ApplyTransform(NodeBuilder node, NodeDef def)
        {
            if (def.Scale is { Length: >= 3 })
                node.WithLocalScale(new Vector3(def.Scale[0], def.Scale[1], def.Scale[2]));
            if (def.RotationDegrees is { Length: >= 3 })
                node.WithLocalRotation(FromEulerDegrees(def.RotationDegrees));
            if (def.Position is { Length: >= 3 })
                node.WithLocalTranslation(new Vector3(def.Position[0], def.Position[1], def.Position[2]) * _scale);
        }

        /// <summary>Local transform matrix of a definition (scale * rotation * translation).</summary>
        private Matrix4x4 LocalMatrix(NodeDef def)
        {
            var m = Matrix4x4.Identity;
            if (def.Scale is { Length: >= 3 })
                m *= Matrix4x4.CreateScale(def.Scale[0], def.Scale[1], def.Scale[2]);
            if (def.RotationDegrees is { Length: >= 3 })
                m *= Matrix4x4.CreateFromQuaternion(FromEulerDegrees(def.RotationDegrees));
            if (def.Position is { Length: >= 3 })
                m *= Matrix4x4.CreateTranslation(def.Position[0] * _scale, def.Position[1] * _scale, def.Position[2] * _scale);
            return m;
        }

        /// <summary>Same convention as <see cref="Node3D.RotationDegrees"/> (yaw Y, pitch X, roll Z).</summary>
        private static Quaternion FromEulerDegrees(float[] degrees) =>
            Quaternion.CreateFromYawPitchRoll(
                degrees[1] * Mathf.Deg2Rad, degrees[0] * Mathf.Deg2Rad, degrees[2] * Mathf.Deg2Rad);

        private static readonly string[] GodotSuffixList =
            { "-col", "-convcol", "-colonly", "-rigid", "-navmesh", "-noimp", "-occ", "-vehicle", "-wheel" };

        private string WithSuffix(string name, string? suffix)
        {
            if (suffix == null || !_options.GodotSuffixes)
                return name;
            if (GodotSuffixList.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                return name;   // already carries an import hint (e.g. embedded model nodes)
            return name + suffix;
        }

        private static JsonObject Extras(string type) => new() { ["ducz_type"] = type };

        // ---- Geometry ------------------------------------------------

        private bool AddGeometry(NodeBuilder node, string meshName, MeshData data, MaterialRef? materialRef,
            Matrix4x4 localOffset)
        {
            var material = ResolveMaterial(materialRef);
            var toExport = Matrix4x4.CreateScale(_scale) * ScaleTranslation(localOffset);
            if (!toExport.IsIdentity)
                data = data.Transformed(toExport);

            _scene.AddRigidMesh(BuildMesh(meshName, new[] { (data, material) }), node);
            Result.TriangleCount += data.TriangleCount;
            return true;
        }

        private bool AddTerrain(NodeDef def, NodeBuilder node, string name)
        {
            var t = def.Terrain ?? new TerrainDef();
            Func<float, float, float> height;
            try
            {
                height = t.Mode.ToLowerInvariant() switch
                {
                    "heightmap" when t.Heightmap != null => Terrain.HeightmapSampler(t.Heightmap, t.SizeX, t.SizeZ, t.MaxHeight),
                    "hills" => Terrain.HillsFunction(t.Amplitude, t.Frequency),
                    _ => (_, _) => 0f
                };
            }
            catch (Exception ex)
            {
                Warn($"Terrain \"{name}\": heightmap could not be read ({ex.Message}); exported flat.");
                height = (_, _) => 0f;
            }

            int resolution = t.Mode.Equals("flat", StringComparison.OrdinalIgnoreCase) ? 2 : t.Resolution;
            var data = Terrain.BuildMeshData(height, t.SizeX, t.SizeZ, resolution);
            if (_scale != 1f)
                data = data.Transformed(Matrix4x4.CreateScale(_scale));

            MaterialBuilder material;
            if (def.Material != null)
                material = ResolveMaterial(def.Material);
            else
                material = GetOrCreate("TerrainDefault", () => new MaterialBuilder("TerrainDefault")
                    .WithMetallicRoughnessShader()
                    .WithBaseColor(Vector4.One)
                    .WithMetallicRoughness(0f, 0.95f));

            _scene.AddRigidMesh(BuildMesh(name, new[] { (data, material) }), node);
            Result.TriangleCount += data.TriangleCount;
            return true;
        }

        private IMeshBuilder<MaterialBuilder> BuildMesh(string name, IEnumerable<(MeshData Data, MaterialBuilder Material)> parts)
        {
            var list = parts.ToList();
            bool useColors = list.Any(p => p.Data.Vertices.Any(v => v.Color != Vector4.One));
            return useColors
                ? Fill(new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(name), list,
                    v => new VertexColor1Texture1(v.Color, v.UV))
                : Fill(new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(name), list,
                    v => new VertexTexture1(v.UV));
        }

        private static MeshBuilder<VertexPositionNormal, TvM, VertexEmpty> Fill<TvM>(
            MeshBuilder<VertexPositionNormal, TvM, VertexEmpty> mesh,
            List<(MeshData Data, MaterialBuilder Material)> parts,
            Func<Vertex, TvM> materialOf)
            where TvM : struct, IVertexMaterial
        {
            foreach (var (data, material) in parts)
            {
                var primitive = mesh.UsePrimitive(material);
                var vertices = data.Vertices;
                var indices = data.Indices;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    primitive.AddTriangle(
                        Convert(vertices[indices[i]], materialOf),
                        Convert(vertices[indices[i + 1]], materialOf),
                        Convert(vertices[indices[i + 2]], materialOf));
                }
            }
            return mesh;
        }

        private static VertexBuilder<VertexPositionNormal, TvM, VertexEmpty> Convert<TvM>(Vertex v, Func<Vertex, TvM> materialOf)
            where TvM : struct, IVertexMaterial
        {
            var normal = v.Normal.LengthSquared() > 1e-8f ? Vector3.Normalize(v.Normal) : Vector3.UnitY;
            var geometry = new VertexPositionNormal(v.Position, normal);
            return new VertexBuilder<VertexPositionNormal, TvM, VertexEmpty>(geometry, materialOf(v));
        }

        // ---- Lights & cameras ----------------------------------------

        private bool AddLight(NodeDef def, string type, NodeBuilder node)
        {
            var color = def.Color != null ? ParseColor(def.Color) : Color.White;
            var linear = new Vector3(SrgbToLinear(color.R), SrgbToLinear(color.G), SrgbToLinear(color.B));

            LightBuilder light = type switch
            {
                "pointlight" => new LightBuilder.Point { Color = linear, Intensity = def.Energy, Range = def.Range * _scale },
                "spotlight" => new LightBuilder.Spot
                {
                    Color = linear,
                    Intensity = def.Energy,
                    Range = def.Range * _scale,
                    OuterConeAngle = def.Angle * 0.5f * Mathf.Deg2Rad,
                    InnerConeAngle = def.Angle * 0.5f * Mathf.Deg2Rad * (1f - Mathf.Clamp01(def.Softness))
                },
                _ => new LightBuilder.Directional { Color = linear, Intensity = def.Energy }
            };
            _scene.AddLight(light, node);
            return true;
        }

        // ---- External models -----------------------------------------

        private bool AddModel(NodeDef def, string baseName, NodeBuilder? parent, out NodeBuilder? node)
        {
            bool solid = def.Collider != null && !def.Collider.Shape.Equals("none", StringComparison.OrdinalIgnoreCase);
            string name = WithSuffix(baseName, solid ? "-col" : null);
            node = parent != null ? parent.CreateNode(name) : new NodeBuilder(name);
            ApplyTransform(node, def);

            if (def.Path == null)
            {
                Warn($"Model \"{baseName}\" has no path.");
                return false;
            }

            var extras = Extras("model");
            extras["path"] = def.Path;
            node.Extras = extras;

            if (!_options.IncludeModels)
                return false;

            string fullPath = Assets.Resolve(def.Path);
            if (!File.Exists(fullPath))
            {
                Warn($"Model file not found: {def.Path}");
                return false;
            }

            MaterialBuilder? overrideMaterial = def.Material != null ? ResolveMaterial(def.Material) : null;
            try
            {
                string extension = Path.GetExtension(fullPath).ToLowerInvariant();
                return extension is ".glb" or ".gltf"
                    ? AddGltfModel(fullPath, node, overrideMaterial, solid, def.SubNode,
                                   string.Equals(def.SubNodePivot, "base", StringComparison.OrdinalIgnoreCase))
                    : AddAssimpModel(fullPath, node, overrideMaterial, solid, def.SubNode);
            }
            catch (Exception ex)
            {
                Warn($"Model \"{def.Path}\" could not be embedded: {ex.Message}");
                return false;
            }
        }

        private bool AddGltfModel(string path, NodeBuilder node, MaterialBuilder? overrideMaterial, bool solid,
            string? subNode, bool recenterPart = false)
        {
            var root = LoadGltfCached(path);
            var scene = root.DefaultScene ?? root.LogicalScenes.FirstOrDefault();
            if (scene == null)
                return false;

            var materialCache = new Dictionary<GltfMaterial, MaterialBuilder>();
            var meshCache = new Dictionary<GltfMesh, IMeshBuilder<MaterialBuilder>>();
            bool any = false;

            void Visit(GltfNode gltfNode, NodeBuilder target, bool bakeWorld = false, Vector3 offset = default)
            {
                var child = target.CreateNode(gltfNode.Name ?? "node");
                // Works for SRT and matrix nodes alike; a sub-node part bakes its parents in.
                var local = bakeWorld ? gltfNode.WorldMatrix : gltfNode.LocalMatrix;
                if (offset != Vector3.Zero)
                    local *= Matrix4x4.CreateTranslation(offset);
                child.LocalMatrix = ScaleTranslation(local);
                if (gltfNode.Mesh != null)
                {
                    if (!meshCache.TryGetValue(gltfNode.Mesh, out var mesh))
                    {
                        mesh = ConvertGltfMesh(gltfNode.Mesh, overrideMaterial, materialCache);
                        meshCache[gltfNode.Mesh] = mesh;
                    }
                    if (solid)
                        child.Name = WithSuffix(child.Name, "-col");
                    _scene.AddRigidMesh(mesh, child);
                    any = true;
                }
                foreach (var grandChild in gltfNode.VisualChildren)
                    Visit(grandChild, child);
            }

            if (subNode != null)
            {
                var part = root.LogicalNodes.FirstOrDefault(n => string.Equals(n.Name, subNode, StringComparison.Ordinal))
                           ?? root.LogicalNodes.FirstOrDefault(n => string.Equals(n.Name, subNode, StringComparison.OrdinalIgnoreCase));
                if (part == null)
                {
                    Warn($"Node \"{subNode}\" not found in {Path.GetFileName(path)} - whole model exported.");
                }
                else
                {
                    // "subNodePivot": "base" puts the piece on the node's own origin, so the
                    // export has to shift it the same way the runtime does.
                    var pivot = recenterPart ? PartPivot(part) : Vector3.Zero;
                    Visit(part, node, bakeWorld: true, offset: -pivot);
                    return any;
                }
            }

            foreach (var gltfNode in scene.VisualChildren)
                Visit(gltfNode, node);
            return any;
        }

        // A modular pack is referenced once per placed piece; without this cache a map with
        // 500 pieces re-parsed the same (often 100 MB+) file 500 times.
        private readonly Dictionary<string, ModelRoot> _gltfCache = new(StringComparer.OrdinalIgnoreCase);

        private ModelRoot LoadGltfCached(string path)
        {
            if (!_gltfCache.TryGetValue(path, out var root))
            {
                root = ModelRoot.Load(path);
                _gltfCache[path] = root;
            }
            return root;
        }

        /// <summary>Footprint centre / base of a sub-node part, in the model file's own space.</summary>
        private static Vector3 PartPivot(GltfNode part)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            void Walk(GltfNode n)
            {
                if (n.Mesh != null)
                {
                    foreach (var primitive in n.Mesh.Primitives)
                    {
                        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                        if (positions == null)
                            continue;
                        foreach (var p in positions)
                        {
                            var w = Vector3.Transform(p, n.WorldMatrix);
                            min = Vector3.Min(min, w);
                            max = Vector3.Max(max, w);
                        }
                    }
                }
                foreach (var c in n.VisualChildren)
                    Walk(c);
            }
            Walk(part);
            if (min.X > max.X)
                return Vector3.Zero;
            return new Vector3((min.X + max.X) * 0.5f, min.Y, (min.Z + max.Z) * 0.5f);
        }

        private IMeshBuilder<MaterialBuilder> ConvertGltfMesh(GltfMesh gltfMesh, MaterialBuilder? overrideMaterial,
            Dictionary<GltfMaterial, MaterialBuilder> materialCache)
        {
            var mesh = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(gltfMesh.Name ?? "mesh");
            foreach (var primitive in gltfMesh.Primitives)
            {
                if (primitive.DrawPrimitiveType != PrimitiveType.TRIANGLES)
                    continue;

                MaterialBuilder material;
                if (overrideMaterial != null)
                    material = overrideMaterial;
                else if (primitive.Material == null)
                    material = DefaultMaterial();
                else if (!materialCache.TryGetValue(primitive.Material, out material!))
                {
                    material = primitive.Material.ToMaterialBuilder();
                    materialCache[primitive.Material] = material;
                }

                var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                if (positions == null)
                    continue;
                var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();

                var builder = mesh.UsePrimitive(material);
                foreach (var (a, b, c) in primitive.GetTriangleIndices())
                {
                    builder.AddTriangle(Vertex(a), Vertex(b), Vertex(c));
                    Result.TriangleCount++;
                }

                VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> Vertex(int i)
                {
                    var normal = normals != null && normals[i].LengthSquared() > 1e-8f ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
                    var uv = uvs != null ? uvs[i] : Vector2.Zero;
                    var color = colors != null ? colors[i] : Vector4.One;
                    return new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(
                        new VertexPositionNormal(positions[i] * _scale, normal), new VertexColor1Texture1(color, uv));
                }
            }
            return mesh;
        }

        private bool AddAssimpModel(string path, NodeBuilder node, MaterialBuilder? overrideMaterial, bool solid, string? subNode)
        {
            var scene = Model.ImportScene(path, out string directory);
            var materialCache = new Dictionary<int, MaterialBuilder>();
            bool any = false;

            MaterialBuilder MaterialFor(int index)
            {
                if (overrideMaterial != null)
                    return overrideMaterial;
                if (materialCache.TryGetValue(index, out var cached))
                    return cached;
                var converted = ConvertAssimpMaterial(scene, scene.Materials[index], directory, index);
                materialCache[index] = converted;
                return converted;
            }

            // Assimp matrices are row-major with translation in the last column; System.Numerics is the transpose.
            static Matrix4x4 ToNumerics(Assimp.Matrix4x4 m) => new(
                m.A1, m.B1, m.C1, m.D1,
                m.A2, m.B2, m.C2, m.D2,
                m.A3, m.B3, m.C3, m.D3,
                m.A4, m.B4, m.C4, m.D4);

            static Matrix4x4 WorldOf(Assimp.Node n)
            {
                var world = Matrix4x4.Identity;
                for (var current = n; current != null; current = current.Parent)
                    world *= ToNumerics(current.Transform);
                return world;
            }

            void Visit(Assimp.Node aiNode, NodeBuilder target, bool bakeWorld = false, string? onlyMesh = null)
            {
                var child = target.CreateNode(string.IsNullOrWhiteSpace(aiNode.Name) ? "node" : aiNode.Name);
                child.LocalMatrix = ScaleTranslation(bakeWorld ? WorldOf(aiNode) : ToNumerics(aiNode.Transform));

                for (int i = 0; i < aiNode.MeshCount; i++)
                {
                    var aiMesh = scene.Meshes[aiNode.MeshIndices[i]];
                    if (aiMesh.PrimitiveType != Assimp.PrimitiveType.Triangle || aiMesh.VertexCount == 0)
                        continue;
                    if (onlyMesh != null && !string.Equals(aiMesh.Name, onlyMesh, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var meshNode = aiNode.MeshCount == 1 ? child : child.CreateNode(aiMesh.Name is { Length: > 0 } n ? n : $"mesh{i}");
                    if (solid)
                        meshNode.Name = WithSuffix(meshNode.Name, "-col");

                    var data = ConvertAssimpMesh(aiMesh, _scale);
                    _scene.AddRigidMesh(BuildMesh(aiMesh.Name is { Length: > 0 } mn ? mn : "mesh", new[] { (data, MaterialFor(aiMesh.MaterialIndex)) }), meshNode);
                    Result.TriangleCount += data.TriangleCount;
                    any = true;
                }

                if (onlyMesh != null)
                    return;
                foreach (var grandChild in aiNode.Children)
                    Visit(grandChild, child);
            }

            if (subNode != null)
            {
                Assimp.Node? FindNode(Assimp.Node n) =>
                    string.Equals(n.Name, subNode, StringComparison.OrdinalIgnoreCase)
                        ? n
                        : n.Children.Select(FindNode).FirstOrDefault(found => found != null);

                var part = FindNode(scene.RootNode);
                if (part != null)
                {
                    Visit(part, node, bakeWorld: true);
                    return any;
                }

                // The engine names Assimp mesh nodes after their mesh: export just that mesh.
                Assimp.Node? FindMeshOwner(Assimp.Node n) =>
                    n.MeshIndices.Any(mi => string.Equals(scene.Meshes[mi].Name, subNode, StringComparison.OrdinalIgnoreCase))
                        ? n
                        : n.Children.Select(FindMeshOwner).FirstOrDefault(found => found != null);

                var owner = FindMeshOwner(scene.RootNode);
                if (owner != null)
                {
                    Visit(owner, node, bakeWorld: true, onlyMesh: subNode);
                    return any;
                }
                Warn($"Node \"{subNode}\" not found in {Path.GetFileName(path)} - whole model exported.");
            }

            Visit(scene.RootNode, node);
            return any;
        }

        private static MeshData ConvertAssimpMesh(Assimp.Mesh aiMesh, float scale)
        {
            var vertices = new Vertex[aiMesh.VertexCount];
            bool hasUvs = aiMesh.HasTextureCoords(0);
            bool hasColors = aiMesh.HasVertexColors(0);
            var uvs = hasUvs ? aiMesh.TextureCoordinateChannels[0] : null;
            var colors = hasColors ? aiMesh.VertexColorChannels[0] : null;

            for (int i = 0; i < aiMesh.VertexCount; i++)
            {
                var p = aiMesh.Vertices[i];
                var n = aiMesh.HasNormals ? aiMesh.Normals[i] : new Assimp.Vector3D(0, 1, 0);
                var uv = hasUvs ? uvs![i] : default;
                var c = hasColors ? colors![i] : new Assimp.Color4D(1, 1, 1, 1);
                vertices[i] = new Vertex(new Vector3(p.X, p.Y, p.Z) * scale, new Vector3(n.X, n.Y, n.Z),
                    new Vector2(uv.X, uv.Y), new Vector4(c.R, c.G, c.B, c.A));
            }

            var intIndices = aiMesh.GetIndices();
            var indices = new uint[intIndices.Length];
            for (int i = 0; i < intIndices.Length; i++)
                indices[i] = (uint)intIndices[i];
            return new MeshData(vertices, indices);
        }

        private MaterialBuilder ConvertAssimpMaterial(Assimp.Scene scene, Assimp.Material aiMaterial, string directory, int index)
        {
            var material = new MaterialBuilder(aiMaterial.HasName ? aiMaterial.Name : $"material_{index}")
                .WithMetallicRoughnessShader()
                .WithMetallicRoughness(0f, 0.8f);

            var baseColor = Vector4.One;
            if (aiMaterial.HasColorDiffuse)
            {
                var d = aiMaterial.ColorDiffuse;
                baseColor = new Vector4(d.R, d.G, d.B, d.A);
            }
            if (aiMaterial.HasOpacity && aiMaterial.Opacity < 0.999f)
            {
                baseColor.W *= aiMaterial.Opacity;
                material.WithAlpha(SharpGLTF.Materials.AlphaMode.BLEND);
            }

            byte[]? textureBytes = aiMaterial.HasTextureDiffuse
                ? Model.ResolveAssimpTextureBytes(scene, aiMaterial.TextureDiffuse.FilePath, directory)
                : null;
            if (textureBytes != null)
            {
                var image = ToGltfImage(textureBytes, aiMaterial.TextureDiffuse.FilePath);
                if (image != null)
                    material.WithBaseColor(image, baseColor);
                else
                    material.WithBaseColor(baseColor);
            }
            else
            {
                material.WithBaseColor(baseColor);
            }

            if (aiMaterial.HasColorEmissive)
            {
                var e = aiMaterial.ColorEmissive;
                if (e.R + e.G + e.B > 0.01f)
                    material.WithEmissive(new Vector3(e.R, e.G, e.B));
            }

            material.WithDoubleSide(aiMaterial.IsTwoSided);
            return material;
        }

        // ---- Materials -----------------------------------------------

        private MaterialBuilder ResolveMaterial(MaterialRef? reference)
        {
            if (reference == null)
                return DefaultMaterial();

            if (reference.Inline != null)
                return GetOrCreate(reference.Inline, () => ConvertMaterial("inline_" + _materials.Count, reference.Inline));

            if (reference.Reference == null)
                return DefaultMaterial();

            if (_materialsByKey.TryGetValue(reference.Reference, out var cached))
                return cached;

            if (!_doc.Materials.TryGetValue(reference.Reference, out var def))
            {
                Warn($"Material \"{reference.Reference}\" not found - default material used.");
                return DefaultMaterial();
            }

            MaterialBuilder material;
            try
            {
                material = ConvertMaterial(reference.Reference, def);
            }
            catch (Exception ex)
            {
                Warn($"Material \"{reference.Reference}\" could not be converted ({ex.Message}) - default material used.");
                material = DefaultMaterial();
            }
            _materialsByKey[reference.Reference] = material;
            return material;
        }

        private MaterialBuilder DefaultMaterial() => _defaultMaterial ??= new MaterialBuilder("Default")
            .WithMetallicRoughnessShader()
            .WithBaseColor(Vector4.One)
            .WithMetallicRoughness(0f, 0.7f);

        private MaterialBuilder GetOrCreate(object key, Func<MaterialBuilder> factory)
        {
            if (_materials.TryGetValue(key, out var existing))
                return existing;
            var created = factory();
            _materials[key] = created;
            return created;
        }

        private MaterialBuilder ConvertMaterial(string name, MaterialDef def)
        {
            var material = new MaterialBuilder(name);
            if (def.Unshaded)
                material.WithUnlitShader();
            else
                material.WithMetallicRoughnessShader();

            var albedo = def.Albedo != null ? ParseColor(def.Albedo) : Color.White;
            var baseColor = new Vector4(SrgbToLinear(albedo.R), SrgbToLinear(albedo.G), SrgbToLinear(albedo.B), albedo.A);

            ImageBuilder? image = null;
            if (def.Texture != null)
            {
                try
                {
                    image = ToGltfImage(Assets.ReadBytes(def.Texture), def.Texture);
                }
                catch (Exception ex)
                {
                    Warn($"Material \"{name}\": texture \"{def.Texture}\" could not be read ({ex.Message}).");
                }
            }
            else if (def.Checkerboard is { } checker)
            {
                var pixels = Texture2D.CheckerboardPixels(checker.Size, checker.Cells,
                    ParseColor(checker.ColorA), ParseColor(checker.ColorB));
                image = ImageBuilder.From(new SharpGLTF.Memory.MemoryImage(PngEncoder.Encode(checker.Size, checker.Size, pixels)), name + "_checker");
            }

            if (image != null)
            {
                material.WithBaseColor(image, baseColor);
                var texture = material.GetChannel(KnownChannel.BaseColor)!.Texture!;

                bool nearest = def.Filter?.Equals("nearest", StringComparison.OrdinalIgnoreCase) == true;
                texture.WithSampler(TextureWrapMode.REPEAT, TextureWrapMode.REPEAT,
                    nearest ? TextureMipMapFilter.NEAREST_MIPMAP_NEAREST : TextureMipMapFilter.LINEAR_MIPMAP_LINEAR,
                    nearest ? TextureInterpolationFilter.NEAREST : TextureInterpolationFilter.LINEAR);

                var scale = def.UvScale is { Length: >= 2 } ? new Vector2(def.UvScale[0], def.UvScale[1]) : Vector2.One;
                var offset = def.UvOffset is { Length: >= 2 } ? new Vector2(def.UvOffset[0], def.UvOffset[1]) : Vector2.Zero;
                if (scale != Vector2.One || offset != Vector2.Zero)
                    texture.WithTransform(offset, scale, 0f, null);
            }
            else
            {
                material.WithBaseColor(baseColor);
            }

            if (!def.Unshaded)
            {
                // Blinn-Phong "specular strength" -> PBR roughness (approximation).
                float roughness = Mathf.Clamp(1f - def.Specular * 0.85f, 0.05f, 1f);
                material.WithMetallicRoughness(0f, roughness);
            }

            // KHR_materials_unlit has no emissive channel (unlit is already "self-lit"): skip it there.
            if (def.Emission != null && !def.Unshaded)
            {
                var emission = ParseColor(def.Emission);
                var rgb = new Vector3(SrgbToLinear(emission.R), SrgbToLinear(emission.G), SrgbToLinear(emission.B));
                if (rgb.LengthSquared() > 1e-6f)
                    material.WithEmissive(rgb, MathF.Max(0f, def.EmissionEnergy));
            }

            if (def.Transparent || albedo.A < 0.999f)
                material.WithAlpha(SharpGLTF.Materials.AlphaMode.BLEND);
            else if (def.AlphaCutout > 0f)
                material.WithAlpha(SharpGLTF.Materials.AlphaMode.MASK, def.AlphaCutout);

            material.WithDoubleSide(def.DoubleSided);
            return material;
        }

        /// <summary>glTF only allows PNG/JPEG images; anything else is decoded and re-encoded as PNG.</summary>
        private ImageBuilder? ToGltfImage(byte[] encoded, string sourceName)
        {
            string ext = Path.GetExtension(sourceName).ToLowerInvariant();
            bool isPngOrJpeg = ext is ".png" or ".jpg" or ".jpeg"
                               || (encoded.Length > 4 && encoded[0] == 0x89 && encoded[1] == (byte)'P')
                               || (encoded.Length > 3 && encoded[0] == 0xFF && encoded[1] == 0xD8);
            try
            {
                if (!isPngOrJpeg)
                {
                    var decoded = StbImageSharp.ImageResult.FromMemory(encoded, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                    encoded = PngEncoder.Encode(decoded.Width, decoded.Height, decoded.Data);
                }
                return ImageBuilder.From(new SharpGLTF.Memory.MemoryImage(encoded), Path.GetFileNameWithoutExtension(sourceName));
            }
            catch (Exception ex)
            {
                Warn($"Texture \"{sourceName}\" could not be embedded ({ex.Message}).");
                return null;
            }
        }

        // ---- Helpers -------------------------------------------------

        /// <summary>Applies the export scale to the translation part of a matrix (rotation/scale untouched).</summary>
        private Matrix4x4 ScaleTranslation(Matrix4x4 m)
        {
            if (_scale == 1f)
                return m;
            m.M41 *= _scale; m.M42 *= _scale; m.M43 *= _scale;
            return m;
        }

        private static float SrgbToLinear(float c) =>
            c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

        private Color ParseColor(string hex)
        {
            try
            {
                return Color.FromHex(hex);
            }
            catch
            {
                Warn($"Invalid color \"{hex}\" - white used.");
                return Color.White;
            }
        }

        private void Warn(string message)
        {
            Result.Warnings.Add(message);
            Log.Warning("GlbExporter: " + message);
        }
    }
}
