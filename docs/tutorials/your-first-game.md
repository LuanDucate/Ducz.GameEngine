# Tutorial: Your First Game

We'll build **Coin Runner** from scratch: a third-person character collecting coins on a floating island before the timer runs out. Along the way you'll touch every major engine system. Total code: ~150 lines. No asset files needed.

> Prerequisite: a project referencing the engine - see [Getting Started](../getting-started.md).

## Step 1 - Empty world

`Program.cs`:

```csharp
using System.Numerics;
using Ducz;
using Ducz.Physics;
using Ducz.Rendering;
using Ducz.UI;

var game = new Game(new GameSettings { Title = "Coin Runner", QuitOnEscape = true });
game.Run(() => new GameScene());

class GameScene : Node3D
{
    protected override void OnReady()
    {
        // Sun + soft fog for depth
        AddChild(new DirectionalLight3D().WithDirection(-45, 30));
        var env = Engine.Renderer.Environment;
        env.FogEnabled = true;
        env.FogStart = 30f;
        env.FogEnd = 90f;

        // The island: a big floor with collision
        AddChild(Prefabs.Floor(40f, 40f,
            Material.FromTexture(Texture2D.CreateCheckerboard(256, 16,
                Color.FromHex("#7ec850"), Color.FromHex("#5aa83e")))));

        // Debug camera so we can see something already
        AddChild(new FlyCamera()).Position = new Vector3(0, 8, 16);
    }
}
```

Run it (`dotnet run`). Fly around with WASD + right mouse. A green island under a procedural sky.

## Step 2 - The player

Add a `Player` class - a capsule that moves relative to the camera:

```csharp
class Player : CharacterBody3D
{
    public ThirdPersonCamera? Camera;

    public Player()
    {
        Shape = new CapsuleShape(0.4f, 1.7f);
    }

    protected override void OnReady()
    {
        InputMap.AddDefaultMovementActions();   // WASD/arrows, space, shift

        // Visual: capsule body + a little nose so we can see where we face
        AddChild(new MeshInstance3D(MeshFactory.Capsule(0.4f, 1.7f),
            Material.FromColor(Color.FromHex("#4f8fea"))));
        var nose = AddChild(new MeshInstance3D(MeshFactory.Box(0.15f, 0.15f, 0.3f),
            Material.FromColor(Color.White)));
        nose.Position = new Vector3(0, 0.4f, -0.45f);
    }

    protected override void OnPhysicsUpdate(float dt)
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var dir = Vector3.Zero;
        if (Camera != null)
            dir = Camera.PlanarForward * -input.Y + Camera.PlanarRight * input.X;

        var v = Velocity;
        v.X = dir.X * 7f;
        v.Z = dir.Z * 7f;
        v.Y -= 20f * dt;                          // gravity

        if (IsOnFloor && Input.IsActionPressed("jump"))
            v.Y = 8.5f;

        Velocity = v;
        MoveAndSlide();
    }
}
```

Replace the `FlyCamera` in `GameScene.OnReady` with:

```csharp
var player = AddChild(new Player());
player.Position = new Vector3(0, 2, 0);

var camera = AddChild(new ThirdPersonCamera { Target = player, Distance = 7f });
player.Camera = camera;
Input.SetMouseMode(MouseMode.Captured);
```

Run: you can now run and jump around the island with mouse-orbit camera.

## Step 3 - Coins

Coins are trigger areas (`Area3D`) with a spinning torus - the engine's `Prefabs.Pickup` does exactly that. Add to `GameScene`:

```csharp
public int Coins { get; private set; }
public const int TotalCoins = 10;
private AudioClip _coinSound = null!;

private void SpawnCoins()
{
    _coinSound = Audio.AudioClip.CreateSweep(600f, 1200f, 0.15f, Audio.WaveForm.Sine, 0.4f);

    var coinMaterial = new Material
    {
        Albedo = Color.Yellow,
        Emission = Color.Orange,
        EmissionEnergy = 0.4f
    };

    for (int i = 0; i < TotalCoins; i++)
    {
        var coin = AddChild(Prefabs.Pickup(MeshFactory.Torus(0.35f, 0.1f), coinMaterial));
        coin.Position = new Vector3(Rng.Range(-17f, 17f), 1f, Rng.Range(-17f, 17f));
        coin.BodyEntered += body =>
        {
            if (body is not Player) return;
            coin.QueueFree();
            Coins++;
            Engine.Audio.Play(_coinSound, pitch: 1f + Coins * 0.05f);
        };
    }
}
```

Call `SpawnCoins();` at the end of `OnReady`. Run - you can collect coins with a satisfying rising *bling*.

## Step 4 - HUD and timer

```csharp
private Label _coinLabel = null!;
private Label _timerLabel = null!;
private float _timeLeft = 45f;
private bool _finished;

private void BuildHud()
{
    var canvas = AddChild(new Canvas());
    _coinLabel = canvas.AddChild(new Label($"Coins: 0 / {TotalCoins}")
    {
        Anchor = Anchor.TopLeft, Position = new Vector2(16, 12), FontSize = 24
    });
    _timerLabel = canvas.AddChild(new Label("45")
    {
        Anchor = Anchor.TopCenter, Position = new Vector2(0, 12), FontSize = 32,
        Color = Color.Yellow
    });
}

protected override void OnUpdate(float dt)
{
    if (_finished) return;

    _timeLeft -= dt;
    _coinLabel.Text = $"Coins: {Coins} / {TotalCoins}";
    _timerLabel.Text = $"{MathF.Ceiling(MathF.Max(0, _timeLeft))}";

    if (Coins >= TotalCoins) Finish(won: true);
    else if (_timeLeft <= 0) Finish(won: false);
}
```

Call `BuildHud();` in `OnReady`.

## Step 5 - Win/lose screen

```csharp
private void Finish(bool won)
{
    _finished = true;
    Input.SetMouseMode(MouseMode.Visible);
    Engine.Audio.Play(won
        ? Audio.AudioClip.CreateSweep(400f, 1600f, 0.6f, Audio.WaveForm.Triangle, 0.4f)
        : Audio.AudioClip.CreateSweep(300f, 80f, 0.8f, Audio.WaveForm.Saw, 0.4f));

    var canvas = AddChild(new Canvas());
    canvas.AddChild(new Panel
    {
        Anchor = Anchor.FullRect,
        BackgroundColor = Color.Black.WithAlpha(0.55f)
    });

    var stack = canvas.AddChild(new VStack { Anchor = Anchor.Center, Spacing = 20 });
    stack.AddChild(new Label(won ? "YOU WIN!" : "TIME'S UP")
    {
        FontSize = 52,
        Color = won ? Color.FromHex("#7fd4ff") : Color.FromHex("#ff6b6b"),
        Anchor = Anchor.TopCenter
    });

    var again = stack.AddChild(new Button("Play Again")
    {
        Size = new Vector2(220, 50), Anchor = Anchor.TopCenter
    });
    again.Clicked += () =>
    {
        Input.SetMouseMode(MouseMode.Captured);
        Engine.Game.ChangeScene(new GameScene());
    };
}
```

## Step 6 - Juice (optional but fun)

Sparkles when collecting (put inside the `BodyEntered` handler):

```csharp
var sparkle = AddChild(new ParticleSystem3D
{
    OneShot = true, Amount = 30, Lifetime = 0.6f, Speed = 4f,
    SpreadDegrees = 180f, StartSize = 0.15f, EndSize = 0.02f,
    StartColor = Color.Yellow, EndColor = Color.Orange.WithAlpha(0f), Additive = true
});
sparkle.GlobalPosition = coin.GlobalPosition;
sparkle.EmitBurst(30);
Tree!.CreateTween().Wait(1f).Call(() => sparkle.QueueFree());
```

Some crates to jump on:

```csharp
for (int i = 0; i < 5; i++)
{
    var crate = AddChild(Prefabs.Crate(Rng.Range(0.8f, 1.5f)));
    crate.Position = new Vector3(Rng.Range(-15f, 15f), 4f, Rng.Range(-15f, 15f));
}
```

## You now know...

- Scenes as `Node3D` subclasses built in `OnReady`
- Character movement with `CharacterBody3D.MoveAndSlide`
- Camera rigs and camera-relative input
- Triggers (`Area3D`) via `Prefabs.Pickup`
- HUD + menus with `Canvas`, `Label`, `Button`, `VStack`
- Procedural audio and particles
- Scene restarting with `ChangeScene`

**Next:** give your game real graphics - [Importing Models & Animations](importing-models.md), or build the level visually with the [Map Builder](map-builder.md).
