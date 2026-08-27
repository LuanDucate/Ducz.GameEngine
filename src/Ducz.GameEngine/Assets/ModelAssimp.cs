using System.Numerics;
using Ducz.Rendering;
using AiContext = Assimp.AssimpContext;
using AiPostProcess = Assimp.PostProcessSteps;
using AiScene = Assimp.Scene;
using AiNode = Assimp.Node;
using AiMesh = Assimp.Mesh;
using AiMaterial = Assimp.Material;
using AiMatrix = Assimp.Matrix4x4;
using AiPrimitiveType = Assimp.PrimitiveType;

namespace Ducz;

public sealed partial class Model
{
    /// <summary>
    /// Assimp-based importer for FBX, OBJ, DAE, STL and other classic formats.
    /// Supports the full node hierarchy, materials (diffuse color/texture, opacity,
    /// emissive), embedded FBX textures, and - crucially - skinned meshes with bones
    /// and animations, so characters exported for other engines work here too.
    /// </summary>
    internal static Model LoadWithAssimp(string path)
    {
        var scene = ImportScene(path, out string directory);
        var model = new Model { SourcePath = path };

        var materialCache = new Dictionary<int, Material>();
        var textureCache = new Dictionary<string, Texture2D>();

        // ---- 1. Node hierarchy (parent-first, matching our Model layout) ----
        var nodeIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pendingMeshNodes = new List<(int NodeIndex, AiMesh Mesh)>();

        void AddNode(AiNode aiNode, int parentIndex)
        {
            var (position, rotation, scale) = DecomposeTransform(aiNode.Transform);
            string name = UniqueNodeName(aiNode.Name, model._nodes.Count, nodeIndexByName);

            int index = model._nodes.Count;
            model._nodes.Add(new ModelNodeData(name, parentIndex, position, rotation, scale, -1, -1));
            nodeIndexByName.TryAdd(name, index);

            // Meshes attached to this node become child entries (one per mesh so
            // each can carry its own skin).
            for (int m = 0; m < aiNode.MeshCount; m++)
            {
                var aiMesh = scene.Meshes[aiNode.MeshIndices[m]];
                if (aiMesh.PrimitiveType == AiPrimitiveType.Triangle && aiMesh.VertexCount > 0)
                    pendingMeshNodes.Add((index, aiMesh));
            }

            foreach (var child in aiNode.Children)
                AddNode(child, index);
        }

        AddNode(scene.RootNode, -1);

        // ---- 2. Meshes, skins and mesh nodes ----
        foreach (var (parentIndex, aiMesh) in pendingMeshNodes)
        {
            int skinIndex = -1;
            VertexSkin[]? skinData = null;

            if (aiMesh.HasBones)
            {
                skinData = BuildSkinData(aiMesh);

                var joints = new int[aiMesh.BoneCount];
                var inverseBinds = new Matrix4x4[aiMesh.BoneCount];
                for (int b = 0; b < aiMesh.BoneCount; b++)
                {
                    var bone = aiMesh.Bones[b];
                    if (!nodeIndexByName.TryGetValue(bone.Name, out int boneNode))
                    {
                        Log.Warning($"Model '{Path.GetFileName(path)}': bone \"{bone.Name}\" has no matching node.");
                        boneNode = 0;
                    }
                    joints[b] = boneNode;
                    inverseBinds[b] = ToNumerics(bone.OffsetMatrix);
                }

                skinIndex = model._skins.Count;
                model._skins.Add(new ModelSkinData(joints, inverseBinds));
            }

            var mesh = ConvertMesh(aiMesh, skinData);
            if (!materialCache.TryGetValue(aiMesh.MaterialIndex, out var material))
            {
                material = ConvertAssimpMaterial(scene, scene.Materials[aiMesh.MaterialIndex], directory, textureCache);
                materialCache[aiMesh.MaterialIndex] = material;
            }

            int meshIndex = model._meshes.Count;
            model._meshes.Add(new List<ModelPrimitive> { new(mesh, material) });

            string meshName = UniqueNodeName(aiMesh.Name is { Length: > 0 } n ? n : "mesh", model._nodes.Count, nodeIndexByName);
            model._nodes.Add(new ModelNodeData(meshName, parentIndex,
                Vector3.Zero, Quaternion.Identity, Vector3.One, meshIndex, skinIndex));
            nodeIndexByName.TryAdd(meshName, model._nodes.Count - 1);
        }

        // ---- 3. Animations ----
        foreach (var clip in ConvertAnimations(scene))
            model.Animations.Add(clip);

        Log.Info($"Model loaded (Assimp): {Path.GetFileName(path)} " +
                 $"({model._meshes.Count} meshes, {model._skins.Count} skins, {model.Animations.Count} animations, {model._nodes.Count} nodes)");
        return model;
    }

    /// <summary>
    /// Loads only the animation clips from a file (FBX/DAE/glTF...). This is how
    /// engines like Unreal ship animations: one skeleton mesh plus one file per clip.
    /// Track names target bones by name, so clips apply to any model that shares
    /// the same skeleton:
    ///
    /// <code>
    /// var clips = Model.LoadAnimationClips("Anims/Walk_F.fbx", renameTo: "walk");
    /// hero.FindNode&lt;AnimationPlayer&gt;()!.AddClip(clips[0]);
    /// </code>
    /// </summary>
    public static List<AnimationClip> LoadAnimationClips(string path, string? renameTo = null)
    {
        List<AnimationClip> clips;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".glb" or ".gltf")
        {
            SharpGLTF.Schema2.ModelRoot root;
            if (Assets.Pack?.Contains(path) == true && extension == ".glb")
            {
                using var stream = new MemoryStream(Assets.ReadBytes(path));
                root = SharpGLTF.Schema2.ModelRoot.ReadGLB(stream);
            }
            else
            {
                root = SharpGLTF.Schema2.ModelRoot.Load(Assets.Resolve(path));
            }
            var names = BuildUniqueNames(root);
            clips = root.LogicalAnimations.Select(a => ConvertAnimation(a, names)).ToList();
        }
        else
        {
            var scene = ImportScene(path, out _);
            clips = ConvertAnimations(scene);
        }

        if (clips.Count == 0)
            Log.Warning($"No animations found in {path}.");

        if (renameTo != null && clips.Count > 0)
            clips[0] = RenameClip(clips[0], renameTo);

        return clips;
    }

    /// <summary>Creates a copy of a clip under a new name (clip names are immutable).</summary>
    public static AnimationClip RenameClip(AnimationClip clip, string newName)
    {
        var renamed = new AnimationClip { Name = newName, Duration = clip.Duration, Loop = clip.Loop };
        renamed.Tracks.AddRange(clip.Tracks);
        return renamed;
    }

    // ------------------------------------------------------------------
    // Assimp scene import + conversion helpers
    // ------------------------------------------------------------------

    internal static AiScene ImportScene(string path, out string directory)
    {
        using var context = new AiContext();
        // Collapse FBX pivot helper nodes so bone/animation names line up.
        context.SetConfig(new Assimp.Configs.FBXPreservePivotsConfig(false));

        var flags = AiPostProcess.Triangulate
                    | AiPostProcess.GenerateSmoothNormals
                    | AiPostProcess.JoinIdenticalVertices
                    | AiPostProcess.LimitBoneWeights      // max 4 influences per vertex
                    | AiPostProcess.FlipUVs
                    | AiPostProcess.SortByPrimitiveType;

        AiScene scene;
        if (Assets.Pack?.Contains(path) == true)
        {
            // Loading from the mounted asset pack.
            directory = Path.GetDirectoryName(AssetPack.NormalizePath(path))?.Replace('\\', '/') ?? "";
            using var stream = new MemoryStream(Assets.ReadBytes(path));
            string hint = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            scene = context.ImportFileFromStream(stream, flags, hint);
        }
        else
        {
            string fullPath = Assets.Resolve(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Model file not found: {fullPath}");
            directory = Path.GetDirectoryName(fullPath) ?? "";
            scene = context.ImportFile(fullPath, flags);
        }

        if (scene == null || scene.RootNode == null)
            throw new InvalidDataException($"Could not import {path}.");
        return scene;
    }

    private static string UniqueNodeName(string baseName, int fallbackIndex, Dictionary<string, int> taken)
    {
        string name = string.IsNullOrWhiteSpace(baseName) ? $"node{fallbackIndex}" : baseName;
        if (!taken.ContainsKey(name))
            return name;
        int suffix = 1;
        while (taken.ContainsKey($"{name}_{suffix}"))
            suffix++;
        return $"{name}_{suffix}";
    }

    private static Mesh ConvertMesh(AiMesh aiMesh, VertexSkin[]? skinData)
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

            vertices[i] = new Vertex(
                new Vector3(p.X, p.Y, p.Z),
                new Vector3(n.X, n.Y, n.Z),
                new Vector2(uv.X, uv.Y),
                new Vector4(c.R, c.G, c.B, c.A));
        }

        var intIndices = aiMesh.GetIndices();
        var indices = new uint[intIndices.Length];
        for (int i = 0; i < intIndices.Length; i++)
            indices[i] = (uint)intIndices[i];

        return new Mesh(vertices, indices, skinData, keepCpuPositions: true);
    }

    /// <summary>Gathers up to 4 (joint, weight) influences per vertex from the mesh bones.</summary>
    private static VertexSkin[] BuildSkinData(AiMesh aiMesh)
    {
        var joints = new int[aiMesh.VertexCount, 4];
        var weights = new float[aiMesh.VertexCount, 4];
        var counts = new int[aiMesh.VertexCount];

        for (int b = 0; b < aiMesh.BoneCount; b++)
        {
            foreach (var vertexWeight in aiMesh.Bones[b].VertexWeights)
            {
                int v = vertexWeight.VertexID;
                if (counts[v] < 4)
                {
                    joints[v, counts[v]] = b;
                    weights[v, counts[v]] = vertexWeight.Weight;
                    counts[v]++;
                }
                else
                {
                    // Replace the smallest influence if this one is stronger.
                    int smallest = 0;
                    for (int s = 1; s < 4; s++)
                        if (weights[v, s] < weights[v, smallest])
                            smallest = s;
                    if (vertexWeight.Weight > weights[v, smallest])
                    {
                        joints[v, smallest] = b;
                        weights[v, smallest] = vertexWeight.Weight;
                    }
                }
            }
        }

        var skin = new VertexSkin[aiMesh.VertexCount];
        for (int v = 0; v < aiMesh.VertexCount; v++)
        {
            float sum = weights[v, 0] + weights[v, 1] + weights[v, 2] + weights[v, 3];
            float inv = sum > Mathf.Epsilon ? 1f / sum : 0f;
            skin[v] = new VertexSkin
            {
                Joints = new Vector4(joints[v, 0], joints[v, 1], joints[v, 2], joints[v, 3]),
                Weights = new Vector4(weights[v, 0], weights[v, 1], weights[v, 2], weights[v, 3]) * inv
            };
        }
        return skin;
    }

    private static List<AnimationClip> ConvertAnimations(AiScene scene)
    {
        var clips = new List<AnimationClip>();
        for (int a = 0; a < scene.AnimationCount; a++)
        {
            var aiAnim = scene.Animations[a];
            double ticksPerSecond = aiAnim.TicksPerSecond > 0 ? aiAnim.TicksPerSecond : 25.0;
            float duration = (float)(aiAnim.DurationInTicks / ticksPerSecond);

            var clip = new AnimationClip
            {
                Name = string.IsNullOrWhiteSpace(aiAnim.Name) ? $"animation{a}" : aiAnim.Name,
                Duration = MathF.Max(duration, 0.001f)
            };

            foreach (var channel in aiAnim.NodeAnimationChannels)
            {
                if (channel.HasPositionKeys)
                {
                    clip.Tracks.Add(new AnimationTrack
                    {
                        TargetName = channel.NodeName,
                        Property = AnimationProperty.Position,
                        Times = channel.PositionKeys.Select(k => (float)(k.Time / ticksPerSecond)).ToArray(),
                        VectorValues = channel.PositionKeys
                            .Select(k => new Vector3(k.Value.X, k.Value.Y, k.Value.Z)).ToArray()
                    });
                }
                if (channel.HasRotationKeys)
                {
                    clip.Tracks.Add(new AnimationTrack
                    {
                        TargetName = channel.NodeName,
                        Property = AnimationProperty.Rotation,
                        Times = channel.RotationKeys.Select(k => (float)(k.Time / ticksPerSecond)).ToArray(),
                        RotationValues = channel.RotationKeys
                            .Select(k => Quaternion.Normalize(new Quaternion(k.Value.X, k.Value.Y, k.Value.Z, k.Value.W)))
                            .ToArray()
                    });
                }
                if (channel.HasScalingKeys)
                {
                    clip.Tracks.Add(new AnimationTrack
                    {
                        TargetName = channel.NodeName,
                        Property = AnimationProperty.Scale,
                        Times = channel.ScalingKeys.Select(k => (float)(k.Time / ticksPerSecond)).ToArray(),
                        VectorValues = channel.ScalingKeys
                            .Select(k => new Vector3(k.Value.X, k.Value.Y, k.Value.Z)).ToArray()
                    });
                }
            }

            clips.Add(clip);
        }
        return clips;
    }

    /// <summary>
    /// Assimp matrices are column-vector convention stored row-major; transposing
    /// yields the System.Numerics row-vector equivalent.
    /// </summary>
    private static Matrix4x4 ToNumerics(AiMatrix m) => new(
        m.A1, m.B1, m.C1, m.D1,
        m.A2, m.B2, m.C2, m.D2,
        m.A3, m.B3, m.C3, m.D3,
        m.A4, m.B4, m.C4, m.D4);

    private static (Vector3 Position, Quaternion Rotation, Vector3 Scale) DecomposeTransform(AiMatrix aiMatrix)
    {
        var matrix = ToNumerics(aiMatrix);
        if (Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
            return (translation, Quaternion.Normalize(rotation), scale);
        return (matrix.Translation, Quaternion.Identity, Vector3.One);
    }

    private static Material ConvertAssimpMaterial(AiScene scene, AiMaterial aiMaterial,
        string directory, Dictionary<string, Texture2D> textureCache)
    {
        var material = new Material();

        if (aiMaterial.HasColorDiffuse)
        {
            var d = aiMaterial.ColorDiffuse;
            material.Albedo = new Color(d.R, d.G, d.B, d.A);
        }

        if (aiMaterial.HasOpacity && aiMaterial.Opacity < 0.999f)
        {
            material.Transparent = true;
            material.Albedo = material.Albedo.WithAlpha(material.Albedo.A * aiMaterial.Opacity);
        }

        if (aiMaterial.HasColorEmissive)
        {
            var e = aiMaterial.ColorEmissive;
            if (e.R + e.G + e.B > 0.01f)
                material.Emission = new Color(e.R, e.G, e.B);
        }

        if (aiMaterial.HasShininess && aiMaterial.Shininess > 1f)
            material.Shininess = Mathf.Clamp(aiMaterial.Shininess, 2f, 256f);
        else
            material.SpecularStrength = 0.15f;

        material.DoubleSided = aiMaterial.IsTwoSided;

        if (aiMaterial.HasTextureDiffuse)
        {
            var texture = LoadAssimpTexture(scene, aiMaterial.TextureDiffuse.FilePath, directory, textureCache);
            if (texture != null)
                material.AlbedoTexture = texture;
        }

        return material;
    }

    private static Texture2D? LoadAssimpTexture(AiScene scene, string? filePath,
        string directory, Dictionary<string, Texture2D> cache)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;
        if (cache.TryGetValue(filePath, out var cached))
            return cached;

        var encoded = ResolveAssimpTextureBytes(scene, filePath, directory);
        if (encoded == null)
            return null;

        Texture2D? texture = null;
        try
        {
            texture = Texture2D.FromEncodedBytes(encoded);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to decode model texture \"{filePath}\": {ex.Message}");
        }

        if (texture != null)
            cache[filePath] = texture;
        return texture;
    }

    /// <summary>
    /// Finds the encoded image bytes (PNG/JPG...) of a texture referenced by an Assimp
    /// material: embedded textures (FBX) or files next to the model. Uncompressed embedded
    /// texels are re-encoded as PNG. Returns null when nothing is found. No GPU involved,
    /// so exporters can use it too.
    /// </summary>
    internal static byte[]? ResolveAssimpTextureBytes(AiScene scene, string? filePath, string directory)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            if (filePath.StartsWith('*') && int.TryParse(filePath[1..], out int embeddedIndex)
                && embeddedIndex >= 0 && embeddedIndex < scene.TextureCount)
            {
                // Embedded texture (common in FBX).
                var embedded = scene.Textures[embeddedIndex];
                if (embedded.IsCompressed)
                {
                    return embedded.CompressedData;
                }
                else if (embedded.NonCompressedData is { Length: > 0 })
                {
                    var texels = embedded.NonCompressedData;
                    var rgba = new byte[texels.Length * 4];
                    for (int i = 0; i < texels.Length; i++)
                    {
                        rgba[i * 4] = texels[i].R;
                        rgba[i * 4 + 1] = texels[i].G;
                        rgba[i * 4 + 2] = texels[i].B;
                        rgba[i * 4 + 3] = texels[i].A;
                    }
                    return PngEncoder.Encode(embedded.Width, embedded.Height, rgba);
                }
            }
            else
            {
                // External file: try the stored path, then just the file name next to
                // the model - both on disk and inside the mounted asset pack.
                string normalized = filePath.Replace('\\', Path.DirectorySeparatorChar);
                string[] candidates =
                {
                    Path.Combine(directory, normalized),
                    Path.Combine(directory, Path.GetFileName(normalized)),
                    normalized
                };
                var found = candidates.FirstOrDefault(Assets.FileExists)
                            ?? FindTextureByBaseName(directory, normalized);
                if (found != null)
                    return Assets.ReadBytes(found);
                Log.Warning($"Model texture not found: {filePath}");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to load model texture \"{filePath}\": {ex.Message}");
        }

        return null;
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };

    /// <summary>
    /// Last-resort texture lookup: a file next to the model with the same base name
    /// but any image extension (authors often convert textures between formats).
    /// Searches both the disk directory and the mounted asset pack.
    /// </summary>
    private static string? FindTextureByBaseName(string directory, string referencedPath)
    {
        string baseName = Path.GetFileNameWithoutExtension(referencedPath);
        if (baseName.Length == 0)
            return null;

        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(file), baseName, StringComparison.OrdinalIgnoreCase)
                    && ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    return file;
            }
        }

        if (Assets.Pack != null)
        {
            string prefix = directory.Length > 0 ? AssetPack.NormalizePath(directory) + "/" : "";
            foreach (var entry in Assets.Pack.EnumeratePaths(prefix.Length > 0 ? prefix : null))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(entry), baseName, StringComparison.OrdinalIgnoreCase)
                    && ImageExtensions.Contains(Path.GetExtension(entry).ToLowerInvariant()))
                    return entry;
            }
        }

        return null;
    }
}
