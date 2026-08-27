using Silk.NET.OpenAL;

namespace Ducz.Audio;

/// <summary>
/// Plays a clip without positioning (music, UI). Add as a node:
/// <code>
/// var music = AddChild(new AudioPlayer { Clip = Assets.LoadAudio("music.wav"), Loop = true, PlayOnReady = true });
/// </code>
/// </summary>
public class AudioPlayer : Node
{
    private uint _source;
    private bool _sourceCreated;

    /// <summary>The clip to play.</summary>
    public AudioClip? Clip { get; set; }

    /// <summary>Volume 0..1.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>Pitch multiplier (also changes speed).</summary>
    public float Pitch { get; set; } = 1f;

    /// <summary>Restart automatically when the clip ends.</summary>
    public bool Loop { get; set; }

    /// <summary>Start playing as soon as the node enters the tree.</summary>
    public bool PlayOnReady { get; set; }

    public AudioPlayer(string? name = null) : base(name) { }

    protected AL? Al => Engine.Audio is { Enabled: true } ? Engine.Audio.Al : null;

    protected uint Source
    {
        get
        {
            if (!_sourceCreated && Al != null)
            {
                _source = Al.GenSource();
                _sourceCreated = true;
                ConfigureSource();
            }
            return _source;
        }
    }

    /// <summary>Override to set spatial properties.</summary>
    protected virtual void ConfigureSource()
    {
        Al?.SetSourceProperty(Source, SourceBoolean.SourceRelative, true);
    }

    /// <summary>True while the source is playing.</summary>
    public bool IsPlaying
    {
        get
        {
            if (Al == null || !_sourceCreated)
                return false;
            Al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            return (SourceState)state == SourceState.Playing;
        }
    }

    /// <summary>Starts playback from the beginning (assign <see cref="Clip"/> first).</summary>
    public void Play()
    {
        if (Al == null || Clip == null || Clip.Buffer == 0)
            return;

        Al.SetSourceProperty(Source, SourceInteger.Buffer, Clip.Buffer);
        Al.SetSourceProperty(Source, SourceFloat.Gain, Mathf.Clamp01(Volume));
        Al.SetSourceProperty(Source, SourceFloat.Pitch, MathF.Max(0.01f, Pitch));
        Al.SetSourceProperty(Source, SourceBoolean.Looping, Loop);
        Al.SourcePlay(Source);
    }

    /// <summary>Stops playback.</summary>
    public void Stop()
    {
        if (Al != null && _sourceCreated)
            Al.SourceStop(_source);
    }

    protected override void OnReady()
    {
        if (PlayOnReady)
            Play();
    }

    protected override void OnExitTree()
    {
        if (Al != null && _sourceCreated)
        {
            Al.SourceStop(_source);
            Al.DeleteSource(_source);
            _sourceCreated = false;
        }
        base.OnExitTree();
    }
}

/// <summary>
/// A positional sound source: volume falls off with distance from the camera/listener.
/// The 3D position follows the closest <see cref="Node3D"/> ancestor.
/// </summary>
public class AudioPlayer3D : AudioPlayer
{
    private Node3D? _positionSource;

    /// <summary>Distance where attenuation starts.</summary>
    public float ReferenceDistance { get; set; } = 2f;

    /// <summary>Distance beyond which the sound does not get quieter.</summary>
    public float MaxDistance { get; set; } = 50f;

    /// <summary>How fast the sound attenuates with distance.</summary>
    public float Rolloff { get; set; } = 1f;

    public AudioPlayer3D(string? name = null) : base(name) { }

    protected override void ConfigureSource()
    {
        var al = Al;
        if (al == null)
            return;
        al.SetSourceProperty(Source, SourceBoolean.SourceRelative, false);
        al.SetSourceProperty(Source, SourceFloat.ReferenceDistance, ReferenceDistance);
        al.SetSourceProperty(Source, SourceFloat.MaxDistance, MaxDistance);
        al.SetSourceProperty(Source, SourceFloat.RolloffFactor, Rolloff);
    }

    protected override void OnReady()
    {
        _positionSource = FindAncestor<Node3D>();
        if (_positionSource == null)
            Log.Warning($"AudioPlayer3D '{Name}': no Node3D ancestor found; the sound will not be positioned.");
        base.OnReady();
    }

    protected override void OnUpdate(float dt)
    {
        if (Al != null && _positionSource != null && IsPlaying)
            Al.SetSourceProperty(Source, SourceVector3.Position, _positionSource.GlobalPosition);
    }
}
