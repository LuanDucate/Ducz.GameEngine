using System.Numerics;
using Ducz.Rendering;

namespace Ducz.UI;

/// <summary>A 2D rectangle in pixels (position = top-left).</summary>
public struct Rect
{
    public Vector2 Position;
    public Vector2 Size;

    public Rect(Vector2 position, Vector2 size)
    {
        Position = position;
        Size = size;
    }

    public float X => Position.X;
    public float Y => Position.Y;
    public float Width => Size.X;
    public float Height => Size.Y;
    public Vector2 Center => Position + Size * 0.5f;

    public bool Contains(Vector2 point) =>
        point.X >= Position.X && point.X <= Position.X + Size.X &&
        point.Y >= Position.Y && point.Y <= Position.Y + Size.Y;
}

/// <summary>Where a UI element anchors inside its parent.</summary>
public enum Anchor
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, Center, MiddleRight,
    BottomLeft, BottomCenter, BottomRight,
    /// <summary>Stretches to fill the parent rect entirely (Position/Size ignored).</summary>
    FullRect
}

/// <summary>Horizontal text alignment.</summary>
public enum HAlign { Left, Center, Right }

/// <summary>Vertical text alignment.</summary>
public enum VAlign { Top, Middle, Bottom }

/// <summary>
/// Global UI defaults: font resolution and shared colors.
/// Set <see cref="FontPath"/> before creating UI to use a custom font; otherwise a
/// common system font is used.
/// </summary>
public static class UITheme
{
    private static readonly Dictionary<int, Font> _fontCache = new();
    private static byte[]? _ttfBytes;

    /// <summary>Path to the TTF used by all controls. Leave null to auto-detect a system font.</summary>
    public static string? FontPath { get; set; }

    /// <summary>Default font size for controls that don't specify one.</summary>
    public static int DefaultFontSize { get; set; } = 18;

    public static Color AccentColor { get; set; } = Color.FromHex("#4f8fea");
    public static Color PanelColor { get; set; } = new(0.10f, 0.11f, 0.14f, 0.92f);
    public static Color TextColor { get; set; } = Color.White;

    /// <summary>Returns a cached font at the requested pixel size.</summary>
    public static Font GetFont(int size)
    {
        if (_fontCache.TryGetValue(size, out var font))
            return font;

        _ttfBytes ??= LoadTtfBytes();
        font = Font.FromBytes(_ttfBytes, size);
        _fontCache[size] = font;
        return font;
    }

    private static byte[] LoadTtfBytes()
    {
        if (FontPath != null)
            return File.ReadAllBytes(Assets.Resolve(FontPath));

        string[] candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                @"C:\Windows\Fonts\segoeui.ttf",
                @"C:\Windows\Fonts\arial.ttf",
                @"C:\Windows\Fonts\calibri.ttf",
                @"C:\Windows\Fonts\tahoma.ttf"
            }
            : OperatingSystem.IsMacOS()
                ? new[]
                {
                    "/System/Library/Fonts/Supplemental/Arial.ttf",
                    "/System/Library/Fonts/Helvetica.ttc"
                }
                : new[]
                {
                    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                    "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
                    "/usr/share/fonts/TTF/DejaVuSans.ttf"
                };

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return File.ReadAllBytes(candidate);

        throw new FileNotFoundException(
            "No system font found. Set UITheme.FontPath to a .ttf file before creating UI elements.");
    }
}

/// <summary>
/// Base class for all UI elements. UI lives under a <see cref="Canvas"/> node and is
/// laid out in window pixels using anchors:
///
/// <code>
/// var canvas = AddChild(new Canvas());
/// var label = canvas.AddChild(new Label("Score: 0") { Anchor = Anchor.TopRight, Position = new(-20, 20) });
/// </code>
/// </summary>
public abstract class UINode : Node
{
    /// <summary>Offset in pixels from the anchor point.</summary>
    public Vector2 Position { get; set; }

    /// <summary>Element size in pixels.</summary>
    public Vector2 Size { get; set; } = new(100, 40);

    /// <summary>Where this element attaches inside its parent.</summary>
    public Anchor Anchor { get; set; } = Anchor.TopLeft;

    /// <summary>Hidden elements (and their children) are not drawn nor receive input.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>The rectangle computed during layout (valid after the first frame).</summary>
    public Rect ComputedRect { get; internal set; }

    /// <summary>True while the mouse is over this element (updated by the canvas).</summary>
    public bool IsHovered { get; internal set; }

    /// <summary>True while the mouse button is held down on this element.</summary>
    public bool IsPressed { get; internal set; }

    /// <summary>
    /// Makes any element clickable: it receives hover state and raises
    /// <see cref="Clicked"/> / <see cref="MouseEntered"/> / <see cref="MouseExited"/>.
    /// Built-in interactive controls (Button, CheckBox, TextBox) are always interactive.
    /// </summary>
    public bool Interactive { get; set; }

    /// <summary>Raised on a full click (press + release on this element). Requires <see cref="Interactive"/>.</summary>
    public event Action? Clicked;

    /// <summary>Raised when the mouse starts hovering this element. Requires <see cref="Interactive"/>.</summary>
    public event Action? MouseEntered;

    /// <summary>Raised when the mouse stops hovering this element.</summary>
    public event Action? MouseExited;

    /// <summary>When true this element consumes mouse clicks (buttons, text fields...).</summary>
    public virtual bool BlocksMouse => Interactive;

    protected UINode(string? name = null) : base(name) { }

    /// <summary>Draws this element above its siblings (last child renders on top).</summary>
    public void MoveToFront()
    {
        Parent?.MoveChild(this, Parent.Children.Count - 1);
    }

    /// <summary>Draws this element below its siblings.</summary>
    public void MoveToBack()
    {
        Parent?.MoveChild(this, 0);
    }

    internal void RaiseClicked() => Clicked?.Invoke();
    internal void RaiseMouseEntered() => MouseEntered?.Invoke();
    internal void RaiseMouseExited() => MouseExited?.Invoke();

    internal virtual void Layout(Rect parentRect)
    {
        ComputedRect = ComputeRect(parentRect);
        foreach (var child in Children)
            if (child is UINode uiChild)
                uiChild.Layout(ComputedRect);
    }

    /// <summary>Places this node inside its parent according to Anchor/Position/Size.</summary>
    private protected Rect ComputeRect(Rect parent)
    {
        if (Anchor == Anchor.FullRect)
            return parent;

        var anchorFraction = Anchor switch
        {
            Anchor.TopLeft => new Vector2(0f, 0f),
            Anchor.TopCenter => new Vector2(0.5f, 0f),
            Anchor.TopRight => new Vector2(1f, 0f),
            Anchor.MiddleLeft => new Vector2(0f, 0.5f),
            Anchor.Center => new Vector2(0.5f, 0.5f),
            Anchor.MiddleRight => new Vector2(1f, 0.5f),
            Anchor.BottomLeft => new Vector2(0f, 1f),
            Anchor.BottomCenter => new Vector2(0.5f, 1f),
            _ => new Vector2(1f, 1f)
        };

        // The element's own pivot matches the anchor so "BottomRight + (-10,-10)"
        // means "10px in from the bottom-right corner".
        var anchorPoint = parent.Position + parent.Size * anchorFraction;
        var topLeft = anchorPoint + Position - Size * anchorFraction;
        return new Rect(topLeft, Size);
    }

    /// <summary>Draws this element. Children are drawn afterwards automatically.</summary>
    protected internal virtual void Draw(SpriteBatch batch) { }

    /// <summary>Runs after this node's children were drawn (a clipping panel pops its clip here).</summary>
    internal virtual void AfterDrawChildren(SpriteBatch batch) { }

    internal void DrawRecursive(SpriteBatch batch)
    {
        if (!Visible)
            return;
        Draw(batch);
        foreach (var child in Children)
            if (child is UINode uiChild)
                uiChild.DrawRecursive(batch);
        AfterDrawChildren(batch);
    }

    internal void CollectInteractive(List<UINode> output)
    {
        if (!Visible)
            return;
        if (BlocksMouse)
            output.Add(this);
        foreach (var child in Children)
            if (child is UINode uiChild)
                uiChild.CollectInteractive(output);
    }
}

/// <summary>
/// Root of a UI hierarchy. Add one to your scene, then add controls to it.
/// The canvas fills the window, lays out children every frame and routes mouse input.
/// </summary>
public class Canvas : UINode
{
    private readonly List<UINode> _interactive = new();
    private UINode? _pressed;

    /// <summary>Element with keyboard focus (e.g. a <see cref="TextBox"/>).</summary>
    public UINode? FocusedElement { get; internal set; }

    /// <summary>True when the mouse is currently over any interactive UI element (any canvas).</summary>
    public static bool IsMouseOverUI { get; private set; }

    public Canvas(string? name = null) : base(name)
    {
        Anchor = Anchor.FullRect;
    }

    protected override void OnUpdate(float dt)
    {
        var windowSize = Engine.WindowSize;
        Layout(new Rect(Vector2.Zero, windowSize));

        // Mouse interaction: the last drawn (topmost) element wins.
        _interactive.Clear();
        CollectInteractive(_interactive);

        var mouse = Input.MousePosition;
        UINode? hovered = null;
        foreach (var element in _interactive)
        {
            if (element.ComputedRect.Contains(mouse))
                hovered = element;   // last drawn (topmost) wins
        }

        IsMouseOverUI = hovered != null;

        foreach (var element in _interactive)
        {
            bool isNowHovered = element == hovered;
            if (isNowHovered && !element.IsHovered)
            {
                element.IsHovered = true;
                element.RaiseMouseEntered();
            }
            else if (!isNowHovered && element.IsHovered)
            {
                element.IsHovered = false;
                element.RaiseMouseExited();
            }
        }

        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _pressed = hovered;
            FocusedElement = hovered is TextBox ? hovered : null;
            if (hovered != null)
            {
                hovered.IsPressed = true;
                (hovered as IPressable)?.OnPressed();
            }
        }

        if (Input.IsMouseButtonReleased(MouseButton.Left))
        {
            if (_pressed != null)
            {
                _pressed.IsPressed = false;
                if (_pressed == hovered)
                {
                    (_pressed as IPressable)?.OnClicked();
                    _pressed.RaiseClicked();
                }
                (_pressed as IPressable)?.OnReleased();
            }
            _pressed = null;
        }
    }

    internal void InternalRender(SpriteBatch batch)
    {
        // Layout may not have run yet on the first frame.
        Layout(new Rect(Vector2.Zero, Engine.WindowSize));
        DrawRecursive(batch);
    }
}

/// <summary>Implemented by controls that react to mouse presses.</summary>
internal interface IPressable
{
    void OnPressed();
    void OnReleased();
    void OnClicked();
}
