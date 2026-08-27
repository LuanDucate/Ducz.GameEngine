namespace Ducz;

/// <summary>
/// Global time information for the running game.
/// All values are updated by the engine once per frame.
/// </summary>
public static class Time
{
    /// <summary>Seconds elapsed since the previous frame, scaled by <see cref="Scale"/>.</summary>
    public static float DeltaTime { get; internal set; }

    /// <summary>Seconds elapsed since the previous frame, ignoring <see cref="Scale"/>.</summary>
    public static float UnscaledDeltaTime { get; internal set; }

    /// <summary>Fixed step used by physics updates (seconds). Default: 1/60.</summary>
    public static float FixedDeltaTime { get; set; } = 1f / 60f;

    /// <summary>Total scaled seconds since the game started.</summary>
    public static float TotalTime { get; internal set; }

    /// <summary>Total unscaled seconds since the game started.</summary>
    public static float UnscaledTotalTime { get; internal set; }

    /// <summary>Number of rendered frames since the game started.</summary>
    public static long FrameCount { get; internal set; }

    /// <summary>
    /// Time scale multiplier. 1 = normal speed, 0.5 = slow motion, 0 = paused gameplay
    /// (rendering and UI keep running).
    /// </summary>
    public static float Scale { get; set; } = 1f;

    /// <summary>Frames per second, smoothed over the last second.</summary>
    public static float Fps { get; internal set; }

    internal static void Advance(float rawDelta)
    {
        // Clamp huge hitches (debugger pauses, window drags) so physics stays stable.
        rawDelta = MathF.Min(rawDelta, 0.25f);

        UnscaledDeltaTime = rawDelta;
        DeltaTime = rawDelta * Scale;
        UnscaledTotalTime += rawDelta;
        TotalTime += DeltaTime;
    }
}
