using System.Numerics;
using Ducz.Audio;
using Ducz.Physics;
using Ducz.Rendering;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Ducz;

/// <summary>
/// The game host: owns the window, the main loop and every engine subsystem.
///
/// Typical usage:
/// <code>
/// var game = new Game(new GameSettings { Title = "My Game" });
/// game.Run(() => new MainScene());
/// </code>
/// </summary>
public sealed class Game
{
    private readonly GameSettings _settings;
    private IWindow? _window;
    private Func<Node>? _sceneFactory;
    private float _physicsAccumulator;
    private int _fpsFrames;
    private float _fpsTimer;

    /// <summary>The scene tree (created before the window opens).</summary>
    public SceneTree Tree { get; }

    /// <summary>The renderer. Available after the window loads.</summary>
    public Renderer Renderer { get; private set; } = null!;

    /// <summary>The physics world.</summary>
    public PhysicsWorld Physics { get; }

    /// <summary>The audio engine (safe to use even when no audio device exists).</summary>
    public AudioEngine Audio { get; private set; } = null!;

    /// <summary>Current framebuffer size in pixels.</summary>
    public Vector2 WindowSize { get; private set; }

    /// <summary>Raised after all subsystems are initialized, right before the first scene loads.</summary>
    public event Action? Initialized;

    /// <summary>Raised when the user drags and drops files onto the game window.</summary>
    public event Action<string[]>? FileDropped;

    public Game(GameSettings? settings = null)
    {
        _settings = settings ?? new GameSettings();
        Time.FixedDeltaTime = 1f / Math.Max(1, _settings.PhysicsTicksPerSecond);

        Tree = new SceneTree();
        Physics = new PhysicsWorld();

        Engine.Bind(this);
    }

    /// <summary>Opens the window and runs the main loop with the given root scene. Blocks until the game quits.</summary>
    public void Run(Node scene) => Run(() => scene);

    /// <summary>Opens the window and runs the main loop. The factory is invoked once the engine is ready.</summary>
    public void Run(Func<Node> sceneFactory)
    {
        _sceneFactory = sceneFactory;

        var options = WindowOptions.Default with
        {
            Title = _settings.Title,
            Size = new Vector2D<int>(_settings.Width, _settings.Height),
            WindowBorder = _settings.Resizable ? WindowBorder.Resizable : WindowBorder.Fixed,
            WindowState = _settings.Fullscreen ? WindowState.Fullscreen : WindowState.Normal,
            VSync = _settings.VSync,
            Samples = Math.Max(0, _settings.Msaa),
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
            PreferredDepthBufferBits = 24,
            PreferredStencilBufferBits = 8
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += OnResize;
        _window.Closing += OnClosing;
        _window.FileDrop += paths => FileDropped?.Invoke(paths);

        Log.Info($"Ducz Engine starting: \"{_settings.Title}\" {_settings.Width}x{_settings.Height}");
        _window.Run();
        _window.Dispose();
    }

    /// <summary>Requests the game to close after the current frame.</summary>
    public void Quit() => _window?.Close();

    /// <summary>Changes the window title at runtime.</summary>
    public void SetWindowTitle(string title)
    {
        if (_window != null)
            _window.Title = title;
    }

    /// <summary>Shortcut for <see cref="SceneTree.ChangeScene"/>.</summary>
    public void ChangeScene(Node scene) => Tree.ChangeScene(scene);

    // ---- Window callbacks ----

    private void OnLoad()
    {
        var window = _window!;
        var gl = Silk.NET.OpenGL.GL.GetApi(window);
        var input = window.CreateInput();
        Input.Attach(input);

        ApplyWindowIcon(window);

        WindowSize = new Vector2(window.FramebufferSize.X, window.FramebufferSize.Y);
        Renderer = new Renderer(gl, (int)WindowSize.X, (int)WindowSize.Y);
        Audio = new AudioEngine(_settings.NoAudio);

        Log.Info($"OpenGL: {Renderer.Device.GlVersion} | {Renderer.Device.GlRenderer}");

        Initialized?.Invoke();

        var scene = _sceneFactory!();
        Tree.ChangeScene(scene);
    }

    private void ApplyWindowIcon(IWindow window)
    {
        if (string.IsNullOrEmpty(_settings.IconPath))
            return;
        try
        {
            string path = Assets.Resolve(_settings.IconPath);
            if (!File.Exists(path))
            {
                Log.Warning($"Window icon not found: {path}");
                return;
            }
            var image = StbImageSharp.ImageResult.FromMemory(File.ReadAllBytes(path), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            var raw = new Silk.NET.Core.RawImage(image.Width, image.Height, new Memory<byte>(image.Data));
            window.SetWindowIcon(ref raw);
        }
        catch (Exception ex)
        {
            Log.Warning($"Could not set the window icon: {ex.Message}");
        }
    }

    private void OnUpdate(double delta)
    {
        Input.NewFrame();
        Time.Advance((float)delta);

        if (_settings.QuitOnEscape && Input.IsKeyPressed(Key.Escape))
        {
            Quit();
            return;
        }

        // Fixed-step physics.
        _physicsAccumulator += Time.DeltaTime;
        int maxSteps = 5; // avoid spiral of death
        while (_physicsAccumulator >= Time.FixedDeltaTime && maxSteps-- > 0)
        {
            Tree.PhysicsUpdate(Time.FixedDeltaTime);
            Physics.Step(Time.FixedDeltaTime);
            _physicsAccumulator -= Time.FixedDeltaTime;
        }
        if (maxSteps <= 0)
            _physicsAccumulator = 0f;

        // Per-frame logic.
        Tree.Update(Time.DeltaTime);
        Audio.Update();
    }

    private void OnRender(double delta)
    {
        Time.FrameCount++;

        _fpsFrames++;
        _fpsTimer += (float)delta;
        if (_fpsTimer >= 1f)
        {
            Time.Fps = _fpsFrames / _fpsTimer;
            _fpsFrames = 0;
            _fpsTimer = 0f;
        }

        Renderer.RenderFrame(Tree);
    }

    private void OnResize(Vector2D<int> size)
    {
        WindowSize = new Vector2(size.X, size.Y);
        Renderer.Resize(size.X, size.Y);
    }

    private void OnClosing()
    {
        Audio?.Dispose();
        Log.Info("Ducz Engine shutting down.");
    }
}

/// <summary>
/// Global access point to the running <see cref="Game"/> and its subsystems.
/// Valid after the <see cref="Game"/> constructor runs.
/// </summary>
public static class Engine
{
    /// <summary>The running game instance.</summary>
    public static Game Game { get; private set; } = null!;

    /// <summary>The scene tree.</summary>
    public static SceneTree Tree => Game.Tree;

    /// <summary>The renderer (available once the window is open).</summary>
    public static Renderer Renderer => Game.Renderer;

    /// <summary>The physics world.</summary>
    public static PhysicsWorld Physics => Game.Physics;

    /// <summary>The audio engine.</summary>
    public static AudioEngine Audio => Game.Audio;

    /// <summary>Current window size in pixels.</summary>
    public static Vector2 WindowSize => Game.WindowSize;

    /// <summary>Quits the game.</summary>
    public static void Quit() => Game.Quit();

    internal static void Bind(Game game) => Game = game;
}
