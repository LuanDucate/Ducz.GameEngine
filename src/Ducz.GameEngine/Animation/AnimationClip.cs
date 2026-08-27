using System.Numerics;

namespace Ducz;

/// <summary>Which transform property an animation track drives.</summary>
public enum AnimationProperty
{
    Position,
    Rotation,
    Scale
}

/// <summary>Keyframe interpolation mode.</summary>
public enum AnimationInterpolation
{
    Linear,
    Step
}

/// <summary>
/// A single animated property of a single target (bone or node), with keyframes.
/// </summary>
public sealed class AnimationTrack
{
    /// <summary>Bone or node name this track animates.</summary>
    public required string TargetName { get; init; }

    public required AnimationProperty Property { get; init; }

    /// <summary>Keyframe times in seconds, ascending.</summary>
    public required float[] Times { get; init; }

    /// <summary>Values for Position/Scale tracks.</summary>
    public Vector3[]? VectorValues { get; init; }

    /// <summary>Values for Rotation tracks.</summary>
    public Quaternion[]? RotationValues { get; init; }

    public AnimationInterpolation Interpolation { get; init; } = AnimationInterpolation.Linear;

    /// <summary>Samples a vector track at a time (clamped to the track range).</summary>
    public Vector3 SampleVector(float time)
    {
        var values = VectorValues!;
        (int i0, int i1, float t) = Locate(time);
        if (Interpolation == AnimationInterpolation.Step)
            return values[i0];
        return Vector3.Lerp(values[i0], values[i1], t);
    }

    /// <summary>Samples a rotation track at a time (clamped to the track range).</summary>
    public Quaternion SampleRotation(float time)
    {
        var values = RotationValues!;
        (int i0, int i1, float t) = Locate(time);
        if (Interpolation == AnimationInterpolation.Step)
            return values[i0];
        return Quaternion.Slerp(values[i0], values[i1], t);
    }

    private (int, int, float) Locate(float time)
    {
        var times = Times;
        if (time <= times[0])
            return (0, 0, 0f);
        if (time >= times[^1])
            return (times.Length - 1, times.Length - 1, 0f);

        int low = 0, high = times.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (times[mid] <= time) low = mid;
            else high = mid;
        }

        float span = times[high] - times[low];
        float t = span < Mathf.Epsilon ? 0f : (time - times[low]) / span;
        return (low, high, t);
    }
}

/// <summary>
/// A named animation: a set of tracks with a duration. Loaded from model files
/// or built procedurally in code.
/// </summary>
public sealed class AnimationClip
{
    public required string Name { get; init; }

    /// <summary>Length in seconds.</summary>
    public required float Duration { get; init; }

    /// <summary>Whether the clip loops by default when played.</summary>
    public bool Loop { get; set; } = true;

    public List<AnimationTrack> Tracks { get; } = new();

    /// <summary>Builds a simple one-track clip in code (e.g. a spinning platform).</summary>
    public static AnimationClip FromRotationKeys(string name, string targetName,
        (float Time, Quaternion Rotation)[] keys, bool loop = true)
    {
        var clip = new AnimationClip { Name = name, Duration = keys[^1].Time, Loop = loop };
        clip.Tracks.Add(new AnimationTrack
        {
            TargetName = targetName,
            Property = AnimationProperty.Rotation,
            Times = keys.Select(k => k.Time).ToArray(),
            RotationValues = keys.Select(k => k.Rotation).ToArray()
        });
        return clip;
    }

    /// <summary>Builds a simple position clip in code (e.g. a moving platform).</summary>
    public static AnimationClip FromPositionKeys(string name, string targetName,
        (float Time, Vector3 Position)[] keys, bool loop = true)
    {
        var clip = new AnimationClip { Name = name, Duration = keys[^1].Time, Loop = loop };
        clip.Tracks.Add(new AnimationTrack
        {
            TargetName = targetName,
            Property = AnimationProperty.Position,
            Times = keys.Select(k => k.Time).ToArray(),
            VectorValues = keys.Select(k => k.Position).ToArray()
        });
        return clip;
    }
}
