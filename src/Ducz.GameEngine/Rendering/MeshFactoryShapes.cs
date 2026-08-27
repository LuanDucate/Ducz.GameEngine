using System.Numerics;

namespace Ducz.Rendering;

/// <summary>
/// Building-block shapes for level building: wedges, roofs, stairs, arches, curved walls,
/// tubes, prisms and rounded boxes. They complement the basic primitives in
/// <see cref="MeshFactory"/> and, like those, come in a GPU (<see cref="Mesh"/>) and a CPU
/// (<see cref="MeshData"/>) flavor. Shapes are centered on the origin and sized in meters.
/// </summary>
public static partial class MeshFactory
{
    // ------------------------------------------------------------------
    // Helpers
    //
    // Every face is added with the direction it should face ("outward"). The helper flips the
    // winding when needed, so a mistake in corner order can never produce an inside-out face.
    // ------------------------------------------------------------------

    private static void Face(List<Vertex> vertices, List<uint> indices, Vector3 outward,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
    {
        var normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() < 1e-12f)
            return;
        if (Vector3.Dot(normal, outward) < 0f)
        {
            (b, d) = (d, b);
            (uvB, uvD) = (uvD, uvB);
            normal = -normal;
        }
        normal = Vector3.Normalize(normal);
        uint start = (uint)vertices.Count;
        vertices.Add(new Vertex(a, normal, uvA));
        vertices.Add(new Vertex(b, normal, uvB));
        vertices.Add(new Vertex(c, normal, uvC));
        vertices.Add(new Vertex(d, normal, uvD));
        indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
    }

    private static void Face(List<Vertex> vertices, List<uint> indices, Vector3 outward,
        Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        var normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() < 1e-12f)
            return;
        if (Vector3.Dot(normal, outward) < 0f)
        {
            (b, c) = (c, b);
            (uvB, uvC) = (uvC, uvB);
            normal = -normal;
        }
        normal = Vector3.Normalize(normal);
        uint start = (uint)vertices.Count;
        vertices.Add(new Vertex(a, normal, uvA));
        vertices.Add(new Vertex(b, normal, uvB));
        vertices.Add(new Vertex(c, normal, uvC));
        indices.AddRange(new[] { start, start + 1, start + 2 });
    }

    private static Vector2 UV(float u, float v) => new(u, v);

    // ------------------------------------------------------------------
    // Wedge (solid ramp)
    // ------------------------------------------------------------------

    /// <summary>
    /// A solid ramp/wedge: full height at +Z, zero at -Z, with a walkable sloped top.
    /// Centered on the origin like a box, so it drops into a grid the same way.
    /// </summary>
    public static Mesh Wedge(float width = 2f, float height = 1f, float length = 3f, bool worldUv = false) =>
        WedgeData(width, height, length, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="Wedge"/>.</summary>
    public static MeshData WedgeData(float width = 2f, float height = 1f, float length = 3f, bool worldUv = false)
    {
        float x = width * 0.5f, y = height * 0.5f, z = length * 0.5f;
        var v = new List<Vertex>();
        var i = new List<uint>();
        float slope = MathF.Sqrt(height * height + length * length);
        float uw = worldUv ? width : 1f, ul = worldUv ? length : 1f, uh = worldUv ? height : 1f, us = worldUv ? slope : 1f;

        Vector3 lbL = new(-x, -y, -z), lbR = new(x, -y, -z);   // bottom, low end (-Z)
        Vector3 lfL = new(-x, -y, z), lfR = new(x, -y, z);     // bottom, high end (+Z)
        Vector3 hfL = new(-x, y, z), hfR = new(x, y, z);       // top of the tall end

        Face(v, i, -Vector3.UnitY, lbL, lfL, lfR, lbR, UV(0, 0), UV(0, ul), UV(uw, ul), UV(uw, 0));                 // bottom
        Face(v, i, Vector3.Normalize(new Vector3(0, length, -height)), lbL, lbR, hfR, hfL,
            UV(0, 0), UV(uw, 0), UV(uw, us), UV(0, us));                                                            // slope
        Face(v, i, Vector3.UnitZ, lfL, hfL, hfR, lfR, UV(0, 0), UV(0, uh), UV(uw, uh), UV(uw, 0));                  // tall face
        Face(v, i, -Vector3.UnitX, lbL, hfL, lfL, UV(0, 0), UV(ul, uh), UV(ul, 0));                                 // left side
        Face(v, i, Vector3.UnitX, lbR, lfR, hfR, UV(0, 0), UV(ul, 0), UV(ul, uh));                                  // right side
        return new MeshData(v, i);
    }

    // ------------------------------------------------------------------
    // Roofs
    // ------------------------------------------------------------------

    /// <summary>A gable roof: two slopes meeting at a ridge that runs along X. Centered on the origin.</summary>
    public static Mesh RoofGable(float width = 6f, float height = 2f, float depth = 8f, float overhang = 0f, bool worldUv = false) =>
        RoofGableData(width, height, depth, overhang, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="RoofGable"/>.</summary>
    public static MeshData RoofGableData(float width = 6f, float height = 2f, float depth = 8f, float overhang = 0f, bool worldUv = false)
    {
        float x = width * 0.5f + overhang, y = height * 0.5f, z = depth * 0.5f + overhang;
        var v = new List<Vertex>();
        var i = new List<uint>();
        float w = x * 2, d = z * 2;
        float slope = MathF.Sqrt(height * height + z * z);
        float uw = worldUv ? w : 1f, ud = worldUv ? d : 1f, us = worldUv ? slope : 1f, uh = worldUv ? height : 1f;

        Vector3 eaveNL = new(-x, -y, -z), eaveNR = new(x, -y, -z);
        Vector3 eaveSL = new(-x, -y, z), eaveSR = new(x, -y, z);
        Vector3 ridgeL = new(-x, y, 0), ridgeR = new(x, y, 0);

        Face(v, i, Vector3.Normalize(new Vector3(0, z, -height)), eaveNL, eaveNR, ridgeR, ridgeL,
            UV(0, 0), UV(uw, 0), UV(uw, us), UV(0, us));                                                            // -Z slope
        Face(v, i, Vector3.Normalize(new Vector3(0, z, height)), ridgeL, ridgeR, eaveSR, eaveSL,
            UV(0, us), UV(uw, us), UV(uw, 0), UV(0, 0));                                                            // +Z slope
        Face(v, i, -Vector3.UnitX, eaveNL, ridgeL, eaveSL, UV(0, 0), UV(ud / 2, uh), UV(ud, 0));                    // gable end -X
        Face(v, i, Vector3.UnitX, eaveNR, eaveSR, ridgeR, UV(0, 0), UV(ud, 0), UV(ud / 2, uh));                     // gable end +X
        Face(v, i, -Vector3.UnitY, eaveNL, eaveSL, eaveSR, eaveNR, UV(0, 0), UV(0, ud), UV(uw, ud), UV(uw, 0));     // underside
        return new MeshData(v, i);
    }

    /// <summary>A hip roof: four slopes meeting at a ridge along X (ridgeLength 0 = pyramid roof).</summary>
    public static Mesh RoofHip(float width = 6f, float height = 2f, float depth = 8f, float ridgeLength = 3f, float overhang = 0f, bool worldUv = false) =>
        RoofHipData(width, height, depth, ridgeLength, overhang, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="RoofHip"/>.</summary>
    public static MeshData RoofHipData(float width = 6f, float height = 2f, float depth = 8f, float ridgeLength = 3f, float overhang = 0f, bool worldUv = false)
    {
        float x = width * 0.5f + overhang, y = height * 0.5f, z = depth * 0.5f + overhang;
        float rx = Mathf.Clamp(ridgeLength, 0f, width) * 0.5f;
        var v = new List<Vertex>();
        var i = new List<uint>();
        float w = x * 2, d = z * 2;
        float slopeZ = MathF.Sqrt(height * height + z * z), slopeX = MathF.Sqrt(height * height + (x - rx) * (x - rx));
        float uw = worldUv ? w : 1f, ud = worldUv ? d : 1f, usZ = worldUv ? slopeZ : 1f, usX = worldUv ? slopeX : 1f;

        Vector3 eNL = new(-x, -y, -z), eNR = new(x, -y, -z), eSL = new(-x, -y, z), eSR = new(x, -y, z);
        Vector3 ridgeL = new(-rx, y, 0), ridgeR = new(rx, y, 0);

        Face(v, i, Vector3.Normalize(new Vector3(0, z, -height)), eNL, eNR, ridgeR, ridgeL,
            UV(0, 0), UV(uw, 0), UV(uw * 0.75f, usZ), UV(uw * 0.25f, usZ));
        Face(v, i, Vector3.Normalize(new Vector3(0, z, height)), ridgeL, ridgeR, eSR, eSL,
            UV(uw * 0.25f, usZ), UV(uw * 0.75f, usZ), UV(uw, 0), UV(0, 0));
        Face(v, i, Vector3.Normalize(new Vector3(-(x - rx) - 0.001f, height, 0)), eNL, ridgeL, eSL,
            UV(0, 0), UV(ud / 2, usX), UV(ud, 0));
        Face(v, i, Vector3.Normalize(new Vector3((x - rx) + 0.001f, height, 0)), eNR, eSR, ridgeR,
            UV(0, 0), UV(ud, 0), UV(ud / 2, usX));
        Face(v, i, -Vector3.UnitY, eNL, eSL, eSR, eNR, UV(0, 0), UV(0, ud), UV(uw, ud), UV(uw, 0));
        return new MeshData(v, i);
    }

    /// <summary>A shed roof: one slab tilted along Z (low at -Z, high at +Z), with thickness.</summary>
    public static Mesh RoofShed(float width = 6f, float height = 1.5f, float depth = 6f, float thickness = 0.2f, bool worldUv = false) =>
        RoofShedData(width, height, depth, thickness, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="RoofShed"/>.</summary>
    public static MeshData RoofShedData(float width = 6f, float height = 1.5f, float depth = 6f, float thickness = 0.2f, bool worldUv = false)
    {
        float x = width * 0.5f, z = depth * 0.5f, t = MathF.Max(0.01f, thickness);
        // Centered vertically on the whole slab (height + thickness).
        float yBase = -(height + t) * 0.5f;
        var v = new List<Vertex>();
        var i = new List<uint>();
        float slope = MathF.Sqrt(height * height + depth * depth);
        float uw = worldUv ? width : 1f, us = worldUv ? slope : 1f, ut = worldUv ? t : 1f;

        Vector3 bNL = new(-x, yBase, -z), bNR = new(x, yBase, -z), bSL = new(-x, yBase + height, z), bSR = new(x, yBase + height, z);
        Vector3 tNL = new(-x, yBase + t, -z), tNR = new(x, yBase + t, -z), tSL = new(-x, yBase + height + t, z), tSR = new(x, yBase + height + t, z);
        var up = Vector3.Normalize(new Vector3(0, depth, -height));

        Face(v, i, up, tNL, tNR, tSR, tSL, UV(0, 0), UV(uw, 0), UV(uw, us), UV(0, us));                       // top
        Face(v, i, -up, bNL, bSL, bSR, bNR, UV(0, 0), UV(0, us), UV(uw, us), UV(uw, 0));                      // underside
        Face(v, i, -Vector3.UnitZ, bNL, bNR, tNR, tNL, UV(0, 0), UV(uw, 0), UV(uw, ut), UV(0, ut));           // low edge
        Face(v, i, Vector3.UnitZ, bSR, bSL, tSL, tSR, UV(0, 0), UV(uw, 0), UV(uw, ut), UV(0, ut));            // high edge
        Face(v, i, -Vector3.UnitX, bSL, bNL, tNL, tSL, UV(0, 0), UV(us, 0), UV(us, ut), UV(0, ut));           // left
        Face(v, i, Vector3.UnitX, bNR, bSR, tSR, tNR, UV(0, 0), UV(us, 0), UV(us, ut), UV(0, ut));            // right
        return new MeshData(v, i);
    }

    // ------------------------------------------------------------------
    // Stairs
    // ------------------------------------------------------------------

    /// <summary>
    /// A flight of stairs climbing toward +Z, centered on the origin like a box.
    /// <paramref name="steps"/> counts the treads; <paramref name="solidSide"/> closes the sides
    /// and the underside so the flight is a solid block.
    /// </summary>
    public static Mesh Stairs(float width = 2f, float height = 2f, float depth = 3f, int steps = 8, bool solidSide = true, bool worldUv = false) =>
        StairsData(width, height, depth, steps, solidSide, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="Stairs"/>.</summary>
    public static MeshData StairsData(float width = 2f, float height = 2f, float depth = 3f, int steps = 8, bool solidSide = true, bool worldUv = false)
    {
        steps = Math.Clamp(steps, 1, 128);
        float x = width * 0.5f;
        float rise = height / steps, run = depth / steps;
        float z0 = -depth * 0.5f, y0 = -height * 0.5f;
        var v = new List<Vertex>();
        var i = new List<uint>();
        float uw = worldUv ? width : 1f, ur = worldUv ? run : 1f, uRise = worldUv ? rise : 1f;

        for (int s = 0; s < steps; s++)
        {
            float zFront = z0 + s * run, zBack = zFront + run;
            float yTop = y0 + (s + 1) * rise, yBottomOfRiser = y0 + s * rise;

            Face(v, i, -Vector3.UnitZ,                                                       // riser
                new Vector3(-x, yBottomOfRiser, zFront), new Vector3(x, yBottomOfRiser, zFront),
                new Vector3(x, yTop, zFront), new Vector3(-x, yTop, zFront),
                UV(0, 0), UV(uw, 0), UV(uw, uRise), UV(0, uRise));
            Face(v, i, Vector3.UnitY,                                                        // tread
                new Vector3(-x, yTop, zFront), new Vector3(x, yTop, zFront),
                new Vector3(x, yTop, zBack), new Vector3(-x, yTop, zBack),
                UV(0, 0), UV(uw, 0), UV(uw, ur), UV(0, ur));

            if (solidSide)
            {
                float uy = worldUv ? yTop - y0 : 1f;
                Face(v, i, -Vector3.UnitX,
                    new Vector3(-x, y0, zFront), new Vector3(-x, yTop, zFront), new Vector3(-x, yTop, zBack), new Vector3(-x, y0, zBack),
                    UV(0, 0), UV(0, uy), UV(ur, uy), UV(ur, 0));
                Face(v, i, Vector3.UnitX,
                    new Vector3(x, y0, zFront), new Vector3(x, y0, zBack), new Vector3(x, yTop, zBack), new Vector3(x, yTop, zFront),
                    UV(0, 0), UV(ur, 0), UV(ur, uy), UV(0, uy));
                Face(v, i, -Vector3.UnitY,
                    new Vector3(-x, y0, zFront), new Vector3(-x, y0, zBack), new Vector3(x, y0, zBack), new Vector3(x, y0, zFront),
                    UV(0, 0), UV(0, ur), UV(uw, ur), UV(uw, 0));
            }
        }
        // closing face at the top of the flight
        float zEnd = z0 + depth, yEnd = y0 + height;
        float uh = worldUv ? height : 1f;
        Face(v, i, Vector3.UnitZ,
            new Vector3(-x, y0, zEnd), new Vector3(-x, yEnd, zEnd), new Vector3(x, yEnd, zEnd), new Vector3(x, y0, zEnd),
            UV(0, 0), UV(0, uh), UV(uw, uh), UV(uw, 0));
        return new MeshData(v, i);
    }

    // ------------------------------------------------------------------
    // Arch
    // ------------------------------------------------------------------

    /// <summary>
    /// A wall with a round-topped opening (an arch). The opening is <paramref name="openingWidth"/>
    /// wide, straight up to <paramref name="openingHeight"/> and then a half circle. Centered on
    /// the origin, the wall faces ±Z.
    /// </summary>
    public static Mesh Arch(float width = 4f, float height = 4f, float thickness = 0.4f,
        float openingWidth = 2f, float openingHeight = 2f, int segments = 16, bool worldUv = false) =>
        ArchData(width, height, thickness, openingWidth, openingHeight, segments, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="Arch"/>.</summary>
    public static MeshData ArchData(float width = 4f, float height = 4f, float thickness = 0.4f,
        float openingWidth = 2f, float openingHeight = 2f, int segments = 16, bool worldUv = false)
    {
        segments = Math.Clamp(segments, 3, 64);
        float x = width * 0.5f, y = height * 0.5f, t = MathF.Max(0.02f, thickness) * 0.5f;
        float ox = MathF.Min(openingWidth, width - 0.02f) * 0.5f;
        float r = ox;
        float baseY = -y;
        float oy = Mathf.Clamp(openingHeight, 0.05f, height - r - 0.02f);
        var v = new List<Vertex>();
        var i = new List<uint>();
        float uT = worldUv ? t * 2 : 1f;

        // Opening outline, left jamb up over the arch and down the right jamb.
        var outline = new List<Vector2> { new(-ox, baseY), new(-ox, baseY + oy) };
        for (int k = 0; k <= segments; k++)
        {
            float a = MathF.PI - k / (float)segments * MathF.PI;
            outline.Add(new Vector2(MathF.Cos(a) * r, baseY + oy + MathF.Sin(a) * r));
        }
        outline.Add(new Vector2(ox, baseY));

        void Wall(float zf, Vector3 outward)
        {
            float uw = worldUv ? x - ox : 1f, uh = worldUv ? height : 1f;
            Face(v, i, outward, new Vector3(-x, baseY, zf), new Vector3(-ox, baseY, zf), new Vector3(-ox, y, zf), new Vector3(-x, y, zf),
                UV(0, 0), UV(uw, 0), UV(uw, uh), UV(0, uh));
            Face(v, i, outward, new Vector3(ox, baseY, zf), new Vector3(x, baseY, zf), new Vector3(x, y, zf), new Vector3(ox, y, zf),
                UV(0, 0), UV(uw, 0), UV(uw, uh), UV(0, uh));
            for (int k = 1; k < outline.Count - 2; k++)
            {
                var p0 = outline[k]; var p1 = outline[k + 1];
                float u0 = worldUv ? p0.X + x : 0f, u1 = worldUv ? p1.X + x : 1f;
                Face(v, i, outward,
                    new Vector3(p0.X, p0.Y, zf), new Vector3(p1.X, p1.Y, zf), new Vector3(p1.X, y, zf), new Vector3(p0.X, y, zf),
                    UV(u0, worldUv ? p0.Y + y : 0f), UV(u1, worldUv ? p1.Y + y : 0f), UV(u1, worldUv ? height : 1f), UV(u0, worldUv ? height : 1f));
            }
        }
        Wall(t, Vector3.UnitZ);
        Wall(-t, -Vector3.UnitZ);

        // Soffit (inside surface of the opening): outward points toward the opening's center.
        var openCenter = new Vector2(0, baseY + oy);
        for (int k = 0; k < outline.Count - 1; k++)
        {
            var p0 = outline[k]; var p1 = outline[k + 1];
            var mid = (p0 + p1) * 0.5f;
            var inward = Vector2.Normalize(openCenter - mid);
            float len = worldUv ? Vector2.Distance(p0, p1) : 1f;
            Face(v, i, new Vector3(inward.X, inward.Y, 0),
                new Vector3(p0.X, p0.Y, -t), new Vector3(p0.X, p0.Y, t), new Vector3(p1.X, p1.Y, t), new Vector3(p1.X, p1.Y, -t),
                UV(0, 0), UV(uT, 0), UV(uT, len), UV(0, len));
        }

        // Outer shell: sides, top, and the two bottom strips beside the opening.
        float uh2 = worldUv ? height : 1f, uw2 = worldUv ? width : 1f, ub = worldUv ? x - ox : 1f;
        Face(v, i, -Vector3.UnitX, new Vector3(-x, baseY, -t), new Vector3(-x, y, -t), new Vector3(-x, y, t), new Vector3(-x, baseY, t),
            UV(0, 0), UV(0, uh2), UV(uT, uh2), UV(uT, 0));
        Face(v, i, Vector3.UnitX, new Vector3(x, baseY, t), new Vector3(x, y, t), new Vector3(x, y, -t), new Vector3(x, baseY, -t),
            UV(0, 0), UV(0, uh2), UV(uT, uh2), UV(uT, 0));
        Face(v, i, Vector3.UnitY, new Vector3(-x, y, -t), new Vector3(-x, y, t), new Vector3(x, y, t), new Vector3(x, y, -t),
            UV(0, 0), UV(0, uT), UV(uw2, uT), UV(uw2, 0));
        Face(v, i, -Vector3.UnitY, new Vector3(-x, baseY, t), new Vector3(-x, baseY, -t), new Vector3(-ox, baseY, -t), new Vector3(-ox, baseY, t),
            UV(0, 0), UV(0, uT), UV(ub, uT), UV(ub, 0));
        Face(v, i, -Vector3.UnitY, new Vector3(ox, baseY, t), new Vector3(ox, baseY, -t), new Vector3(x, baseY, -t), new Vector3(x, baseY, t),
            UV(0, 0), UV(0, uT), UV(ub, uT), UV(ub, 0));
        return new MeshData(v, i);
    }

    // ------------------------------------------------------------------
    // Curved wall / tube / prism / pyramid / rounded box
    // ------------------------------------------------------------------

    /// <summary>
    /// A curved wall: a ring segment around the Y axis spanning <paramref name="arcDegrees"/>,
    /// centered on the origin and opening toward +X. 360° makes a full cylindrical shell.
    /// </summary>
    public static Mesh CurvedWall(float radius = 4f, float height = 3f, float thickness = 0.3f,
        float arcDegrees = 90f, int segments = 16, bool worldUv = false) =>
        CurvedWallData(radius, height, thickness, arcDegrees, segments, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="CurvedWall"/>.</summary>
    public static MeshData CurvedWallData(float radius = 4f, float height = 3f, float thickness = 0.3f,
        float arcDegrees = 90f, int segments = 16, bool worldUv = false)
    {
        segments = Math.Clamp(segments, 2, 128);
        float arcDeg = Mathf.Clamp(arcDegrees, 1f, 360f);
        float arc = arcDeg * Mathf.Deg2Rad;
        float rOut = radius + thickness * 0.5f, rIn = MathF.Max(0.01f, radius - thickness * 0.5f);
        float y = height * 0.5f;
        var v = new List<Vertex>();
        var i = new List<uint>();
        bool closed = arcDeg >= 359.9f;
        float uh = worldUv ? height : 1f, ut = worldUv ? thickness : 1f;

        for (int k = 0; k < segments; k++)
        {
            float a0 = -arc / 2 + arc * k / segments, a1 = -arc / 2 + arc * (k + 1) / segments;
            float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0), c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
            float u0 = worldUv ? radius * (a0 + arc / 2) : 0f, u1 = worldUv ? radius * (a1 + arc / 2) : 1f;
            var outward0 = new Vector3(c0, 0, s0);
            var outward = Vector3.Normalize(new Vector3(c0 + c1, 0, s0 + s1));

            Vector3 o0b = new(c0 * rOut, -y, s0 * rOut), o1b = new(c1 * rOut, -y, s1 * rOut);
            Vector3 o0t = new(c0 * rOut, y, s0 * rOut), o1t = new(c1 * rOut, y, s1 * rOut);
            Vector3 i0b = new(c0 * rIn, -y, s0 * rIn), i1b = new(c1 * rIn, -y, s1 * rIn);
            Vector3 i0t = new(c0 * rIn, y, s0 * rIn), i1t = new(c1 * rIn, y, s1 * rIn);

            Face(v, i, outward, o0b, o1b, o1t, o0t, UV(u0, 0), UV(u1, 0), UV(u1, uh), UV(u0, uh));        // outer skin
            Face(v, i, -outward, i1b, i0b, i0t, i1t, UV(u1, 0), UV(u0, 0), UV(u0, uh), UV(u1, uh));       // inner skin
            Face(v, i, Vector3.UnitY, o0t, o1t, i1t, i0t, UV(u0, 0), UV(u1, 0), UV(u1, ut), UV(u0, ut));  // top
            Face(v, i, -Vector3.UnitY, i0b, i1b, o1b, o0b, UV(u0, 0), UV(u1, 0), UV(u1, ut), UV(u0, ut)); // bottom
            _ = outward0;
        }
        if (!closed)
        {
            foreach (var (a, sign) in new[] { (-arc / 2, -1f), (arc / 2, 1f) })
            {
                float c = MathF.Cos(a), s = MathF.Sin(a);
                // Cap normal is tangential to the ring at that angle.
                var capOut = Vector3.Normalize(new Vector3(-s, 0, c)) * sign;
                Vector3 ob = new(c * rOut, -y, s * rOut), ot = new(c * rOut, y, s * rOut);
                Vector3 ib = new(c * rIn, -y, s * rIn), it = new(c * rIn, y, s * rIn);
                Face(v, i, capOut, ib, ob, ot, it, UV(0, 0), UV(ut, 0), UV(ut, uh), UV(0, uh));
            }
        }
        return new MeshData(v, i);
    }

    /// <summary>A hollow tube (pipe) aligned with the Y axis, open at both ends.</summary>
    public static Mesh Tube(float radius = 1f, float height = 2f, float thickness = 0.15f, int segments = 24, bool worldUv = false) =>
        TubeData(radius, height, thickness, segments, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="Tube"/>.</summary>
    public static MeshData TubeData(float radius = 1f, float height = 2f, float thickness = 0.15f, int segments = 24, bool worldUv = false)
    {
        segments = Math.Clamp(segments, 3, 128);
        float rOut = MathF.Max(0.02f, radius), rIn = MathF.Max(0.01f, radius - MathF.Max(0.01f, thickness));
        float y = height * 0.5f;
        var v = new List<Vertex>();
        var i = new List<uint>();
        float uh = worldUv ? height : 1f, ut = worldUv ? rOut - rIn : 1f;

        for (int k = 0; k < segments; k++)
        {
            float a0 = k / (float)segments * Mathf.Tau, a1 = (k + 1) / (float)segments * Mathf.Tau;
            float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0), c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
            float u0 = worldUv ? rOut * a0 : 0f, u1 = worldUv ? rOut * a1 : 1f;
            var outward = Vector3.Normalize(new Vector3(c0 + c1, 0, s0 + s1));

            Vector3 o0b = new(c0 * rOut, -y, s0 * rOut), o1b = new(c1 * rOut, -y, s1 * rOut);
            Vector3 o0t = new(c0 * rOut, y, s0 * rOut), o1t = new(c1 * rOut, y, s1 * rOut);
            Vector3 i0b = new(c0 * rIn, -y, s0 * rIn), i1b = new(c1 * rIn, -y, s1 * rIn);
            Vector3 i0t = new(c0 * rIn, y, s0 * rIn), i1t = new(c1 * rIn, y, s1 * rIn);

            Face(v, i, outward, o0b, o1b, o1t, o0t, UV(u0, 0), UV(u1, 0), UV(u1, uh), UV(u0, uh));
            Face(v, i, -outward, i1b, i0b, i0t, i1t, UV(u1, 0), UV(u0, 0), UV(u0, uh), UV(u1, uh));
            Face(v, i, Vector3.UnitY, o0t, o1t, i1t, i0t, UV(u0, 0), UV(u1, 0), UV(u1, ut), UV(u0, ut));
            Face(v, i, -Vector3.UnitY, i0b, i1b, o1b, o0b, UV(u0, 0), UV(u1, 0), UV(u1, ut), UV(u0, ut));
        }
        return new MeshData(v, i);
    }

    /// <summary>A prism with <paramref name="sides"/> flat faces (3 = triangular, 6 = hexagonal...), Y axis.</summary>
    public static Mesh Prism(float radius = 0.5f, float height = 1f, int sides = 6, bool worldUv = false) =>
        PrismData(radius, height, sides, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="Prism"/>.</summary>
    public static MeshData PrismData(float radius = 0.5f, float height = 1f, int sides = 6, bool worldUv = false)
    {
        sides = Math.Clamp(sides, 3, 64);
        float y = height * 0.5f;
        var v = new List<Vertex>();
        var i = new List<uint>();
        float side = 2f * radius * MathF.Sin(MathF.PI / sides);
        float uh = worldUv ? height : 1f;

        for (int k = 0; k < sides; k++)
        {
            float a0 = k / (float)sides * Mathf.Tau, a1 = (k + 1) / (float)sides * Mathf.Tau;
            float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0), c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
            Vector3 p0 = new(c0 * radius, -y, s0 * radius), p1 = new(c1 * radius, -y, s1 * radius);
            var outward = Vector3.Normalize(new Vector3(c0 + c1, 0, s0 + s1));
            float u0 = worldUv ? k * side : 0f, u1 = worldUv ? (k + 1) * side : 1f;
            Face(v, i, outward, p0, p1, p1 + Vector3.UnitY * height, p0 + Vector3.UnitY * height,
                UV(u0, 0), UV(u1, 0), UV(u1, uh), UV(u0, uh));
            // caps as triangle fans
            Face(v, i, Vector3.UnitY, new Vector3(0, y, 0), new Vector3(c0 * radius, y, s0 * radius), new Vector3(c1 * radius, y, s1 * radius),
                UV(0.5f, 0.5f), UV(0.5f + c0 * 0.5f, 0.5f + s0 * 0.5f), UV(0.5f + c1 * 0.5f, 0.5f + s1 * 0.5f));
            Face(v, i, -Vector3.UnitY, new Vector3(0, -y, 0), p1, p0,
                UV(0.5f, 0.5f), UV(0.5f + c1 * 0.5f, 0.5f + s1 * 0.5f), UV(0.5f + c0 * 0.5f, 0.5f + s0 * 0.5f));
        }
        return new MeshData(v, i);
    }

    /// <summary>A pyramid with a rectangular base and the apex centered above it.</summary>
    public static Mesh Pyramid(float width = 1f, float height = 1f, float depth = 1f, bool worldUv = false) =>
        PyramidData(width, height, depth, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="Pyramid"/>.</summary>
    public static MeshData PyramidData(float width = 1f, float height = 1f, float depth = 1f, bool worldUv = false)
    {
        float x = width * 0.5f, y = height * 0.5f, z = depth * 0.5f;
        var v = new List<Vertex>();
        var i = new List<uint>();
        Vector3 apex = new(0, y, 0);
        Vector3 a = new(-x, -y, -z), b = new(x, -y, -z), c = new(x, -y, z), d = new(-x, -y, z);
        float slopeZ = MathF.Sqrt(height * height + z * z), slopeX = MathF.Sqrt(height * height + x * x);
        float uw = worldUv ? width : 1f, ud = worldUv ? depth : 1f;
        float usZ = worldUv ? slopeZ : 1f, usX = worldUv ? slopeX : 1f;

        Face(v, i, Vector3.Normalize(new Vector3(0, z, -height)), a, b, apex, UV(0, 0), UV(uw, 0), UV(uw / 2, usZ));
        Face(v, i, Vector3.Normalize(new Vector3(x, 0, 0) + new Vector3(0, z, 0)), b, c, apex, UV(0, 0), UV(ud, 0), UV(ud / 2, usX));
        Face(v, i, Vector3.Normalize(new Vector3(0, z, height)), c, d, apex, UV(0, 0), UV(uw, 0), UV(uw / 2, usZ));
        Face(v, i, Vector3.Normalize(new Vector3(-x, z, 0)), d, a, apex, UV(0, 0), UV(ud, 0), UV(ud / 2, usX));
        Face(v, i, -Vector3.UnitY, a, d, c, b, UV(0, 0), UV(0, ud), UV(uw, ud), UV(uw, 0));
        return new MeshData(v, i);
    }

    /// <summary>A box with chamfered (beveled) edges - reads much better than a hard cube for props.</summary>
    public static Mesh RoundedBox(float sizeX = 1f, float sizeY = 1f, float sizeZ = 1f, float bevel = 0.08f, bool worldUv = false) =>
        RoundedBoxData(sizeX, sizeY, sizeZ, bevel, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="RoundedBox"/>.</summary>
    public static MeshData RoundedBoxData(float sizeX = 1f, float sizeY = 1f, float sizeZ = 1f, float bevel = 0.08f, bool worldUv = false)
    {
        float b = MathF.Max(0.001f, MathF.Min(bevel, MathF.Min(sizeX, MathF.Min(sizeY, sizeZ)) * 0.45f));
        float x = sizeX * 0.5f, y = sizeY * 0.5f, z = sizeZ * 0.5f;
        var v = new List<Vertex>();
        var i = new List<uint>();
        float uX = worldUv ? sizeX : 1f, uY = worldUv ? sizeY : 1f, uZ = worldUv ? sizeZ : 1f, uB = worldUv ? b * 1.414f : 1f;

        // 6 inset faces
        Face(v, i, Vector3.UnitZ, new Vector3(-x + b, -y + b, z), new Vector3(x - b, -y + b, z), new Vector3(x - b, y - b, z), new Vector3(-x + b, y - b, z),
            UV(0, 0), UV(uX, 0), UV(uX, uY), UV(0, uY));
        Face(v, i, -Vector3.UnitZ, new Vector3(x - b, -y + b, -z), new Vector3(-x + b, -y + b, -z), new Vector3(-x + b, y - b, -z), new Vector3(x - b, y - b, -z),
            UV(0, 0), UV(uX, 0), UV(uX, uY), UV(0, uY));
        Face(v, i, Vector3.UnitX, new Vector3(x, -y + b, z - b), new Vector3(x, -y + b, -z + b), new Vector3(x, y - b, -z + b), new Vector3(x, y - b, z - b),
            UV(0, 0), UV(uZ, 0), UV(uZ, uY), UV(0, uY));
        Face(v, i, -Vector3.UnitX, new Vector3(-x, -y + b, -z + b), new Vector3(-x, -y + b, z - b), new Vector3(-x, y - b, z - b), new Vector3(-x, y - b, -z + b),
            UV(0, 0), UV(uZ, 0), UV(uZ, uY), UV(0, uY));
        Face(v, i, Vector3.UnitY, new Vector3(-x + b, y, z - b), new Vector3(x - b, y, z - b), new Vector3(x - b, y, -z + b), new Vector3(-x + b, y, -z + b),
            UV(0, 0), UV(uX, 0), UV(uX, uZ), UV(0, uZ));
        Face(v, i, -Vector3.UnitY, new Vector3(-x + b, -y, -z + b), new Vector3(x - b, -y, -z + b), new Vector3(x - b, -y, z - b), new Vector3(-x + b, -y, z - b),
            UV(0, 0), UV(uX, 0), UV(uX, uZ), UV(0, uZ));

        // 12 bevel strips: outward is the average of the two faces they join.
        void Bevel(Vector3 outward, Vector3 a1, Vector3 a2, Vector3 b2, Vector3 b1, float len)
            => Face(v, i, outward, a1, a2, b2, b1, UV(0, 0), UV(len, 0), UV(len, uB), UV(0, uB));
        var xy = Vector3.Normalize(new Vector3(1, 1, 0));
        var xz = Vector3.Normalize(new Vector3(1, 0, 1));
        var yz = Vector3.Normalize(new Vector3(0, 1, 1));
        // vertical (along Y)
        Bevel(new Vector3(xz.X, 0, xz.Z), new Vector3(x - b, -y + b, z), new Vector3(x, -y + b, z - b), new Vector3(x, y - b, z - b), new Vector3(x - b, y - b, z), uY);
        Bevel(new Vector3(-xz.X, 0, xz.Z), new Vector3(-x, -y + b, z - b), new Vector3(-x + b, -y + b, z), new Vector3(-x + b, y - b, z), new Vector3(-x, y - b, z - b), uY);
        Bevel(new Vector3(xz.X, 0, -xz.Z), new Vector3(x, -y + b, -z + b), new Vector3(x - b, -y + b, -z), new Vector3(x - b, y - b, -z), new Vector3(x, y - b, -z + b), uY);
        Bevel(new Vector3(-xz.X, 0, -xz.Z), new Vector3(-x + b, -y + b, -z), new Vector3(-x, -y + b, -z + b), new Vector3(-x, y - b, -z + b), new Vector3(-x + b, y - b, -z), uY);
        // top (along X and Z)
        Bevel(new Vector3(0, yz.Y, yz.Z), new Vector3(-x + b, y - b, z), new Vector3(x - b, y - b, z), new Vector3(x - b, y, z - b), new Vector3(-x + b, y, z - b), uX);
        Bevel(new Vector3(0, yz.Y, -yz.Z), new Vector3(x - b, y - b, -z), new Vector3(-x + b, y - b, -z), new Vector3(-x + b, y, -z + b), new Vector3(x - b, y, -z + b), uX);
        Bevel(new Vector3(xy.X, xy.Y, 0), new Vector3(x, y - b, z - b), new Vector3(x, y - b, -z + b), new Vector3(x - b, y, -z + b), new Vector3(x - b, y, z - b), uZ);
        Bevel(new Vector3(-xy.X, xy.Y, 0), new Vector3(-x, y - b, -z + b), new Vector3(-x, y - b, z - b), new Vector3(-x + b, y, z - b), new Vector3(-x + b, y, -z + b), uZ);
        // bottom
        Bevel(new Vector3(0, -yz.Y, yz.Z), new Vector3(x - b, -y + b, z), new Vector3(-x + b, -y + b, z), new Vector3(-x + b, -y, z - b), new Vector3(x - b, -y, z - b), uX);
        Bevel(new Vector3(0, -yz.Y, -yz.Z), new Vector3(-x + b, -y + b, -z), new Vector3(x - b, -y + b, -z), new Vector3(x - b, -y, -z + b), new Vector3(-x + b, -y, -z + b), uX);
        Bevel(new Vector3(xy.X, -xy.Y, 0), new Vector3(x, -y + b, -z + b), new Vector3(x, -y + b, z - b), new Vector3(x - b, -y, z - b), new Vector3(x - b, -y, -z + b), uZ);
        Bevel(new Vector3(-xy.X, -xy.Y, 0), new Vector3(-x, -y + b, z - b), new Vector3(-x, -y + b, -z + b), new Vector3(-x + b, -y, -z + b), new Vector3(-x + b, -y, z - b), uZ);

        // 8 corner triangles
        void Corner(Vector3 outward, Vector3 p1, Vector3 p2, Vector3 p3)
            => Face(v, i, outward, p1, p2, p3, UV(0, 0), UV(uB, 0), UV(0, uB));
        var d1 = Vector3.Normalize(Vector3.One);
        Corner(new Vector3(d1.X, d1.Y, d1.Z), new Vector3(x - b, y - b, z), new Vector3(x, y - b, z - b), new Vector3(x - b, y, z - b));
        Corner(new Vector3(-d1.X, d1.Y, d1.Z), new Vector3(-x, y - b, z - b), new Vector3(-x + b, y - b, z), new Vector3(-x + b, y, z - b));
        Corner(new Vector3(d1.X, d1.Y, -d1.Z), new Vector3(x, y - b, -z + b), new Vector3(x - b, y - b, -z), new Vector3(x - b, y, -z + b));
        Corner(new Vector3(-d1.X, d1.Y, -d1.Z), new Vector3(-x + b, y - b, -z), new Vector3(-x, y - b, -z + b), new Vector3(-x + b, y, -z + b));
        Corner(new Vector3(d1.X, -d1.Y, d1.Z), new Vector3(x, -y + b, z - b), new Vector3(x - b, -y + b, z), new Vector3(x - b, -y, z - b));
        Corner(new Vector3(-d1.X, -d1.Y, d1.Z), new Vector3(-x + b, -y + b, z), new Vector3(-x, -y + b, z - b), new Vector3(-x + b, -y, z - b));
        Corner(new Vector3(d1.X, -d1.Y, -d1.Z), new Vector3(x - b, -y + b, -z), new Vector3(x, -y + b, -z + b), new Vector3(x - b, -y, -z + b));
        Corner(new Vector3(-d1.X, -d1.Y, -d1.Z), new Vector3(-x, -y + b, -z + b), new Vector3(-x + b, -y + b, -z), new Vector3(-x + b, -y, -z + b));
        return new MeshData(v, i);
    }

    // ------------------------------------------------------------------
    // Per-face box (one material per side)
    // ------------------------------------------------------------------

    /// <summary>The six sides of a box, in the order used by <see cref="BoxFacesData"/>.</summary>
    public enum BoxFace
    {
        Front = 0,   // +Z
        Back = 1,    // -Z
        Right = 2,   // +X
        Left = 3,    // -X
        Top = 4,     // +Y
        Bottom = 5   // -Y
    }

    /// <summary>
    /// A box split into six independent quads, one per side - so each face can take its own
    /// material (grass on top, dirt on the sides...). Index the result with <see cref="BoxFace"/>.
    /// </summary>
    public static MeshData[] BoxFacesData(float sizeX = 1f, float sizeY = 1f, float sizeZ = 1f, bool worldUv = false)
    {
        float x = sizeX * 0.5f, y = sizeY * 0.5f, z = sizeZ * 0.5f;
        float uX = worldUv ? sizeX : 1f, uY = worldUv ? sizeY : 1f, uZ = worldUv ? sizeZ : 1f;
        var faces = new MeshData[6];

        MeshData One(Vector3 outward, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uw, float uh)
        {
            var v = new List<Vertex>();
            var i = new List<uint>();
            Face(v, i, outward, a, b, c, d, UV(0, uh), UV(uw, uh), UV(uw, 0), UV(0, 0));
            return new MeshData(v, i);
        }

        faces[(int)BoxFace.Front] = One(Vector3.UnitZ,
            new(-x, -y, z), new(x, -y, z), new(x, y, z), new(-x, y, z), uX, uY);
        faces[(int)BoxFace.Back] = One(-Vector3.UnitZ,
            new(x, -y, -z), new(-x, -y, -z), new(-x, y, -z), new(x, y, -z), uX, uY);
        faces[(int)BoxFace.Right] = One(Vector3.UnitX,
            new(x, -y, z), new(x, -y, -z), new(x, y, -z), new(x, y, z), uZ, uY);
        faces[(int)BoxFace.Left] = One(-Vector3.UnitX,
            new(-x, -y, -z), new(-x, -y, z), new(-x, y, z), new(-x, y, -z), uZ, uY);
        faces[(int)BoxFace.Top] = One(Vector3.UnitY,
            new(-x, y, z), new(x, y, z), new(x, y, -z), new(-x, y, -z), uX, uZ);
        faces[(int)BoxFace.Bottom] = One(-Vector3.UnitY,
            new(-x, -y, -z), new(x, -y, -z), new(x, -y, z), new(-x, -y, z), uX, uZ);
        return faces;
    }

    // ------------------------------------------------------------------
    // Extruded polygon: an arbitrary footprint pulled up into a solid.
    // This is what real building outlines need - they are neither boxes nor
    // regular prisms. Points are (x, z) pairs in metres, in any winding order.
    // ------------------------------------------------------------------

    /// <summary>
    /// Extrudes a closed 2D footprint into a solid of <paramref name="height"/> metres,
    /// centred on the origin horizontally with its base at -height/2 (like every other
    /// primitive). Concave outlines are fine; self-intersecting ones are not.
    /// </summary>
    public static MeshData PolygonData(IReadOnlyList<Vector2> points, float height = 3f, bool worldUv = false)
    {
        var ring = CleanRing(points);
        if (ring.Count < 3)
            return BoxData(1f, MathF.Max(0.01f, height), 1f, worldUv);

        float half = MathF.Max(0.005f, height) * 0.5f;
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        // --- side walls, one quad per edge (outward normals via the Face helper)
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            var edge = b - a;
            float len = edge.Length();
            if (len < 1e-5f)
                continue;
            // CCW ring seen from +Y: the outward side is to the right of the edge
            var outward = Vector3.Normalize(new Vector3(edge.Y, 0f, -edge.X));
            float uw = worldUv ? len : 1f;
            float uh = worldUv ? height : 1f;
            Face(vertices, indices, outward,
                new Vector3(a.X, -half, a.Y), new Vector3(b.X, -half, b.Y),
                new Vector3(b.X, half, b.Y), new Vector3(a.X, half, a.Y),
                UV(0, uh), UV(uw, uh), UV(uw, 0), UV(0, 0));
        }

        // --- top and bottom caps from an ear-clipping triangulation
        var triangles = Triangulate(ring);
        AddCap(vertices, indices, ring, triangles, half, up: true, worldUv);
        AddCap(vertices, indices, ring, triangles, -half, up: false, worldUv);

        return new MeshData(vertices, indices);
    }

    /// <summary>Drops duplicate/collinear points and makes the ring counter-clockwise.</summary>
    private static List<Vector2> CleanRing(IReadOnlyList<Vector2> points)
    {
        var ring = new List<Vector2>(points.Count);
        foreach (var p in points)
        {
            if (ring.Count > 0 && Vector2.DistanceSquared(ring[^1], p) < 1e-8f)
                continue;
            ring.Add(p);
        }
        if (ring.Count > 2 && Vector2.DistanceSquared(ring[0], ring[^1]) < 1e-8f)
            ring.RemoveAt(ring.Count - 1);
        if (SignedArea(ring) < 0f)
            ring.Reverse();
        return ring;
    }

    private static float SignedArea(List<Vector2> ring)
    {
        float area = 0f;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area * 0.5f;
    }

    /// <summary>Ear clipping. Returns index triples into the (CCW) ring.</summary>
    private static List<int> Triangulate(List<Vector2> ring)
    {
        var result = new List<int>();
        int n = ring.Count;
        if (n < 3)
            return result;

        var remaining = new List<int>(n);
        for (int i = 0; i < n; i++)
            remaining.Add(i);

        int guard = 0;
        while (remaining.Count > 3 && guard++ < n * n + 16)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int i0 = remaining[(i - 1 + remaining.Count) % remaining.Count];
                int i1 = remaining[i];
                int i2 = remaining[(i + 1) % remaining.Count];
                var a = ring[i0];
                var b = ring[i1];
                var c = ring[i2];
                if (Cross(b - a, c - b) <= 0f)
                    continue;                       // reflex corner, not an ear

                bool contains = false;
                foreach (int j in remaining)
                {
                    if (j == i0 || j == i1 || j == i2)
                        continue;
                    if (PointInTriangle(ring[j], a, b, c))
                    {
                        contains = true;
                        break;
                    }
                }
                if (contains)
                    continue;

                result.Add(i0);
                result.Add(i1);
                result.Add(i2);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
                break;                              // degenerate outline: stop with what we have
        }
        if (remaining.Count == 3)
        {
            result.Add(remaining[0]);
            result.Add(remaining[1]);
            result.Add(remaining[2]);
        }
        return result;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(b - a, p - a);
        float d2 = Cross(c - b, p - b);
        float d3 = Cross(a - c, p - c);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0;
        bool pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    private static void AddCap(List<Vertex> vertices, List<uint> indices, List<Vector2> ring,
        List<int> triangles, float y, bool up, bool worldUv)
    {
        var normal = up ? Vector3.UnitY : -Vector3.UnitY;
        uint start = (uint)vertices.Count;
        foreach (var p in ring)
        {
            var uv = worldUv ? new Vector2(p.X, p.Y) : new Vector2(p.X * 0.5f + 0.5f, p.Y * 0.5f + 0.5f);
            vertices.Add(new Vertex(new Vector3(p.X, y, p.Y), normal, uv));
        }
        for (int t = 0; t + 2 < triangles.Count; t += 3)
        {
            if (up)
            {
                indices.Add(start + (uint)triangles[t]);
                indices.Add(start + (uint)triangles[t + 2]);
                indices.Add(start + (uint)triangles[t + 1]);
            }
            else
            {
                indices.Add(start + (uint)triangles[t]);
                indices.Add(start + (uint)triangles[t + 1]);
                indices.Add(start + (uint)triangles[t + 2]);
            }
        }
    }
}
