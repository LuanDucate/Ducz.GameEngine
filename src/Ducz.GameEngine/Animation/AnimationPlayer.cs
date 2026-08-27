using System.Numerics;

namespace Ducz;

/// <summary>
/// Plays <see cref="AnimationClip"/>s on bones (via a sibling/child <see cref="Skeleton3D"/>)
/// and/or on plain <see cref="Node3D"/> descendants matched by name.
///
/// <code>
/// var player = model.FindNode&lt;AnimationPlayer&gt;();
/// player.Play("Run", fadeSeconds: 0.25f);
/// </code>
/// </summary>
public class AnimationPlayer : Node
{
    private sealed class Playback
    {
        public required AnimationClip Clip;
        public float Time;
        public bool Finished;
    }

    private readonly Dictionary<string, AnimationClip> _clips = new(StringComparer.OrdinalIgnoreCase);
    private Playback? _current;
    private Playback? _previous;
    private float _fadeDuration;
    private float _fadeTime;
    private Skeleton3D? _skeleton;
    private Node3D? _searchRoot;
    private readonly Dictionary<string, Node3D?> _nodeCache = new();

    /// <summary>Playback speed multiplier (1 = normal, negative plays backwards).</summary>
    public float Speed { get; set; } = 1f;

    /// <summary>Name of the clip currently playing, or null.</summary>
    public string? CurrentAnimation => _current?.Clip.Name;

    /// <summary>Playback position of the current clip in seconds.</summary>
    public float CurrentTime => _current?.Time ?? 0f;

    /// <summary>True while a non-finished clip is playing.</summary>
    public bool IsPlaying => _current is { Finished: false };

    /// <summary>Raised when a non-looping clip reaches its end (argument: clip name).</summary>
    public event Action<string>? AnimationFinished;

    /// <summary>All registered clip names.</summary>
    public IEnumerable<string> ClipNames => _clips.Keys;

    public AnimationPlayer(string? name = null) : base(name) { }

    /// <summary>Registers a clip. Returns this player (fluent).</summary>
    public AnimationPlayer AddClip(AnimationClip clip)
    {
        _clips[clip.Name] = clip;
        return this;
    }

    public bool HasClip(string name) => _clips.ContainsKey(name);

    public AnimationClip? GetClip(string name) => _clips.GetValueOrDefault(name);

    /// <summary>
    /// Plays a clip. If another clip is active it cross-fades over <paramref name="fadeSeconds"/>.
    /// Playing the already-active clip does nothing (so it is safe to call every frame).
    /// </summary>
    public void Play(string clipName, float fadeSeconds = 0.2f, bool restart = false)
    {
        if (!_clips.TryGetValue(clipName, out var clip))
        {
            Log.Warning($"AnimationPlayer '{Name}': unknown clip \"{clipName}\".");
            return;
        }

        if (!restart && _current is { Finished: false } && _current.Clip == clip)
            return;

        if (_current != null && fadeSeconds > 0f)
        {
            _previous = _current;
            _fadeDuration = fadeSeconds;
            _fadeTime = 0f;
        }
        else
        {
            _previous = null;
        }

        _current = new Playback { Clip = clip };
    }

    /// <summary>Stops playback (leaves the pose as-is).</summary>
    public void Stop()
    {
        _current = null;
        _previous = null;
    }

    /// <summary>Jumps the current clip to a specific time in seconds.</summary>
    public void Seek(float time)
    {
        if (_current != null)
            _current.Time = Mathf.Clamp(time, 0f, _current.Clip.Duration);
    }

    protected override void OnReady()
    {
        ResolveTargets();
    }

    /// <summary>Finds the skeleton / search root again (call after re-parenting).</summary>
    public void ResolveTargets()
    {
        _nodeCache.Clear();
        _searchRoot = Parent as Node3D ?? FindAncestor<Node3D>();
        _skeleton = (Parent?.FindNode<Skeleton3D>()) ?? (Parent as Skeleton3D);
    }

    protected override void OnUpdate(float dt)
    {
        if (_current == null)
            return;

        Advance(_current, dt * Speed);

        if (_previous != null)
        {
            Advance(_previous, dt * Speed);
            _fadeTime += dt;
            float weight = _fadeDuration <= 0f ? 1f : Mathf.Clamp01(_fadeTime / _fadeDuration);

            ApplyClip(_previous.Clip, _previous.Time, 1f);
            ApplyClip(_current.Clip, _current.Time, Easing.Apply(Ease.InOutSine, weight));

            if (weight >= 1f)
                _previous = null;
        }
        else
        {
            ApplyClip(_current.Clip, _current.Time, 1f);
        }
    }

    private void Advance(Playback playback, float dt)
    {
        if (playback.Finished)
            return;

        playback.Time += dt;
        var clip = playback.Clip;

        if (clip.Loop)
        {
            if (clip.Duration > 0f)
            {
                playback.Time %= clip.Duration;
                if (playback.Time < 0f)
                    playback.Time += clip.Duration;
            }
        }
        else if (playback.Time >= clip.Duration)
        {
            playback.Time = clip.Duration;
            playback.Finished = true;
            AnimationFinished?.Invoke(clip.Name);
        }
        else if (playback.Time < 0f)
        {
            playback.Time = 0f;
            playback.Finished = true;
            AnimationFinished?.Invoke(clip.Name);
        }
    }

    private void ApplyClip(AnimationClip clip, float time, float weight)
    {
        foreach (var track in clip.Tracks)
        {
            int boneIndex = _skeleton?.FindBone(track.TargetName) ?? -1;
            if (boneIndex >= 0)
            {
                var bone = _skeleton!.Bones[boneIndex];
                switch (track.Property)
                {
                    case AnimationProperty.Position:
                        bone.LocalPosition = Vector3.Lerp(bone.LocalPosition, track.SampleVector(time), weight);
                        break;
                    case AnimationProperty.Rotation:
                        bone.LocalRotation = Quaternion.Slerp(bone.LocalRotation, track.SampleRotation(time), weight);
                        break;
                    case AnimationProperty.Scale:
                        bone.LocalScale = Vector3.Lerp(bone.LocalScale, track.SampleVector(time), weight);
                        break;
                }
                continue;
            }

            var node = ResolveNode(track.TargetName);
            if (node == null)
                continue;

            switch (track.Property)
            {
                case AnimationProperty.Position:
                    node.Position = Vector3.Lerp(node.Position, track.SampleVector(time), weight);
                    break;
                case AnimationProperty.Rotation:
                    node.Rotation = Quaternion.Slerp(node.Rotation, track.SampleRotation(time), weight);
                    break;
                case AnimationProperty.Scale:
                    node.Scale = Vector3.Lerp(node.Scale, track.SampleVector(time), weight);
                    break;
            }
        }
    }

    private Node3D? ResolveNode(string targetName)
    {
        if (_nodeCache.TryGetValue(targetName, out var cached))
            return cached;

        Node3D? node = null;
        if (_searchRoot != null)
        {
            node = _searchRoot.Name == targetName
                ? _searchRoot
                : _searchRoot.FindNode<Node3D>(targetName);
        }
        _nodeCache[targetName] = node;
        return node;
    }
}
