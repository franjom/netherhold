// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Data.Media;

public readonly record struct SoundEntryId(int Value);

/// <summary>One SDT archive with clips decoded on first use and retained for the session.</summary>
public sealed class SoundBank
{
    private readonly SdtArchive _archive;
    private readonly Dictionary<SoundEntryId, PcmClip?> _clips = new();

    public SoundBank(SdtArchive archive) => _archive = archive ?? throw new ArgumentNullException(nameof(archive));

    public PcmClip? GetClip(SoundEntryId entryId)
    {
        if ((uint)entryId.Value >= _archive.Entries.Count) throw new ArgumentOutOfRangeException(nameof(entryId));
        if (_clips.TryGetValue(entryId, out PcmClip? cached)) return cached;
        SdtEntry entry = _archive.Entries[entryId.Value];
        PcmClip? clip;
        if (entry.Codec == SdtCodec.Empty) clip = null;
        else if (entry.Codec == SdtCodec.Pcm)
        {
            byte[] wave = _archive.Extract(entry);
            int bytesPerSample = entry.BitsPerSample / 8;
            if (bytesPerSample != 1 && bytesPerSample != 2) throw new InvalidDataException("Unsupported PCM sample width.");
            var samples = new short[entry.StoredSize / bytesPerSample];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = bytesPerSample == 1 ? (short)((wave[44 + i] - 128) << 8)
                    : BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44 + i * 2));
            clip = new PcmClip(entry.SampleRate, entry.ChannelCount!.Value, samples);
        }
        else if (entry.Codec is SdtCodec.MpegLayer1 or SdtCodec.MpegLayer2 or SdtCodec.MpegLayer3)
            clip = MpegAudioDecoder.Decode(_archive.Extract(entry));
        else throw new InvalidDataException("Unknown SDT audio codec.");
        _clips.Add(entryId, clip);
        return clip;
    }
}
