namespace DirtAndDiamonds.Simulation.Arcade;

/// <summary>
/// Double-precision 3-vector for the arcade physics core
/// (docs/design/arcade_baseball_overhaul.md §3). Field frame: origin at the
/// back point of home plate, +X toward the first-base side (the catcher's
/// right), +Y toward center field, +Z up; metres and seconds throughout.
/// A readonly struct so every physics step stays allocation-free.
/// </summary>
public readonly struct Vec3
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Vec3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Vec3 Zero = new(0.0, 0.0, 0.0);
    public static readonly Vec3 UnitZ = new(0.0, 0.0, 1.0);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double LengthSquared => X * X + Y * Y + Z * Z;

    /// <summary>Distance from home plate measured along the ground (the X/Y plane).</summary>
    public double GroundDistance => Math.Sqrt(X * X + Y * Y);

    public static Vec3 operator +(in Vec3 a, in Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(in Vec3 a, in Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(in Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(in Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, in Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(in Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public static double Dot(in Vec3 a, in Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(in Vec3 a, in Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    /// <summary>Linear interpolation; t = 0 → a, t = 1 → b.</summary>
    public static Vec3 Lerp(in Vec3 a, in Vec3 b, double t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t);

    public Vec3 Normalized()
    {
        double len = Length;
        return len > 0.0 ? this / len : Zero;
    }

    public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
}
