using System.Numerics;
using Ducz.Audio;
using Ducz.Physics;
using Ducz.Rendering;

namespace Ducz.Serialization;

/// <summary>
/// Turns a <see cref="SceneDocument"/> (usually loaded from JSON) into a live node tree.
///
/// <code>
/// var game = new Game();
/// game.Run(() => SceneLoader.LoadScene("Assets/level.json"));
/// </code>
///
/// Behavior/scripting still lives in C#: load the scene, then find nodes by name
/// (<c>scene.FindNode("Door")</c>) and drive them from your own code.
/// </summary>
public static class SceneLoader
{
    /// <summary>Loads a JSON scene file and instantiates it.</summary>
    public static Node3D LoadScene(string path) => Instantiate(SceneDocument.Load(path));

    /// <summary>Instantiates a document: applies environment + input, builds all nodes, wires cameras.</summary>
    public static Node3D Instantiate(SceneDocument document)
    {
        ApplyEnvironment(document.Environment);
        RegisterInput(document.Input);

        var context = new BuildContext(document);
        var root = new Node3D(document.Name);

        foreach (var def in document.Nodes)
        {
            var node = BuildNode(def, context);
            if (node != null)
                root.AddChild(node);
        }

        ResolveCameraTargets(root, context);
        ActivateFallbackCamera(root, context);
        return root;
    }

    /// <summary>
    /// A scene whose camera forgot <c>"current": true</c> would render from whatever camera the
    /// host happens to have active - a confusing failure in a hand-written or generated scene.
    /// If nothing claimed the view, the first camera in the document takes it.
    /// </summary>
    private static void ActivateFallbackCamera(Node3D root, BuildContext context)
    {
        if (context.AnyCameraCurrent)
            return;
        var camera = root.FindNode<Camera3D>();
        if (camera == null)
            return;
        camera.MakeCurrent();
        Log.Warning($"SceneLoader: no camera had \"current\": true - activating \"{camera.Name}\".");
    }

    /// <summary>
    /// Builds a single node definition (with children) using the document for
    /// material lookups. Used by tools like the scene editor.
    /// </summary>
    public static Node3D? InstantiateNode(SceneDocument document, NodeDef def)
    {
        var context = new BuildContext(document);
        var node = BuildNode(def, context);
        if (node != null)
            ResolveCameraTargets(node, context);
        return node;
    }

    // ------------------------------------------------------------------
    // Context
    // ------------------------------------------------------------------

    private sealed class BuildContext
    {
        public readonly SceneDocument Document;
        public readonly Dictionary<string, Material> MaterialCache = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<(ThirdPersonCamera Camera, string TargetName)> PendingTargets = new();

        /// <summary>Set when any camera definition asked to be the current one.</summary>
        public bool AnyCameraCurrent;

        public BuildContext(SceneDocument document) => Document = document;
    }

    // ------------------------------------------------------------------
    // Environment & input
    // ------------------------------------------------------------------

    /// <summary>Applies environment settings (sky, ambient, fog) to the renderer. Public so tools can preview them.</summary>
    public static void ApplyEnvironment(EnvironmentDef? def)
    {
        if (def == null)
            return;

        var env = Engine.Renderer.Environment;
        env.Background = def.Background.Equals("solidColor", StringComparison.OrdinalIgnoreCase)
            ? BackgroundMode.SolidColor
            : BackgroundMode.ProceduralSky;

        if (def.ClearColor != null) env.ClearColor = ParseColor(def.ClearColor);
        if (def.SkyTop != null) env.SkyTopColor = ParseColor(def.SkyTop);
        if (def.SkyHorizon != null) env.SkyHorizonColor = ParseColor(def.SkyHorizon);
        if (def.SkyGround != null) env.SkyGroundColor = ParseColor(def.SkyGround);
        env.SkySunEnabled = def.SunDisk;

        if (def.AmbientColor != null) env.AmbientColor = ParseColor(def.AmbientColor);
        env.AmbientIntensity = def.AmbientIntensity;

        if (def.Fog != null)
        {
            env.FogEnabled = def.Fog.Enabled;
            if (def.Fog.Color != null) env.FogColor = ParseColor(def.Fog.Color);
            env.FogStart = def.Fog.Start;
            env.FogEnd = def.Fog.End;
        }
        else
        {
            env.FogEnabled = false;
        }
    }

    private static void RegisterInput(InputDef? def)
    {
        if (def == null || def.DefaultMovement)
            InputMap.AddDefaultMovementActions();

        if (def?.Actions == null)
            return;

        foreach (var (action, bindings) in def.Actions)
        {
            foreach (var binding in bindings)
            {
                if (binding.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase))
                {
                    var buttonName = binding[5..];
                    if (Enum.TryParse<MouseButton>(buttonName, true, out var button))
                        InputMap.AddAction(action, button);
                    else
                        Log.Warning($"SceneLoader: unknown mouse binding \"{binding}\" for action \"{action}\".");
                }
                else if (Enum.TryParse<Key>(binding, true, out var key))
                {
                    InputMap.AddAction(action, key);
                }
                else
                {
                    Log.Warning($"SceneLoader: unknown key \"{binding}\" for action \"{action}\".");
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Node building
    // ------------------------------------------------------------------

    private static Node3D? BuildNode(NodeDef def, BuildContext context)
    {
        Node3D? node;
        try
        {
            node = CreateByType(def, context);
        }
        catch (Exception ex)
        {
            Log.Error($"SceneLoader: failed to build node \"{def.Name ?? def.Type}\": {ex.Message}");
            return null;
        }

        if (node == null)
            return null;

        if (def.Name != null)
            node.Name = def.Name;
        if (def.Position is { Length: >= 3 })
            node.Position = ToVector3(def.Position);
        if (def.RotationDegrees is { Length: >= 3 })
            node.RotationDegrees = ToVector3(def.RotationDegrees);
        if (def.Scale is { Length: >= 3 })
            node.Scale = ToVector3(def.Scale);
        node.Visible = def.Visible;

        if (def.Groups != null)
            foreach (var group in def.Groups)
                node.AddToGroup(group);

        if (def.Children != null)
        {
            foreach (var childDef in def.Children)
            {
                var child = BuildNode(childDef, context);
                if (child != null)
                    node.AddChild(child);
            }
        }

        return node;
    }

    private static Node3D? CreateByType(NodeDef def, BuildContext context)
    {
        var node = CreateByTypeCore(def, context);

        // "collider": { "shape": "none" } disables collision on any node type,
        // including prefabs that build their own colliders (floor, wall, crate...).
        if (node != null && def.Collider?.Shape.Equals("none", StringComparison.OrdinalIgnoreCase) == true)
            StripColliders(node);

        return node;
    }

    /// <summary>Removes every collision shape under a node (visuals stay).</summary>
    private static void StripColliders(Node3D root)
    {
        if (root is PhysicsBody3D selfBody)
            selfBody.Shape = null;
        foreach (var descendant in root.Descendants())
            if (descendant is PhysicsBody3D body)
                body.Shape = null;
    }

    private static Node3D? CreateByTypeCore(NodeDef def, BuildContext context)
    {
        switch (def.Type.ToLowerInvariant())
        {
            case "node" or "group":
                return new Node3D();

            case "mesh":
                return CreateMeshInstance(def, context);

            case "static":
            {
                var body = new StaticBody3D();
                AttachMeshAndCollider(body, def, context);
                return body;
            }

            case "rigid":
            {
                var body = new RigidBody3D
                {
                    Mass = def.Mass,
                    Restitution = def.Restitution,
                    Friction = def.Friction
                };
                AttachMeshAndCollider(body, def, context);
                return body;
            }

            case "area":
            {
                var area = new Area3D { Shape = BuildCollider(def.Collider, def.Mesh, def.WorldUv) };
                if (def.Mesh != null)
                    area.AddChild(CreateMeshInstance(def, context));
                return area;
            }

            case "floor":
            {
                // Wrapped so the JSON position marks the floor's top surface
                // (the prefab keeps its internal downward offset).
                var size = def.Size ?? new[] { 20f, 20f };
                var wrapper = new Node3D("Floor");
                wrapper.AddChild(Prefabs.Floor(size[0], size.Length > 1 ? size[1] : size[0],
                    ResolveMaterial(def.Material, context), worldUv: def.WorldUv));
                return wrapper;
            }

            case "wall":
            {
                var size = def.Size ?? new[] { 4f, 3f, 0.3f };
                return Prefabs.Wall(size[0], size.Length > 1 ? size[1] : 3f,
                    ResolveMaterial(def.Material, context),
                    size.Length > 2 ? size[2] : 0.3f, def.WorldUv);
            }

            case "ramp":
            {
                // Wrapped so the JSON position marks the ramp center at its low-end height.
                var size = def.Size ?? new[] { 2f, 1f, 3f };
                var wrapper = new Node3D("Ramp");
                wrapper.AddChild(Prefabs.Ramp(size[0], size.Length > 1 ? size[1] : 1f,
                    size.Length > 2 ? size[2] : 3f, ResolveMaterial(def.Material, context), def.WorldUv));
                return wrapper;
            }

            case "crate":
            {
                float size = def.Size is { Length: > 0 } ? def.Size[0] : 1f;
                var crate = Prefabs.Crate(size, ResolveMaterial(def.Material, context), def.Mass, def.WorldUv);
                crate.Restitution = def.Restitution;
                crate.Friction = def.Friction;
                return crate;
            }

            case "terrain":
                return CreateTerrain(def, context);

            case "model":
            {
                if (def.Path == null)
                {
                    Log.Warning("SceneLoader: model node without a path.");
                    return new Node3D();
                }
                var model = Assets.LoadModel(def.Path);
                Node3D instance;
                if (def.SubNode != null)
                {
                    bool recenter = string.Equals(def.SubNodePivot, "base", StringComparison.OrdinalIgnoreCase);
                    var part = model.InstantiatePart(def.SubNode, recenter: recenter);
                    if (part == null)
                    {
                        Log.Warning($"SceneLoader: node \"{def.SubNode}\" not found in {def.Path} - using the whole model.");
                        instance = model.Instantiate();
                    }
                    else
                    {
                        instance = part;
                    }
                }
                else
                {
                    instance = model.Instantiate();
                }
                if (def.Animation != null)
                    instance.FindNode<AnimationPlayer>()?.Play(def.Animation);

                // Optional material override: applies to every surface of the model.
                // Useful when the file has no usable material/texture references.
                if (def.Material != null)
                {
                    var overrideMaterial = ResolveMaterial(def.Material, context);
                    if (overrideMaterial != null)
                        ApplyMaterialOverride(instance, overrideMaterial);
                }

                // Optional collider: "auto" wraps the model in a StaticBody3D sized
                // to its visual bounds (great for imported props like houses/trees).
                bool wantsCollider = def.Collider != null &&
                    !def.Collider.Shape.Equals("none", StringComparison.OrdinalIgnoreCase);
                if (!wantsCollider)
                    return instance;

                var wrapper = new Node3D(instance.Name);
                wrapper.AddChild(instance);

                CollisionShape? shape = null;
                var bodyPosition = Vector3.Zero;
                bool meshBody = false;
                string colliderShape = def.Collider!.Shape.ToLowerInvariant();

                if (colliderShape is "auto" or "mesh")
                {
                    // Exact triangle-mesh collider: characters walk on the model's floors and
                    // hit its walls. Falls back to a bounds box when the model kept no CPU geometry.
                    shape = MeshShape.FromNode(instance);
                    meshBody = shape != null;
                    if (shape == null && colliderShape == "mesh")
                        Log.Warning($"SceneLoader: model {def.Path} has no CPU geometry for a mesh collider - using its bounds.");
                }

                if (shape == null && (colliderShape is "auto" or "mesh" or "box") && def.Collider.Size == null)
                {
                    var bounds = wrapper.ComputeVisualBounds();
                    if (bounds == null)
                        return wrapper;
                    var (min, max) = bounds.Value;
                    shape = BoxShape.FromSize(Vector3.Max(max - min, new Vector3(0.05f)));
                    bodyPosition = (min + max) * 0.5f;
                }
                else if (shape == null)
                {
                    shape = BuildCollider(def.Collider, null);
                }

                if (shape != null)
                {
                    var body = new StaticBody3D("ModelCollider")
                    {
                        Shape = shape,
                        Position = bodyPosition,
                        CollisionLayer = def.Collider.Layer,
                        CollisionMask = def.Collider.Mask,
                        // Mesh shapes use the full global matrix (wrapper scale included); the
                        // primitive shapes only see their own node's scale, so they carry it.
                        Scale = !meshBody && def.Scale is { Length: >= 3 } ? ToVector3(def.Scale) : Vector3.One
                    };
                    wrapper.AddChild(body);
                }
                return wrapper;
            }

            case "player":
            {
                var player = new PlayerController3D
                {
                    MoveSpeed = def.MoveSpeed,
                    JumpSpeed = def.JumpSpeed,
                    Gravity = def.Gravity
                };
                if (def.Color != null)
                    player.VisualColor = ParseColor(def.Color);
                if (def.Visual is { Path: not null })
                    AttachPlayerVisual(player, def.Visual);
                return player;
            }

            case "spawn":
            {
                var marker = new Node3D("SpawnPoint");
                marker.AddToGroup("spawn");
                return marker;
            }

            case "camera":
            {
                var camera = new Camera3D { FovDegrees = def.Fov, Near = def.Near, Far = def.Far };
                if (def.Current)
                {
                    camera.MakeCurrent();
                    context.AnyCameraCurrent = true;
                }
                return camera;
            }

            case "flycamera":
            {
                var camera = new FlyCamera { FovDegrees = def.Fov, Near = def.Near, Far = def.Far };
                if (def.Current)
                {
                    camera.MakeCurrent();
                    context.AnyCameraCurrent = true;
                }
                return camera;
            }

            case "thirdpersoncamera":
            {
                var camera = new ThirdPersonCamera
                {
                    FovDegrees = def.Fov,
                    Near = def.Near,
                    Far = def.Far,
                    Distance = def.Distance,
                    TargetHeight = def.TargetHeight,
                    Sensitivity = def.Sensitivity,
                    Smoothing = def.Smoothing
                };
                if (def.Target != null)
                    context.PendingTargets.Add((camera, def.Target));
                if (def.Current)
                {
                    camera.MakeCurrent();
                    context.AnyCameraCurrent = true;
                }
                return camera;
            }

            case "directionallight":
            {
                var light = new DirectionalLight3D
                {
                    Color = def.Color != null ? ParseColor(def.Color) : Color.White,
                    Energy = def.Energy,
                    ShadowsEnabled = def.Shadows
                };
                return light;
            }

            case "pointlight":
                return new PointLight3D
                {
                    Color = def.Color != null ? ParseColor(def.Color) : Color.White,
                    Energy = def.Energy,
                    Range = def.Range
                };

            case "spotlight":
                return new SpotLight3D
                {
                    Color = def.Color != null ? ParseColor(def.Color) : Color.White,
                    Energy = def.Energy,
                    Range = def.Range,
                    AngleDegrees = def.Angle,
                    Softness = def.Softness
                };

            case "particles":
                return CreateParticles(def.Particles ?? new ParticlesDef());

            case "audio":
            case "audio3d":
            {
                if (def.Path == null)
                {
                    Log.Warning("SceneLoader: audio node without a path.");
                    return new Node3D();
                }
                var holder = new Node3D();
                AudioPlayer player = def.Type.Equals("audio3d", StringComparison.OrdinalIgnoreCase)
                    ? new AudioPlayer3D()
                    : new AudioPlayer();
                player.Clip = Assets.LoadAudio(def.Path);
                player.Loop = def.Loop;
                player.Volume = def.Volume;
                player.PlayOnReady = def.Autoplay;
                holder.AddChild(player);
                return holder;
            }

            default:
                Log.Warning($"SceneLoader: unknown node type \"{def.Type}\" - created a plain Node3D.");
                return new Node3D();
        }
    }

    private static MeshInstance3D CreateMeshInstance(NodeDef def, BuildContext context)
    {
        var meshDef = def.Mesh ?? new MeshDef();

        // Per-face materials: a box becomes six surfaces so each side can look different
        // (grass on top, dirt on the sides, a poster on one wall...).
        if (def.FaceMaterials is { Count: > 0 } faces && IsBoxLike(meshDef.Primitive))
        {
            var size = meshDef.Size;
            var parts = MeshFactory.BoxFacesData(
                size is { Length: > 0 } ? size[0] : 1f,
                size is { Length: > 1 } ? size[1] : (size is { Length: > 0 } ? size[0] : 1f),
                size is { Length: > 2 } ? size[2] : (size is { Length: > 0 } ? size[0] : 1f),
                def.WorldUv);

            var fallback = ResolveMaterial(def.Material, context);
            var instance = new MeshInstance3D();
            for (int i = 0; i < parts.Length; i++)
                instance.AddSurface(parts[i].ToMesh(), ResolveFaceMaterial(faces, (MeshFactory.BoxFace)i, context) ?? fallback);
            return instance;
        }

        var mesh = BuildMesh(meshDef, def.WorldUv);
        return new MeshInstance3D(mesh, ResolveMaterial(def.Material, context));
    }

    private static bool IsBoxLike(string primitive) =>
        primitive.Equals("box", StringComparison.OrdinalIgnoreCase) ||
        primitive.Equals("cube", StringComparison.OrdinalIgnoreCase);

    /// <summary>Picks the material for one box face from the "faceMaterials" dictionary.</summary>
    private static Material? ResolveFaceMaterial(Dictionary<string, MaterialRef> faces, MeshFactory.BoxFace face, BuildContext context)
    {
        // Most specific key wins: exact face > "sides" (vertical faces) > "all".
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
                return ResolveMaterial(reference, context);
        return null;
    }

    /// <summary>
    /// Builds a player's character visual from a definition: loads the model,
    /// applies scale/offset/orientation, loads animation clip files and wires
    /// everything into the controller's automatic locomotion.
    /// </summary>
    private static void AttachPlayerVisual(PlayerController3D player, PlayerVisualDef visual)
    {
        try
        {
            var instance = Assets.LoadModel(visual.Path!).Instantiate();
            instance.Scale = new Vector3(visual.Scale);
            if (visual.Offset is { Length: >= 3 })
                instance.Position = ToVector3(visual.Offset);
            if (visual.RotationDegrees is { Length: >= 3 })
                instance.RotationDegrees = ToVector3(visual.RotationDegrees);

            // The model may bring its own AnimationPlayer (glTF); otherwise create one.
            var animator = instance.FindNode<AnimationPlayer>();
            if (animator == null)
            {
                animator = new AnimationPlayer("AnimationPlayer");
                instance.AddChild(animator);
                animator.ResolveTargets();
            }

            if (visual.Animations != null)
            {
                foreach (var (clipName, file) in visual.Animations)
                {
                    try
                    {
                        var clips = Model.LoadAnimationClips(file, renameTo: clipName);
                        if (clips.Count > 0)
                            animator.AddClip(clips[0]);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"SceneLoader: failed to load animation \"{clipName}\" from {file}: {ex.Message}");
                    }
                }
            }

            player.ShowDefaultVisual = false;
            player.SetVisualModel(instance, animator);
        }
        catch (Exception ex)
        {
            Log.Error($"SceneLoader: failed to load player visual {visual.Path}: {ex.Message}");
        }
    }

    /// <summary>Replaces the material of every surface under a node (used by model overrides).</summary>
    private static void ApplyMaterialOverride(Node3D root, Material material)
    {
        if (root is MeshInstance3D self)
            foreach (var surface in self.Surfaces)
                surface.Material = material;

        foreach (var descendant in root.Descendants())
            if (descendant is MeshInstance3D instance)
                foreach (var surface in instance.Surfaces)
                    surface.Material = material;
    }

    private static void AttachMeshAndCollider(PhysicsBody3D body, NodeDef def, BuildContext context)
    {
        if (def.Mesh != null)
            body.AddChild(CreateMeshInstance(def, context));
        body.Shape = BuildCollider(def.Collider, def.Mesh, def.WorldUv);
        if (def.Collider != null)
        {
            body.CollisionLayer = def.Collider.Layer;
            body.CollisionMask = def.Collider.Mask;
        }
    }

    private static Terrain CreateTerrain(NodeDef def, BuildContext context)
    {
        var t = def.Terrain ?? new TerrainDef();
        Terrain terrain = t.Mode.ToLowerInvariant() switch
        {
            "heightmap" when t.Heightmap != null =>
                Terrain.FromHeightmap(t.Heightmap, t.SizeX, t.SizeZ, t.MaxHeight, t.Resolution),
            "hills" =>
                Terrain.FromFunction(Terrain.HillsFunction(t.Amplitude, t.Frequency), t.SizeX, t.SizeZ, t.Resolution),
            _ => Terrain.Flat(t.SizeX, t.SizeZ)
        };

        if (def.Material != null)
            terrain.Material = ResolveMaterial(def.Material, context) ?? terrain.Material;
        return terrain;
    }

    private static ParticleSystem3D CreateParticles(ParticlesDef def)
    {
        return new ParticleSystem3D
        {
            Amount = def.Amount,
            Lifetime = def.Lifetime,
            Speed = def.Speed,
            Direction = def.Direction is { Length: >= 3 } ? ToVector3(def.Direction) : Vector3.UnitY,
            SpreadDegrees = def.Spread,
            Gravity = def.Gravity is { Length: >= 3 } ? ToVector3(def.Gravity) : new Vector3(0, -3f, 0),
            StartSize = def.StartSize,
            EndSize = def.EndSize,
            StartColor = ParseColor(def.StartColor),
            EndColor = ParseColor(def.EndColor),
            Additive = def.Additive,
            Emitting = def.Emitting,
            Shape = def.Shape.ToLowerInvariant() switch
            {
                "sphere" => EmissionShape.Sphere,
                "box" => EmissionShape.Box,
                _ => EmissionShape.Point
            },
            ShapeRadius = def.ShapeRadius
        };
    }

    // ------------------------------------------------------------------
    // Meshes, colliders, materials
    // ------------------------------------------------------------------

    /// <summary>Builds a GPU mesh from a definition. Public so tools can preview meshes.</summary>
    public static Mesh BuildMesh(MeshDef def, bool worldUv = false) => BuildMeshData(def, worldUv).ToMesh();

    /// <summary>
    /// Builds the CPU geometry of a mesh definition (used by exporters and tools).
    /// <paramref name="worldUv"/> maps box/plane/cylinder UVs in meters (see <see cref="NodeDef.WorldUv"/>).
    /// </summary>
    public static MeshData BuildMeshData(MeshDef def, bool worldUv = false)
    {
        var size = def.Size;
        return def.Primitive.ToLowerInvariant() switch
        {
            "box" => MeshFactory.BoxData(
                size is { Length: > 0 } ? size[0] : 1f,
                size is { Length: > 1 } ? size[1] : 1f,
                size is { Length: > 2 } ? size[2] : 1f, worldUv),
            "sphere" => MeshFactory.SphereData(def.Radius,
                def.Segments > 0 ? def.Segments : 24,
                def.Segments > 0 ? def.Segments + 8 : 32),
            "plane" => MeshFactory.PlaneData(
                size is { Length: > 0 } ? size[0] : 10f,
                size is { Length: > 1 } ? size[1] : 10f,
                1, def.UvTiling, worldUv),
            "quad" => MeshFactory.QuadData(
                size is { Length: > 0 } ? size[0] : 1f,
                size is { Length: > 1 } ? size[1] : 1f),
            "cylinder" => MeshFactory.CylinderData(def.Radius, def.Height,
                def.Segments > 0 ? def.Segments : 32, worldUv),
            "capsule" => MeshFactory.CapsuleData(def.Radius, def.Height),
            "cone" => MeshFactory.ConeData(def.Radius, def.Height,
                def.Segments > 0 ? def.Segments : 32),
            "torus" => MeshFactory.TorusData(def.Radius, def.Thickness),

            // ---- building shapes ----
            "wedge" or "ramp" => MeshFactory.WedgeData(S(size, 0, 2f), S(size, 1, 1f), S(size, 2, 3f), worldUv),
            "roofgable" => MeshFactory.RoofGableData(S(size, 0, 6f), S(size, 1, 2f), S(size, 2, 8f), def.Overhang, worldUv),
            "roofhip" => MeshFactory.RoofHipData(S(size, 0, 6f), S(size, 1, 2f), S(size, 2, 8f),
                def.RidgeLength >= 0f ? def.RidgeLength : S(size, 0, 6f) * 0.5f, def.Overhang, worldUv),
            "roofshed" => MeshFactory.RoofShedData(S(size, 0, 6f), S(size, 1, 1.5f), S(size, 2, 6f), def.Thickness, worldUv),
            "stairs" => MeshFactory.StairsData(S(size, 0, 2f), S(size, 1, 2f), S(size, 2, 3f),
                def.Steps > 0 ? def.Steps : 8, def.SolidSide, worldUv),
            "arch" => MeshFactory.ArchData(S(size, 0, 4f), S(size, 1, 4f), def.Thickness <= 0.15f ? 0.4f : def.Thickness,
                def.OpeningWidth >= 0f ? def.OpeningWidth : S(size, 0, 4f) * 0.5f,
                def.OpeningHeight >= 0f ? def.OpeningHeight : S(size, 1, 4f) * 0.5f,
                def.Segments > 0 ? def.Segments : 16, worldUv),
            "curvedwall" => MeshFactory.CurvedWallData(def.Radius <= 0.5f ? 4f : def.Radius, Given(size, 1, def.Height),
                Given(size, 2, def.Thickness <= 0.15f ? 0.3f : def.Thickness),
                def.ArcDegrees > 0f ? def.ArcDegrees : 90f,
                def.Segments > 0 ? def.Segments : 16, worldUv),
            "tube" => MeshFactory.TubeData(Half(size, 0, def.Radius <= 0f ? 1f : def.Radius), Given(size, 1, def.Height),
                Given(size, 2, def.Thickness), def.Segments > 0 ? def.Segments : 24, worldUv),
            "prism" => MeshFactory.PrismData(Half(size, 0, def.Radius), Given(size, 1, def.Height),
                def.Sides > 0 ? def.Sides : 6, worldUv),
            "pyramid" => MeshFactory.PyramidData(S(size, 0, 1f), S(size, 1, 1f), S(size, 2, 1f), worldUv),
            "roundedbox" => MeshFactory.RoundedBoxData(S(size, 0, 1f), S(size, 1, 1f), S(size, 2, 1f),
                def.Bevel > 0f ? def.Bevel : 0.08f, worldUv),

            "polygon" => MeshFactory.PolygonData(ToFootprint(def.Points), S(size, 1, def.Height), worldUv),

            _ => MeshFactory.CubeData(size is { Length: > 0 } ? size[0] : 1f, worldUv)
        };

        static float S(float[]? size, int index, float fallback) =>
            size is { } s && s.Length > index ? s[index] : fallback;

        // Round shapes (tube, prism, curved wall) are described by radius/height/thickness, but a
        // hand-written scene may give the bounding "size" instead - honour it when it is filled in.
        static float Given(float[]? size, int index, float fallback) =>
            size is { } s && s.Length > index && s[index] > 0f ? s[index] : fallback;

        static float Half(float[]? size, int index, float fallback) =>
            size is { } s && s.Length > index && s[index] > 0f ? s[index] * 0.5f : fallback;
    }

    private static CollisionShape? BuildCollider(ColliderDef? collider, MeshDef? mesh, bool worldUv = false)
    {
        if (collider == null || collider.Shape.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return AutoCollider(mesh, collider, worldUv);
        if (collider.Shape.Equals("mesh", StringComparison.OrdinalIgnoreCase) && mesh != null)
        {
            try { return Physics.MeshShape.FromMeshData(BuildMeshData(mesh, worldUv)); }
            catch (Exception ex) { Log.Warning($"SceneLoader: mesh collider failed ({ex.Message}); using a box."); }
        }

        return collider.Shape.ToLowerInvariant() switch
        {
            "none" => null,
            "sphere" => new SphereShape(collider.Radius),
            "capsule" => new CapsuleShape(collider.Radius, collider.Height),
            _ => BoxShape.FromSize(collider.Size is { Length: >= 3 }
                ? ToVector3(collider.Size)
                : Vector3.One)
        };
    }

    /// <summary>Shapes whose silhouette a box cannot represent - they get an exact triangle collider.</summary>
    /// <summary>Flat [x, z, x, z...] pairs into footprint points.</summary>
    private static Vector2[] ToFootprint(float[]? points)
    {
        if (points is not { Length: >= 6 })
            return new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
        var result = new Vector2[points.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = new Vector2(points[i * 2], points[i * 2 + 1]);
        return result;
    }

    private static readonly HashSet<string> MeshColliderPrimitives = new(StringComparer.OrdinalIgnoreCase)
    {
        "wedge", "ramp", "stairs", "roofgable", "roofhip", "roofshed", "arch", "curvedwall", "tube", "prism",
        "pyramid", "polygon"
    };

    private static CollisionShape? AutoCollider(MeshDef? mesh, ColliderDef? _, bool worldUv = false)
    {
        if (mesh == null)
            return BoxShape.FromSize(Vector3.One);

        // Building shapes (ramps, stairs, arches, curves...) collide with their real geometry:
        // walking up a staircase or through an arch works exactly as it looks.
        if (MeshColliderPrimitives.Contains(mesh.Primitive))
        {
            try
            {
                return Physics.MeshShape.FromMeshData(BuildMeshData(mesh, worldUv));
            }
            catch (Exception ex)
            {
                Log.Warning($"SceneLoader: mesh collider for \"{mesh.Primitive}\" failed ({ex.Message}); using a box.");
            }
        }

        var size = mesh.Size;
        return mesh.Primitive.ToLowerInvariant() switch
        {
            "sphere" => new SphereShape(mesh.Radius),
            "capsule" => new CapsuleShape(mesh.Radius, mesh.Height),
            "cylinder" => BoxShape.FromSize(new Vector3(mesh.Radius * 2f, mesh.Height, mesh.Radius * 2f)),
            "cone" => BoxShape.FromSize(new Vector3(mesh.Radius * 2f, mesh.Height, mesh.Radius * 2f)),
            "plane" => BoxShape.FromSize(new Vector3(
                size is { Length: > 0 } ? size[0] : 10f, 0.1f,
                size is { Length: > 1 } ? size[1] : 10f)),
            "box" => BoxShape.FromSize(new Vector3(
                size is { Length: > 0 } ? size[0] : 1f,
                size is { Length: > 1 } ? size[1] : 1f,
                size is { Length: > 2 } ? size[2] : 1f)),
            _ => BoxShape.FromSize(new Vector3(size is { Length: > 0 } ? size[0] : 1f))
        };
    }

    private static Material? ResolveMaterial(MaterialRef? reference, BuildContext context)
    {
        if (reference == null)
            return null;

        if (reference.Inline != null)
            return BuildMaterial(reference.Inline);

        if (reference.Reference == null)
            return null;

        if (context.MaterialCache.TryGetValue(reference.Reference, out var cached))
            return cached;

        if (!context.Document.Materials.TryGetValue(reference.Reference, out var def))
        {
            Log.Warning($"SceneLoader: material \"{reference.Reference}\" not found.");
            return null;
        }

        var material = BuildMaterial(def);
        context.MaterialCache[reference.Reference] = material;
        return material;
    }

    private static readonly string[] NormalMapSuffixes = { "_normal", "_nrm", "_norm", "_n", "-normal", "normal" };
    private static readonly string[] RoughnessMapSuffixes = { "_roughness", "_rough", "_rgh", "-roughness", "roughness" };
    private static readonly string[] MapExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };

    /// <summary>
    /// Looks for a sibling texture of <paramref name="albedoPath"/> whose name adds one of the
    /// given suffixes (album.png -> album_normal.png). Returns null when there is none, so a
    /// texture pack that ships PBR maps "just works" without extra JSON.
    /// </summary>
    private static string? FindCompanionMap(string albedoPath, string[] suffixes)
    {
        try
        {
            string resolved = Assets.Resolve(albedoPath);
            string dir = Path.GetDirectoryName(resolved) ?? "";
            string name = Path.GetFileNameWithoutExtension(resolved);
            string original = Path.GetExtension(resolved);
            foreach (var suffix in suffixes)
            {
                foreach (var ext in new[] { original }.Concat(MapExtensions).Distinct())
                {
                    string candidate = Path.Combine(dir, name + suffix + ext);
                    if (Assets.FileExists(candidate))
                        return candidate;
                    // packs often use "albedo"/"basecolor" in the name: swap it for the map kind
                    foreach (var albedoWord in new[] { "_albedo", "_basecolor", "_diffuse", "_color", "_col" })
                    {
                        if (!name.EndsWith(albedoWord, StringComparison.OrdinalIgnoreCase))
                            continue;
                        string swapped = Path.Combine(dir, name[..^albedoWord.Length] + suffix + ext);
                        if (Assets.FileExists(swapped))
                            return swapped;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"SceneLoader: companion map lookup failed for {albedoPath}: {ex.Message}");
        }
        return null;
    }

    private static Texture2D? TryLoadTexture(string path, TextureFilter filter)
    {
        try
        {
            return Assets.LoadTexture(path, filter);
        }
        catch (Exception ex)
        {
            Log.Warning($"SceneLoader: could not load map \"{path}\": {ex.Message}");
            return null;
        }
    }

    /// <summary>Builds a material from a definition. Public so tools can preview materials.</summary>
    public static Material BuildMaterial(MaterialDef def)
    {
        var material = new Material
        {
            SpecularStrength = def.Specular,
            Shininess = def.Shininess,
            EmissionEnergy = def.EmissionEnergy,
            Transparent = def.Transparent,
            Unshaded = def.Unshaded,
            DoubleSided = def.DoubleSided,
            AlphaCutout = def.AlphaCutout,
            CastShadows = def.CastShadows,
            ReceiveShadows = def.ReceiveShadows
        };

        if (def.Albedo != null)
            material.Albedo = ParseColor(def.Albedo);
        if (def.Emission != null)
            material.Emission = ParseColor(def.Emission);
        if (def.UvScale is { Length: >= 2 })
            material.UvScale = new Vector2(def.UvScale[0], def.UvScale[1]);
        if (def.UvOffset is { Length: >= 2 })
            material.UvOffset = new Vector2(def.UvOffset[0], def.UvOffset[1]);

        var filter = def.Filter?.Equals("nearest", StringComparison.OrdinalIgnoreCase) == true
            ? TextureFilter.Nearest
            : TextureFilter.Linear;

        if (def.Texture != null)
        {
            material.AlbedoTexture = Assets.LoadTexture(def.Texture, filter);
            material.NormalStrength = def.NormalStrength;
            string? normalPath = def.NormalMap ?? (def.AutoMaps ? FindCompanionMap(def.Texture, NormalMapSuffixes) : null);
            if (normalPath != null)
                material.NormalMap = TryLoadTexture(normalPath, filter);
            string? roughPath = def.RoughnessMap ?? (def.AutoMaps ? FindCompanionMap(def.Texture, RoughnessMapSuffixes) : null);
            if (roughPath != null)
                material.RoughnessMap = TryLoadTexture(roughPath, filter);
        }
        else if (def.Checkerboard is { } checker)
            material.AlbedoTexture = Texture2D.CreateCheckerboard(checker.Size, checker.Cells,
                ParseColor(checker.ColorA), ParseColor(checker.ColorB));

        return material;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void ResolveCameraTargets(Node root, BuildContext context)
    {
        foreach (var (camera, targetName) in context.PendingTargets)
        {
            var target = root.Name == targetName
                ? root as Node3D
                : root.FindNode<Node3D>(targetName);

            if (target == null)
            {
                Log.Warning($"SceneLoader: camera target \"{targetName}\" not found.");
                continue;
            }

            camera.Target = target;
            if (target is PlayerController3D player)
                player.Camera = camera;
        }
    }

    private static Vector3 ToVector3(float[] values) => new(values[0], values[1], values[2]);

    private static Color ParseColor(string hex)
    {
        try
        {
            return Color.FromHex(hex);
        }
        catch
        {
            Log.Warning($"SceneLoader: invalid color \"{hex}\" - using white.");
            return Color.White;
        }
    }
}
