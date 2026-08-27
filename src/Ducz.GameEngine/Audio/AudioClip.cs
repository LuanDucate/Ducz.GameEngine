using Silk.NET.OpenAL;

namespace Ducz.Audio;

/// <summary>Waveforms for procedural tones.</summary>
public enum WaveForm
{
    Sine,
    Square,
    Triangle,
    Saw,
    Noise
}

/// <summary>
/// Audio data uploaded to the sound card. Load WAV files or generate tones in code
/// (handy while prototyping, no asset files needed):
/// <code>
/// var jump = AudioClip.CreateTone(660, 0.15f, WaveForm.Square);
/// Engine.Audio.Play(jump);
/// </code>
/// </summary>
public sealed class AudioClip
{
    /// <summary>OpenAL buffer handle (0 when audio is disabled).</summary>
    public uint Buffer { get; }

    /// <summary>Length in seconds.</summary>
    public float Duration { get; }

    private unsafe AudioClip(byte[] pcm, BufferFormat format, int sampleRate)
    {
        int bytesPerSample = format is BufferFormat.Mono16 or BufferFormat.Stereo16 ? 2 : 1;
        int channels = format is BufferFormat.Stereo16 or BufferFormat.Stereo8 ? 2 : 1;
        Duration = pcm.Length / (float)(sampleRate * bytesPerSample * channels);

        var audio = Engine.Audio;
        if (audio is not { Enabled: true })
        {
            Buffer = 0;
            return;
        }

        var al = audio.Al!;
        Buffer = al.GenBuffer();
        fixed (byte* ptr = pcm)
        {
            al.BufferData(Buffer, format, ptr, pcm.Length, sampleRate);
        }
    }

    // ------------------------------------------------------------------
    // WAV loading (PCM 8/16-bit, mono or stereo)
    // ------------------------------------------------------------------

    /// <summary>Loads a .wav file (PCM 8-bit or 16-bit, mono or stereo).</summary>
    public static AudioClip FromWavFile(string path) =>
        FromWavBytes(File.ReadAllBytes(path), path);

    /// <summary>Parses WAV data from memory (used for asset-pack loading).</summary>
    public static AudioClip FromWavBytes(byte[] wavData, string path = "wav data")
    {
        using var stream = new MemoryStream(wavData);
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException($"{path}: not a RIFF/WAV file.");
        reader.ReadInt32(); // file size
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException($"{path}: not a WAV file.");

        short channels = 0, bitsPerSample = 0;
        int sampleRate = 0;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            switch (chunkId)
            {
                case "fmt ":
                {
                    short audioFormat = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32(); // byte rate
                    reader.ReadInt16(); // block align
                    bitsPerSample = reader.ReadInt16();
                    if (chunkSize > 16)
                        reader.ReadBytes(chunkSize - 16);
                    if (audioFormat != 1)
                        throw new InvalidDataException($"{path}: only PCM WAV is supported (format {audioFormat}).");
                    break;
                }
                case "data":
                    data = reader.ReadBytes(chunkSize);
                    break;
                default:
                    reader.ReadBytes(chunkSize + (chunkSize % 2)); // chunks are word-aligned
                    break;
            }

            if (data != null && sampleRate != 0)
                break;
        }

        if (data == null || sampleRate == 0)
            throw new InvalidDataException($"{path}: missing fmt/data chunks.");

        var format = (channels, bitsPerSample) switch
        {
            (1, 8) => BufferFormat.Mono8,
            (1, 16) => BufferFormat.Mono16,
            (2, 8) => BufferFormat.Stereo8,
            (2, 16) => BufferFormat.Stereo16,
            _ => throw new InvalidDataException($"{path}: unsupported WAV layout ({channels}ch, {bitsPerSample}-bit).")
        };

        return new AudioClip(data, format, sampleRate);
    }

    // ------------------------------------------------------------------
    // Procedural audio
    // ------------------------------------------------------------------

    /// <summary>Generates a tone (mono 16-bit, 44.1 kHz) with a short fade to avoid clicks.</summary>
    public static AudioClip CreateTone(float frequency, float duration, WaveForm wave = WaveForm.Sine,
        float volume = 0.5f)
    {
        const int sampleRate = 44100;
        int sampleCount = Math.Max(1, (int)(duration * sampleRate));
        var pcm = new byte[sampleCount * 2];

        float fadeSamples = MathF.Min(sampleCount * 0.5f, sampleRate * 0.01f);

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float phase = t * frequency % 1f;

            float sample = wave switch
            {
                WaveForm.Square => phase < 0.5f ? 1f : -1f,
                WaveForm.Triangle => 4f * MathF.Abs(phase - 0.5f) - 1f,
                WaveForm.Saw => 2f * phase - 1f,
                WaveForm.Noise => Rng.Range(-1f, 1f),
                _ => MathF.Sin(phase * Mathf.Tau)
            };

            // Fade in/out
            float envelope = MathF.Min(1f, MathF.Min(i / fadeSamples, (sampleCount - 1 - i) / fadeSamples));
            short value = (short)(Mathf.Clamp(sample * volume * envelope, -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)(value >> 8);
        }

        return new AudioClip(pcm, BufferFormat.Mono16, sampleRate);
    }

    /// <summary>Generates a simple frequency sweep - nice for laser/jump effects.</summary>
    public static AudioClip CreateSweep(float startFrequency, float endFrequency, float duration,
        WaveForm wave = WaveForm.Sine, float volume = 0.5f)
    {
        const int sampleRate = 44100;
        int sampleCount = Math.Max(1, (int)(duration * sampleRate));
        var pcm = new byte[sampleCount * 2];
        float fadeSamples = MathF.Min(sampleCount * 0.5f, sampleRate * 0.01f);

        float phase = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            float progress = i / (float)sampleCount;
            float frequency = Mathf.Lerp(startFrequency, endFrequency, progress);
            phase += frequency / sampleRate;
            float p = phase % 1f;

            float sample = wave switch
            {
                WaveForm.Square => p < 0.5f ? 1f : -1f,
                WaveForm.Triangle => 4f * MathF.Abs(p - 0.5f) - 1f,
                WaveForm.Saw => 2f * p - 1f,
                WaveForm.Noise => Rng.Range(-1f, 1f),
                _ => MathF.Sin(p * Mathf.Tau)
            };

            float envelope = MathF.Min(1f, MathF.Min(i / fadeSamples, (sampleCount - 1 - i) / fadeSamples));
            short value = (short)(Mathf.Clamp(sample * volume * envelope, -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)(value >> 8);
        }

        return new AudioClip(pcm, BufferFormat.Mono16, sampleRate);
    }
}
