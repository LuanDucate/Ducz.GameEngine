# UI

The UI system draws in pixels on top of the 3D scene: HUDs, menus, dialogs. Everything is a node under a `Canvas`, positioned with **anchors**.

## Canvas

```csharp
var canvas = AddChild(new Canvas());
```

The canvas fills the window, lays out its children every frame and routes mouse input (hover, clicks, focus). Add one per logical layer (HUD, pause menu). While any interactive element is hovered, `Canvas.IsMouseOverUI` is true - use it to suppress gameplay clicks.

## Anchors and layout

Every `UINode` has:

- `Anchor` - which point of the parent it attaches to (`TopLeft`, `Center`, `BottomRight`, ..., or `FullRect` to stretch)
- `Position` - pixel offset from that anchor point
- `Size` - pixel size

The element's own pivot matches its anchor, so offsets always push *inward*:

```csharp
// 20px from the top-right corner
canvas.AddChild(new Label("Score: 0") { Anchor = Anchor.TopRight,   Position = new(-20, 20) });
// centered
canvas.AddChild(new Button("Play")    { Anchor = Anchor.Center,     Size = new(220, 56) });
// bottom-left health bar
canvas.AddChild(new ProgressBar       { Anchor = Anchor.BottomLeft, Position = new(20, -30) });
```

## Controls

### Label
```csharp
new Label("Hello")
{
    FontSize = 24,
    Color = Color.Yellow,
    HAlign = HAlign.Center,   // alignment inside Size when AutoSize = false
    AutoSize = true           // default: size follows the text
}
```
Supports `\n` for multiple lines.

### Button
```csharp
var button = canvas.AddChild(new Button("Start") { Size = new(220, 52), FontSize = 22 });
button.Clicked += () => Engine.Game.ChangeScene(new GameScene());
button.Disabled = false;
// Colors: NormalColor, HoverColor, PressedColor, TextColor
```

### Panel
```csharp
new Panel
{
    Size = new(300, 200),
    BackgroundColor = new Color(0, 0, 0, 0.6f),
    BorderColor = Color.White.WithAlpha(0.2f),
    BorderThickness = 1f
}
```
Panels are also containers - add labels/buttons as children; their anchors are relative to the panel.

### ProgressBar
```csharp
var hp = new ProgressBar { Size = new(240, 20), FillColor = Color.Green };
hp.Value = health / maxHealth;    // 0..1
```

### ImageBox
```csharp
new ImageBox(Assets.LoadTexture("logo.png")) { Size = new(256, 128), Tint = Color.White }
```

### CheckBox
```csharp
var vsync = new CheckBox("VSync") { Checked = true };
vsync.Toggled += on => { ... };
```

### TextBox
```csharp
var nameField = new TextBox { Placeholder = "Enter name...", Size = new(260, 36) };
nameField.Submitted += text => Log.Info($"hello {text}");   // Enter key
nameField.TextChanged += text => { ... };
```
Click to focus; respects keyboard layout (accents included).

### VStack / HStack
Automatic sequential layout - perfect for menus:

```csharp
var menu = canvas.AddChild(new VStack { Anchor = Anchor.Center, Spacing = 14 });
menu.AddChild(new Label("PAUSED") { FontSize = 40, Anchor = Anchor.TopCenter });
menu.AddChild(new Button("Resume") { Size = new(220, 48), Anchor = Anchor.TopCenter });
menu.AddChild(new Button("Quit")   { Size = new(220, 48), Anchor = Anchor.TopCenter });
```

Children keep their `Size`; the stack sizes itself to fit. A child's anchor controls its cross-axis alignment (e.g. `TopCenter` centers it horizontally in a VStack).

## ScrollPanel

A panel whose contents may be taller than itself. It clips its children to its own rectangle
and the wheel scrolls them; a slim bar on the right shows the position.

```csharp
var side = canvas.AddChild(new ScrollPanel { Size = new Vector2(190, 800) });
var stack = side.AddChild(new VStack { Anchor = Anchor.TopCenter, Spacing = 4 });
// ... add as many children as you like ...

// in your Update:
side.HandleWheel(Input.MousePosition, Input.ScrollDelta.Y);
```

`CanScroll` says whether the contents overflow, `Scroll` is the offset in pixels, and
`WheelStep` how far one notch moves. Clipping uses `SpriteBatch.PushClip`/`PopClip`, which
you can also call from a custom control's `Draw`.

## Fonts and theming

```csharp
UITheme.FontPath = "Assets/Fonts/MyFont.ttf";  // set BEFORE creating UI; else a system font is used
UITheme.DefaultFontSize = 18;
UITheme.AccentColor = Color.FromHex("#4f8fea");
UITheme.PanelColor  = new Color(0.1f, 0.11f, 0.14f, 0.92f);
UITheme.TextColor   = Color.White;
```

Fonts are baked per size and cached (`UITheme.GetFont(size)`); ASCII plus Latin-1 accents are covered.

## Custom controls

Subclass `UINode` and override `Draw`:

```csharp
class Minimap : UINode
{
    protected internal override void Draw(SpriteBatch batch)
    {
        batch.DrawRect(ComputedRect.Position, ComputedRect.Size, new Color(0, 0, 0, 0.5f));
        foreach (var enemy in Tree!.GetNodesInGroup("enemies").OfType<Node3D>())
        {
            var p = WorldToMap(enemy.GlobalPosition);
            batch.DrawRect(p, new Vector2(3, 3), Color.Red);
        }
    }
}
```

`SpriteBatch` gives you `DrawRect`, `DrawRectOutline`, `DrawTexture` (with UV region overload) and `DrawText(font, ...)`. Override `BlocksMouse => true` and implement press handling like `Button` does if the control is interactive.

## Floating labels over 3D objects

```csharp
var screen = camera.WorldToScreenPoint(enemy.GlobalPosition + Vector3.UnitY * 2);
if (screen is { } s)
    healthBar.Position = new Vector2(s.X, s.Y);   // with Anchor.TopLeft
```
