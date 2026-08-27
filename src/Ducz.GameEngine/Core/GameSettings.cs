namespace Ducz;

/// <summary>Window and startup configuration passed to <see cref="Game"/>.</summary>
public sealed class GameSettings
{
    /// <summary>Window title.</summary>
    public string Title { get; set; } = "Ducz Engine Game";

    /// <summary>Initial window width in pixels.</summary>
    public int Width { get; set; } = 1280;

    /// <summary>Initial window height in pixels.</summary>
    public int Height { get; set; } = 720;

    /// <summary>Whether the user can resize the window.</summary>
    public bool Resizable { get; set; } = true;

    /// <summary>Start in fullscreen (borderless) mode.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>Enable vertical sync. Recommended: true.</summary>
    public bool VSync { get; set; } = true;

    /// <summary>MSAA sample count (0 = off, common values: 2, 4, 8).</summary>
    public int Msaa { get; set; } = 4;

    /// <summary>Physics steps per second. Default 60.</summary>
    public int PhysicsTicksPerSecond { get; set; } = 60;

    /// <summary>When true, pressing Escape closes the game (handy while prototyping).</summary>
    public bool QuitOnEscape { get; set; }

    /// <summary>Disable audio entirely (the engine also degrades gracefully if no device exists).</summary>
    public bool NoAudio { get; set; }

    /// <summary>
    /// Optional PNG shown as the window / taskbar icon (path resolved like assets, or absolute).
    /// The executable icon itself comes from the project's ApplicationIcon (.ico).
    /// </summary>
    public string? IconPath { get; set; }
}
