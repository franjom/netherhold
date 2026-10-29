// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>
/// The simulation's stable structural hash: one multiply-rotate round per value,
/// finished by xxHash64's avalanche. Byte-wise FNV-1a lives in <see cref="Fnv1a64"/>.
/// For a fixed value each round is a bijection on the state (xor, rotate,
/// multiply by an odd constant, add), so distinct states cannot merge on that value.
/// Reproducible across machines, runtimes and restarts; <see cref="object.GetHashCode"/>
/// is ruled out because string hashes are randomised per process.
/// Not cryptographic or collision-proof: this is a 64-bit divergence detector.
/// </summary>
public struct Hash64
{
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;
    private const ulong Prime4 = 0x85EBCA77C2B2AE63UL;
    private const ulong Prime5 = 0x27D4EB2F165667C5UL;

    private ulong _h;

    public static Hash64 Start() => new() { _h = Prime5 };

    public readonly ulong Value
    {
        get
        {
            ulong h = _h;
            h ^= h >> 33;
            h *= Prime2;
            h ^= h >> 29;
            h *= Prime3;
            h ^= h >> 32;
            return h;
        }
    }

    public void Add(ulong v)
    {
        ulong x = _h ^ (v * Prime2);
        // netstandard2.1 has no BitOperations; the JIT turns this pattern into one rotate.
        _h = (((x << 31) | (x >> 33)) * Prime1) + Prime4;
    }

    public void Add(byte b) => Add((ulong)b);
    public void Add(long v) => Add(unchecked((ulong)v));
    public void Add(int v) => Add(unchecked((ulong)(long)v));
    public void Add(uint v) => Add((ulong)v);
    public void Add(short v) => Add((long)v);
    public void Add(ushort v) => Add((ulong)v);
    public void Add(bool v) => Add(v ? 1UL : 0UL);
    public void Add(GameTick t) => Add(t.Value);
    public void Add(Fp32 f) => Add(f.Raw);

    /// <summary>Hashes a string by its characters, never by <c>GetHashCode</c>.</summary>
    public void Add(string? s)
    {
        if (s is null) { Add(ulong.MaxValue); return; }
        Add(s.Length);
        foreach (char c in s) Add((ushort)c);
    }
}
