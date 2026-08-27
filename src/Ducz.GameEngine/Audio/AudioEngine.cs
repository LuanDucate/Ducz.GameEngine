using System.Numerics;
using Silk.NET.OpenAL;

namespace Ducz.Audio;

/// <summary>
/// Audio subsystem built on OpenAL. Degrades gracefully: when no audio device
/// exists every call becomes a no-op. Access via <c>Engine.Audio</c>.
/// The listener automatically follows the current <see cref="Camera3D"/>.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    internal AL? Al { get; }
    private readonly ALContext? _alc;
    private unsafe Device* _device;
    private unsafe Context* _context;

    private readonly List<uint> _oneShotSources = new();

    /// <summary>False when audio could not be initialized (missing device/driver).</summary>
    public bool Enabled { get; }

    /// <summary>Master volume (0..1) applied to the listener.</summary>
    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Mathf.Clamp01(value);
            Al?.SetListenerProperty(ListenerFloat.Gain, _masterVolume);
        }
    }
    private float _masterVolume = 1f;

    internal unsafe AudioEngine(bool disabled)
    {
        if (disabled)
        {
            Enabled = false;
            return;
        }

        try
        {
            _alc = ALContext.GetApi(true);
            Al = AL.GetApi(true);
            _device = _alc.OpenDevice("");
            if (_device == null)
            {
                Log.Warning("Audio: no output device found. Sound disabled.");
                Enabled = false;
                return;
            }

            _context = _alc.CreateContext(_device, null);
            _alc.MakeContextCurrent(_context);
            Al.DistanceModel(DistanceModel.InverseDistanceClamped);
            Enabled = true;
            Log.Info("Audio initialized (OpenAL Soft).");
        }
        catch (Exception ex)
        {
            Log.Warning($"Audio initialization failed: {ex.Message}. Sound disabled.");
            Enabled = false;
        }
    }

    /// <summary>Plays a clip immediately, non-positional (UI sounds, music stingers).</summary>
    public void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (!Enabled || clip.Buffer == 0)
            return;

        uint source = CreateOneShotSource(clip, volume, pitch);
        Al!.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
        Al.SetSourceProperty(source, SourceVector3.Position, Vector3.Zero);
        Al.SourcePlay(source);
    }

    /// <summary>Plays a clip at a world position (footsteps, explosions).</summary>
    public void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = 50f)
    {
        if (!Enabled || clip.Buffer == 0)
            return;

        uint source = CreateOneShotSource(clip, volume, pitch);
        Al!.SetSourceProperty(source, SourceVector3.Position, position);
        Al.SetSourceProperty(source, SourceFloat.MaxDistance, maxDistance);
        Al.SetSourceProperty(source, SourceFloat.ReferenceDistance, 2f);
        Al.SourcePlay(source);
    }

    private uint CreateOneShotSource(AudioClip clip, float volume, float pitch)
    {
        uint source = Al!.GenSource();
        Al.SetSourceProperty(source, SourceInteger.Buffer, clip.Buffer);
        Al.SetSourceProperty(source, SourceFloat.Gain, Mathf.Clamp01(volume));
        Al.SetSourceProperty(source, SourceFloat.Pitch, MathF.Max(0.01f, pitch));
        _oneShotSources.Add(source);
        return source;
    }

    /// <summary>Called by the engine every frame.</summary>
    internal void Update()
    {
        if (!Enabled)
            return;

        // Listener follows the active camera.
        var camera = Camera3D.CurrentCamera;
        if (camera is { IsInsideTree: true })
        {
            var forward = camera.GlobalForward;
            var up = camera.GlobalUp;
            Al!.SetListenerProperty(ListenerVector3.Position, camera.GlobalPosition);
            Span<float> orientation = stackalloc float[6]
            {
                forward.X, forward.Y, forward.Z,
                up.X, up.Y, up.Z
            };
            unsafe
            {
                fixed (float* ptr = orientation)
                {
                    Al.SetListenerProperty(ListenerFloatArray.Orientation, ptr);
                }
            }
        }

        // Clean up finished one-shots.
        for (int i = _oneShotSources.Count - 1; i >= 0; i--)
        {
            Al!.GetSourceProperty(_oneShotSources[i], GetSourceInteger.SourceState, out int state);
            if ((SourceState)state != SourceState.Playing)
            {
                Al.DeleteSource(_oneShotSources[i]);
                _oneShotSources.RemoveAt(i);
            }
        }
    }

    public unsafe void Dispose()
    {
        if (!Enabled)
            return;

        foreach (var source in _oneShotSources)
            Al!.DeleteSource(source);
        _oneShotSources.Clear();

        if (_context != null)
        {
            _alc!.MakeContextCurrent(null);
            _alc.DestroyContext(_context);
        }
        if (_device != null)
            _alc!.CloseDevice(_device);
    }
}
