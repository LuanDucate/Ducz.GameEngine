using System.Numerics;
using Ducz.Rendering;
using SharpGLTF.Schema2;
using GltfNode = SharpGLTF.Schema2.Node;
using GltfAnimation = SharpGLTF.Schema2.Animation;
using Mesh = Ducz.Rendering.Mesh;
using Material = Ducz.Rendering.Material;

namespace Ducz;

/// <summary>
/// A 3D model loaded from a file: meshes, materials, skeletons and animations.
/// Load once (ideally through <see cref="Assets.LoadModel"/>) and call <see cref="Instantiate"/>
/// for every copy you want in the scene:
///
/// <code>
/// var model = Assets.LoadModel("Assets/Models/hero.glb");
/// var hero = AddChild(model.Instantiate());
/// hero.FindNode&lt;AnimationPlayer&gt;()!.Play("Run");
/// </code>
///
/// Formats:
/// <list type="bullet">
/// <item>.glb / .gltf - full support: PBR base color, vertex colors, skinned meshes,
/// TRS animations (linear, step, cubic-spline). Preferred for characters.</item>
/// <item>.fbx / .obj / .dae / .stl / .3ds / .ply - static geometry with materials and
/// diffuse textures (via Assimp). Great for props: houses, trees, rocks.</item>
/// </list>
/// </summary>
public sealed partial class Model
{
    private sealed record ModelNodeData(
        string Name, int ParentIndex, Vector3 Position, Quaternion Rotation, Vector3 Scale,
        int MeshIndex, int SkinIndex);

    private sealed record ModelSkinData(int[] JointNodeIndices, Matrix4x4[] InverseBinds);

    private sealed record ModelPrimitive(Mesh Mesh, Material Material);

    private readonly List<List<ModelPrimitive>> _meshes = new();
    private readonly List<ModelNodeData> _nodes = new();      // depth-first, parent before child
    private readonly List<ModelSkinData> _skins = new();

    /// <summary>Animation clips found in the file (shared between instances).</summary>
    public List<AnimationClip> Animations { get; } = new();

    /// <summary>Names of all animations in the file.</summary>
    public IEnumerable<string> AnimationNames => Animations.Select(a => a.Name);

    /// <summary>True when the model contains skinned meshes.</summary>
    public bool HasSkins => _skins.Count > 0;

    /// <summary>File the model was loaded from.</summary>
    public string SourcePath { get; private set; } = "";

    private Model() { }

    // ------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------

    /// <summary>
    /// Loads a model file, picking the importer from the extension
    /// (.glb/.gltf: full pipeline; anything else: static import via Assimp).
    /// Must be called after the engine window is open.
    /// </summary>
    public static Model Load(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".glb" or ".gltf"))
            return LoadWithAssimp(path);

        ModelRoot root;
        if (Assets.Pack?.Contains(path) == true)
        {
            if (extension == ".gltf")
                throw new NotSupportedException(
                    "Only .glb (binary glTF) is supported inside asset packs - re-export the model as .glb.");
            using var stream = new MemoryStream(Assets.ReadBytes(path));
            root = ModelRoot.ReadGLB(stream);
        }
        else
        {
            root = ModelRoot.Load(Assets.Resolve(path));
        }

        var model = new Model { SourcePath = path };
        model.Import(root);
        Log.Info($"Model loaded: {Path.GetFileName(path)} " +
                 $"({model._meshes.Count} meshes, {model._skins.Count} skins, {model.Animations.Count} animations)");
        return model;
    }

    private void Import(ModelRoot root)
    {
        // Unique name per glTF node (animation targets and bones rely on this).
        var nodeNames = BuildUniqueNames(root);

        // Meshes + materials
        var materialCache = new Dictionary<int, Material>();
        var textureCache = new Dictionary<int, Texture2D>();
        var meshIndexByLogical = new Dictionary<int, int>();

        foreach (var gltfMesh in root.LogicalMeshes)
        {
            var primitives = new List<ModelPrimitive>();
            foreach (var primitive in gltfMesh.Primitives)
            {
                var converted = ConvertPrimitive(primitive, materialCache, textureCache);
                if (converted != null)
                    primitives.Add(converted);
            }
            meshIndexByLogical[gltfMesh.LogicalIndex] = _meshes.Count;
            _meshes.Add(primitives);
        }

        // Nodes: depth-first from the default scene so parents always come first.
        var nodeIndexByLogical = new Dictionary<int, int>();
        var scene = root.DefaultScene ?? root.LogicalScenes.FirstOrDefault();
        if (scene == null)
            return;

        void AddNode(GltfNode gltfNode, int parentIndex)
        {
            // Some exporters (Sketchfab, Blender with shear...) store a matrix instead of
            // translation/rotation/scale; decompose it so the SRT accessors are valid.
            var transform = gltfNode.LocalTransform.GetDecomposed();
            int meshIndex = gltfNode.Mesh != null ? meshIndexByLogical[gltfNode.Mesh.LogicalIndex] : -1;
            int skinIndex = gltfNode.Skin?.LogicalIndex ?? -1;

            int index = _nodes.Count;
            nodeIndexByLogical[gltfNode.LogicalIndex] = index;
            _nodes.Add(new ModelNodeData(
                nodeNames[gltfNode.LogicalIndex], parentIndex,
                transform.Translation, transform.Rotation, transform.Scale,
                meshIndex, skinIndex));

            foreach (var child in gltfNode.VisualChildren)
                AddNode(child, index);
        }

        foreach (var rootNode in scene.VisualChildren)
            AddNode(rootNode, -1);

        // Skins
        foreach (var skin in root.LogicalSkins)
        {
            var joints = new int[skin.JointsCount];
            var inverseBinds = new Matrix4x4[skin.JointsCount];
            for (int i = 0; i < skin.JointsCount; i++)
            {
                var (joint, inverseBind) = skin.GetJoint(i);
                joints[i] = nodeIndexByLogical.TryGetValue(joint.LogicalIndex, out int mapped) ? mapped : -1;
                inverseBinds[i] = inverseBind;
            }
            _skins.Add(new ModelSkinData(joints, inverseBinds));
        }

        // Animations
        foreach (var animation in root.LogicalAnimations)
            Animations.Add(ConvertAnimation(animation, nodeNames));
    }

    private static Dictionary<int, string> BuildUniqueNames(ModelRoot root)
    {
        var names = new Dictionary<int, string>();
        var used = new HashSet<string>();
        foreach (var node in root.LogicalNodes)
        {
            string baseName = string.IsNullOrWhiteSpace(node.Name) ? $"node{node.LogicalIndex}" : node.Name;
            string name = baseName;
            int suffix = 1;
            while (!used.Add(name))
                name = $"{baseName}_{suffix++}";
            names[node.LogicalIndex] = name;
        }
        return names;
    }

    private static ModelPrimitive? ConvertPrimitive(MeshPrimitive primitive,
        Dictionary<int, Material> materialCache, Dictionary<int, Texture2D> textureCache)
    {
        var positionAccessor = primitive.GetVertexAccessor("POSITION");
        if (positionAccessor == null)
            return null;

        var positions = positionAccessor.AsVector3Array();
        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
        var joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
        var weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();

        var vertices = new Vertex[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            vertices[i] = new Vertex(
                positions[i],
                normals != null && i < normals.Count ? normals[i] : Vector3.UnitY,
                uvs != null && i < uvs.Count ? uvs[i] : Vector2.Zero,
                colors != null && i < colors.Count ? colors[i] : Vector4.One);
        }

        var indexList = primitive.GetIndices();
        uint[] indices;
        if (indexList is { Count: > 0 })
        {
            indices = indexList.ToArray();
        }
        else
        {
            indices = new uint[positions.Count];
            for (uint i = 0; i < indices.Length; i++)
                indices[i] = i;
        }

        VertexSkin[]? skinData = null;
        if (joints != null && weights != null)
        {
            skinData = new VertexSkin[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var w = weights[i];
                float sum = w.X + w.Y + w.Z + w.W;
                if (sum > Mathf.Epsilon)
                    w /= sum;
                skinData[i] = new VertexSkin { Joints = joints[i], Weights = w };
            }
        }

        var material = ConvertMaterial(primitive.Material, materialCache, textureCache);

        // Normals missing entirely -> compute them.
        if (normals == null)
            Mesh.RecalculateNormals(vertices, indices);

        var mesh = new Mesh(vertices, indices, skinData, keepCpuPositions: true);
        return new ModelPrimitive(mesh, material);
    }

    private static Material ConvertMaterial(SharpGLTF.Schema2.Material? gltfMaterial,
        Dictionary<int, Material> materialCache, Dictionary<int, Texture2D> textureCache)
    {
        if (gltfMaterial == null)
            return new Material();

        if (materialCache.TryGetValue(gltfMaterial.LogicalIndex, out var cached))
            return cached;

        var material = new Material();

        var baseColor = gltfMaterial.FindChannel("BaseColor");
        if (baseColor.HasValue)
        {
            var c = baseColor.Value.Color;
            material.Albedo = new Color(c.X, c.Y, c.Z, c.W);

            var texture = baseColor.Value.Texture;
            if (texture?.PrimaryImage != null)
            {
                int imageIndex = texture.PrimaryImage.LogicalIndex;
                if (!textureCache.TryGetValue(imageIndex, out var tex))
                {
                    var content = texture.PrimaryImage.Content.Content.ToArray();
                    tex = Texture2D.FromEncodedBytes(content);
                    textureCache[imageIndex] = tex;
                }
                material.AlbedoTexture = tex;
            }
        }

        var emissive = gltfMaterial.FindChannel("Emissive");
        if (emissive.HasValue)
        {
            var e = emissive.Value.Color;
            material.Emission = new Color(e.X, e.Y, e.Z);
        }

        material.DoubleSided = gltfMaterial.DoubleSided;
        switch (gltfMaterial.Alpha)
        {
            case AlphaMode.BLEND:
                material.Transparent = true;
                break;
            case AlphaMode.MASK:
                material.AlphaCutout = gltfMaterial.AlphaCutoff;
                break;
        }

        materialCache[gltfMaterial.LogicalIndex] = material;
        return material;
    }

    private static AnimationClip ConvertAnimation(GltfAnimation animation, Dictionary<int, string> nodeNames)
    {
        var clip = new AnimationClip
        {
            Name = string.IsNullOrWhiteSpace(animation.Name) ? $"animation{animation.LogicalIndex}" : animation.Name,
            Duration = animation.Duration
        };

        foreach (var channel in animation.Channels)
        {
            if (channel.TargetNode == null)
                continue;
            string target = nodeNames[channel.TargetNode.LogicalIndex];

            switch (channel.TargetNodePath)
            {
                case PropertyPath.translation:
                {
                    var track = ConvertVectorTrack(channel.GetTranslationSampler(), target,
                        AnimationProperty.Position, animation.Duration);
                    if (track != null) clip.Tracks.Add(track);
                    break;
                }
                case PropertyPath.scale:
                {
                    var track = ConvertVectorTrack(channel.GetScaleSampler(), target,
                        AnimationProperty.Scale, animation.Duration);
                    if (track != null) clip.Tracks.Add(track);
                    break;
                }
                case PropertyPath.rotation:
                {
                    var track = ConvertRotationTrack(channel.GetRotationSampler(), target, animation.Duration);
                    if (track != null) clip.Tracks.Add(track);
                    break;
                }
            }
        }

        return clip;
    }

    private static AnimationTrack? ConvertVectorTrack(IAnimationSampler<Vector3>? sampler,
        string target, AnimationProperty property, float duration)
    {
        if (sampler == null)
            return null;

        float[] times;
        Vector3[] values;
        var interpolation = AnimationInterpolation.Linear;

        if (sampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE)
        {
            (times, values) = Resample(sampler.CreateCurveSampler(), duration);
        }
        else
        {
            var keys = sampler.GetLinearKeys().ToArray();
            if (keys.Length == 0)
                return null;
            times = keys.Select(k => k.Key).ToArray();
            values = keys.Select(k => k.Value).ToArray();
            if (sampler.InterpolationMode == AnimationInterpolationMode.STEP)
                interpolation = AnimationInterpolation.Step;
        }

        return new AnimationTrack
        {
            TargetName = target,
            Property = property,
            Times = times,
            VectorValues = values,
            Interpolation = interpolation
        };
    }

    private static AnimationTrack? ConvertRotationTrack(IAnimationSampler<Quaternion>? sampler,
        string target, float duration)
    {
        if (sampler == null)
            return null;

        float[] times;
        Quaternion[] values;
        var interpolation = AnimationInterpolation.Linear;

        if (sampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE)
        {
            (times, values) = Resample(sampler.CreateCurveSampler(), duration);
            for (int i = 0; i < values.Length; i++)
                values[i] = Quaternion.Normalize(values[i]);
        }
        else
        {
            var keys = sampler.GetLinearKeys().ToArray();
            if (keys.Length == 0)
                return null;
            times = keys.Select(k => k.Key).ToArray();
            values = keys.Select(k => k.Value).ToArray();
            if (sampler.InterpolationMode == AnimationInterpolationMode.STEP)
                interpolation = AnimationInterpolation.Step;
        }

        return new AnimationTrack
        {
            TargetName = target,
            Property = AnimationProperty.Rotation,
            Times = times,
            RotationValues = values,
            Interpolation = interpolation
        };
    }

    private static (float[], T[]) Resample<T>(SharpGLTF.Animations.ICurveSampler<T> curve, float duration)
    {
        const float fps = 30f;
        int sampleCount = Math.Max(2, (int)MathF.Ceiling(duration * fps) + 1);
        var times = new float[sampleCount];
        var values = new T[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = duration * i / (sampleCount - 1);
            times[i] = t;
            values[i] = curve.GetPoint(t);
        }
        return (times, values);
    }

    // ------------------------------------------------------------------
    // Instantiation
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a fresh node hierarchy for this model. Add the result to your scene.
    /// Models with animations get an <see cref="AnimationPlayer"/> (find it with
    /// <c>instance.FindNode&lt;AnimationPlayer&gt;()</c>); skinned models get a <see cref="Skeleton3D"/>.
    /// </summary>
    public Node3D Instantiate(string? name = null)
    {
        var root = new Node3D(name ?? Path.GetFileNameWithoutExtension(SourcePath));

        AnimationPlayer? player = null;
        if (Animations.Count > 0)
        {
            // First child => updates before the skeleton and attachments.
            player = root.AddChild(new AnimationPlayer("AnimationPlayer"));
            foreach (var clip in Animations)
                player.AddClip(clip);
        }

        if (HasSkins)
            InstantiateSkinned(root);
        else
            InstantiatePlain(root);

        player?.ResolveTargets();
        return root;
    }

    /// <summary>
    /// Instantiates only one node of the model (and its children), placed where it sits
    /// inside the full model (parent transforms are baked into the returned root). Lets
    /// a big file - a whole map - be split into independently editable pieces.
    /// Returns null when no node has that name. Skinned/animated models are not supported here.
    /// </summary>
    /// <summary>
    /// Builds one named piece of the model. By default the piece keeps the place it has
    /// inside the file (so a split model stays assembled); with
    /// <paramref name="recenter"/> it is moved onto the returned node's own origin -
    /// footprint centred, base at y = 0 - which is what you want when placing the pieces
    /// of a modular pack one by one.
    /// </summary>
    public Node3D? InstantiatePart(string nodeName, string? name = null, bool recenter = false)
    {
        int index = _nodes.FindIndex(n => string.Equals(n.Name, nodeName, StringComparison.Ordinal));
        if (index < 0)
            index = _nodes.FindIndex(n => string.Equals(n.Name, nodeName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return null;

        // Accumulated transform of the part's parents (so it lands where the full model puts it).
        var parentMatrix = Matrix4x4.Identity;
        for (int p = _nodes[index].ParentIndex; p >= 0; p = _nodes[p].ParentIndex)
        {
            var d = _nodes[p];
            parentMatrix *= Matrix4x4.CreateScale(d.Scale) * Matrix4x4.CreateFromQuaternion(d.Rotation) * Matrix4x4.CreateTranslation(d.Position);
        }

        var root = new Node3D(name ?? nodeName);
        var created = new Dictionary<int, Node3D>();
        for (int i = index; i < _nodes.Count; i++)
        {
            var data = _nodes[i];
            bool isRoot = i == index;
            if (!isRoot && !created.ContainsKey(data.ParentIndex))
                continue;   // not part of this subtree (nodes are stored parent-first)

            Node3D node = data.MeshIndex >= 0
                ? CreateMeshInstance(data.MeshIndex, data.Name)
                : new Node3D(data.Name);
            node.Position = data.Position;
            node.Rotation = data.Rotation;
            node.Scale = data.Scale;
            created[i] = node;

            if (isRoot)
            {
                // Bake the parents into the root so the part keeps its place in the model.
                var full = Matrix4x4.CreateScale(data.Scale) * Matrix4x4.CreateFromQuaternion(data.Rotation)
                           * Matrix4x4.CreateTranslation(data.Position) * parentMatrix;
                Matrix4x4.Decompose(full, out var scale, out var rotation, out var translation);
                node.Position = translation;
                node.Rotation = rotation;
                node.Scale = scale;
                root.AddChild(node);
            }
            else
            {
                created[data.ParentIndex].AddChild(node);
            }
        }

        if (recenter && root.ComputeVisualBounds() is { } bounds)
        {
            var shift = new Vector3((bounds.Min.X + bounds.Max.X) * 0.5f, bounds.Min.Y,
                                    (bounds.Min.Z + bounds.Max.Z) * 0.5f);
            foreach (var child in root.Children.OfType<Node3D>())
                child.Position -= shift;
        }
        return root;
    }

    /// <summary>Local bounds of one piece once it is re-centred (for placement previews).</summary>
    public (Vector3 Min, Vector3 Max)? PartBounds(string nodeName)
    {
        var part = InstantiatePart(nodeName, recenter: true);
        return part?.ComputeVisualBounds();
    }

    /// <summary>Names of the nodes that carry geometry (candidates for <see cref="InstantiatePart"/>).</summary>
    public IEnumerable<string> MeshNodeNames => _nodes.Where(n => n.MeshIndex >= 0).Select(n => n.Name);

    private void InstantiatePlain(Node3D root)
    {
        var created = new Node3D[_nodes.Count];
        for (int i = 0; i < _nodes.Count; i++)
        {
            var data = _nodes[i];
            Node3D node = data.MeshIndex >= 0
                ? CreateMeshInstance(data.MeshIndex, data.Name)
                : new Node3D(data.Name);

            node.Position = data.Position;
            node.Rotation = data.Rotation;
            node.Scale = data.Scale;

            created[i] = node;
            if (data.ParentIndex >= 0)
                created[data.ParentIndex].AddChild(node);
            else
                root.AddChild(node);
        }
    }

    private void InstantiateSkinned(Node3D root)
    {
        // The whole hierarchy becomes a skeleton so animations drive every node.
        var skeleton = root.AddChild(new Skeleton3D("Skeleton"));
        for (int i = 0; i < _nodes.Count; i++)
        {
            var data = _nodes[i];
            skeleton.AddBone(data.Name, data.ParentIndex,
                data.Position, data.Rotation, data.Scale, Matrix4x4.Identity);
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            var data = _nodes[i];
            if (data.MeshIndex < 0)
                continue;

            if (data.SkinIndex >= 0 && data.SkinIndex < _skins.Count)
            {
                var skin = _skins[data.SkinIndex];
                var binding = new SkinBinding(skeleton, skin.JointNodeIndices, skin.InverseBinds);
                var instance = CreateMeshInstance(data.MeshIndex, data.Name);
                instance.Skin = binding;
                skeleton.AddChild(instance);
            }
            else
            {
                // Rigid mesh inside an animated hierarchy: follow its bone.
                var attachment = skeleton.AddChild(new BoneAttachment3D(data.Name, $"{data.Name}_Attachment"));
                attachment.Skeleton = skeleton;
                attachment.AddChild(CreateMeshInstance(data.MeshIndex, data.Name));
            }
        }
    }

    private MeshInstance3D CreateMeshInstance(int meshIndex, string name)
    {
        var instance = new MeshInstance3D(name);
        foreach (var primitive in _meshes[meshIndex])
            instance.Surfaces.Add(new Surface(primitive.Mesh, primitive.Material));
        return instance;
    }
}
