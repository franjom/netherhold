// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Data.Media;

public enum EaMovieChunkKind
{
    AudioHeader,
    Audio,
    Video,

    /// <summary><c>SCCl</c> and <c>SCLl</c>: skipped unread by <c>0x005A7EA0</c>.</summary>
    Skipped,
    End,
}

/// <summary>One chunk of an EA movie: its kind and where its payload lies in the file.</summary>
public readonly record struct EaMovieChunk(EaMovieChunkKind Kind, int Offset, int Length);

/// <summary>
/// An EA movie file (<c>Data\Movies\*.tgq</c>) as DKII.EXE's linked-in EA player reads it
/// (<c>docs/research/movie-format.md</c> §2). A chunk is a 4-byte tag and a little-endian size
/// that counts its 8-byte header; <c>0x005A7EA0</c> accepts both byte orders only for the
/// <c>SC?l</c> family and <c>SNDC</c>, and one order for the rest. Only the first audio header
/// is read and every audio chunk is listed: DK2's selection of the n-th chunk of each audio run
/// for multi-stream files is not built (every shipped movie has one stream). The file stays in
/// memory and nothing derived from it is written anywhere.
/// </summary>
public sealed class EaMovie
{
    private readonly byte[] _data;

    private EaMovie(byte[] data, IReadOnlyList<EaMovieChunk> chunks, int channels, int sampleRate, int compression, int sampleBits)
    {
        _data = data;
        Chunks = chunks;
        Channels = channels;
        SampleRate = sampleRate;
        Compression = compression;
        SampleBits = sampleBits;
    }

    public IReadOnlyList<EaMovieChunk> Chunks { get; }
    public bool HasAudio => Chunks.Any(c => c.Kind == EaMovieChunkKind.AudioHeader);

    /// <summary>The header's 0x82 element; 1 when absent.</summary>
    public int Channels { get; }

    /// <summary>The header's 0x84 element; 22050 when absent.</summary>
    public int SampleRate { get; }

    /// <summary>The header's 0x83 element: 0 raw PCM, 7 EA ADPCM, 10 unread (<c>0x005AC703</c>).</summary>
    public int Compression { get; }

    /// <summary>The header's 0x81 element; 16 when absent.</summary>
    public int SampleBits { get; }

    public int VideoFrameCount => Chunks.Count(c => c.Kind == EaMovieChunkKind.Video);

    public ReadOnlySpan<byte> Payload(EaMovieChunk chunk) => _data.AsSpan(chunk.Offset, chunk.Length);

    public static EaMovie Open(string path) => Read(File.ReadAllBytes(path), path);

    public static EaMovie Read(byte[] data, string sourceName = "<memory>")
    {
        var chunks = new List<EaMovieChunk>();
        int channels = 1, rate = 22050, compression = 0, bits = 16;
        bool headerSeen = false;
        int offset = 0;
        while (offset + 8 <= data.Length)
        {
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4));
            if (size < 8 || size > data.Length - offset)
                throw new InvalidDataException($"{sourceName}: chunk at 0x{offset:X} has size {size}");
            var chunk = new EaMovieChunk(Classify(tag, sourceName, offset), offset + 8, size - 8);
            if (chunk.Kind == EaMovieChunkKind.AudioHeader && !headerSeen)
            {
                if (Canonical(tag) == Tag.SEAD)
                    (channels, rate, compression) = (2, 22050, 10); // fixed values, 0x005A7EA0
                else
                    ReadAudioHeader(data.AsSpan(chunk.Offset, chunk.Length), ref channels, ref rate, ref compression, ref bits);
                headerSeen = true;
            }
            chunks.Add(chunk);
            offset += size;
            if (chunk.Kind == EaMovieChunkKind.End) break;
        }
        return new EaMovie(data, chunks, channels, rate, compression, bits);
    }

    private static EaMovieChunkKind Classify(uint tag, string sourceName, int offset)
    {
        switch (Canonical(tag))
        {
            case Tag.SCHl: case Tag.SNh1: case Tag.SEAD: return EaMovieChunkKind.AudioHeader;
            case Tag.SCDl: case Tag.SNd1: case Tag.SNDC: return EaMovieChunkKind.Audio;
            case Tag.pIQT: return EaMovieChunkKind.Video;
            case Tag.SCEl: case Tag.SNe1: case Tag.SEND: return EaMovieChunkKind.End;
            case Tag.SCCl: case Tag.SCLl: return EaMovieChunkKind.Skipped;
        }
        // TGV, TGQ and MAD frames (kinds 1-3, 5-7 of 0x005A7EA0) are not decoded here.
        throw new InvalidDataException($"{sourceName}: unsupported chunk tag 0x{tag:X8} at 0x{offset:X}");
    }

    private static uint Canonical(uint tag)
    {
        uint swapped = BinaryPrimitives.ReverseEndianness(tag);
        foreach (uint known in Tag.BothOrders)
            if (swapped == known) return known;
        return tag;
    }

    /// <summary><c>0x005A87F0</c>: skip "PT\0\0", then tag/len/big-endian elements; those before
    /// 0xFD are ignored, after it 0x81-0x84 are read, and 0x8A ends the parse.</summary>
    private static void ReadAudioHeader(ReadOnlySpan<byte> payload, ref int channels, ref int rate, ref int compression, ref int bits)
    {
        int p = 4;
        bool audio = false;
        while (p < payload.Length)
        {
            byte element = payload[p++];
            if (element == 0xFD) { audio = true; continue; }
            if (audio && element == 0x8A) return;
            if (p >= payload.Length) return;
            int length = payload[p++];
            if (p + length > payload.Length) return;
            int value = 0;
            for (int i = 0; i < length; i++) value = (value << 8) | payload[p + i];
            if (length is > 0 and < 4) value = (value << (32 - 8 * length)) >> (32 - 8 * length);
            p += length;
            if (!audio) continue;
            switch (element)
            {
                case 0x81: bits = value; break;
                case 0x82: channels = value; break;
                case 0x83: compression = value; break;
                case 0x84: rate = value; break;
            }
        }
    }

    private static class Tag
    {
        public const uint SCHl = 0x6C484353, SCDl = 0x6C444353, SCEl = 0x6C454353, SCCl = 0x6C434353, SCLl = 0x6C4C4353;
        public const uint SNh1 = 0x684E5331, SNd1 = 0x644E5331, SNe1 = 0x654E5331; // "1SNh" etc. as read
        public const uint SEAD = 0x44414553, SNDC = 0x43444E53, SEND = 0x444E4553;
        public const uint pIQT = 0x54514970;
        public static readonly uint[] BothOrders = [SCHl, SCDl, SCCl, SCLl, SNDC];
    }
}
