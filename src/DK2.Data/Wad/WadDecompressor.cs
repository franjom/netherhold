// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Wad;

/// <summary>Decompressor for DK2's WAD entry compression (type 4): an LZ77-family byte stream with
/// four token classes selected by the high bits of a flag byte <c>f</c> — short, medium and long
/// backrefs taking 1, 2 and 3 extra bytes, and a literal run of
/// <c>(f &amp; 0x1F) * 4 + 4</c> bytes where values above <c>0x70</c> terminate the stream. Backref
/// tokens carry 0-3 leading literal bytes before the copy. The prelude is a flags byte (bit 0 set
/// means three extra header bytes follow), one skipped byte, then a 3-byte <b>big-endian</b>
/// uncompressed size. The token table with its masks is in docs/research/wad-format.md.
/// <para>Algorithm adapted from GLKeeper's <c>DK2WADArchive.cpp</c> (MIT,
/// https://github.com/codenamecpp/GLKeeper) — see THIRD-PARTY-NOTICES.md. Validated against every
/// compressed entry of the reference install's five WAD archives.</para></summary>
public static class WadDecompressor
{
    public static byte[] Decompress(ReadOnlySpan<byte> input, int expectedSize)
    {
        byte[] output = new byte[expectedSize];
        int inPos = 0;
        int outPos = 0;

        byte Next(ref int pos, ReadOnlySpan<byte> data) => pos < data.Length ? data[pos++] : (byte)0;

        // Prelude.
        if ((Next(ref inPos, input) & 1) != 0)
            inPos += 3;
        inPos++;

        int embeddedSize = (Next(ref inPos, input) << 16)
                         | (Next(ref inPos, input) << 8)
                         | Next(ref inPos, input);
        if (embeddedSize != expectedSize)
            throw new InvalidDataException(
                $"WAD entry declares decompressed size {embeddedSize}, directory says {expectedSize}.");

        while (inPos < input.Length)
        {
            byte flag = input[inPos++];

            if ((flag & 0x80) == 0)
            {
                // Short backref. Extra byte = low distance bits.
                byte b1 = Next(ref inPos, input);
                int literals = flag & 3;
                while (literals-- != 0) output[outPos++] = Next(ref inPos, input);

                int src = outPos - (((flag & 0x60) << 3) + b1 + 1);
                int count = ((flag >> 2) & 7) + 2;
                do { output[outPos++] = output[src++]; } while (count-- != 0);
            }
            else if ((flag & 0x40) == 0)
            {
                // Medium backref.
                byte b1 = Next(ref inPos, input);
                byte b2 = Next(ref inPos, input);
                int literals = b1 >> 6;
                while (literals-- != 0) output[outPos++] = Next(ref inPos, input);

                int src = outPos - (((b1 & 0x3F) << 8) + b2 + 1);
                int count = (flag & 0x3F) + 3;
                do { output[outPos++] = output[src++]; } while (count-- != 0);
            }
            else if ((flag & 0x20) == 0)
            {
                // Long backref.
                byte b1 = Next(ref inPos, input);
                byte b2 = Next(ref inPos, input);
                byte b3 = Next(ref inPos, input);
                int literals = flag & 3;
                while (literals-- != 0) output[outPos++] = Next(ref inPos, input);

                int src = outPos - (((flag & 0x10) << 12) + (b1 << 8) + b2 + 1);
                int count = b3 + ((flag & 0x0C) << 6) + 4;
                do { output[outPos++] = output[src++]; } while (count-- != 0);
            }
            else
            {
                // Literal run / terminator.
                int count = (flag & 0x1F) * 4 + 4;
                bool finished = count > 0x70;
                if (finished) count = flag & 3;
                while (count-- != 0) output[outPos++] = Next(ref inPos, input);
                if (finished) break;
            }
        }

        if (outPos != expectedSize)
            throw new InvalidDataException(
                $"WAD decompression produced {outPos} bytes, expected {expectedSize}.");
        return output;
    }
}
