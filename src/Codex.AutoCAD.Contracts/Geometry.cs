using System.Globalization;

namespace Codex.AutoCAD.Contracts;

public sealed class CadPoint3
{
    public CadPoint3()
    {
    }

    public CadPoint3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public bool IsFinite => IsFiniteNumber(X) && IsFiniteNumber(Y) && IsFiniteNumber(Z);

    public string ToCanonicalString()
    {
        return string.Join(",",
            X.ToString("R", CultureInfo.InvariantCulture),
            Y.ToString("R", CultureInfo.InvariantCulture),
            Z.ToString("R", CultureInfo.InvariantCulture));
    }

    private static bool IsFiniteNumber(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public sealed class CadExtents3
{
    public CadPoint3 Minimum { get; set; } = new();

    public CadPoint3 Maximum { get; set; } = new();
}
