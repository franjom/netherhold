// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Core;

/// <summary>Seed-zero xxHash64 for file integrity, independent of the simulation's Hash64.</summary>
public static class PayloadChecksum
{
    private const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL,
        P3 = 1609587929392839161UL, P4 = 9650029242287828579UL, P5 = 2870177450012600261UL;

    public static ulong Compute(ReadOnlySpan<byte> bytes)
    {
        unchecked
        {
            int offset = 0;
            ulong hash;
            if (bytes.Length >= 32)
            {
                ulong v1 = P1 + P2, v2 = P2, v3 = 0, v4 = 0UL - P1;
                do
                {
                    v1 = Round(v1, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset))); offset += 8;
                    v2 = Round(v2, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset))); offset += 8;
                    v3 = Round(v3, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset))); offset += 8;
                    v4 = Round(v4, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset))); offset += 8;
                } while (offset <= bytes.Length - 32);
                hash = Rotate(v1, 1) + Rotate(v2, 7) + Rotate(v3, 12) + Rotate(v4, 18);
                hash = Merge(Merge(Merge(Merge(hash, v1), v2), v3), v4);
            }
            else hash = P5;
            hash += (ulong)bytes.Length;
            while (offset <= bytes.Length - 8)
            {
                hash ^= Round(0, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset)));
                hash = Rotate(hash, 27) * P1 + P4;
                offset += 8;
            }
            if (offset <= bytes.Length - 4)
            {
                hash ^= BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset)) * P1;
                hash = Rotate(hash, 23) * P2 + P3;
                offset += 4;
            }
            while (offset < bytes.Length)
            {
                hash ^= bytes[offset++] * P5;
                hash = Rotate(hash, 11) * P1;
            }
            hash ^= hash >> 33; hash *= P2;
            hash ^= hash >> 29; hash *= P3;
            return hash ^ (hash >> 32);
        }
    }

    private static ulong Rotate(ulong value, int bits) => (value << bits) | (value >> (64 - bits));
    private static ulong Round(ulong accumulator, ulong value) => unchecked(Rotate(accumulator + value * P2, 31) * P1);
    private static ulong Merge(ulong hash, ulong value) => unchecked((hash ^ Round(0, value)) * P1 + P4);
}
