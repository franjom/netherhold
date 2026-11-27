// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers;
using System.Buffers.Binary;

namespace DK2.Art.Textures;

/// <summary>
/// DKII.EXE's two 8x8 block image codecs, written from the binary. Engine textures
/// (setup 0x005B26F0, block 0x0065C290, 32-bit output 0x0065CA50) carry a predicted DC
/// and luma/chroma; .444 loading screens (0x005D77A0, block 0x00668810) carry an absolute
/// RGB DC. Both share the AC code, zigzag and dequantisation tables and the separable
/// transform. Output is RGBA, four bytes per pixel, rows top first. The x87 arithmetic
/// is reproduced exactly with integers scaled by 2^25 (see <see cref="Units"/>).
/// </summary>
internal sealed class Dk2TextureDecoder
{
    private const int BlockSide = 8;
    private const int BlockArea = BlockSide * BlockSide;
    private const int EndOfBlock = 0x41;

    /// <summary>Decodes an engine texture (EngineTextures.dat payload after its header).</summary>
    public static byte[] Decode(ReadOnlySpan<byte> compressed, int width, int height, bool alpha)
    {
        return DecodeImage(compressed, width, height, alpha, engineTexture: true);
    }

    /// <summary>Decodes an unmasked .444 payload (after its 8-byte header) to padded RGBA.
    /// Assumes 64- or 53-bit x87 precision (both give these pixels); 0x005D77A0 sets none and the
    /// game's precision there is an open question (the 0x00668810 record). The mask path is not decoded.</summary>
    internal static byte[] DecodeLoadingScreen(ReadOnlySpan<byte> compressed, int width, int height, bool alpha)
    {
        return DecodeImage(compressed, width, height, alpha, engineTexture: false);
    }

    private static byte[] DecodeImage(ReadOnlySpan<byte> compressed, int width, int height, bool alpha, bool engineTexture)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        byte[] pixels = new byte[checked(width * height * 4)];
        // The native reader always fetches the dword after the current one (0x00665EF0's
        // SHLD pair), so two zero words of padding keep the last code readable.
        int wordCount = (compressed.Length + 3) / 4 + 2;
        uint[] words = ArrayPool<uint>.Shared.Rent(wordCount);
        try
        {
            var bits = new BitReader(words, wordCount, compressed);
            int channels = alpha ? 4 : 3;
            Span<int> planes = stackalloc int[4 * BlockArea];
            Span<int> coefficients = stackalloc int[BlockArea];
            Span<int> columns = stackalloc int[BlockArea];
            Span<int> predictors = stackalloc int[4];
            predictors.Clear();
            for (int top = 0; top < height; top += BlockSide)
            {
                for (int left = 0; left < width; left += BlockSide)
                {
                    for (int channel = 0; channel < channels; channel++)
                    {
                        Span<int> plane = planes.Slice(channel * BlockArea, BlockArea);
                        if (engineTexture)
                        {
                            DecodeEngineChannel(ref bits, ref predictors[channel], plane, coefficients, columns);
                        }
                        else
                        {
                            DecodeLoadingChannel(ref bits, plane, coefficients, columns);
                        }
                    }
                    if (engineTexture)
                    {
                        WriteEngineBlock(planes, alpha, pixels, width, height, left, top);
                    }
                    else
                    {
                        WriteLoadingBlock(planes, alpha, pixels, width, height, left, top);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(words);
        }
        return pixels;
    }

    // 0x0065C290, one channel: a DC difference added to the channel's running predictor,
    // then either the DC-only shortcut (0x0065C120) or the full AC decode and transform.
    private static void DecodeEngineChannel(ref BitReader bits, ref int predictor, Span<int> plane,
        Span<int> coefficients, Span<int> columns)
    {
        predictor = unchecked(predictor + bits.ReadDcDifference());
        if (bits.Peek() >> 30 == 2)
        {
            // An immediate end-of-block code: the plane is the DC in 16.16, no dequantisation.
            bits.Skip(2);
            plane.Fill(unchecked(predictor << 16));
            return;
        }
        DecodeCoefficients(ref bits, predictor, coefficients);
        InverseTransform(coefficients, columns, plane, engineTexture: true);
    }

    // 0x00668810, one channel: an absolute unsigned 8-bit DC, then the AC decode and transform.
    private static void DecodeLoadingChannel(ref BitReader bits, Span<int> plane,
        Span<int> coefficients, Span<int> columns)
    {
        int dc = (int)(bits.Peek() >> 24);
        bits.Skip(8);
        DecodeCoefficients(ref bits, dc, coefficients);
        InverseTransform(coefficients, columns, plane, engineTexture: false);
    }

    // 0x00665EF0 (loading screens) and 0x00659608 (engine textures) read the same codes
    // through the same tables. A slot counter runs down from 63 through the zigzag table;
    // short codes carry run+1 and a magnitude followed by a sign bit, escapes carry a 6-bit
    // run and an 8-bit level whose 0x00 and 0x80 forms extend to a further positive or
    // negative byte. Every value is stored premultiplied by its dequantisation step.
    private static void DecodeCoefficients(ref BitReader bits, int dc, Span<int> coefficients)
    {
        coefficients.Clear();
        ReadOnlySpan<int> step = Dequantisation;
        coefficients[0] = unchecked(dc * step[0]);
        ReadOnlySpan<byte> zigzag = ZigzagSlots;
        int slotCounter = 63;
        while (true)
        {
            uint window = bits.Peek();
            uint code = LookupAcCode(window);
            int length = (int)code >> 16;
            int run = (int)(code >> 8) & 0xFF;
            bits.Skip(length);
            if (run == EndOfBlock)
            {
                return;
            }

            int level, slot;
            if (run < EndOfBlock)
            {
                slotCounter -= run;
                slot = slotCounter + 1;
                level = (int)(code & 0xFF);
                if (((window >> (31 - length)) & 1) != 0)
                {
                    level = -level;
                }

                bits.Skip(1);
            }
            else
            {
                uint escape = bits.Peek() >> 18;
                bits.Skip(14);
                slot = slotCounter - (int)(escape >> 8);
                slotCounter = slot - 1;
                level = (int)(escape & 0xFF);
                if (level == 0 || level == 0x80)
                {
                    int extension = (int)(bits.Peek() >> 24);
                    bits.Skip(8);
                    level = level == 0 ? extension : extension - 0x100;
                }
                else if (level > 0x80)
                {
                    level -= 0x100;
                }
            }
            // The binary keeps this counter in one byte and never bounds it: a run past slot 1 wraps it
            // to 0xFF and the zigzag lookup reads the table after it (0x006597D2..0x00659929). Slot 0 is
            // the DC term, which no AC code reaches in a well-formed stream, so anything outside 1..63 is
            // malformed input and is refused here rather than mirrored.
            if (slot < 1 || slot >= BlockArea)
            {
                throw new InvalidDataException("Texture AC run passes the end of the block.");
            }

            int index = zigzag[slot];
            coefficients[index] = unchecked(level * step[index]);
        }
    }

    // The 17 leading bits pick one of eight sub-tables by their leading zeros (0x00666077
    // onward); all eight live in one 160-entry run, so each reduces to an offset into it.
    // Entry: bits 16..31 code length (signed), 8..15 run+1 or 0x41/0x42, 0..7 magnitude.
    private static uint LookupAcCode(uint window)
    {
        uint prefix = window >> 15;
        int index;
        if (prefix >= 0x8000)
        {
            index = (int)(prefix >> 13) - 4;
        }
        else if (prefix >= 0x800)
        {
            index = (int)(prefix >> 9) + 8;
        }
        else if (prefix >= 0x400)
        {
            index = (int)(prefix >> 7) + 64;
        }
        else if (prefix >= 0x200)
        {
            index = (int)(prefix >> 5) + 64;
        }
        else if (prefix >= 0x100)
        {
            index = (int)(prefix >> 4) + 80;
        }
        else if (prefix >= 0x80)
        {
            index = (int)(prefix >> 3) + 96;
        }
        else if (prefix >= 0x40)
        {
            index = (int)(prefix >> 2) + 112;
        }
        else if (prefix >= 0x20)
        {
            index = (int)(prefix >> 1) + 128;
        }
        else
        {
            throw new InvalidDataException("Invalid texture AC code.");
        }

        return AcCodes[index];
    }

    // First pass (0x00666210 / 0x00659930) on each coefficient row r writes column r of the
    // intermediate block; the second pass (0x00666370 / 0x00659A90) on each intermediate row
    // writes one output row. Only the second pass of the loading-screen path stores its
    // rounded products as 64-bit integers.
    private static void InverseTransform(ReadOnlySpan<int> coefficients, Span<int> columns, Span<int> plane, bool engineTexture)
    {
        for (int row = 0; row < BlockSide; row++)
        {
            Transform8(coefficients.Slice(row * BlockSide, BlockSide), columns, row, BlockSide, engineTexture, wideStore: false);
        }

        for (int row = 0; row < BlockSide; row++)
        {
            Transform8(columns.Slice(row * BlockSide, BlockSide), plane, row * BlockSide, 1, engineTexture, wideStore: !engineTexture);
        }
    }

    private static void Transform8(ReadOnlySpan<int> x, Span<int> output, int start, int stride, bool engineTexture, bool wideStore)
    {
        int x0 = x[0], x1 = x[1], x2 = x[2], x3 = x[3], x4 = x[4], x5 = x[5], x6 = x[6], x7 = x[7];
        if ((x1 | x2 | x3 | x4 | x5 | x6 | x7) == 0)
        {
            // The binary takes this branch in the first pass only; the full arithmetic gives the same values.
            for (int i = 0; i < BlockSide; i++)
            {
                output[start + i * stride] = x0;
            }

            return;
        }
        unchecked
        {
            int sum35 = x3 + x5, diff53 = x5 - x3, sum17 = x1 + x7, diff17 = x1 - x7;
            int oddSum = sum17 + sum35;
            // Odd half: three products on the FPU (operands summed before rounding), one in Q31.
            long shared = ((long)diff17 + diff53) * Units.Rotate3827;
            int a = Units.ToInt(diff53 * Units.Rotate5412 + shared, wideStore);
            int d = Units.ToInt(diff17 * Units.Rotate13066 - shared, wideStore);
            int m = ScaleBySqrtHalf(sum17 - sum35, engineTexture);
            int b = a + m, c = m + d;
            d += oddSum;

            int n = ScaleBySqrtHalf(x2 - x6, engineTexture);
            int evenSum = x2 + x6 + n;
            int sum04 = x0 + x4, diff04 = x0 - x4;
            int e0 = sum04 + evenSum, e3 = sum04 - evenSum, e1 = diff04 + n, e2 = diff04 - n;

            output[start] = e0 + d;
            output[start + stride] = e1 + c;
            output[start + 2 * stride] = e2 + b;
            output[start + 3 * stride] = e3 + a;
            output[start + 4 * stride] = e3 - a;
            output[start + 5 * stride] = e2 - b;
            output[start + 6 * stride] = e1 - c;
            output[start + 7 * stride] = e0 - d;
        }
    }

    // Both multiply by 0x5A82799A (sqrt(1/2) in Q31). The loading-screen transform keeps
    // product bits 31..62 (ADD/ADC); the engine transform doubles the high dword alone and
    // so drops bit 31 (0x006599AB).
    private static int ScaleBySqrtHalf(int value, bool engineTexture)
    {
        long product = (long)value * 0x5A82799A;
        return unchecked(engineTexture ? (int)(product >> 32) * 2 : (int)(product >> 31));
    }

    // 0x0065CA50: Y, Cb, Cr and optional alpha planes in 16.16 to RGBA, eight pixels a row.
    private static void WriteEngineBlock(ReadOnlySpan<int> planes, bool alpha, byte[] pixels, int width, int height, int left, int top)
    {
        ReadOnlySpan<int> luma = planes.Slice(0, BlockArea);
        ReadOnlySpan<int> blueDiff = planes.Slice(BlockArea, BlockArea);
        ReadOnlySpan<int> redDiff = planes.Slice(2 * BlockArea, BlockArea);
        ReadOnlySpan<int> opacity = planes.Slice(3 * BlockArea, BlockArea);
        int rows = Math.Min(BlockSide, height - top), cols = Math.Min(BlockSide, width - left);
        for (int row = 0; row < rows; row++)
        {
            int target = ((top + row) * width + left) * 4;
            for (int col = 0; col < cols; col++, target += 4)
            {
                int i = row * BlockSide + col;
                long y = Units.RoundToExtendedGrid(((long)luma[i] - Units.LumaBlack) * Units.LumaScale);
                long cb = (long)blueDiff[i] - Units.ChromaZero, cr = (long)redDiff[i] - Units.ChromaZero;
                pixels[target] = Units.ToColour(cr * Units.RedFromCr + y);
                pixels[target + 1] = Units.ToColour(cb * Units.GreenFromCb + cr * Units.GreenFromCr + y);
                pixels[target + 2] = Units.ToColour(cb * Units.BlueFromCb + y);
                pixels[target + 3] = alpha ? Clamp(opacity[i] >> 16) : (byte)255;
            }
        }
    }

    // 0x00668810's unmasked output: R, G, B rounded from 16.16 by +0x8000, alpha truncated.
    private static void WriteLoadingBlock(ReadOnlySpan<int> planes, bool alpha, byte[] pixels, int width, int height, int left, int top)
    {
        int rows = Math.Min(BlockSide, height - top), cols = Math.Min(BlockSide, width - left);
        for (int row = 0; row < rows; row++)
        {
            int target = ((top + row) * width + left) * 4;
            for (int col = 0; col < cols; col++, target += 4)
            {
                int i = row * BlockSide + col;
                pixels[target] = Clamp(unchecked(planes[i] + 0x8000) >> 16);
                pixels[target + 1] = Clamp(unchecked(planes[BlockArea + i] + 0x8000) >> 16);
                pixels[target + 2] = Clamp(unchecked(planes[2 * BlockArea + i] + 0x8000) >> 16);
                pixels[target + 3] = alpha ? Clamp(planes[3 * BlockArea + i] >> 16) : (byte)255;
            }
        }
    }

    // The native lookup tables (0x005B21E0, 0x005D7360) map -0x180..0x27F to 0..255 by
    // clamping. Natively an index outside that range reads the neighbouring channel's table and
    // can set another byte; shipped data never gets there, and here it simply clamps.
    private static byte Clamp(int value)
    {
        return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
    }

    /// <summary>
    /// Exact x87 arithmetic for the transform and colour conversion. Every float constant is
    /// an integer mantissa times a power of two, so each product is held exactly as a count
    /// of 2^-25; rounding steps are then explicit, round-half-to-even as the FPU does.
    /// </summary>
    private static class Units
    {
        private const int FractionBits = 25;

        // Transform rotations at 0x007AF5F4..FC (loading) and 0x007AF03C..44 (engine): equal floats.
        public static readonly long Rotate5412 = FromFloatBits(0x3F0A8BD4);
        public static readonly long Rotate13066 = FromFloatBits(0x3FA73D75);
        public static readonly long Rotate3827 = FromFloatBits(0x3EC3EF15);

        // Colour conversion floats at 0x007AF000..18.
        public static readonly long LumaBlack = 16 << 16;
        public static readonly long ChromaZero = 128 << 16;
        public static readonly long LumaScale = FromFloatBits(0x3F95A1CB);
        public static readonly long GreenFromCr = FromFloatBits(0xBF5020C5);
        public static readonly long GreenFromCb = FromFloatBits(0xBEC83127);
        public static readonly long RedFromCr = FromFloatBits(0x3FCD0E56);
        public static readonly long BlueFromCb = FromFloatBits(0x4001999A);

        private static long FromFloatBits(uint bits)
        {
            int exponent = (int)((bits >> 23) & 0xFF) - 127 - 23;
            long mantissa = (bits & 0x7FFFFF) | 0x800000;
            long scaled = mantissa << (exponent + FractionBits);
            return (bits & 0x80000000) != 0 ? -scaled : scaled;
        }

        // FISTP to a dword: nearest-even; out of range gives the integer-indefinite value.
        // A qword store keeps the low dword instead.
        public static int ToInt(long units, bool wideStore)
        {
            long value = RoundShift(units, FractionBits);
            if (value >= int.MinValue && value <= int.MaxValue)
            {
                return (int)value;
            }

            return wideStore ? unchecked((int)value) : int.MinValue;
        }

        // 0x005B26F0 sets 64-bit precision and the conversion adds the double at 0x007AF048,
        // 1.5*2^52 + 0x8008. Sums near that bias keep 2^-11 on the FPU; storing the double
        // keeps integers; the low dword shifted right 16 indexes the clamp table.
        public static long RoundToExtendedGrid(long units)
        {
            return RoundShift(units, FractionBits - 11) << (FractionBits - 11);
        }

        public static byte ToColour(long units)
        {
            long integer = RoundShift(RoundShift(units, FractionBits - 11), 11);
            return Clamp(unchecked((int)(integer + 0x8008)) >> 16);
        }

        private static long RoundShift(long value, int shift)
        {
            long quotient = value >> shift;
            long remainder = value - (quotient << shift);
            long half = 1L << (shift - 1);
            if (remainder > half || (remainder == half && (quotient & 1) != 0))
            {
                quotient++;
            }

            return quotient;
        }
    }

    // Bits are taken most significant first from little-endian dwords (the SHLD pair).
    private struct BitReader
    {
        private readonly uint[] _words;
        private readonly int _limit;
        private int _position;

        public BitReader(uint[] words, int wordCount, ReadOnlySpan<byte> bytes)
        {
            _words = words;
            _limit = wordCount;
            _position = 0;
            int whole = bytes.Length / 4;
            for (int i = 0; i < whole; i++)
            {
                words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i * 4));
            }

            Array.Clear(words, whole, wordCount - whole);
            for (int i = whole * 4; i < bytes.Length; i++)
            {
                words[whole] |= (uint)bytes[i] << ((i & 3) * 8);
            }
        }

        public uint Peek()
        {
            int word = _position >> 5;
            if (word + 1 >= _limit)
            {
                throw new InvalidDataException("Texture bitstream ends inside a block.");
            }

            ulong pair = ((ulong)_words[word] << 32) | _words[word + 1];
            return (uint)((pair << (_position & 31)) >> 32);
        }

        public void Skip(int count)
        {
            _position += count;
        }

        // 0x0065C290: the leading byte indexes 0x007AF4E0 for a size (high nibble) and a
        // prefix length (low nibble). The value has that many bits; a clear top bit makes it
        // negative by subtracting 2^size - 1.
        public int ReadDcDifference()
        {
            byte entry = DcCodes[(int)(Peek() >> 24)];
            _position += entry & 0xF;
            int size = entry >> 4;
            if (size == 0)
            {
                return 0;
            }

            int value = (int)(Peek() >> (32 - size));
            _position += size;
            return (value & (1 << (size - 1))) != 0 ? value : value - ((1 << size) - 1);
        }
    }

    // 0x007AF0E0 (engine) and 0x007AF680 (loading screens), identical: block position of
    // each slot. The slot counter starts at 63, so the first AC value lands at slot 63.
    private static ReadOnlySpan<byte> ZigzagSlots
    {
        get
        {
            return [
        0, 63, 55, 62, 61, 54, 47, 39, 46, 53, 60, 59, 52, 45, 38, 31,
        23, 30, 37, 44, 51, 58, 57, 50, 43, 36, 29, 22, 15, 7, 14, 21,
        28, 35, 42, 49, 56, 48, 41, 34, 27, 20, 13, 6, 5, 12, 19, 26,
        33, 40, 32, 25, 18, 11, 4, 3, 10, 17, 24, 16, 9, 2, 1, 8,
    ];
        }
    }

    // 0x007AF1E0..0x007AF460 (engine) and 0x007AF780..0x007AFA00 (loading screens), identical.
    private static readonly uint[] AcCodes =
    [
        0x40102, 0x40301, 0x30201, 0x30201, 0x24100, 0x24100, 0x24100, 0x24100,
        0x20101, 0x20101, 0x20101, 0x20101, 0x64200, 0x64200, 0x64200, 0x64200,
        0x70302, 0x70302, 0x70A01, 0x70A01, 0x70104, 0x70104, 0x70901, 0x70901,
        0x60801, 0x60801, 0x60801, 0x60801, 0x60701, 0x60701, 0x60701, 0x60701,
        0x60202, 0x60202, 0x60202, 0x60202, 0x60601, 0x60601, 0x60601, 0x60601,
        0x80E01, 0x80106, 0x80D01, 0x80C01, 0x80402, 0x80203, 0x80105, 0x80B01,
        0x50103, 0x50103, 0x50103, 0x50103, 0x50103, 0x50103, 0x50103, 0x50103,
        0x50501, 0x50501, 0x50501, 0x50501, 0x50501, 0x50501, 0x50501, 0x50501,
        0x50401, 0x50401, 0x50401, 0x50401, 0x50401, 0x50401, 0x50401, 0x50401,
        0xA1101, 0xA0602, 0xA0107, 0xA0303, 0xA0204, 0xA1001, 0xA0F01, 0xA0502,
        0xC010B, 0xC0902, 0xC0503, 0xC010A, 0xC0304, 0xC0802, 0xC1601, 0xC1501,
        0xC0109, 0xC1401, 0xC1301, 0xC0205, 0xC0403, 0xC0108, 0xC0702, 0xC1201,
        0xD0B02, 0xD0A02, 0xD0603, 0xD0404, 0xD0305, 0xD0207, 0xD0206, 0xD010F,
        0xD010E, 0xD010D, 0xD010C, 0xD1B01, 0xD1A01, 0xD1901, 0xD1801, 0xD1701,
        0xE011F, 0xE011E, 0xE011D, 0xE011C, 0xE011B, 0xE011A, 0xE0119, 0xE0118,
        0xE0117, 0xE0116, 0xE0115, 0xE0114, 0xE0113, 0xE0112, 0xE0111, 0xE0110,
        0xF0128, 0xF0127, 0xF0126, 0xF0125, 0xF0124, 0xF0123, 0xF0122, 0xF0121,
        0xF0120, 0xF020E, 0xF020D, 0xF020C, 0xF020B, 0xF020A, 0xF0209, 0xF0208,
        0x100212, 0x100211, 0x100210, 0x10020F, 0x100703, 0x101102, 0x101002, 0x100F02,
        0x100E02, 0x100D02, 0x100C02, 0x102001, 0x101F01, 0x101E01, 0x101D01, 0x101C01,
    ];

    // 0x007AF4E0, indexed by the next eight bits.
    private static ReadOnlySpan<byte> DcCodes
    {
        get
        {
            return [
        0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
        0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
        0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
        0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
        0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12,
        0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12,
        0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12,
        0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12, 0x12,
        0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22,
        0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22,
        0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22,
        0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22,
        0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33,
        0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x33,
        0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x44,
        0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x66, 0x66, 0x66, 0x66, 0x77, 0x77, 0x88, 0x00,
    ];
        }
    }

    // 0x006C10C0 (engine) and 0x00672608 (loading screens), identical, times 8 as the setup
    // routines 0x005B2450 and 0x005D7360 store them: the DC step is 65536, so DC is 16.16.
    private static readonly int[] Dequantisation = BuildDequantisation(
    [
        8192, 5906, 6270, 6967, 8192, 10426, 15137, 29692,
        5906, 4258, 4520, 5023, 5906, 7517, 10913, 21407,
        6270, 4520, 4799, 5332, 6270, 7980, 11585, 22725,
        6967, 5023, 5332, 5925, 6967, 8867, 12873, 25251,
        8192, 5906, 6270, 6967, 8192, 10426, 15137, 29692,
        10426, 7517, 7980, 8867, 10426, 13270, 19266, 37791,
        15137, 10913, 11585, 12873, 15137, 19266, 27969, 54864,
        29692, 21407, 22725, 25251, 29692, 37791, 54864, 107619,
    ]);

    private static int[] BuildDequantisation(int[] table)
    {
        for (int i = 0; i < table.Length; i++)
        {
            table[i] *= 8;
        }

        return table;
    }
}
