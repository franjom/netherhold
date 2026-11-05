// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Media;

/// <summary>Interleaved signed 16-bit PCM decoded in memory from the local install.</summary>
public sealed record PcmClip(int SampleRate, int Channels, short[] Samples);

/// <summary>Runtime MPEG audio decoding. No derived data is written to disk (ADR-0583).</summary>
public static class MpegAudioDecoder
{
    public static PcmClip Decode(ReadOnlySpan<byte> mpeg)
    {
        if (mpeg.Length < 4) throw new InvalidDataException("MPEG audio is truncated.");
        byte[] copy = mpeg.ToArray();
        return Decode(copy, 0, copy.Length);
    }

    /// <summary>Decodes the stream in place into one array sized from the stream's frames: no copy of the input
    /// and no growing list, so a clip allocates its samples once (and one 4,096-float block).</summary>
    public static PcmClip Decode(byte[] buffer, int offset, int count)
    {
        if (count < 4) throw new InvalidDataException("MPEG audio is truncated.");
        using var stream = new MemoryStream(buffer, offset, count, false);
        try
        {
            using var decoder = new NLayer.MpegFile(stream);
            long expected = decoder.Length / sizeof(float);
            // No Layer I/II stream gives 64 samples per input byte (the shipped banks give about 6); a larger count
            // is a corrupt header, and the array grows from a small start instead.
            var samples = new short[expected > 0 && expected <= (long)count * 64 ? (int)expected : 4096];
            int written = 0;
            var block = new float[4096];
            int read;
            while ((read = decoder.ReadSamples(block, 0, block.Length)) > 0)
            {
                if (written + read > samples.Length) Array.Resize(ref samples, Math.Max(samples.Length * 2, written + read));
                for (int i = 0; i < read; i++)
                    samples[written++] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue,
                        (int)Math.Round(block[i] * 32768.0)));
            }
            if (written == 0) throw new InvalidDataException("MPEG audio has no decodable samples.");
            if (written != samples.Length) Array.Resize(ref samples, written);
            return new PcmClip(decoder.SampleRate, decoder.Channels, samples);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidDataException("Invalid MPEG audio.", error);
        }
    }
}
