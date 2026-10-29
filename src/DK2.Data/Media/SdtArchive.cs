// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;

namespace DK2.Data.Media;

public enum SdtCodec
{
    Empty,
    Pcm,
    MpegLayer1,
    MpegLayer2,
    MpegLayer3,
    Unknown,
}

public sealed record SdtEntry(
    int Index,
    uint Offset,
    uint HeaderSize,
    uint StoredSize,
    string Name,
    ushort SampleRate,
    byte BitsPerSample,
    byte Type,
    uint Reserved1,
    uint SampleCountField,
    uint Reserved2,
    SdtCodec Codec,
    int? ChannelCount,
    ushort? PayloadSampleRate,
    uint DataOffset);

/// <summary>Reads a Dungeon Keeper 2 SDT sound bank and extracts its audio payloads.</summary>
public sealed class SdtArchive
{
    private const int FixedHeaderSize = 40;
    private readonly byte[] _data;

    private SdtArchive(byte[] data, IReadOnlyList<SdtEntry> entries)
    {
        _data = data;
        Entries = entries;
    }

    public IReadOnlyList<SdtEntry> Entries { get; }

    public static SdtArchive Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        return Read(File.ReadAllBytes(path));
    }

    public static SdtArchive Read(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Read(copy.ToArray());
    }

    public static SdtArchive Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) throw Invalid(0, "Missing entry count.");
        uint count = U32(data, 0);
        if (count > int.MaxValue || count > (uint)((data.Length - 4) / 4))
            throw Invalid(0, $"Entry count {count} does not fit the offset table.");
        int directoryEnd = checked(4 + (int)count * 4);
        var entries = new List<SdtEntry>((int)count);
        uint previousOffset = (uint)directoryEnd;

        for (int index = 0; index < (int)count; index++)
        {
            uint offset = U32(data, 4 + index * 4);
            if (offset < directoryEnd || (index > 0 && offset <= previousOffset)
                || offset > data.Length - FixedHeaderSize)
                throw Invalid(4 + index * 4, $"Entry {index} offset {offset} is outside the archive or out of order.");
            int start = checked((int)offset);
            uint headerSize = U32(data, start);
            uint storedSize = U32(data, start + 4);
            if (headerSize < FixedHeaderSize)
                throw Invalid(start, $"Entry {index} header is {headerSize} bytes; at least 40 are required.");
            ulong dataOffset = (ulong)offset + headerSize;
            ulong end = dataOffset + storedSize;
            uint nextOffset = index + 1 < (int)count ? U32(data, 8 + index * 4) : (uint)data.Length;
            if (dataOffset > (uint)data.Length || end > nextOffset)
                throw Invalid(start, $"Entry {index} data extends beyond its archive range.");

            string name = FixedAscii(data.Slice(start + 8, 16));
            ushort sampleRate = U16(data, start + 24);
            byte bitsPerSample = data[start + 26];
            byte type = data[start + 27];
            (SdtCodec codec, int? channels, ushort? payloadRate) = type switch
            {
                0 when storedSize == 0 => (SdtCodec.Empty, (int?)null, (ushort?)null),
                2 => (SdtCodec.Pcm, (int?)1, (ushort?)sampleRate),
                36 or 37 => MpegMetadata(data, checked((int)dataOffset), storedSize,
                    type),
                _ => (SdtCodec.Unknown, (int?)null, (ushort?)null),
            };
            if (codec == SdtCodec.Pcm)
                ValidatePcm(index, sampleRate, bitsPerSample, channels!.Value, storedSize, start);

            entries.Add(new SdtEntry(index, offset, headerSize, storedSize, name,
                sampleRate, bitsPerSample, type, U32(data, start + 28),
                U32(data, start + 32), U32(data, start + 36), codec, channels,
                payloadRate, checked((uint)dataOffset)));
            previousOffset = offset;
        }

        return new SdtArchive(data.ToArray(), entries);
    }

    public byte[] Extract(SdtEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        SdtEntry owned = Entry(entry);
        ReadOnlySpan<byte> payload = _data.AsSpan(checked((int)owned.DataOffset), checked((int)owned.StoredSize));
        return owned.Codec == SdtCodec.Pcm ? Wave(payload, owned) : payload.ToArray();
    }

    public string OutputFileName(SdtEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        SdtEntry owned = Entry(entry);
        string stem = owned.Name;
        int dot = stem.LastIndexOf('.');
        if (dot > 0) stem = stem[..dot];
        char[] invalid = Path.GetInvalidFileNameChars();
        stem = new string(stem.Select(character => invalid.Contains(character)
            || character is '/' or '\\' ? '_' : character).ToArray()).Trim(' ', '.');
        if (stem.Length == 0) stem = "entry";
        string extension = owned.Codec switch
        {
            SdtCodec.Pcm => ".wav",
            SdtCodec.MpegLayer1 => ".mp1",
            SdtCodec.MpegLayer2 => ".mp2",
            SdtCodec.MpegLayer3 => ".mp3",
            _ => ".bin",
        };
        return $"{owned.Index:D4}-{stem}{extension}";
    }

    private SdtEntry Entry(SdtEntry entry)
    {
        if ((uint)entry.Index >= Entries.Count || !ReferenceEquals(Entries[entry.Index], entry))
            throw new ArgumentException("Entry does not belong to this SDT archive.", nameof(entry));
        return entry;
    }

    private static byte[] Wave(ReadOnlySpan<byte> pcm, SdtEntry entry)
    {
        int channels = entry.ChannelCount!.Value;
        int blockAlign = checked(channels * entry.BitsPerSample / 8);
        int byteRate = checked(entry.SampleRate * blockAlign);
        int fileLength = checked(44 + pcm.Length + (pcm.Length & 1));
        var result = new byte[fileLength];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)(fileLength - 8)));
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(result, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(22), checked((ushort)channels));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24), entry.SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28), checked((uint)byteRate));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(32), checked((ushort)blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(34), entry.BitsPerSample);
        Encoding.ASCII.GetBytes("data").CopyTo(result, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(40), checked((uint)pcm.Length));
        pcm.CopyTo(result.AsSpan(44));
        return result;
    }

    private static void ValidatePcm(int index, ushort sampleRate, byte bitsPerSample,
        int channels, uint storedSize, int position)
    {
        if (sampleRate == 0 || bitsPerSample == 0 || bitsPerSample % 8 != 0)
            throw Invalid(position, $"PCM entry {index} has invalid {sampleRate} Hz/{bitsPerSample}-bit metadata.");
        int blockAlign = checked(channels * bitsPerSample / 8);
        if (storedSize % blockAlign != 0)
            throw Invalid(position, $"PCM entry {index} size {storedSize} is not aligned to {blockAlign} bytes.");
    }

    private static (SdtCodec Codec, int? Channels, ushort? PayloadRate) MpegMetadata(
        ReadOnlySpan<byte> data, int dataOffset, uint storedSize, byte type)
    {
        if (storedSize < 4) return (SdtCodec.Unknown, null, null);
        uint header = BinaryPrimitives.ReadUInt32BigEndian(data[dataOffset..]);
        if ((header & 0xffe00000) != 0xffe00000) return (SdtCodec.Unknown, null, null);
        int version = (int)((header >> 19) & 3);
        int layer = (int)((header >> 17) & 3);
        int rateIndex = (int)((header >> 10) & 3);
        if (version == 1 || layer == 0 || rateIndex == 3) return (SdtCodec.Unknown, null, null);
        int baseRate = rateIndex switch { 0 => 44100, 1 => 48000, _ => 32000 };
        int streamRate = version switch { 3 => baseRate, 2 => baseRate / 2, _ => baseRate / 4 };
        int channels = ((header >> 6) & 3) == 3 ? 1 : 2;
        int expectedChannels = type == 36 ? 1 : 2;
        if (channels != expectedChannels) return (SdtCodec.Unknown, null, checked((ushort)streamRate));
        SdtCodec codec = layer switch
        {
            3 => SdtCodec.MpegLayer1,
            2 => SdtCodec.MpegLayer2,
            1 => SdtCodec.MpegLayer3,
            _ => SdtCodec.Unknown,
        };
        return (codec, channels, checked((ushort)streamRate));
    }

    private static string FixedAscii(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);
        if (length < 0) length = bytes.Length;
        return Encoding.ASCII.GetString(bytes[..length].ToArray()).TrimEnd();
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    private static uint U32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private static InvalidDataException Invalid(int offset, string message) =>
        new($"Invalid SDT at byte 0x{offset:x}: {message}");
}
