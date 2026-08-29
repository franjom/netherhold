namespace DK2.Data.Wad;

/// <summary>
/// Decompressor for DK2's WAD entry compression (type 4): an LZ77-family byte
/// stream with four token classes selected by the high bits of a flag byte.
///
/// Algorithm adapted from GLKeeper's DK2WADArchive.cpp (MIT license,
/// https://github.com/codenamecpp/GLKeeper) — see THIRD-PARTY-NOTICES.md.
/// Validated against every compressed entry of the reference install's five
/// WAD archives (docs/research/wad-format.md).
///
/// Stream layout:
///   prelude: flags byte (bit0 set => 3 extra header bytes follow), 1 skipped
///            byte, then a 3-byte BIG-endian uncompressed size.
///   tokens, selected by flag byte f:
///     f &amp; 0x80 == 0          short backref: 1 extra byte
///     f &amp; 0xC0 == 0x80       medium backref: 2 extra bytes
///     f &amp; 0xE0 == 0xC0       long backref: 3 extra bytes
///     f &amp; 0xE0 == 0xE0       literal run: ((f &amp; 0x1F) * 4 + 4) bytes;
///                            values &gt; 0x70 terminate (final run = f &amp; 3 bytes)
/// Backref tokens carry 0-3 leading literal bytes before the copy.
/// </summary>
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
