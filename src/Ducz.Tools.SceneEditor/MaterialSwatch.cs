using System.Numerics;
using Ducz.Rendering;
using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// A clickable square previewing a material: its texture (tinted by the albedo) or
/// a flat color. Highlights when hovered / selected. Used by the texture palette in
/// the sidebar and by the properties panel.
/// </summary>
public sealed class MaterialSwatch : UINode
{
    /// <summary>Material key in the document.</summary>
    public string Key { get; }

    public Texture2D? Texture { get; set; }
    public Color Tint { get; set; } = Color.White;
    public bool Selected { get; set; }
    public bool Emissive { get; set; }

    public MaterialSwatch(string key, float size = 38f) : base("Swatch_" + key)
    {
        Key = key;
        Size = new Vector2(size);
        Interactive = true;
    }

    protected override void Draw(SpriteBatch batch)
    {
        var rect = ComputedRect;
        // Dark backing so transparent materials read as "glass" instead of vanishing.
        batch.DrawRect(rect.Position, rect.Size, new Color(0.08f, 0.09f, 0.11f, 1f));

        var inset = new Vector2(2f);
        var innerPos = rect.Position + inset;
        var innerSize = rect.Size - inset * 2f;
        if (Texture != null)
            batch.DrawTexture(Texture, innerPos, innerSize, Tint);
        else
            batch.DrawRect(innerPos, innerSize, Tint);

        if (Emissive)
            batch.DrawRectOutline(innerPos + new Vector2(3f), innerSize - new Vector2(6f), Color.FromHex("#ffd75e").WithAlpha(0.8f));

        var outline = Selected ? UITheme.AccentColor
            : IsHovered ? Color.White
            : Color.White.WithAlpha(0.18f);
        batch.DrawRectOutline(rect.Position, rect.Size, outline, Selected ? 2f : 1f);
    }
}
