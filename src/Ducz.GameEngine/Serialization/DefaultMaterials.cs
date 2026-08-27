namespace Ducz.Serialization;

/// <summary>
/// The palette every new map starts with. The surfaces use the prototype grid textures that
/// ship with the engine (<c>Textures/prototype/</c>): a near-white 1 m grid multiplied by the
/// material's own colour, so an untextured map reads as a deliberate blockout - proper scale
/// reference on every wall and floor - instead of flat paint. Drop your own texture on a
/// material at any time and it replaces the grid.
/// </summary>
public static class DefaultMaterials
{
    private const string Grid = "Textures/prototype/grid_light.png";
    private const string GridMid = "Textures/prototype/grid_mid.png";
    private const string GridDark = "Textures/prototype/grid_dark.png";
    private const string Checker = "Textures/prototype/checker.png";

    /// <summary>The standard starting palette, keyed by name.</summary>
    public static Dictionary<string, MaterialDef> Create() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["stone"]     = Gridded("#b4b2ad", GridMid, specular: 0.12f),
        ["concrete"]  = Gridded("#a5a29c", GridMid, specular: 0.08f),
        ["brick"]     = Gridded("#a85a45", Grid),
        ["wood"]      = Gridded("#a9744f", Grid, specular: 0.2f, shininess: 12f),
        ["plaster"]   = Gridded("#ded5c6", Grid),
        ["asphalt"]   = Gridded("#54565a", GridDark, specular: 0.07f),
        ["sidewalk"]  = Gridded("#b0aca3", Checker, specular: 0.05f),
        ["roof"]      = Gridded("#8f4a35", Grid, specular: 0.12f),
        ["grass"]     = Gridded("#6d9a52", Grid, specular: 0.04f),
        ["dirt"]      = Gridded("#8a7550", Grid, specular: 0.03f),
        ["metal"]     = Gridded("#b8bec8", GridMid, specular: 0.9f, shininess: 96f),
        ["glow"]      = new MaterialDef { Albedo = "#ffd75e", Emission = "#ffb400", EmissionEnergy = 0.8f },
        ["glass"]     = new MaterialDef { Albedo = "#7fd4ff77", Transparent = true, Specular = 0.9f, Shininess = 128f },
        // The classic coloured prototype tiles, for blocking out by function.
        ["proto grey"]   = Gridded("#ffffff", "Textures/prototype/proto_grey.png"),
        ["proto orange"] = Gridded("#ffffff", "Textures/prototype/proto_orange.png"),
        ["proto blue"]   = Gridded("#ffffff", "Textures/prototype/proto_blue.png"),
        ["proto green"]  = Gridded("#ffffff", "Textures/prototype/proto_green.png"),
        ["proto red"]    = Gridded("#ffffff", "Textures/prototype/proto_red.png"),
    };

    private static MaterialDef Gridded(string albedo, string texture, float specular = 0.06f,
                                       float shininess = 16f) => new()
    {
        Albedo = albedo,
        Texture = texture,
        UvScale = new[] { 1f, 1f },     // one grid square per metre (with worldUv)
        Specular = specular,
        Shininess = shininess
    };
}
