using System.Numerics;

namespace Ducz;

/// <summary>
/// Global random number helpers. Deterministic when you set <see cref="Seed"/>.
/// </summary>
public static class Rng
{
    private static Random _random = new();

    /// <summary>Setting the seed resets the generator, making sequences reproducible.</summary>
    public static int Seed
    {
        set => _random = new Random(value);
    }

    /// <summary>Random float in [0, 1).</summary>
    public static float Value() => _random.NextSingle();

    /// <summary>Random float in [min, max).</summary>
    public static float Range(float min, float max) => min + _random.NextSingle() * (max - min);

    /// <summary>Random int in [min, max) - max is exclusive.</summary>
    public static int Range(int min, int max) => _random.Next(min, max);

    /// <summary>Returns true with the given probability (0..1).</summary>
    public static bool Chance(float probability) => _random.NextSingle() < probability;

    /// <summary>Random point inside the unit sphere.</summary>
    public static Vector3 InsideUnitSphere()
    {
        while (true)
        {
            var v = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
            if (v.LengthSquared() <= 1f)
                return v;
        }
    }

    /// <summary>Random direction on the unit sphere.</summary>
    public static Vector3 OnUnitSphere() => Mathf.NormalizeSafe(InsideUnitSphere() + new Vector3(0, 1e-4f, 0));

    /// <summary>Random point inside the unit circle (XZ plane, Y = 0).</summary>
    public static Vector3 InsideUnitCircleXz()
    {
        while (true)
        {
            var v = new Vector3(Range(-1f, 1f), 0f, Range(-1f, 1f));
            if (v.LengthSquared() <= 1f)
                return v;
        }
    }

    /// <summary>Picks a random element from a list.</summary>
    public static T Pick<T>(IReadOnlyList<T> list) => list[_random.Next(list.Count)];
}
