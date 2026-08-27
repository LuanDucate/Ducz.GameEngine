# API Reference

A map of every public type, by namespace. All public members carry XML documentation - IntelliSense is the detailed reference.

## `Ducz` - core

| Type | Purpose |
| --- | --- |
| `Game` | Window + main loop host. `Run(sceneFactory)`, `ChangeScene`, `Quit`, `SetWindowTitle` |
| `GameSettings` | Startup config: title, size, vsync, MSAA, fullscreen, physics rate |
| `Engine` | Static access to subsystems: `Game`, `Tree`, `Renderer`, `Physics`, `Audio`, `WindowSize`, `Quit()` |
| `Node` | Base scene object: hierarchy, lifecycle, groups, `QueueFree`, `FindNode` |
| `Node3D` | Node with transform: `Position/Rotation/Scale`, global equivalents, `LookAt`, `Visible` |
| `SceneTree` | The running tree: `Root`, `CurrentScene`, `ChangeScene`, groups, `CreateTween` |
| `Input` | Keyboard/mouse state + action queries (`IsActionDown`, `GetVector`...); `ClipboardText` reads/writes the system clipboard |
| `InputMap` | Binds action names to keys/mouse buttons |
| `Key`, `MouseButton`, `MouseMode` | Input enums |
| `Time` | `DeltaTime`, `FixedDeltaTime`, `TotalTime`, `Scale`, `Fps` |
| `Mathf` | Math helpers: lerp/damp/clamp, angles, `LookRotation`, GL projections |
| `VectorExtensions` | `v.Flat()`, `q.Forward()`, matrix transform helpers |
| `Color` | Float RGBA color; hex/HSV/byte constructors, lerp, palette constants |
| `Rng` | Global random: ranges, chance, spheres, pick |
| `Log` | Leveled console logging, `OnMessage` hook |
| `Tween`, `Ease`, `Easing` | Sequential value animation with easing |
| `SaveSystem` | JSON save slots under AppData |
| `Assets` | Cached loading: textures, models, audio, fonts; `BasePath` (project), `EngineRoot` (shipped content), `Resolve` |
| `Model` | Imported glTF: `Load`, `Instantiate`, `Animations` |

## `Ducz` - scene nodes

| Type | Purpose |
| --- | --- |
| `Camera3D` | Perspective/ortho camera; `MakeCurrent`, `ScreenPointToRay`, `ScreenPointToGround`, `WorldToScreenPoint` |
| `MeshInstance3D` | Renders surfaces (mesh + material pairs); `Skin` for skinned meshes |
| `Surface` | One mesh + material pair |
| `DirectionalLight3D` | Sun light with shadow mapping (`WithDirection`, `ShadowOrthoSize`) |
| `PointLight3D`, `SpotLight3D` | Local lights (`Range`, `AngleDegrees`, `Softness`) |
| `ParticleSystem3D`, `EmissionShape` | CPU billboard particles; `EmitBurst`, color/size over life |
| `Skeleton3D`, `Bone` | Bone hierarchy for skinning; `FindBone`, `GetBoneGlobalPose` |
| `SkinBinding` | Joint remap + inverse binds connecting a mesh to a skeleton |
| `BoneAttachment3D` | Node that follows a bone (weapons, hats) |
| `AnimationPlayer` | Plays clips on bones/nodes; cross-fade, speed, events |
| `AnimationClip`, `AnimationTrack` | Keyframe data; `FromPositionKeys`/`FromRotationKeys` builders |
| `AnimationProperty`, `AnimationInterpolation` | Track enums |
| `Terrain` | Heightmap ground: mesh + heightfield collider; `GetHeight`, `GetNormal`; static `BuildMeshData`, `HeightmapSampler`, `HillsFunction` |
| `GridMap` | Block map from registered items; `SetCell`, `FillRegion`, `Rebuild` |
| `Prefabs` | One-line pieces: `Floor`, `Wall`, `Box`, `Ramp`, `Crate`, `Pickup` |
| `FlyCamera` | Free-flight debug camera |
| `ThirdPersonCamera` | Follow/orbit camera with collision; `PlanarForward/Right` |
| `TopDownCamera` | RTS/strategy camera: pan, zoom, rotate around a ground focus |
| `PlayerController3D` | Turnkey third-person player: camera-relative movement, jump, sprint, respawn |

## `Ducz.Rendering`

| Type | Purpose |
| --- | --- |
| `Renderer` | The frame pipeline; `Environment`, `SpriteBatch`, size/aspect |
| `GraphicsDevice` | GL state wrapper: blend/depth/cull, clear, stats, raw `GL` |
| `Environment`, `BackgroundMode` | Sky colors, ambient light, fog |
| `Mesh`, `Vertex`, `VertexSkin` | GPU geometry; `RecalculateNormals` |
| `MeshData` | CPU geometry (vertices + indices): `ToMesh`, `Transformed`, `Merge`, `ComputeBounds` - what exporters read |
| `MeshFactory` | Primitives (cube, box, sphere, plane, quad, cylinder, capsule, cone, torus) **and building shapes** (wedge, roofGable, roofHip, roofShed, stairs, arch, curvedWall, tube, prism, pyramid, roundedBox, BoxFacesData) - each as GPU `Mesh` (`Box`) or CPU `MeshData` (`BoxData`); `worldUv` option |
| `PngEncoder` | Minimal PNG writer for RGBA pixels (procedural textures in exports) |
| `Material` | Surface appearance: albedo, `NormalMap` + `NormalStrength`, `RoughnessMap`, specular, emission, transparency, UV scale/offset |
| `Texture2D`, `TextureFilter` | GPU textures; file/pixels/color/checkerboard factories; `CheckerboardPixels` |
| `Shader` | GLSL program wrapper with uniform cache (`FromSource`) |
| `SpriteBatch` | Batched 2D drawing (used by UI; available for overlays); `PushClip`/`PopClip` limit drawing to a rectangle |
| `DebugDraw` | Immediate lines: line/ray/box/sphere/axes/grid |
| `BlendMode` | Opaque / Alpha / Additive |

## `Ducz.Physics`

| Type | Purpose |
| --- | --- |
| `PhysicsWorld` | Simulation + queries: `Raycast`, `OverlapSphere`, `Gravity` |
| `RaycastHit` | Body, point, normal, distance |
| `CollisionShape` | Base of `SphereShape`, `CapsuleShape`, `BoxShape`, `HeightfieldShape`, `MeshShape` (static triangle mesh: `FromNode`, `FromMeshData`) |
| `PhysicsBody3D` | Base body: `Shape`, `CollisionLayer`, `CollisionMask` |
| `StaticBody3D` | Immovable collider |
| `RigidBody3D` | Simulated body: velocity, mass, restitution, `ApplyImpulse`, `BodyCollided` |
| `CharacterBody3D` | Controller: `Velocity`, `MoveAndSlide`, `IsOnFloor`, floor snap |
| `Area3D` | Trigger volume: `BodyEntered`/`BodyExited`, `OverlappingBodies` |
| `Contact` | Normal/depth/point of a collision |

## `Ducz.UI`

| Type | Purpose |
| --- | --- |
| `Canvas` | UI root: layout + mouse routing; `IsMouseOverUI` |
| `UINode` | Base element: `Anchor`, `Position`, `Size`, `ComputedRect`, `Draw` |
| `Anchor`, `HAlign`, `VAlign`, `Rect` | Layout primitives |
| `UITheme` | Font path/size, accent/panel/text colors, `GetFont(size)` |
| `Font` | Baked TTF atlas: `MeasureText`, used by labels and `SpriteBatch.DrawText` |
| `Panel`, `Label`, `ImageBox` | Static elements |
| `Button`, `CheckBox`, `TextBox` | Interactive elements with events |
| `ProgressBar` | 0..1 bar |
| `VStack`, `HStack` | Auto-layout containers |
| `ScrollPanel` | Panel whose contents scroll and are clipped to its edge - `HandleWheel(mouse, wheel)`, `Scroll`, `CanScroll` |

## `Ducz.Audio`

| Type | Purpose |
| --- | --- |
| `AudioEngine` | Device + listener; `Play`, `PlayAt`, `MasterVolume`, `Enabled` |
| `AudioClip` | Sound data: `FromWavFile`, `CreateTone`, `CreateSweep` |
| `WaveForm` | Sine, Square, Triangle, Saw, Noise |
| `AudioPlayer` | Non-positional player node (music/UI) |
| `AudioPlayer3D` | Positional player node (follows its Node3D ancestor) |

## `Ducz.Serialization`

| Type | Purpose |
| --- | --- |
| `SceneDocument` | JSON scene file: `Load`, `Save`, `FromJson`, `ToJson`, `Nodes`, `Materials` |
| `SceneLoader` | Instantiates documents: `LoadScene(path)`, `Instantiate(doc)`, `InstantiateNode`, `BuildMesh`, `BuildMeshData`, `BuildMaterial` |
| `DefaultMaterials` | `Create()` - the gridded starting palette used by new maps |
| `NodeDef` (incl. `FaceMaterials`, `WorldUv`), `MaterialDef` (incl. `NormalMap`, `RoughnessMap`, `AutoMaps`), `MeshDef` (incl. `Steps`, `ArcDegrees`, `Overhang`, `RidgeLength`, `Sides`, `Bevel`), `ColliderDef` | Data definitions (see [JSON Scenes](json-scenes.md)) |
| `EnvironmentDef`, `InputDef`, `TerrainDef`, `ParticlesDef`, `MaterialRef` | Supporting definitions |

## `Ducz.Export`

| Type | Purpose |
| --- | --- |
| `GlbExporter` | `Export(doc, path, options)` writes a scene document as binary glTF (.glb) - see [GLB Export](glb-export.md) |
| `GlbExportOptions` | `IncludeModels`, `IncludeLights`, `IncludeMarkers`, `GodotSuffixes`, `MergeByMaterial`, `IncludeHiddenNodes` |
| `GlbExportResult` | Counts + `Warnings` |

## `Ducz.AI`

| Type | Purpose |
| --- | --- |
| `StateMachine` | Named states with enter/update/exit callbacks |
| `NavGrid` | XZ walkability grid + A* `FindPath`; `BakeFromPhysics` |
| `Steering` | `Seek`, `Arrive`, `Flee`, `Wander`, `Separation` |
| `PathFollower` | Waypoint follower producing velocities |

## Conventions

- Math types are `System.Numerics` (`Vector2/3`, `Quaternion`, `Matrix4x4`)
- **-Z is forward, +Y is up, units are meters**, angles in API surface are degrees where named so, radians otherwise
- GPU resources (meshes, textures, fonts, clips) must be created after the window opens - i.e. inside node lifecycle callbacks or `Game.Initialized`
- Every node type is safe to subclass; override the `On...` lifecycle methods
