# Audio

OpenAL-based audio with graceful degradation: if the machine has no audio device, every call becomes a safe no-op (a warning is logged) - your game still runs.

## Quick sounds (fire and forget)

```csharp
var clip = Assets.LoadAudio("Assets/Sfx/coin.wav");

Engine.Audio.Play(clip);                          // non-positional (UI, stingers)
Engine.Audio.Play(clip, volume: 0.8f, pitch: 1.2f);
Engine.Audio.PlayAt(clip, explosion.GlobalPosition);  // 3D positioned one-shot
```

The 3D listener automatically follows the current camera.

## Procedural clips - no files needed

Perfect for prototypes and game jams:

```csharp
var jump   = AudioClip.CreateTone(660f, 0.15f, WaveForm.Square, volume: 0.4f);
var laser  = AudioClip.CreateSweep(900f, 300f, 0.12f, WaveForm.Square);   // freq slides down
var powerUp= AudioClip.CreateSweep(400f, 1600f, 0.5f, WaveForm.Triangle);
var boom   = AudioClip.CreateSweep(500f, 60f, 0.6f, WaveForm.Noise);

Engine.Audio.Play(jump);
```

Waveforms: `Sine`, `Square`, `Triangle`, `Saw`, `Noise`. Clips get a tiny fade in/out automatically so they never click. Create clips once (e.g. in `OnReady`) and reuse them.

## WAV files

`Assets.LoadAudio(path)` reads PCM WAV - 8 or 16-bit, mono or stereo, any sample rate. Convert other formats:

```bash
ffmpeg -i input.mp3 -acodec pcm_s16le -ar 44100 output.wav
```

(Only mono clips are truly positional in 3D - stereo files play as-is.)

## Player nodes

For sounds tied to objects (loops, music, engine hums), use player nodes instead of one-shots.

### AudioPlayer - non-positional

```csharp
var music = AddChild(new AudioPlayer
{
    Clip = Assets.LoadAudio("Assets/Music/theme.wav"),
    Loop = true,
    Volume = 0.5f,
    PlayOnReady = true
});
// music.Play(); music.Stop(); music.IsPlaying; music.Pitch = 0.9f;
```

### AudioPlayer3D - positional

Attach under any `Node3D`; the sound follows it and attenuates with distance:

```csharp
var campfire = AddChild(new Node3D());
campfire.AddChild(new AudioPlayer3D
{
    Clip = crackleLoop,
    Loop = true,
    PlayOnReady = true,
    ReferenceDistance = 2f,   // full volume inside this radius
    MaxDistance = 30f,        // silent beyond (roughly)
    Rolloff = 1f              // attenuation speed
});
```

## Master volume

```csharp
Engine.Audio.MasterVolume = 0.7f;   // 0..1
Engine.Audio.Enabled                 // false when no device was found
```

## Tips

- Randomize pitch slightly on repeated effects to avoid machine-gun ear fatigue: `Play(clip, pitch: Rng.Range(0.95f, 1.05f))`.
- Long music tracks are fully decoded into memory (no streaming yet) - a 3-minute stereo WAV is ~30 MB of RAM. Keep music short or mono, or lower the sample rate.
- Disable audio entirely (e.g. for servers/CI) with `new GameSettings { NoAudio = true }`.
