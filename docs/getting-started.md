# Getting Started

This guide takes you from nothing to a running Ducz Engine game in about five minutes.

## 1. Prerequisites

- **.NET 8 SDK** or newer - check with `dotnet --version`
- Any GPU with **OpenGL 3.3** support
- Windows, Linux or macOS

## 2. Create a project

```bash
dotnet new console -n MyGame
cd MyGame
dotnet add reference path/to/Ducz.GameEngine/src/Ducz.GameEngine
```

> If you prefer, add the engine project to your own solution with
> `dotnet sln add path/to/src/Ducz.GameEngine`.

## 3. Write your first scene

Replace `Program.cs` with:

```csharp
using System.Numerics;
using Ducz;
using Ducz.Rendering;

var game = new Game(new GameSettings
{
    Title = "My First Game",
    Width = 1280,
    Height = 720,
    QuitOnEscape = true    // Esc closes the window - handy while developing
});

game.Run(() => new MainScene());

class MainScene : Node3D
{
    private MeshInstance3D _cube = null!;

    protected override void OnReady()
    {
        // A camera. The first camera added becomes the active one.
        var camera = AddChild(new Camera3D());
        camera.Position = new Vector3(0, 2.5f, 5);
        camera.LookAt(new Vector3(0, 0.5f, 0));

        // A sun. Angles are pitch (down) and yaw.
        AddChild(new DirectionalLight3D().WithDirection(-50, 30));

        // A checkerboard floor.
        AddChild(new MeshInstance3D(
            MeshFactory.Plane(12, 12),
            Material.FromTexture(Texture2D.CreateCheckerboard())));

        // A red cube to admire.
        _cube = AddChild(new MeshInstance3D(
            MeshFactory.Cube(),
            Material.FromColor(Color.Red)));
        _cube.Position = new Vector3(0, 0.5f, 0);
    }

    protected override void OnUpdate(float dt)
    {
        _cube.RotateY(1.5f * dt);   // radians per second
    }
}
```

## 4. Run it

```bash
dotnet run
```

You should see a spinning red cube casting a real-time shadow onto a checkerboard floor, under a procedural gradient sky.

## 5. What just happened?

- `Game` owns the window and the main loop. `game.Run(...)` blocks until the game quits.
- Everything in the world is a **node**. Your scene is a `Node3D` subclass; you build it by adding children in `OnReady`.
- `OnUpdate(dt)` runs every frame; `dt` is the frame time in seconds.
- Rendering, lighting, shadows and the sky all come from the engine defaults - you can customize everything later.

## 6. Where to go next

- Learn the node system and input: **[Core Concepts](core-concepts.md)**
- Make something playable: **[Tutorial - Your First Game](tutorials/your-first-game.md)**
- Bring in 3D models and animations: **[Assets & Models](assets.md)**

## Troubleshooting

| Problem | Fix |
| --- | --- |
| Window opens then closes with a shader error | Your GPU/driver must support OpenGL 3.3. Update graphics drivers. |
| `No system font found` when creating UI | Set `UITheme.FontPath = "path/to/font.ttf";` before creating UI elements. |
| No sound | The engine logs a warning and continues if no audio device exists. Check the console output. |
| Textures/models not found | Relative paths resolve against `Assets.BasePath` (the executable folder by default). Either use absolute paths, or copy asset files to the output directory. |

To copy an asset folder to the output automatically, add this to your `.csproj`:

```xml
<ItemGroup>
  <None Include="Assets/**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```
