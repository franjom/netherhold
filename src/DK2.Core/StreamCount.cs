// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>
/// Counts and lengths read from journals and other untrusted input (ADR-0724 R1). The journal
/// counterpart of <see cref="SaveReader.CheckCount"/>, with the journals' exception contract:
/// malformed input throws <see cref="InvalidDataException"/>, input cut short throws
/// <see cref="EndOfStreamException"/>, which journal readers treat as their end.
/// </summary>
public static class StreamCount
{
    /// <summary>Reads a count and refuses one the remaining bytes cannot hold, so nothing is
    /// allocated from a number the input does not back. Needs a seekable stream.</summary>
    public static int Read(BinaryReader reader, int minBytesPerElement)
    {
        int count = reader.ReadInt32();
        if (count < 0) throw new InvalidDataException($"negative count {count}");
        Stream stream = reader.BaseStream;
        if (!stream.CanSeek) throw new NotSupportedException("A bounded count needs a seekable stream.");
        if ((long)count * minBytesPerElement > stream.Length - stream.Position)
            throw new EndOfStreamException($"{count} elements run past the end of the input.");
        return count;
    }
}
