// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Data.Media;

/// <summary>
/// Decodes one EA movie audio block (<c>SCDl</c>) to interleaved 16-bit stereo, as
/// <c>0x005A8430</c> does (<c>docs/research/movie-format.md</c> §3). Compression 7 is EA ADPCM
/// (<c>0x005ABC60</c> stereo, <c>0x005ABAE0</c> mono); compression 0 is raw 16-bit PCM. Mono is
/// duplicated into both channels, as DK2's output is always stereo.
/// </summary>
public static class EaAudio
{
    // 0x00670AA0 (c1) and 0x00670AB0 (c2).
    private static readonly int[] C1 = [0, 240, 460, 392];
    private static readonly int[] C2 = [0, 0, -208, -220];

    /// <summary>The number of stereo sample frames <see cref="DecodeBlock"/> writes for this payload.</summary>
    public static int FrameCount(ReadOnlySpan<byte> payload, int channels, int compression) => compression switch
    {
        7 => channels == 2 ? (payload.Length - 12) / 30 * 28 : (payload.Length - 12) / 15 * 28,
        0 => (payload.Length - 4) / (2 * channels),
        _ => throw new NotSupportedException($"EA audio compression {compression} is not decoded (0x005AC703 is unread)"),
    };

    /// <summary>Writes the block's samples to <paramref name="stereo"/> (L, R, L, R ...) and
    /// returns the number of stereo frames written.</summary>
    public static int DecodeBlock(ReadOnlySpan<byte> payload, int channels, int compression, Span<short> stereo)
    {
        int frames = FrameCount(payload, channels, compression);
        if (stereo.Length < frames * 2) throw new ArgumentException("output is too small", nameof(stereo));
        if (compression == 0)
        {
            // The leading u32 sample count is skipped; samples are little-endian int16.
            ReadOnlySpan<byte> pcm = payload[4..];
            for (int i = 0; i < frames; i++)
            {
                short l = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * 2 * channels)..]);
                short r = channels == 2 ? BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * 4 + 2)..]) : l;
                stereo[2 * i] = l;
                stereo[2 * i + 1] = r;
            }
            return frames;
        }

        // The u32 sample count at +0 is ignored; the predictor state is reloaded from each block.
        int curL = BinaryPrimitives.ReadInt16LittleEndian(payload[4..]);
        int prevL = BinaryPrimitives.ReadInt16LittleEndian(payload[6..]);
        if (channels != 2)
        {
            // 0x005A8430 skips the same 12-byte header as stereo (payload+8/+10 are loaded and
            // never read by 0x005ABAE0). EA's own mono layout may start at +8; DK2's code is
            // followed, and no shipped movie is mono.
            int o = 0;
            for (int p = 12; p + 15 <= payload.Length; p += 15)
            {
                int index = payload[p] >> 4, shift = payload[p] & 0xF;
                for (int i = 0; i < 14; i++)
                {
                    byte b = payload[p + 1 + i];
                    Step(b >> 4, index, shift, ref curL, ref prevL);
                    stereo[o++] = (short)curL; stereo[o++] = (short)curL;
                    Step(b & 0xF, index, shift, ref curL, ref prevL);
                    stereo[o++] = (short)curL; stereo[o++] = (short)curL;
                }
            }
            return frames;
        }

        int curR = BinaryPrimitives.ReadInt16LittleEndian(payload[8..]);
        int prevR = BinaryPrimitives.ReadInt16LittleEndian(payload[10..]);
        int w = 0;
        for (int p = 12; p + 30 <= payload.Length; p += 30)
        {
            int indexL = payload[p] >> 4, indexR = payload[p] & 0xF;
            int shiftL = payload[p + 1] >> 4, shiftR = payload[p + 1] & 0xF;
            for (int i = 0; i < 28; i++)
            {
                byte b = payload[p + 2 + i];
                Step(b >> 4, indexL, shiftL, ref curL, ref prevL);
                Step(b & 0xF, indexR, shiftR, ref curR, ref prevR);
                stereo[w++] = (short)curL;
                stereo[w++] = (short)curR;
            }
        }
        return frames;
    }

    private static void Step(int nibble, int index, int shift, ref int current, ref int previous)
    {
        // DK2 reads indices above 3 past 0x00670AA0's table into unrelated data; no shipped
        // stream uses them, so they are refused rather than guessed.
        if (index > 3) throw new InvalidDataException($"EA ADPCM coefficient index {index} is not valid");
        int c1 = C1[index];
        int c2 = C2[index];
        int delta = (nibble << 28) >> (shift + 8);
        int next = (delta + current * c1 + previous * c2 + 0x80) >> 8;
        previous = current;
        current = Math.Clamp(next, short.MinValue, short.MaxValue);
    }
}
