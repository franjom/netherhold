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
/// A stable 64-bit structural hash (FNV-1a).
///
/// Used for the determinism gate: two simulation runs that agree on this value
/// at every tick are behaviourally identical. It must therefore be
/// *reproducible across machines, runtimes and process restarts*, which rules
/// out <see cref="object.GetHashCode"/> (randomised per process for strings).
///
/// Not cryptographic and not collision-proof; it is a divergence detector, so
/// a false "identical" is the only dangerous outcome and 64 bits makes that
/// vanishingly unlikely for the state sizes involved.
/// </summary>
public struct Hash64
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong _h;

    public static Hash64 Start() => new() { _h = Offset };

    public readonly ulong Value => _h;

    public void Add(byte b)
    {
        _h ^= b;
        _h *= Prime;
    }

    public void Add(ulong v)
    {
        for (int i = 0; i < 8; i++)
            Add((byte)(v >> (i * 8)));
    }

    public void Add(long v) => Add(unchecked((ulong)v));
    public void Add(int v) => Add(unchecked((ulong)(long)v));
    public void Add(uint v) => Add((ulong)v);
    public void Add(short v) => Add((long)v);
    public void Add(ushort v) => Add((ulong)v);
    public void Add(bool v) => Add((byte)(v ? 1 : 0));
    public void Add(GameTick t) => Add(t.Value);
    public void Add(Fp32 f) => Add(f.Raw);

    /// <summary>Hashes a string by its bytes, never by <c>GetHashCode</c>.</summary>
    public void Add(string? s)
    {
        if (s is null) { Add((byte)0xFF); return; }
        Add(s.Length);
        foreach (char c in s) Add((ushort)c);
    }
}
