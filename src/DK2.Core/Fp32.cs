// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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
