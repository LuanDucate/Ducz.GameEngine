using Ducz.Audio;
using Ducz.Rendering;
using Ducz.UI;

namespace Ducz;

/// <summary>
/// Central asset loader with caching. Loading the same path twice returns the
/// same instance. Relative paths are resolved against <see cref="BasePath"/>
/// (defaults to the executable directory).
/// </summary>
public static class Assets
{
    private static readonly Dictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Root folder for relative asset paths. Defaults to the app directory.</summary>
    public static string BasePath { get; set; } = AppContext.BaseDirectory;

    /// <summary>The mounted asset pack, if any. Loads check it before the file system.</summary>
    public static AssetPack? Pack { get; private set; }

    /// <summary>
    /// Mounts a pack file (see <see cref="AssetPack"/>). Exported games call this at
    /// startup so every asset (scenes, models, textures, audio) loads from the pack.
    /// </summary>
    public static void MountPack(string packFile) => Pack = AssetPack.Open(packFile);

    /// <summary>True when the asset exists in the mounted pack or on disk.</summary>
    public static bool FileExists(string path) =>
        Pack?.Contains(path) == true || File.Exists(Resolve(path));

    /// <summary>
    /// Reads an asset's bytes - from the mounted pack when present there, else from disk.
    /// All engine loaders go through this, which is what makes packs transparent.
    /// </summary>
    public static byte[] ReadBytes(string path)
    {
        if (Pack?.Contains(path) == true)
            return Pack.Read(path);
        return File.ReadAllBytes(Resolve(path));
    }

    /// <summary>Loads (or returns the cached) texture.</summary>
    public static Texture2D LoadTexture(string path, TextureFilter filter = TextureFilter.Linear)
    {
        string key = $"tex:{filter}:{path}";
        if (_cache.TryGetValue(key, out var cached))
            return (Texture2D)cached;

        var texture = Texture2D.FromEncodedBytes(ReadBytes(path), filter);
        _cache[key] = texture;
        return texture;
    }

    /// <summary>Loads (or returns the cached) glTF/GLB model.</summary>
    public static Model LoadModel(string path)
    {
        string key = $"model:{path}";
        if (_cache.TryGetValue(key, out var cached))
            return (Model)cached;

        // Pass the raw path: Model.Load resolves pack-vs-disk itself.
        var model = Model.Load(path);
        _cache[key] = model;
        return model;
    }

    /// <summary>Loads (or returns the cached) WAV audio clip.</summary>
    public static AudioClip LoadAudio(string path)
    {
        string key = $"audio:{path}";
        if (_cache.TryGetValue(key, out var cached))
            return (AudioClip)cached;

        var clip = AudioClip.FromWavBytes(ReadBytes(path));
        _cache[key] = clip;
        return clip;
    }

    /// <summary>Loads (or returns the cached) TTF font at a pixel size.</summary>
    public static Font LoadFont(string path, int size = 24)
    {
        string key = $"font:{size}:{path}";
        if (_cache.TryGetValue(key, out var cached))
            return (Font)cached;

        var font = Font.FromBytes(ReadBytes(path), size);
        _cache[key] = font;
        return font;
    }

    /// <summary>Reads a text file (no caching).</summary>
    public static string LoadText(string path) => System.Text.Encoding.UTF8.GetString(ReadBytes(path));

    /// <summary>Removes everything from the cache (does not dispose GPU resources in use).</summary>
    public static void ClearCache() => _cache.Clear();

    /// <summary>
    /// Folder holding the content that ships with the engine (Prefabs, Textures). In a
    /// published build that is the folder next to the executable; running from the repository
    /// it is the build output. See <see cref="Resolve"/> for how it is used.
    /// </summary>
    public static string EngineRoot { get; set; } = FindEngineRoot();

    private static string FindEngineRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Prefabs")))
                return dir.FullName;
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Folders searched for shipped content, nearest first: the binary's own folder and every
    /// folder above it. A published install finds everything next to the executable; running
    /// from the repository, content kept at the repository root (the model packs the stock
    /// prefabs use) is found too instead of the search stopping at bin/Debug.
    /// </summary>
    private static IEnumerable<string> ContentRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string start in new[] { EngineRoot, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                if (seen.Add(dir.FullName))
                    yield return dir.FullName;
        }
    }

    /// <summary>
    /// Resolves a possibly-relative asset path: as given, then inside the open project
    /// (<see cref="BasePath"/>), then in the engine's content folders - so a prefab that ships
    /// with the engine finds its own textures and models from any project.
    /// </summary>
    public static string Resolve(string path)
    {
        if (Path.IsPathRooted(path) || File.Exists(path))
            return path;

        string inProject = Path.Combine(BasePath, path);
        if (Exists(inProject))
            return inProject;

        foreach (string root in ContentRoots())
        {
            string candidate = Path.Combine(root, path);
            if (Exists(candidate))
                return candidate;
        }
        return inProject;

        static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
    }
}
