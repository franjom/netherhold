namespace DK2.Core;

/// <summary>
/// DK2's 32-bit fixed-point number: raw integer with denominator 4096 (2^12).
/// CONFIRMED: ufdada/dk2-tools util_struc.bt (`FLOAT32 = 4096.0`).
/// Stored raw so simulation math stays in integers (ADR-0001); convert to
/// floating point only for display.
/// </summary>
public readonly record struct Fp32(int Raw)
{
    public const int One = 4096;

    public double ToDouble() => Raw / 4096.0;

    public static Fp32 FromDouble(double value) => new((int)Math.Round(value * 4096.0));

    public override string ToString() => ToDouble().ToString("0.###");
}
