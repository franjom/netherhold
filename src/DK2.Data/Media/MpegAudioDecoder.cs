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
        using var stream = new MemoryStream(mpeg.ToArray(), false);
        try
        {
            using var decoder = new NLayer.MpegFile(stream);
            var samples = new List<short>();
            var block = new float[4096];
            int read;
            while ((read = decoder.ReadSamples(block, 0, block.Length)) > 0)
                for (int i = 0; i < read; i++)
                    samples.Add((short)Math.Max(short.MinValue, Math.Min(short.MaxValue,
                        (int)Math.Round(block[i] * 32768.0))));
            if (samples.Count == 0) throw new InvalidDataException("MPEG audio has no decodable samples.");
            return new PcmClip(decoder.SampleRate, decoder.Channels, samples.ToArray());
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidDataException("Invalid MPEG audio.", error);
        }
    }
}
