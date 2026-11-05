// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Data.Media;

public readonly record struct SoundEntryId(int Value);

/// <summary>One SDT archive with clips decoded on first use and retained for the session. Safe to call from
/// several threads: a clip is decoded outside the lock, and the first decode of an entry is the one kept.</summary>
public sealed class SoundBank
{
    private readonly SdtArchive _archive;
    private readonly Dictionary<SoundEntryId, PcmClip?> _clips = new();

    public SoundBank(SdtArchive archive) => _archive = archive ?? throw new ArgumentNullException(nameof(archive));

    public PcmClip? GetClip(SoundEntryId entryId)
    {
        if ((uint)entryId.Value >= _archive.Entries.Count) throw new ArgumentOutOfRangeException(nameof(entryId));
        lock (_clips)
            if (_clips.TryGetValue(entryId, out PcmClip? cached)) return cached;
        PcmClip? clip = Decode(_archive.Entries[entryId.Value]);
        lock (_clips)
        {
            if (_clips.TryGetValue(entryId, out PcmClip? first)) return first;
            _clips.Add(entryId, clip);
        }
        return clip;
    }

    private PcmClip? Decode(SdtEntry entry)
    {
        if (entry.Codec == SdtCodec.Empty) return null;
        if (entry.Codec == SdtCodec.Pcm)
        {
            ArraySegment<byte> wave = _archive.Payload(entry);
            int bytesPerSample = entry.BitsPerSample / 8;
            if (bytesPerSample != 1 && bytesPerSample != 2) throw new InvalidDataException("Unsupported PCM sample width.");
            var samples = new short[entry.StoredSize / bytesPerSample];
            ReadOnlySpan<byte> data = wave.AsSpan();
            for (int i = 0; i < samples.Length; i++)
                samples[i] = bytesPerSample == 1 ? (short)((data[i] - 128) << 8)
                    : BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i * 2));
            return new PcmClip(entry.SampleRate, entry.ChannelCount!.Value, samples);
        }
        if (entry.Codec is SdtCodec.MpegLayer1 or SdtCodec.MpegLayer2 or SdtCodec.MpegLayer3)
        {
            ArraySegment<byte> payload = _archive.Payload(entry);
            return MpegAudioDecoder.Decode(payload.Array!, payload.Offset, payload.Count);
        }
        throw new InvalidDataException("Unknown SDT audio codec.");
    }
}
