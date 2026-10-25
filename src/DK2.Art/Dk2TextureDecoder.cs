// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Art.Textures;

/// <summary>
/// Decodes the 8x8 coefficient blocks used by DK2's engine texture cache.
/// Numeric decode tables are format data corroborated with OpenKeeper.
/// </summary>
internal sealed class Dk2TextureDecoder
{
    private static readonly int[] QuantizerSource =
    [
        0x2000, 0x1712, 0x187e, 0x1b37, 0x2000, 0x28ba, 0x3b21, 0x73fc,
        0x1712, 0x10a2, 0x11a8, 0x139f, 0x1712, 0x1d5d, 0x2aa1, 0x539f,
        0x187e, 0x11a8, 0x12bf, 0x14d4, 0x187e, 0x1f2c, 0x2d41, 0x58c5,
        0x1b37, 0x139f, 0x14d4, 0x1725, 0x1b37, 0x22a3, 0x3249, 0x62a3,
        0x2000, 0x1712, 0x187e, 0x1b37, 0x2000, 0x28ba, 0x3b21, 0x73fc,
        0x28ba, 0x1d5d, 0x1f2c, 0x22a3, 0x28ba, 0x33d6, 0x4b42, 0x939f,
        0x3b21, 0x2aa1, 0x2d41, 0x3249, 0x3b21, 0x4b42, 0x6d41, 0xd650,
        0x73fc, 0x539f, 0x58c5, 0x62a3, 0x73fc, 0x939f, 0xd650, 0x1a463,
    ];

    private static readonly int[] CoefficientControl =
    [
        0x00000000, 0x0000003f, 0x00000037, 0x0000003e, 0x0000003d, 0x00000036,
        0x0000002f, 0x00000027, 0x0000002e, 0x00000035, 0x0000003c, 0x0000003b,
        0x00000034, 0x0000002d, 0x00000026, 0x0000001f, 0x00000017, 0x0000001e,
        0x00000025, 0x0000002c, 0x00000033, 0x0000003a, 0x00000039, 0x00000032,
        0x0000002b, 0x00000024, 0x0000001d, 0x00000016, 0x0000000f, 0x00000007,
        0x0000000e, 0x00000015, 0x0000001c, 0x00000023, 0x0000002a, 0x00000031,
        0x00000038, 0x00000030, 0x00000029, 0x00000022, 0x0000001b, 0x00000014,
        0x0000000d, 0x00000006, 0x00000005, 0x0000000c, 0x00000013, 0x0000001a,
        0x00000021, 0x00000028, 0x00000020, 0x00000019, 0x00000012, 0x0000000b,
        0x00000004, 0x00000003, 0x0000000a, 0x00000011, 0x00000018, 0x00000010,
        0x00000009, 0x00000002, 0x00000001, 0x00000008,
        0x00040102, 0x00040301, 0x00030201, 0x00030201,
        0x00024100, 0x00024100, 0x00024100, 0x00024100,
        0x00020101, 0x00020101, 0x00020101, 0x00020101,
        0x00064200, 0x00064200, 0x00064200, 0x00064200,
        0x00070302, 0x00070302, 0x00070a01, 0x00070a01,
        0x00070104, 0x00070104, 0x00070901, 0x00070901,
        0x00060801, 0x00060801, 0x00060801, 0x00060801,
        0x00060701, 0x00060701, 0x00060701, 0x00060701,
        0x00060202, 0x00060202, 0x00060202, 0x00060202,
        0x00060601, 0x00060601, 0x00060601, 0x00060601,
        0x00080e01, 0x00080106, 0x00080d01, 0x00080c01,
        0x00080402, 0x00080203, 0x00080105, 0x00080b01,
        0x00050103, 0x00050103, 0x00050103, 0x00050103,
        0x00050103, 0x00050103, 0x00050103, 0x00050103,
        0x00050501, 0x00050501, 0x00050501, 0x00050501,
        0x00050501, 0x00050501, 0x00050501, 0x00050501,
        0x00050401, 0x00050401, 0x00050401, 0x00050401,
        0x00050401, 0x00050401, 0x00050401, 0x00050401,
        0x000a1101, 0x000a0602, 0x000a0107, 0x000a0303,
        0x000a0204, 0x000a1001, 0x000a0f01, 0x000a0502,
        0x000c010b, 0x000c0902, 0x000c0503, 0x000c010a,
        0x000c0304, 0x000c0802, 0x000c1601, 0x000c1501,
        0x000c0109, 0x000c1401, 0x000c1301, 0x000c0205,
        0x000c0403, 0x000c0108, 0x000c0702, 0x000c1201,
        0x000d0b02, 0x000d0a02, 0x000d0603, 0x000d0404,
        0x000d0305, 0x000d0207, 0x000d0206, 0x000d010f,
        0x000d010e, 0x000d010d, 0x000d010c, 0x000d1b01,
        0x000d1a01, 0x000d1901, 0x000d1801, 0x000d1701,
        0x000e011f, 0x000e011e, 0x000e011d, 0x000e011c,
        0x000e011b, 0x000e011a, 0x000e0119, 0x000e0118,
        0x000e0117, 0x000e0116, 0x000e0115, 0x000e0114,
        0x000e0113, 0x000e0112, 0x000e0111, 0x000e0110,
        0x000f0128, 0x000f0127, 0x000f0126, 0x000f0125,
        0x000f0124, 0x000f0123, 0x000f0122, 0x000f0121,
        0x000f0120, 0x000f020e, 0x000f020d, 0x000f020c,
        0x000f020b, 0x000f020a, 0x000f0209, 0x000f0208,
        0x00100212, 0x00100211, 0x00100210, 0x0010020f,
        0x00100703, 0x00101102, 0x00101002, 0x00100f02,
        0x00100e02, 0x00100d02, 0x00100c02, 0x00102001,
        0x00101f01, 0x00101e01, 0x00101d01, 0x00101c01,
    ];

    private static readonly int[] Quantizer = QuantizerSource.Select(value =>
        ((value & unchecked((int)0xfffe0000)) >> 3) + ((value & 0x1ffff) << 3)).ToArray();

    private readonly uint[] _bits;
    private readonly int[] _coefficients = new int[256];
    private readonly int[] _firstPass = new int[288];
    private readonly int[] _samples = new int[512];
    private int _bitPosition;
    private long _lumaPredictor;
    private long _bluePredictor;
    private long _redPredictor;
    private long _alphaPredictor;

    private Dk2TextureDecoder(ReadOnlySpan<byte> compressed)
    {
        if ((compressed.Length & 3) != 0)
            throw new InvalidDataException("Engine texture payload is not a whole number of words.");
        _bits = new uint[compressed.Length / 4];
        for (int i = 0; i < _bits.Length; i++)
            _bits[i] = BinaryPrimitives.ReadUInt32LittleEndian(compressed[(i * 4)..]);
    }

    public static byte[] Decode(ReadOnlySpan<byte> compressed, int width, int height, bool alpha)
        => Decode(compressed, width, height, alpha, directRgb: false);

    internal static byte[] DecodeLoadingScreen(ReadOnlySpan<byte> compressed, int width, int height, bool alpha)
        => Decode(compressed, width, height, alpha, directRgb: true);

    private static byte[] Decode(ReadOnlySpan<byte> compressed, int width, int height, bool alpha, bool directRgb)
    {
        if (width <= 0 || height <= 0 || (width & 7) != 0 || (height & 7) != 0)
            throw new InvalidDataException("Engine texture dimensions must be positive multiples of eight.");
        var decoder = new Dk2TextureDecoder(compressed);
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y += 8)
            for (int x = 0; x < width; x += 8)
                decoder.DecodeBlock(pixels, width, x, y, alpha, directRgb);
        return pixels;
    }

    private void DecodeBlock(byte[] output, int width, int left, int top, bool alpha, bool directRgb)
    {
        DecodeChannel(ref _lumaPredictor, 0, directRgb);
        DecodeChannel(ref _bluePredictor, 9, directRgb);
        DecodeChannel(ref _redPredictor, 18, directRgb);
        if (alpha) DecodeChannel(ref _alphaPredictor, 27, directRgb);

        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int sample = y * 64 + x;
                int pixel = ((top + y) * width + left + x) * 4;
                if (directRgb)
                {
                    output[pixel] = ClampByte((int)(((long)_samples[sample] + 0x8000) >> 16));
                    output[pixel + 1] = ClampByte((int)(((long)_samples[sample + 9] + 0x8000) >> 16));
                    output[pixel + 2] = ClampByte((int)(((long)_samples[sample + 18] + 0x8000) >> 16));
                    output[pixel + 3] = alpha ? ClampByte(_samples[sample + 27] >> 16) : (byte)255;
                    continue;
                }
                double luma = 1.169 * (_samples[sample] - 1_048_576f);
                double blue = 2.0250001 * (_samples[sample + 9] - 8_388_608f) + luma;
                double red = 1.602 * (_samples[sample + 18] - 8_388_608f) + luma;
                double green = -0.391 * (_samples[sample + 9] - 8_388_608f)
                    - 0.81300002 * (_samples[sample + 18] - 8_388_608f) + luma;
                output[pixel] = ClampByte(ConvertColor(red));
                output[pixel + 1] = ClampByte(ConvertColor(green));
                output[pixel + 2] = ClampByte(ConvertColor(blue));
                output[pixel + 3] = alpha ? ClampByte(_samples[sample + 27] >> 16) : (byte)255;
            }
    }

    private void DecodeChannel(ref long predictor, int sampleOffset, bool directRgb)
    {
        if (directRgb)
        {
            int dc = (int)ReadBits(_bitPosition, 8);
            _bitPosition = DecodeCoefficients(dc, _bitPosition + 8);
            for (int i = 0; i < 8; i++) TransformColumns(i * 8, i);
            for (int i = 0; i < 8; i++) TransformRows(i * 9, i * 64 + sampleOffset);
            return;
        }
        int prefix = (int)ReadBits(_bitPosition, 8);
        (int skip, int bits) = Prefix(prefix);
        _bitPosition += skip;
        int delta = 0;
        if (bits != 0)
        {
            delta = (int)ReadBits(_bitPosition, bits);
            if ((delta & (1 << (bits - 1))) == 0) delta -= (1 << bits) - 1;
            _bitPosition += bits;
        }
        predictor += delta;
        if (ReadBits(_bitPosition, 2) == 2)
        {
            _bitPosition += 2;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    _samples[y * 64 + x + sampleOffset] = unchecked((int)predictor << 16);
            return;
        }

        _bitPosition = DecodeCoefficients(unchecked((int)predictor), _bitPosition);
        for (int i = 0; i < 8; i++) TransformColumns(i * 8, i);
        for (int i = 0; i < 8; i++) TransformRows(i * 9, i * 64 + sampleOffset);
    }

    private int DecodeCoefficients(int dc, int position)
    {
        _coefficients[0] = unchecked(dc * Quantizer[0]);
        Array.Clear(_coefficients, 1, _coefficients.Length - 1);
        int zigzag = 0x3f;
        int lookahead = 0;
        int control = 0;
        bool selected = false;
        while (true)
        {
            if (!selected) lookahead = (int)ReadBits(position, 17);
            int index = lookahead;
            if (index >= 0x8000 || selected)
            {
                if (!selected) control = CoefficientControl[60 + (index >> 13)];
                selected = false;
                if ((control & 0xff00) == 0x4100) return position + (control >> 16);
                int coefficient;
                if ((control & 0xff00) > 0x4100)
                {
                    position += control >> 16;
                    int extended = (int)ReadBits(position, 14);
                    position += 14;
                    zigzag -= (extended & 0xff00) >> 8;
                    coefficient = extended & 0xff;
                    if (coefficient != 0)
                    {
                        if (coefficient != 0x80)
                        {
                            if (coefficient > 0x80) coefficient -= 0x100;
                        }
                        else
                        {
                            coefficient = (int)ReadBits(position, 8) - 0x100;
                            position += 8;
                        }
                    }
                    else
                    {
                        coefficient = (int)ReadBits(position, 8);
                        position += 8;
                    }
                    zigzag--;
                }
                else
                {
                    int length = control >> 16;
                    zigzag -= (control & 0xff00) >> 8;
                    int signMask = (lookahead & (1 << (16 - length))) != 0 ? -1 : 0;
                    coefficient = (control & 0xff) ^ signMask;
                    position++;
                    coefficient -= signMask;
                    position += length;
                }
                if (zigzag < 0 || zigzag >= 64)
                    throw new InvalidDataException("Texture coefficient run exceeds its block.");
                int outputIndex = CoefficientControl[zigzag + 1];
                _coefficients[outputIndex] = unchecked((short)coefficient * Quantizer[outputIndex]);
                continue;
            }

            if (index >= 0x800) control = CoefficientControl[72 + (index >> 9)];
            else if (index >= 0x400) control = CoefficientControl[128 + (index >> 7)];
            else if (index >= 0x200) control = CoefficientControl[128 + (index >> 5)];
            else if (index >= 0x100) control = CoefficientControl[144 + (index >> 4)];
            else if (index >= 0x80) control = CoefficientControl[160 + (index >> 3)];
            else if (index >= 0x40) control = CoefficientControl[176 + (index >> 2)];
            else if (index >= 0x20) control = CoefficientControl[192 + (index >> 1)];
            else throw new InvalidDataException("Invalid texture coefficient code.");
            selected = true;
        }
    }

    private void TransformColumns(int input, int output)
    {
        if ((_coefficients[input + 1] | _coefficients[input + 2] | _coefficients[input + 3]
            | _coefficients[input + 4] | _coefficients[input + 5] | _coefficients[input + 6] | _coefficients[input + 7]) == 0)
        {
            int value = _coefficients[input];
            for (int i = 0; i < 8; i++) _firstPass[output + i * 9] = value;
            return;
        }
        Transform(_coefficients, input, _firstPass, output, 9);
    }

    private void TransformRows(int input, int output) => Transform(_firstPass, input, _samples, output, 1);

    private static void Transform(int[] source, int input, int[] destination, int output, int stride)
    {
        int differenceA = source[input + 5] - source[input + 3];
        int differenceB = source[input + 1] - source[input + 7];
        int sumA = source[input + 3] + source[input + 5];
        int sumB = source[input + 7] + source[input + 1];
        int centre = unchecked(sumA + sumB);
        int diagonal = sumB - sumA;
        double combined = differenceB + differenceA;
        int rotatedA = Round(differenceA * 0.54119611 + 0.38268343 * combined);
        int rotatedB = Round(differenceB * 1.306563 - 0.38268343 * combined);
        int norm = unchecked((int)(((long)diagonal * 0x5a82799a) >> 32) * 2);
        int axisA = rotatedA + norm;
        int axisB = rotatedB + norm;
        int centreB = rotatedB + centre;
        int evenA = source[input + 6];
        int evenB = source[input + 2];
        int pairSum = evenA + evenB;
        int pairDifference = evenB - evenA;
        int outerSum = source[input + 4] + source[input];
        int outerDifference = source[input] - source[input + 4];
        int evenNorm = unchecked((int)(((long)pairDifference * 0x5a82799a) >> 32) * 2);

        destination[output + 2 * stride] = unchecked(outerDifference - evenNorm + axisA);
        destination[output + 5 * stride] = unchecked(outerDifference - evenNorm - axisA);
        destination[output + 3 * stride] = unchecked(outerSum - pairSum - evenNorm + rotatedA);
        destination[output + 4 * stride] = unchecked(outerSum - pairSum - evenNorm - rotatedA);
        destination[output] = unchecked(pairSum + evenNorm + outerSum + centreB);
        destination[output + stride] = unchecked(axisB + evenNorm + outerDifference);
        destination[output + 6 * stride] = unchecked(evenNorm + outerDifference - axisB);
        destination[output + 7 * stride] = unchecked(pairSum + evenNorm + outerSum - centreB);
    }

    private uint ReadBits(int position, int count)
    {
        int word = position >> 5;
        int shift = position & 31;
        if ((uint)word >= _bits.Length) throw new InvalidDataException("Texture bitstream ended early.");
        ulong first = ((ulong)_bits[word] << shift) & uint.MaxValue;
        ulong second = shift != 0 && word + 1 < _bits.Length ? _bits[word + 1] >> (32 - shift) : 0;
        return (uint)((first | second) >> (32 - count));
    }

    private static (int Skip, int Bits) Prefix(int value)
    {
        if (value < 64) return (2, 0);
        if (value < 128) return (2, 1);
        if (value < 192) return (2, 2);
        if (value < 224) return (3, 3);
        if (value < 240) return (4, 4);
        if (value < 248) return (5, 5);
        if (value < 252) return (6, 6);
        if (value < 254) return (7, 7);
        if (value == 254) return (8, 8);
        return (0, 0);
    }

    private static int ConvertColor(double value)
    {
        double biased = value + 6.75539944108852e15;
        long rounded = (long)(biased + (biased > 0 ? 0.5 : -0.5));
        return unchecked((int)(uint)rounded) >> 16;
    }
    private static int Round(double value) => (int)(value + (value > 0 ? 0.5 : -0.5));
    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);
}
