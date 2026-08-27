using System.Text;

namespace Ducz;

/// <summary>
/// A single-file asset archive ("game.duczpak") used by exported games so that
/// scene JSON, models and textures don't ship as loose files. Entries are lightly
/// scrambled - not encryption, just enough that assets aren't casually readable,
/// which is the same approach classic .pak files take.
///
/// Runtime: <c>Assets.MountPack("game.duczpak")</c> - every asset load
/// (textures, models, audio, scenes, fonts) then checks the pack first.
/// </summary>
public sealed class AssetPack
{
    private const string Magic = "DUCZPAK1";

    private readonly string _file;
    private readonly Dictionary<string, (long Offset, int Length)> _entries = new(StringComparer.OrdinalIgnoreCase);

    private AssetPack(string file) => _file = file;

    /// <summary>Number of entries in the pack.</summary>
    public int Count => _entries.Count;

    /// <summary>Normalizes an asset path into the pack's key form (forward slashes, lowercase).</summary>
    public static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/', '.').ToLowerInvariant();

    // ------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------

    /// <summary>Opens a pack file and reads its entry table.</summary>
    public static AssetPack Open(string file)
    {
        var pack = new AssetPack(file);
        using var stream = File.OpenRead(file);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        var magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
        if (magic != Magic)
            throw new InvalidDataException($"{file} is not a Ducz asset pack.");

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string path = reader.ReadString();
            long offset = reader.ReadInt64();
            int length = reader.ReadInt32();
            pack._entries[path] = (offset, length);
        }

        Log.Info($"Asset pack mounted: {Path.GetFileName(file)} ({count} entries)");
        return pack;
    }

    /// <summary>True when the pack contains the (normalized) path.</summary>
    public bool Contains(string path) => _entries.ContainsKey(NormalizePath(path));

    /// <summary>Reads and unscrambles an entry.</summary>
    public byte[] Read(string path)
    {
        string key = NormalizePath(path);
        if (!_entries.TryGetValue(key, out var entry))
            throw new FileNotFoundException($"Asset \"{path}\" not found in pack.");

        using var stream = File.OpenRead(_file);
        stream.Seek(entry.Offset, SeekOrigin.Begin);
        var data = new byte[entry.Length];
        int read = 0;
        while (read < data.Length)
        {
            int n = stream.Read(data, read, data.Length - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
        Scramble(data, key);
        return data;
    }

    /// <summary>Enumerates entry paths, optionally filtered by a normalized prefix (e.g. "assets/").</summary>
    public IEnumerable<string> EnumeratePaths(string? prefix = null)
    {
        var normalized = prefix != null ? NormalizePath(prefix) : null;
        foreach (var key in _entries.Keys)
            if (normalized == null || key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                yield return key;
    }

    // ------------------------------------------------------------------
    // Writing (used by the scene editor's exporter)
    // ------------------------------------------------------------------

    /// <summary>Creates a pack from (packPath, data) entries.</summary>
    public static void Create(string file, IReadOnlyList<(string Path, byte[] Data)> entries)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(file);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);

        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(entries.Count);

        // Reserve the table, then come back to fill offsets.
        var tablePositions = new long[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            writer.Write(NormalizePath(entries[i].Path));
            tablePositions[i] = stream.Position;
            writer.Write(0L);                    // offset placeholder
            writer.Write(entries[i].Data.Length);
        }

        var offsets = new long[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            offsets[i] = stream.Position;
            var scrambled = (byte[])entries[i].Data.Clone();
            Scramble(scrambled, NormalizePath(entries[i].Path));
            writer.Write(scrambled);
        }

        for (int i = 0; i < entries.Count; i++)
        {
            stream.Seek(tablePositions[i], SeekOrigin.Begin);
            writer.Write(offsets[i]);
        }
    }

    /// <summary>Symmetric XOR scramble keyed by the entry path (applied on write and read).</summary>
    private static void Scramble(byte[] data, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key + ":ducz");
        for (int i = 0; i < data.Length; i++)
            data[i] ^= (byte)(keyBytes[i % keyBytes.Length] + (i * 31 & 0xFF));
    }
}
