using System.Numerics;
using Silk.NET.Input;

namespace Ducz;

/// <summary>
/// Global input state: keyboard, mouse and named actions.
/// Query it from any node's Update method.
/// </summary>
public static class Input
{
    // Events arrive from the window while the OS pump runs (before Update), so
    // they accumulate in "pending" buffers and are promoted once per frame.
    private static readonly HashSet<Key> _keysDown = new();
    private static readonly HashSet<Key> _keysPressed = new();
    private static readonly HashSet<Key> _keysReleased = new();
    private static readonly HashSet<Key> _keysPressedPending = new();
    private static readonly HashSet<Key> _keysReleasedPending = new();

    private static readonly HashSet<MouseButton> _mouseDown = new();
    private static readonly HashSet<MouseButton> _mousePressed = new();
    private static readonly HashSet<MouseButton> _mouseReleased = new();
    private static readonly HashSet<MouseButton> _mousePressedPending = new();
    private static readonly HashSet<MouseButton> _mouseReleasedPending = new();

    private static readonly List<char> _typedChars = new();
    private static readonly List<char> _typedCharsPending = new();

    private static IInputContext? _context;
    private static IMouse? _mouse;
    private static Vector2 _mousePosition;
    private static Vector2 _lastMousePosition;
    private static bool _firstMouseSample = true;

    /// <summary>Current mouse position in window pixels (origin: top-left).</summary>
    public static Vector2 MousePosition => _mousePosition;

    /// <summary>Mouse movement since last frame, in pixels.</summary>
    public static Vector2 MouseDelta { get; private set; }

    /// <summary>Scroll wheel movement this frame (Y is the common vertical wheel).</summary>
    public static Vector2 ScrollDelta { get; private set; }

    private static Vector2 _pendingScroll;

    /// <summary>Characters typed this frame (respects keyboard layout). Useful for text fields.</summary>
    public static IReadOnlyList<char> TypedCharacters => _typedChars;

    // ---- Keyboard ----

    /// <summary>True while the key is held down.</summary>
    public static bool IsKeyDown(Key key) => _keysDown.Contains(key);

    /// <summary>True only on the frame the key was pressed.</summary>
    public static bool IsKeyPressed(Key key) => _keysPressed.Contains(key);

    /// <summary>True only on the frame the key was released.</summary>
    public static bool IsKeyReleased(Key key) => _keysReleased.Contains(key);

    // ---- Mouse ----

    public static bool IsMouseButtonDown(MouseButton button) => _mouseDown.Contains(button);
    public static bool IsMouseButtonPressed(MouseButton button) => _mousePressed.Contains(button);
    public static bool IsMouseButtonReleased(MouseButton button) => _mouseReleased.Contains(button);

    /// <summary>The mouse mode set by the last <see cref="SetMouseMode"/> call.</summary>
    public static MouseMode CurrentMouseMode { get; private set; } = MouseMode.Visible;

    /// <summary>Changes cursor visibility / capture. Use <see cref="MouseMode.Captured"/> for FPS cameras.</summary>
    public static void SetMouseMode(MouseMode mode)
    {
        if (_mouse is null) return;
        _mouse.Cursor.CursorMode = mode switch
        {
            MouseMode.Hidden => CursorMode.Hidden,
            MouseMode.Captured => CursorMode.Raw,
            _ => CursorMode.Normal
        };
        CurrentMouseMode = mode;
        _firstMouseSample = true;
    }

    // ---- Actions (InputMap) ----

    /// <summary>True while any binding of the action is held.</summary>
    public static bool IsActionDown(string action) => InputMap.Resolve(action, IsKeyDown, IsMouseButtonDown);

    /// <summary>True only on the frame any binding of the action was pressed.</summary>
    public static bool IsActionPressed(string action) => InputMap.Resolve(action, IsKeyPressed, IsMouseButtonPressed);

    /// <summary>True only on the frame any binding of the action was released.</summary>
    public static bool IsActionReleased(string action) => InputMap.Resolve(action, IsKeyReleased, IsMouseButtonReleased);

    /// <summary>1 while the action is held, 0 otherwise.</summary>
    public static float GetActionStrength(string action) => IsActionDown(action) ? 1f : 0f;

    /// <summary>Value in -1..1 built from two actions (e.g. "move_left" / "move_right").</summary>
    public static float GetAxis(string negativeAction, string positiveAction) =>
        GetActionStrength(positiveAction) - GetActionStrength(negativeAction);

    /// <summary>
    /// Normalized 2D vector built from four actions. X: left/right, Y: up/down.
    /// Perfect for WASD movement.
    /// </summary>
    public static Vector2 GetVector(string left, string right, string up, string down)
    {
        var v = new Vector2(GetAxis(left, right), GetAxis(up, down));
        return v.LengthSquared() > 1f ? Vector2.Normalize(v) : v;
    }

    /// <summary>
    /// The system clipboard as text. Reading returns an empty string when no window is
    /// attached or the platform refuses; writing is a no-op in the same case. Tools use it to
    /// copy and paste objects between windows.
    /// </summary>
    public static string ClipboardText
    {
        get
        {
            try { return _context?.Keyboards.FirstOrDefault()?.ClipboardText ?? string.Empty; }
            catch { return string.Empty; }
        }
        set
        {
            try
            {
                var keyboard = _context?.Keyboards.FirstOrDefault();
                if (keyboard != null)
                    keyboard.ClipboardText = value;
            }
            catch { /* no clipboard on this platform */ }
        }
    }

    // ---- Engine internals ----

    internal static void Attach(IInputContext context)
    {
        _context = context;

        foreach (var keyboard in context.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) =>
            {
                var k = (Key)key;
                if (_keysDown.Add(k))
                    _keysPressedPending.Add(k);
            };
            keyboard.KeyUp += (_, key, _) =>
            {
                var k = (Key)key;
                _keysDown.Remove(k);
                _keysReleasedPending.Add(k);
            };
            keyboard.KeyChar += (_, c) => _typedCharsPending.Add(c);
        }

        foreach (var mouse in context.Mice)
        {
            _mouse ??= mouse;
            mouse.MouseDown += (_, button) =>
            {
                var b = (MouseButton)button;
                if (_mouseDown.Add(b))
                    _mousePressedPending.Add(b);
            };
            mouse.MouseUp += (_, button) =>
            {
                var b = (MouseButton)button;
                _mouseDown.Remove(b);
                _mouseReleasedPending.Add(b);
            };
            mouse.Scroll += (_, wheel) => _pendingScroll += new Vector2(wheel.X, wheel.Y);
        }
    }

    /// <summary>Called by the engine at the start of each frame: promotes pending events.</summary>
    internal static void NewFrame()
    {
        Promote(_keysPressedPending, _keysPressed);
        Promote(_keysReleasedPending, _keysReleased);
        Promote(_mousePressedPending, _mousePressed);
        Promote(_mouseReleasedPending, _mouseReleased);

        _typedChars.Clear();
        _typedChars.AddRange(_typedCharsPending);
        _typedCharsPending.Clear();

        ScrollDelta = _pendingScroll;
        _pendingScroll = Vector2.Zero;

        if (_mouse is not null)
        {
            _mousePosition = _mouse.Position;
            if (_firstMouseSample)
            {
                _lastMousePosition = _mousePosition;
                _firstMouseSample = false;
            }
            MouseDelta = _mousePosition - _lastMousePosition;
            _lastMousePosition = _mousePosition;
        }
    }

    private static void Promote<T>(HashSet<T> pending, HashSet<T> current)
    {
        current.Clear();
        foreach (var item in pending)
            current.Add(item);
        pending.Clear();
    }
}

/// <summary>
/// Maps named actions ("jump", "shoot", "move_left") to keys and mouse buttons.
/// Register actions once at startup, then query them through <see cref="Input"/>.
/// </summary>
public static class InputMap
{
    private sealed class Binding
    {
        public readonly List<Key> Keys = new();
        public readonly List<MouseButton> MouseButtons = new();
    }

    private static readonly Dictionary<string, Binding> _actions = new();

    /// <summary>Registers (or extends) an action with any number of key bindings.</summary>
    public static void AddAction(string action, params Key[] keys)
    {
        var binding = GetOrCreate(action);
        binding.Keys.AddRange(keys);
    }

    /// <summary>Registers (or extends) an action with mouse button bindings.</summary>
    public static void AddAction(string action, params MouseButton[] buttons)
    {
        var binding = GetOrCreate(action);
        binding.MouseButtons.AddRange(buttons);
    }

    /// <summary>Removes an action and all its bindings.</summary>
    public static void RemoveAction(string action) => _actions.Remove(action);

    /// <summary>Removes every registered action.</summary>
    public static void Clear() => _actions.Clear();

    public static bool HasAction(string action) => _actions.ContainsKey(action);

    /// <summary>Registers the common defaults: move_left/right/forward/back (WASD + arrows), jump (Space), sprint (Shift). Safe to call more than once.</summary>
    public static void AddDefaultMovementActions()
    {
        if (HasAction("move_forward"))
            return;
        AddAction("move_left", Key.A, Key.Left);
        AddAction("move_right", Key.D, Key.Right);
        AddAction("move_forward", Key.W, Key.Up);
        AddAction("move_back", Key.S, Key.Down);
        AddAction("jump", Key.Space);
        AddAction("sprint", Key.LeftShift);
    }

    internal static bool Resolve(string action, Func<Key, bool> keyTest, Func<MouseButton, bool> mouseTest)
    {
        if (!_actions.TryGetValue(action, out var binding))
            return false;

        foreach (var key in binding.Keys)
            if (keyTest(key))
                return true;
        foreach (var button in binding.MouseButtons)
            if (mouseTest(button))
                return true;
        return false;
    }

    private static Binding GetOrCreate(string action)
    {
        if (!_actions.TryGetValue(action, out var binding))
        {
            binding = new Binding();
            _actions[action] = binding;
        }
        return binding;
    }
}
