// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Data.Media;

/// <summary>
/// Decodes one TQI video frame (<c>pIQT</c>) to RGBA32 at the frame's stored size, as DKII.EXE's
/// EA player does (<c>docs/research/movie-format.md</c> §4): the matrix of <c>0x005AAB10</c>, the
/// block decoder <c>0x005AE220</c>, the AAN IDCT <c>0x005B018C</c>/<c>0x005B03B5</c> and the colour
/// tables of <c>0x005A9DA0</c> with the combiner <c>0x005AF310</c>. DK2 shows the 320-wide frame
/// doubled horizontally (§4.7); that is the caller's display concern, not done here.
/// Rows are written top-down.
/// </summary>
public sealed class TqiDecoder
{
    private const int PeekBits = 17;

    // MPEG-1 Table B.14 (dct_coeff_next) as DK2's eight lookup tables at 0x007AC69C..0x007AC8AC
    // hold it: code, run, level; "EOB" and "ESC" are the end of block and the escape.
    private static readonly (string Code, int Run, int Level)[] AcCodes =
    [
        ("10", -1, 0), ("000001", -2, 0),
        ("11", 0, 1), ("011", 1, 1), ("0100", 0, 2), ("0101", 2, 1), ("00101", 0, 3), ("00111", 3, 1),
        ("00110", 4, 1), ("000110", 1, 2), ("000111", 5, 1), ("000101", 6, 1), ("000100", 7, 1),
        ("0000110", 0, 4), ("0000100", 2, 2), ("0000111", 8, 1), ("0000101", 9, 1),
        ("00100110", 0, 5), ("00100001", 0, 6), ("00100101", 1, 3), ("00100100", 3, 2), ("00100111", 10, 1),
        ("00100011", 11, 1), ("00100010", 12, 1), ("00100000", 13, 1),
        ("0000001010", 0, 7), ("0000001100", 1, 4), ("0000001011", 2, 3), ("0000001111", 4, 2),
        ("0000001001", 5, 2), ("0000001110", 14, 1), ("0000001101", 15, 1), ("0000001000", 16, 1),
        ("000000011101", 0, 8), ("000000011000", 0, 9), ("000000010011", 0, 10), ("000000010000", 0, 11),
        ("000000011011", 1, 5), ("000000010100", 2, 4), ("000000011100", 3, 3), ("000000010010", 4, 3),
        ("000000011110", 6, 2), ("000000010101", 7, 2), ("000000010001", 8, 2), ("000000011111", 17, 1),
        ("000000011010", 18, 1), ("000000011001", 19, 1), ("000000010111", 20, 1), ("000000010110", 21, 1),
        ("0000000011010", 0, 12), ("0000000011001", 0, 13), ("0000000011000", 0, 14), ("0000000010111", 0, 15),
        ("0000000010110", 1, 6), ("0000000010101", 1, 7), ("0000000010100", 2, 5), ("0000000010011", 3, 4),
        ("0000000010010", 5, 3), ("0000000010001", 9, 2), ("0000000010000", 10, 2), ("0000000011111", 22, 1),
        ("0000000011110", 23, 1), ("0000000011101", 24, 1), ("0000000011100", 25, 1), ("0000000011011", 26, 1),
        ("00000000011111", 0, 16), ("00000000011110", 0, 17), ("00000000011101", 0, 18), ("00000000011100", 0, 19),
        ("00000000011011", 0, 20), ("00000000011010", 0, 21), ("00000000011001", 0, 22), ("00000000011000", 0, 23),
        ("00000000010111", 0, 24), ("00000000010110", 0, 25), ("00000000010101", 0, 26), ("00000000010100", 0, 27),
        ("00000000010011", 0, 28), ("00000000010010", 0, 29), ("00000000010001", 0, 30), ("00000000010000", 0, 31),
        ("000000000011000", 0, 32), ("000000000010111", 0, 33), ("000000000010110", 0, 34), ("000000000010101", 0, 35),
        ("000000000010100", 0, 36), ("000000000010011", 0, 37), ("000000000010010", 0, 38), ("000000000010001", 0, 39),
        ("000000000010000", 0, 40), ("000000000011111", 1, 8), ("000000000011110", 1, 9), ("000000000011101", 1, 10),
        ("000000000011100", 1, 11), ("000000000011011", 1, 12), ("000000000011010", 1, 13), ("000000000011001", 1, 14),
        ("0000000000010011", 1, 15), ("0000000000010010", 1, 16), ("0000000000010001", 1, 17), ("0000000000010000", 1, 18),
        ("0000000000010100", 6, 3), ("0000000000011010", 11, 2), ("0000000000011001", 12, 2), ("0000000000011000", 13, 2),
        ("0000000000010111", 14, 2), ("0000000000010110", 15, 2), ("0000000000010101", 16, 2), ("0000000000011111", 27, 1),
        ("0000000000011110", 28, 1), ("0000000000011101", 29, 1), ("0000000000011100", 30, 1), ("0000000000011011", 31, 1),
    ];

    // DC size codes, tables 0x007AC92C (luma, 7-bit peek) and 0x007AC9AC (chroma, 8-bit peek).
    private static readonly string[] LumaDc = ["100", "00", "01", "101", "110", "1110", "11110", "111110", "1111110"];
    private static readonly string[] ChromaDc = ["00", "01", "10", "110", "1110", "11110", "111110", "1111110", "11111110"];

    // The MPEG zigzag in natural order; DK2 stores it transposed (0x007AC5B0) and transposes
    // back in the IDCT, which this decoder folds away by using the natural order throughout.
    private static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // 0x00670860: the AAN prescale in Q13, 8192 / (a_r a_c).
    private static readonly int[] Prescale =
    [
        8192, 5906, 6270, 6967, 8192, 10426, 15137, 29692, 5906, 4258, 4520, 5023, 5906, 7517, 10913, 21407,
        6270, 4520, 4799, 5332, 6270, 7980, 11585, 22725, 6967, 5023, 5332, 5925, 6967, 8867, 12873, 25251,
        8192, 5906, 6270, 6967, 8192, 10426, 15137, 29692, 10426, 7517, 7980, 8867, 10426, 13270, 19266, 37791,
        15137, 10913, 11585, 12873, 15137, 19266, 27969, 54864, 29692, 21407, 22725, 25251, 29692, 37791, 54864, 107619,
    ];

    // 0x00670960: the MPEG-1 default intra matrix.
    private static readonly int[] IntraMatrix =
    [
        8, 16, 19, 22, 26, 27, 29, 34, 16, 16, 22, 24, 27, 29, 34, 37, 19, 22, 26, 27, 29, 34, 34, 38, 22, 22, 26, 27, 29, 34, 37, 40,
        22, 26, 27, 29, 32, 35, 40, 48, 26, 27, 29, 32, 35, 40, 48, 58, 26, 27, 29, 34, 38, 46, 56, 69, 27, 29, 35, 38, 46, 56, 69, 83,
    ];

    // IDCT constants 0x007AC598..0x007AC5A4; the floats are widened, as x87 loads them.
    private const long SqrtHalfQ31 = 0x5A82799A;
    private const double OddA = 0.54119611f, OddB = 1.30656302f, OddZ = 0.38268343f;

    private static readonly int[] AcTable = BuildAcTable();
    private static readonly byte[] LumaDcTable = BuildDcTable(LumaDc);
    private static readonly byte[] ChromaDcTable = BuildDcTable(ChromaDc);
    private static readonly uint[] YTable = new uint[128], CbTable = new uint[128], CrTable = new uint[128];

    private readonly int[] _matrix = new int[64];
    private readonly int[] _block = new int[64];
    private readonly int[] _scratch = new int[64];
    private int _matrixQuantiser = -1;
    private uint[] _words = [];

    static TqiDecoder()
    {
        // 0x005A9DA0: x = clamp(v - 64, -64, 63); chroma uses f = x + 0.5 and truncates toward zero.
        for (int i = 0; i < 128; i++)
        {
            int x = i - 64;
            double f = x + 0.5;
            YTable[i] = unchecked((uint)(x * 0x800801) + 0x4883D8A9u);
            int a = (int)(0.5 - 0.3441 * f), b = (int)(0.5 + 1.772 * f);
            int c = (int)(0.5 - 0.7141 * f), d = (int)(0.5 + 1.402 * f);
            CbTable[i] = unchecked((uint)(((a & 0x1FF) << 11) + (b & 0x1FF)));
            CrTable[i] = unchecked((uint)(((c & 0x1FF) + ((d & 0x1FF) << 12)) << 11));
        }
    }

    public static (int Width, int Height) FrameSize(ReadOnlySpan<byte> payload) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(payload), BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]));

    /// <summary>Decodes <paramref name="payload"/> into <paramref name="rgba"/> (width * height * 4
    /// bytes) and returns the number of bitstream bits consumed.</summary>
    public int Decode(ReadOnlySpan<byte> payload, Span<byte> rgba)
    {
        if (payload.Length < 8) throw new InvalidDataException("TQI frame is shorter than its header");
        var (width, height) = FrameSize(payload);
        if (width % 16 != 0 || height % 16 != 0) throw new InvalidDataException($"TQI frame {width}x{height} is not whole macroblocks");
        if (rgba.Length < width * height * 4) throw new ArgumentException("output is too small", nameof(rgba));
        BuildMatrix(payload[4]);
        LoadWords(payload[8..]);

        int position = 0;
        int predY = 0, predCb = 0, predCr = 0; // reset once per frame (0x005A95B0)
        Span<byte> luma = stackalloc byte[256];
        Span<byte> cb = stackalloc byte[64];
        Span<byte> cr = stackalloc byte[64];
        for (int my = 0; my < height / 16; my++)
        {
            for (int mx = 0; mx < width / 16; mx++)
            {
                for (int b = 0; b < 4; b++)
                {
                    DecodeBlock(ref position, LumaDcTable, ref predY);
                    Store(luma, 16, (b & 1) * 8, (b >> 1) * 8);
                }
                DecodeBlock(ref position, ChromaDcTable, ref predCb);
                Store(cb, 8, 0, 0);
                DecodeBlock(ref position, ChromaDcTable, ref predCr);
                Store(cr, 8, 0, 0);
                WritePixels(rgba, width, mx * 16, my * 16, luma, cb, cr);
            }
        }
        return position;
    }

    private void BuildMatrix(int quantiser)
    {
        if (quantiser == _matrixQuantiser) return;
        // 0x005AAB10. q = 100 is a flat 8 * S; otherwise S scaled by the intra matrix and
        // (107 - q) / 16. Coefficient 0 always takes 0x10000. DK2 indexes M by the transposed
        // position; with the natural zigzag used here the multiplier is M at the transposed index.
        var natural = new int[64];
        float factor = (107 - quantiser) * 0.0625f;
        for (int i = 0; i < 64; i++)
            natural[i] = quantiser == 100
                ? FixMul(Prescale[i], 0x80000)
                : FixMul(Prescale[i], (int)(IntraMatrix[i] * (double)factor * 65536.0));
        natural[0] = FixMul(Prescale[0], 0x80000);
        for (int i = 0; i < 64; i++) _matrix[i] = natural[(i & 7) * 8 + (i >> 3)];
        _matrixQuantiser = quantiser;
    }

    private static int FixMul(int a, int b) => (int)(((long)a * b + 0x8000) >> 16);

    private void LoadWords(ReadOnlySpan<byte> bits)
    {
        int count = bits.Length / 4 + 3;
        if (_words.Length < count) _words = new uint[count];
        Array.Clear(_words, 0, _words.Length);
        for (int i = 0; i < bits.Length / 4; i++) _words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bits[(i * 4)..]);
        // A trailing partial word is read as DK2 reads it: the bytes present, low first.
        for (int i = bits.Length / 4 * 4, shift = 0; i < bits.Length; i++, shift += 8) _words[bits.Length / 4] |= (uint)bits[i] << shift;
    }

    // Words are little-endian u32s, bits taken most significant first within each (0x005AE220).
    private uint Peek(int position, int count)
    {
        int index = position >> 5;
        ulong window = ((ulong)_words[index] << 32) | _words[index + 1];
        return (uint)((window << (position & 31)) >> (64 - count));
    }

    private void DecodeBlock(ref int position, byte[] dcTable, ref int predictor)
    {
        Array.Clear(_block, 0, 64);
        byte dc = dcTable[Peek(position, 8)];
        if (dc == 0) throw new InvalidDataException($"invalid DC size code at bit {position}");
        position += dc >> 4;
        int size = dc & 0xF;
        if (size > 0)
        {
            int value = (int)Peek(position, size);
            position += size;
            predictor += (value >> (size - 1)) != 0 ? value : value - ((1 << size) - 1);
        }
        _block[0] = unchecked(predictor * _matrix[0]);

        bool any = false;
        int k = 0;
        while (true)
        {
            int entry = AcTable[Peek(position, PeekBits)];
            if (entry == 0) throw new InvalidDataException($"invalid AC code at bit {position}");
            position += entry >> 24;
            int run = (entry >> 16) & 0xFF;
            if (run == 0xFF) break; // end of block
            int level;
            if (run == 0xFE)
            {
                // Escape: 6-bit run, 8-bit level, 0x00 and 0x80 extend by 8 bits; no sign bit.
                run = (int)Peek(position, 6);
                level = (int)Peek(position + 6, 8);
                position += 14;
                if (level == 0) { level = (int)Peek(position, 8); position += 8; }
                else if (level == 0x80) { level = (int)Peek(position, 8) - 256; position += 8; }
                else if (level > 0x80) level -= 256;
            }
            else
            {
                level = entry & 0xFFFF;
                if (Peek(position, 1) != 0) level = -level;
                position++;
            }
            k += run + 1;
            if (k > 63) throw new InvalidDataException($"coefficient index past 63 at bit {position}");
            int at = ZigZag[k];
            _block[at] = unchecked(level * _matrix[at]);
            any = true;
        }

        if (!any)
        {
            // 0x005AE91A skips the IDCT: every sample is coefficient 0 >> 17.
            int v = _block[0] >> 17;
            for (int i = 0; i < 64; i++) _block[i] = v;
            return;
        }
        InverseDct();
    }

    private void InverseDct()
    {
        Span<int> s = stackalloc int[8];
        Span<int> o = stackalloc int[8];
        // DK2's first pass runs on the rows of its transposed block, that is on natural columns.
        for (int c = 0; c < 8; c++)
        {
            for (int r = 0; r < 8; r++) s[r] = _block[r * 8 + c];
            Butterfly(s, o);
            for (int r = 0; r < 8; r++) _scratch[r * 8 + c] = o[r];
        }
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++) s[c] = _scratch[r * 8 + c];
            Butterfly(s, o);
            for (int c = 0; c < 8; c++) _block[r * 8 + c] = o[c] >> 17;
        }
    }

    private static int MulSqrtHalf(int x) => (int)(((long)x * SqrtHalfQ31) >> 32) * 2;

    private static int Round(double x) => (int)Math.Round(x, MidpointRounding.ToEven);

    private static void Butterfly(ReadOnlySpan<int> s, Span<int> o)
    {
        unchecked
        {
            int d53 = s[5] - s[3], d17 = s[1] - s[7];
            int oddSum = s[1] + s[3] + s[5] + s[7];
            int oddDiff = (s[1] + s[7]) - (s[3] + s[5]);
            double z5 = OddZ * ((double)d17 + d53);
            int a = Round(OddA * d53 + z5);
            int b = Round(OddB * d17 - z5);
            int t = MulSqrtHalf(oddDiff);
            int q1 = a + t, q2 = b + t, q3 = b + oddSum;
            int e = MulSqrtHalf(s[2] - s[6]);
            int even = s[2] + s[6] + e;
            int sum = s[0] + s[4], diff = s[0] - s[4];
            int c0 = sum + even, c3 = sum - even, c1 = diff + e, c2 = diff - e;
            o[0] = c0 + q3; o[1] = c1 + q2; o[2] = c2 + q1; o[3] = c3 + a;
            o[4] = c3 - a; o[5] = c2 - q1; o[6] = c1 - q2; o[7] = c0 - q3;
        }
    }

    private void Store(Span<byte> target, int stride, int x0, int y0)
    {
        // The colour tables clamp v - 64 to [-64, 63], so a sample is kept as its table index.
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                target[(y0 + r) * stride + x0 + c] = (byte)Math.Clamp(_block[r * 8 + c], 0, 127);
    }

    private static void WritePixels(Span<byte> rgba, int width, int x0, int y0, ReadOnlySpan<byte> luma, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr)
    {
        // 0x005AF310: one chroma sample serves a 2x2 luma square; the three table words add as
        // one wrapping uint and split into 9-bit fields, then R/G/B = clamp(2i - bias).
        for (int y = 0; y < 16; y++)
        {
            int row = ((y0 + y) * width + x0) * 4;
            for (int x = 0; x < 16; x++)
            {
                int chroma = (y >> 1) * 8 + (x >> 1);
                uint w = unchecked(YTable[luma[y * 16 + x]] + CbTable[cb[chroma]] + CrTable[cr[chroma]]);
                int at = row + x * 4;
                rgba[at] = Channel((int)(w >> 23), 177);
                rgba[at + 1] = Channel((int)((w >> 11) & 0x1FF), 133);
                rgba[at + 2] = Channel((int)(w & 0x1FF), 225);
                rgba[at + 3] = 255;
            }
        }
    }

    // The R, G and B tables are contiguous (306, 262, 354 entries); an index past one table's
    // end reads the next table in DK2, which this clamp does not reproduce (extreme colours only).
    private static byte Channel(int index, int bias) => (byte)Math.Clamp(2 * index - bias, 0, 255);

    private static int[] BuildAcTable()
    {
        var table = new int[1 << PeekBits];
        foreach (var (code, run, level) in AcCodes)
        {
            int length = code.Length;
            int prefix = Convert.ToInt32(code, 2) << (PeekBits - length);
            int packedRun = run == -1 ? 0xFF : run == -2 ? 0xFE : run;
            int entry = (length << 24) | (packedRun << 16) | level;
            for (int i = 0; i < 1 << (PeekBits - length); i++) table[prefix + i] = entry;
        }
        return table;
    }

    private static byte[] BuildDcTable(string[] codes)
    {
        var table = new byte[256];
        for (int size = 0; size < codes.Length; size++)
        {
            int length = codes[size].Length;
            int prefix = Convert.ToInt32(codes[size], 2) << (8 - length);
            for (int i = 0; i < 1 << (8 - length); i++) table[prefix + i] = (byte)((length << 4) | size);
        }
        return table;
    }
}
