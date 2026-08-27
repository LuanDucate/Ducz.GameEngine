using System.Numerics;
using Ducz.Rendering;

namespace Ducz.UI;

/// <summary>A solid rectangle, optionally with a border. The building block for HUDs and menus.</summary>
public class Panel : UINode
{
    public Color BackgroundColor { get; set; } = UITheme.PanelColor;
    public Color BorderColor { get; set; } = Color.Transparent;
    public float BorderThickness { get; set; } = 1f;

    public Panel(string? name = null) : base(name) { }

    protected internal override void Draw(SpriteBatch batch)
    {
        if (BackgroundColor.A > 0f)
            batch.DrawRect(ComputedRect.Position, ComputedRect.Size, BackgroundColor);
        if (BorderColor.A > 0f && BorderThickness > 0f)
            batch.DrawRectOutline(ComputedRect.Position, ComputedRect.Size, BorderColor, BorderThickness);
    }
}

/// <summary>A text element. Size is computed from the text automatically unless <see cref="AutoSize"/> is off.</summary>
public class Label : UINode
{
    public string Text { get; set; }
    public Color Color { get; set; } = UITheme.TextColor;
    public int FontSize { get; set; } = UITheme.DefaultFontSize;
    public HAlign HAlign { get; set; } = HAlign.Left;
    public VAlign VAlign { get; set; } = VAlign.Top;

    /// <summary>When true (default) the element resizes to fit the text.</summary>
    public bool AutoSize { get; set; } = true;

    public Label(string text = "", string? name = null) : base(name)
    {
        Text = text;
    }

    internal override void Layout(Rect parentRect)
    {
        if (AutoSize && Text.Length > 0)
            Size = UITheme.GetFont(FontSize).MeasureText(Text);
        base.Layout(parentRect);
    }

    protected internal override void Draw(SpriteBatch batch)
    {
        if (Text.Length == 0)
            return;

        var font = UITheme.GetFont(FontSize);
        var textSize = font.MeasureText(Text);
        var rect = ComputedRect;

        float x = HAlign switch
        {
            HAlign.Center => rect.X + (rect.Width - textSize.X) * 0.5f,
            HAlign.Right => rect.X + rect.Width - textSize.X,
            _ => rect.X
        };
        float y = VAlign switch
        {
            VAlign.Middle => rect.Y + (rect.Height - textSize.Y) * 0.5f,
            VAlign.Bottom => rect.Y + rect.Height - textSize.Y,
            _ => rect.Y
        };

        batch.DrawText(font, Text, new Vector2(x, y), Color);
    }
}

/// <summary>Displays a texture stretched into the element rect.</summary>
public class ImageBox : UINode
{
    public Texture2D? Texture { get; set; }
    public Color Tint { get; set; } = Color.White;

    public ImageBox(Texture2D? texture = null, string? name = null) : base(name)
    {
        Texture = texture;
    }

    protected internal override void Draw(SpriteBatch batch)
    {
        if (Texture != null)
            batch.DrawTexture(Texture, ComputedRect.Position, ComputedRect.Size, Tint);
    }
}

/// <summary>
/// A clickable button with hover/pressed states.
/// <code>
/// var play = canvas.AddChild(new Button("Play") { Anchor = Anchor.Center, Size = new(220, 56) });
/// play.Clicked += () => Engine.Game.ChangeScene(new GameScene());
/// </code>
/// </summary>
public class Button : UINode, IPressable
{
    public string Text { get; set; }
    public int FontSize { get; set; } = UITheme.DefaultFontSize;

    public Color NormalColor { get; set; } = UITheme.PanelColor;
    public Color HoverColor { get; set; } = UITheme.AccentColor.Darkened(0.35f);
    public Color PressedColor { get; set; } = UITheme.AccentColor;
    public Color TextColor { get; set; } = UITheme.TextColor;

    /// <summary>Disabled buttons draw dimmed and ignore clicks.</summary>
    public bool Disabled { get; set; }

    // Click notifications come from the inherited UINode.Clicked event.

    public override bool BlocksMouse => !Disabled;

    /// <summary>True while the mouse is held down on the button - lets callers auto-repeat an action.</summary>
    public bool IsHeld => _isPressed && IsHovered && !Disabled;

    private bool _isPressed;

    public Button(string text = "Button", string? name = null) : base(name)
    {
        Text = text;
        Size = new Vector2(160, 44);
    }

    void IPressable.OnPressed() => _isPressed = true;
    void IPressable.OnReleased() => _isPressed = false;
    void IPressable.OnClicked() { }

    protected internal override void Draw(SpriteBatch batch)
    {
        var background = Disabled ? NormalColor.Darkened(0.4f)
            : _isPressed && IsHovered ? PressedColor
            : IsHovered ? HoverColor
            : NormalColor;

        batch.DrawRect(ComputedRect.Position, ComputedRect.Size, background);
        batch.DrawRectOutline(ComputedRect.Position, ComputedRect.Size,
            IsHovered && !Disabled ? UITheme.AccentColor : Color.White.WithAlpha(0.15f));

        var font = UITheme.GetFont(FontSize);
        var textSize = font.MeasureText(Text);
        var textPos = ComputedRect.Center - textSize * 0.5f;
        batch.DrawText(font, Text, textPos, Disabled ? TextColor.WithAlpha(0.4f) : TextColor);
    }
}

/// <summary>A horizontal progress/health bar (value 0..1).</summary>
public class ProgressBar : UINode
{
    private float _value = 1f;

    /// <summary>Fill amount, clamped to 0..1.</summary>
    public float Value
    {
        get => _value;
        set => _value = Mathf.Clamp01(value);
    }

    public Color BackgroundColor { get; set; } = new(0f, 0f, 0f, 0.55f);
    public Color FillColor { get; set; } = UITheme.AccentColor;

    public ProgressBar(string? name = null) : base(name)
    {
        Size = new Vector2(200, 18);
    }

    protected internal override void Draw(SpriteBatch batch)
    {
        batch.DrawRect(ComputedRect.Position, ComputedRect.Size, BackgroundColor);
        if (_value > 0f)
        {
            var fillSize = new Vector2(ComputedRect.Width * _value, ComputedRect.Height);
            batch.DrawRect(ComputedRect.Position, fillSize, FillColor);
        }
        batch.DrawRectOutline(ComputedRect.Position, ComputedRect.Size, Color.White.WithAlpha(0.2f));
    }
}

/// <summary>A toggleable checkbox with a text label.</summary>
public class CheckBox : UINode, IPressable
{
    public string Text { get; set; }
    public bool Checked { get; set; }
    public int FontSize { get; set; } = UITheme.DefaultFontSize;
    public Color TextColor { get; set; } = UITheme.TextColor;

    /// <summary>Raised when the value flips (argument: new value).</summary>
    public event Action<bool>? Toggled;

    public override bool BlocksMouse => true;

    public CheckBox(string text = "", string? name = null) : base(name)
    {
        Text = text;
        Size = new Vector2(200, 26);
    }

    void IPressable.OnPressed() { }
    void IPressable.OnReleased() { }
    void IPressable.OnClicked()
    {
        Checked = !Checked;
        Toggled?.Invoke(Checked);
    }

    protected internal override void Draw(SpriteBatch batch)
    {
        float boxSize = ComputedRect.Height * 0.8f;
        var boxPos = new Vector2(ComputedRect.X, ComputedRect.Y + (ComputedRect.Height - boxSize) * 0.5f);

        batch.DrawRect(boxPos, new Vector2(boxSize), UITheme.PanelColor);
        batch.DrawRectOutline(boxPos, new Vector2(boxSize), IsHovered ? UITheme.AccentColor : Color.White.WithAlpha(0.3f));
        if (Checked)
            batch.DrawRect(boxPos + new Vector2(boxSize * 0.22f), new Vector2(boxSize * 0.56f), UITheme.AccentColor);

        if (Text.Length > 0)
        {
            var font = UITheme.GetFont(FontSize);
            var textPos = new Vector2(boxPos.X + boxSize + 8f,
                ComputedRect.Y + (ComputedRect.Height - font.MeasureText(Text).Y) * 0.5f);
            batch.DrawText(font, Text, textPos, TextColor);
        }
    }
}

/// <summary>
/// A single-line text input. Click to focus, type to edit; Enter raises <see cref="Submitted"/>.
/// </summary>
public class TextBox : UINode, IPressable
{
    public string Text { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public int FontSize { get; set; } = UITheme.DefaultFontSize;
    public int MaxLength { get; set; } = 256;
    public Color TextColor { get; set; } = UITheme.TextColor;

    /// <summary>Raised when Enter is pressed while focused (argument: current text).</summary>
    public event Action<string>? Submitted;

    /// <summary>Raised whenever the text changes.</summary>
    public event Action<string>? TextChanged;

    public override bool BlocksMouse => true;

    private float _caretBlink;

    public bool IsFocused => FindAncestor<Canvas>()?.FocusedElement == this
                             || (Parent as Canvas)?.FocusedElement == this;

    public TextBox(string? name = null) : base(name)
    {
        Size = new Vector2(240, 36);
    }

    void IPressable.OnPressed() { }
    void IPressable.OnReleased() { }
    void IPressable.OnClicked() { }

    protected override void OnUpdate(float dt)
    {
        _caretBlink += dt;
        if (!IsFocused)
            return;

        foreach (char c in Input.TypedCharacters)
        {
            if (!char.IsControl(c) && Text.Length < MaxLength)
            {
                Text += c;
                TextChanged?.Invoke(Text);
            }
        }

        if (Input.IsKeyPressed(Key.Backspace) && Text.Length > 0)
        {
            Text = Text[..^1];
            TextChanged?.Invoke(Text);
        }

        if (Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.KeypadEnter))
            Submitted?.Invoke(Text);
    }

    protected internal override void Draw(SpriteBatch batch)
    {
        batch.DrawRect(ComputedRect.Position, ComputedRect.Size, UITheme.PanelColor);
        batch.DrawRectOutline(ComputedRect.Position, ComputedRect.Size,
            IsFocused ? UITheme.AccentColor : IsHovered ? Color.White.WithAlpha(0.4f) : Color.White.WithAlpha(0.15f));

        var font = UITheme.GetFont(FontSize);
        bool empty = Text.Length == 0;
        string shown = empty ? Placeholder : Text;
        var color = empty ? TextColor.WithAlpha(0.35f) : TextColor;

        float padding = 8f;
        var textPos = new Vector2(ComputedRect.X + padding,
            ComputedRect.Y + (ComputedRect.Height - font.LineHeight) * 0.5f);
        float width = shown.Length > 0 ? batch.DrawText(font, shown, textPos, color) : 0f;

        // Caret
        if (IsFocused && _caretBlink % 1f < 0.55f)
        {
            float caretX = textPos.X + (empty ? 0f : width) + 1f;
            batch.DrawRect(new Vector2(caretX, textPos.Y), new Vector2(1.5f, font.LineHeight), UITheme.AccentColor);
        }
    }
}

/// <summary>Stacks visible children vertically with spacing, resizing itself to fit.</summary>
/// <summary>
/// A panel whose contents can be taller than the panel itself: the wheel scrolls them and
/// everything is clipped to the panel edge. Put a single <see cref="VStack"/> inside and let
/// it grow - the scroll range follows its height automatically.
/// </summary>
public class ScrollPanel : Panel
{
    /// <summary>How many pixels one wheel notch moves.</summary>
    public float WheelStep { get; set; } = 48f;

    /// <summary>Width of the scrollbar drawn on the right edge (0 hides it).</summary>
    public float BarWidth { get; set; } = 5f;

    public Color BarColor { get; set; } = new(1f, 1f, 1f, 0.28f);

    private float _scroll;
    private float _contentHeight;

    public ScrollPanel(string? name = null) : base(name) { }

    /// <summary>Current scroll offset in pixels (0 = top).</summary>
    public float Scroll
    {
        get => _scroll;
        set => _scroll = Mathf.Clamp(value, 0f, MathF.Max(0f, _contentHeight - ComputedRect.Height));
    }

    /// <summary>True when the contents do not fit and a scrollbar is shown.</summary>
    public bool CanScroll => _contentHeight > ComputedRect.Height + 0.5f;

    internal override void Layout(Rect parentRect)
    {
        ComputedRect = ComputeRect(parentRect);

        // Children are laid out inside a rectangle shifted up by the scroll offset.
        var inner = new Rect(new Vector2(ComputedRect.X, ComputedRect.Y - _scroll),
                             new Vector2(ComputedRect.Width, ComputedRect.Height));
        float bottom = ComputedRect.Y;
        foreach (var child in Children)
        {
            if (child is not UINode ui || !ui.Visible)
                continue;
            ui.Layout(inner);
            bottom = MathF.Max(bottom, ui.ComputedRect.Y + ui.ComputedRect.Height);
        }
        _contentHeight = bottom - inner.Y;
        _scroll = Mathf.Clamp(_scroll, 0f, MathF.Max(0f, _contentHeight - ComputedRect.Height));
    }

    /// <summary>Scrolls when the wheel turns over the panel. Call from the owner's update.</summary>
    public bool HandleWheel(Vector2 mousePosition, float wheelDelta)
    {
        if (!Visible || !CanScroll || MathF.Abs(wheelDelta) < 0.01f || !ComputedRect.Contains(mousePosition))
            return false;
        Scroll -= wheelDelta * WheelStep;
        return true;
    }

    protected internal override void Draw(SpriteBatch batch)
    {
        base.Draw(batch);
        batch.PushClip(ComputedRect.Position, ComputedRect.Size);
    }

    internal override void AfterDrawChildren(SpriteBatch batch)
    {
        batch.PopClip();
        if (BarWidth <= 0f || !CanScroll)
            return;
        float visible = ComputedRect.Height;
        float thumb = MathF.Max(24f, visible * visible / _contentHeight);
        float travel = visible - thumb;
        float offset = _contentHeight > visible ? _scroll / (_contentHeight - visible) : 0f;
        batch.DrawRect(
            new Vector2(ComputedRect.X + ComputedRect.Width - BarWidth - 2f, ComputedRect.Y + travel * offset),
            new Vector2(BarWidth, thumb), BarColor);
    }
}

public class VStack : UINode
{
    public float Spacing { get; set; } = 8f;

    public VStack(string? name = null) : base(name) { }

    internal override void Layout(Rect parentRect)
    {
        // Measure
        float height = 0f, width = 0f;
        foreach (var child in Children)
        {
            if (child is not UINode ui || !ui.Visible) continue;
            if (ui is Label { AutoSize: true } label && label.Text.Length > 0)
                ui.Size = UITheme.GetFont(label.FontSize).MeasureText(label.Text);
            height += ui.Size.Y + Spacing;
            width = MathF.Max(width, ui.Size.X);
        }
        Size = new Vector2(MathF.Max(width, 1f), MathF.Max(height - Spacing, 1f));

        ComputedRect = ComputeSelfRect(parentRect);

        // Place children
        float y = ComputedRect.Y;
        foreach (var child in Children)
        {
            if (child is not UINode ui || !ui.Visible) continue;
            float x = ui.Anchor switch
            {
                Anchor.TopCenter or Anchor.Center or Anchor.BottomCenter =>
                    ComputedRect.X + (ComputedRect.Width - ui.Size.X) * 0.5f,
                Anchor.TopRight or Anchor.MiddleRight or Anchor.BottomRight =>
                    ComputedRect.X + ComputedRect.Width - ui.Size.X,
                _ => ComputedRect.X
            };
            ui.ComputedRect = new Rect(new Vector2(x, y), ui.Size);
            foreach (var grandChild in ui.Children)
                if (grandChild is UINode uiGrandChild)
                    uiGrandChild.Layout(ui.ComputedRect);
            y += ui.Size.Y + Spacing;
        }
    }

    private Rect ComputeSelfRect(Rect parent)
    {
        var probe = new ProbeNode { Position = Position, Size = Size, Anchor = Anchor };
        probe.Layout(parent);
        return probe.ComputedRect;
    }

    private sealed class ProbeNode : UINode { }
}

/// <summary>Stacks visible children horizontally with spacing, resizing itself to fit.</summary>
public class HStack : UINode
{
    public float Spacing { get; set; } = 8f;

    public HStack(string? name = null) : base(name) { }

    internal override void Layout(Rect parentRect)
    {
        float width = 0f, height = 0f;
        foreach (var child in Children)
        {
            if (child is not UINode ui || !ui.Visible) continue;
            if (ui is Label { AutoSize: true } label && label.Text.Length > 0)
                ui.Size = UITheme.GetFont(label.FontSize).MeasureText(label.Text);
            width += ui.Size.X + Spacing;
            height = MathF.Max(height, ui.Size.Y);
        }
        Size = new Vector2(MathF.Max(width - Spacing, 1f), MathF.Max(height, 1f));

        var probe = new ProbeNode { Position = Position, Size = Size, Anchor = Anchor };
        probe.Layout(parentRect);
        ComputedRect = probe.ComputedRect;

        float x = ComputedRect.X;
        foreach (var child in Children)
        {
            if (child is not UINode ui || !ui.Visible) continue;
            float y = ui.Anchor switch
            {
                Anchor.MiddleLeft or Anchor.Center or Anchor.MiddleRight =>
                    ComputedRect.Y + (ComputedRect.Height - ui.Size.Y) * 0.5f,
                Anchor.BottomLeft or Anchor.BottomCenter or Anchor.BottomRight =>
                    ComputedRect.Y + ComputedRect.Height - ui.Size.Y,
                _ => ComputedRect.Y
            };
            ui.ComputedRect = new Rect(new Vector2(x, y), ui.Size);
            foreach (var grandChild in ui.Children)
                if (grandChild is UINode uiGrandChild)
                    uiGrandChild.Layout(ui.ComputedRect);
            x += ui.Size.X + Spacing;
        }
    }

    private sealed class ProbeNode : UINode { }
}
