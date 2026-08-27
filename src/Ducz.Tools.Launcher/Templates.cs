using System.Text.Json;
using Ducz.Serialization;

namespace Ducz.Tools.Launcher;

/// <summary>One entry in the launcher's project list.</summary>
public sealed class ProjectEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; }
}

/// <summary>
/// Launcher state persisted in %AppData%/DuczEngine/launcher.json:
/// known projects and the default location for new ones.
/// </summary>
public sealed class LauncherRegistry
{
    public List<ProjectEntry> Projects { get; set; } = new();
    public string? DefaultLocation { get; set; }

    private static string FilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DuczEngine", "launcher.json");

    public static LauncherRegistry Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LauncherRegistry>(
                    File.ReadAllText(FilePath), SceneDocument.JsonOptions) ?? new LauncherRegistry();
        }
        catch (Exception ex)
        {
            Log.Warning($"Launcher registry unreadable, starting fresh: {ex.Message}");
        }
        return new LauncherRegistry();
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SceneDocument.JsonOptions));
    }

    public string ResolveDefaultLocation() => DefaultLocation ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DuczProjects");
}

/// <summary>A new-project template: a name, a blurb and a scene generator.</summary>
public sealed record ProjectTemplate(string Name, string Description, Func<string, SceneDocument> BuildScene);

/// <summary>The built-in templates offered when creating a project.</summary>
public static class Templates
{
    public static readonly IReadOnlyList<ProjectTemplate> All = new List<ProjectTemplate>
    {
        new("Empty Map",
            "Textured ground, sun and spawn.\nA blank canvas for your map.",
            BuildEmpty),
        new("Arena",
            "Walled arena with ramps, pillars,\ncrates and lights.",
            BuildArena),
        new("Terrain",
            "Rolling hills terrain with fog\nand scattered props.",
            BuildTerrain)
    };

    private static Dictionary<string, MaterialDef> DefaultMaterials() =>
        Ducz.Serialization.DefaultMaterials.Create();

    private static SceneDocument NewDocument(string name)
    {
        var doc = new SceneDocument
        {
            Name = name,
            Environment = new EnvironmentDef
            {
                SkyTop = "#2a4a8f",
                SkyHorizon = "#b8cfe8",
                AmbientIntensity = 0.3f,
                Fog = new FogDef { Color = "#b8cfe8", Start = 40f, End = 160f }
            }
        };
        foreach (var (key, material) in DefaultMaterials())
            doc.Materials[key] = material;

        doc.Nodes.Add(new NodeDef { Type = "directionalLight", Name = "Sun", RotationDegrees = new[] { -48f, 35f, 0f }, Energy = 1.1f });
        return doc;
    }

    private static void AddPlayerSet(SceneDocument doc, float[] spawn)
    {
        doc.Nodes.Add(new NodeDef { Type = "spawn", Name = "SpawnPoint", Position = spawn });
        doc.Nodes.Add(new NodeDef { Type = "player", Name = "Player", Position = new[] { spawn[0], spawn[1] + 1.2f, spawn[2] } });
        doc.Nodes.Add(new NodeDef { Type = "thirdPersonCamera", Name = "MainCamera", Target = "Player", Distance = 6.5f, TargetHeight = 1.4f, Current = true });
    }

    private static SceneDocument BuildEmpty(string name)
    {
        var doc = NewDocument(name);
        doc.Nodes.Add(new NodeDef { Type = "floor", Name = "Ground", Size = new[] { 40f, 40f }, Material = "grass", WorldUv = true });
        AddPlayerSet(doc, new[] { 0f, 0f, 0f });
        return doc;
    }

    private static SceneDocument BuildArena(string name)
    {
        var doc = NewDocument(name);
        doc.Nodes.Add(new NodeDef { Type = "floor", Name = "Ground", Size = new[] { 36f, 36f }, Material = "stone", WorldUv = true });

        // Perimeter walls
        (float x, float z, float yaw)[] walls = { (0, -18, 0), (0, 18, 0), (-18, 0, 90), (18, 0, 90) };
        for (int i = 0; i < walls.Length; i++)
        {
            doc.Nodes.Add(new NodeDef
            {
                Type = "wall", Name = $"Wall_{i}", Size = new[] { 36f, 4f, 0.6f }, Material = "brick", WorldUv = true,
                Position = new[] { walls[i].x, 2f, walls[i].z },
                RotationDegrees = walls[i].yaw != 0 ? new[] { 0f, walls[i].yaw, 0f } : null
            });
        }

        // Center platform + ramps
        doc.Nodes.Add(new NodeDef
        {
            Type = "static", Name = "Platform",
            Mesh = new MeshDef { Primitive = "box", Size = new[] { 8f, 1f, 8f } },
            Material = "stone", Position = new[] { 0f, 0.5f, 0f }, WorldUv = true
        });
        doc.Nodes.Add(new NodeDef { Type = "ramp", Name = "RampN", Size = new[] { 3f, 1f, 5f }, Material = "stone", Position = new[] { 0f, 0f, -6.5f }, WorldUv = true });
        doc.Nodes.Add(new NodeDef { Type = "ramp", Name = "RampS", Size = new[] { 3f, 1f, 5f }, Material = "stone", Position = new[] { 0f, 0f, 6.5f }, RotationDegrees = new[] { 0f, 180f, 0f }, WorldUv = true });

        // Pillars with lights
        (float x, float z)[] pillars = { (-12, -12), (12, -12), (-12, 12), (12, 12) };
        for (int i = 0; i < pillars.Length; i++)
        {
            doc.Nodes.Add(new NodeDef
            {
                Type = "static", Name = $"Pillar_{i}",
                Mesh = new MeshDef { Primitive = "cylinder", Radius = 0.5f, Height = 5f },
                Material = "metal", Position = new[] { pillars[i].x, 2.5f, pillars[i].z }, WorldUv = true
            });
            doc.Nodes.Add(new NodeDef
            {
                Type = "pointLight", Name = $"PillarLight_{i}",
                Color = "#ffd9a0", Energy = 2f, Range = 12f,
                Position = new[] { pillars[i].x, 5.6f, pillars[i].z }
            });
        }

        // Crates to push around
        for (int i = 0; i < 5; i++)
        {
            doc.Nodes.Add(new NodeDef
            {
                Type = "crate", Name = $"Crate_{i}", Size = new[] { 1f }, Material = "wood", Mass = 2f, WorldUv = true,
                Position = new[] { -8f + i * 3.5f, 2f + i * 1.1f, 9f }
            });
        }

        AddPlayerSet(doc, new[] { 0f, 1.2f, 12f });
        return doc;
    }

    private static SceneDocument BuildTerrain(string name)
    {
        var doc = NewDocument(name);
        doc.Environment!.Fog = new FogDef { Color = "#b8cfe8", Start = 35f, End = 130f };

        doc.Nodes.Add(new NodeDef
        {
            Type = "terrain", Name = "Hills",
            Terrain = new TerrainDef { Mode = "hills", SizeX = 160f, SizeZ = 160f, Amplitude = 3f, Frequency = 0.07f, Resolution = 140 }
        });

        for (int i = 0; i < 6; i++)
        {
            float angle = i / 6f * 360f * Mathf.Deg2Rad;
            doc.Nodes.Add(new NodeDef
            {
                Type = "crate", Name = $"Crate_{i}", Size = new[] { 1.1f }, Material = "wood",
                Position = new[] { MathF.Cos(angle) * 14f, 6f, MathF.Sin(angle) * 14f }
            });
        }

        doc.Nodes.Add(new NodeDef
        {
            Type = "pointLight", Name = "CampLight", Color = "#ff8a3a", Energy = 2.5f, Range = 12f,
            Position = new[] { 4f, 3f, 4f },
            Children = new List<NodeDef>
            {
                new()
                {
                    Type = "particles", Name = "CampFire",
                    Position = new[] { 0f, -1.5f, 0f },
                    Particles = new ParticlesDef
                    {
                        Amount = 50, Lifetime = 1.2f, Speed = 2f, Spread = 18f,
                        Gravity = new[] { 0f, 1f, 0f },
                        StartSize = 0.3f, EndSize = 0.05f,
                        StartColor = "#ffb400", EndColor = "#ff3c0000", Additive = true
                    }
                }
            }
        });

        AddPlayerSet(doc, new[] { 0f, 2f, 0f });
        return doc;
    }
}
