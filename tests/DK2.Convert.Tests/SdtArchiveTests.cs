// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Convert.Audio;
using DK2.Data;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class SdtArchiveTests
{
    [Fact]
    public void SyntheticBankEnumeratesEveryVariantAndWrapsOnlyKnownPcmAsWave()
    {
        byte[] pcm = [0, 128, 0, 0, 255, 127];
        byte[] mp2 = [0xff, 0xfd, 0xb0, 0x00, 1, 2, 3, 4];
        byte[] mp1 = [0xff, 0xf7, 0x40, 0xc0, 5, 6, 7, 8];
        byte[] unknown = [9, 8, 7];
        SdtArchive archive = SdtArchive.Read(Bank(
            new Sample("variant.wav", 22050, 16, 2, 0, pcm),
            new Sample("variant.mp2", 44100, 16, 37, 1152, mp2),
            new Sample("variant.mp1", 22050, 16, 36, 1152, mp1),
            new Sample("Blank", 22050, 16, 0, 0, []),
            new Sample("mystery", 32000, 8, 3, 0, unknown)));

        Assert.Equal(5, archive.Entries.Count);
        SdtEntry wave = archive.Entries[0];
        Assert.Equal(SdtCodec.Pcm, wave.Codec);
        Assert.Equal(1, wave.ChannelCount);
        Assert.Equal(22050, wave.SampleRate);
        byte[] extractedWave = archive.Extract(wave);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(extractedWave, 0, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(extractedWave, 8, 8));
        Assert.Equal(1, U16(extractedWave, 20));
        Assert.Equal(1, U16(extractedWave, 22));
        Assert.Equal(22050u, U32(extractedWave, 24));
        Assert.Equal(16, U16(extractedWave, 34));
        Assert.Equal(pcm, extractedWave.AsSpan(44).ToArray());

        SdtEntry compressed = archive.Entries[1];
        Assert.Equal(SdtCodec.MpegLayer2, compressed.Codec);
        Assert.Equal(2, compressed.ChannelCount);
        Assert.Equal(44100, compressed.SampleRate);
        Assert.Equal((ushort?)44100, compressed.PayloadSampleRate);
        Assert.Equal(1152u, compressed.SampleCountField);
        Assert.Equal(mp2, archive.Extract(compressed));
        Assert.Equal("0001-variant.mp2", archive.OutputFileName(compressed));
        Assert.NotEqual(archive.OutputFileName(wave), archive.OutputFileName(compressed));

        SdtEntry layer1 = archive.Entries[2];
        Assert.Equal(SdtCodec.MpegLayer1, layer1.Codec);
        Assert.Equal(mp1, archive.Extract(layer1));
        Assert.Equal("0002-variant.mp1", archive.OutputFileName(layer1));

        Assert.Equal(SdtCodec.Empty, archive.Entries[3].Codec);
        Assert.Empty(archive.Extract(archive.Entries[3]));
        SdtEntry undecoded = archive.Entries[4];
        Assert.Equal(SdtCodec.Unknown, undecoded.Codec);
        Assert.Null(undecoded.ChannelCount);
        Assert.Equal(unknown, archive.Extract(undecoded));
        Assert.EndsWith(".bin", archive.OutputFileName(undecoded), StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedOrOverlappingEntriesAreRejected()
    {
        byte[] valid = Bank(new Sample("one", 22050, 16, 2, 0, [0, 0]));
        BinaryPrimitives.WriteUInt32LittleEndian(valid.AsSpan(12), 1000);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => SdtArchive.Read(valid));

        Assert.Contains("beyond", error.Message, StringComparison.Ordinal);
    }

    [InstallFact]
    public void EveryShippedBankHasTheConfirmedLayoutAndKnownPayloadTypes()
    {
        string install = Dk2InstallGate.Path;
        string soundRoot = Path.Combine(install, "Data", "Sound", "Sfx");
        string[] files = Directory.GetFiles(soundRoot, "*.sdt", SearchOption.AllDirectories);
        Assert.Equal(142, files.Length);
        int entries = 0;
        var rates = new HashSet<ushort>();
        var mixedRateBanks = new List<string>();
        int payloadRateDisagreements = 0;
        var codecs = new Dictionary<SdtCodec, int>();
        foreach (string file in files)
        {
            SdtArchive archive = SdtArchive.Open(file);
            entries += archive.Entries.Count;
            Assert.All(archive.Entries, entry =>
            {
                Assert.Equal(40u, entry.HeaderSize);
                Assert.Equal(0u, entry.Reserved1);
                Assert.Equal(0u, entry.Reserved2);
                Assert.Equal(16, entry.BitsPerSample);
                Assert.Contains(entry.Codec, new[]
                    { SdtCodec.Empty, SdtCodec.Pcm, SdtCodec.MpegLayer1,
                        SdtCodec.MpegLayer2 });
                codecs[entry.Codec] = codecs.GetValueOrDefault(entry.Codec) + 1;
                rates.Add(entry.SampleRate);
                if (entry.PayloadSampleRate is ushort payloadRate
                    && payloadRate != entry.SampleRate)
                    payloadRateDisagreements++;
            });
            if (archive.Entries.Select(entry => entry.SampleRate).Distinct().Count() > 1)
                mixedRateBanks.Add(file);
        }
        Assert.Equal(8959, entries);
        Assert.Equal(999, codecs[SdtCodec.Empty]);
        Assert.Equal(211, codecs[SdtCodec.Pcm]);
        Assert.Equal(4004, codecs[SdtCodec.MpegLayer1]);
        Assert.Equal(3745, codecs[SdtCodec.MpegLayer2]);
        Assert.Equal(new ushort[] { 22050, 44100 }, rates.OrderBy(rate => rate).ToArray());
        Assert.Equal(17, mixedRateBanks.Count);
        Assert.Equal(6, payloadRateDisagreements);
    }

    [InstallFact]
    public void EveryCompressedPayloadEndsWithExactlyOneTrailingZeroByte()
    {
        // Walking a payload's MPEG frames lands exactly one byte short of
        // StoredSize and that byte is always zero: 7,749 of 7,749 shipped
        // payloads. It is in the data, not in the extractor, which preserves the
        // stream. Whether it is a terminator, an alignment pad or an off-by-one in
        // EA's own packer is UNKNOWN; this test exists so that the day it stops
        // being universally true, somebody finds out.
        string install = Dk2InstallGate.Path;

        string soundRoot = Path.Combine(install, "Data", "Sound", "Sfx");
        int compressed = 0;
        foreach (string file in Directory.GetFiles(soundRoot, "*.sdt", SearchOption.AllDirectories))
        {
            SdtArchive archive = SdtArchive.Open(file);
            foreach (SdtEntry entry in archive.Entries)
            {
                if (entry.Codec is not (SdtCodec.MpegLayer1 or SdtCodec.MpegLayer2)) continue;

                byte[] payload = archive.Extract(entry);
                int walked = MpegFrameBytes(payload);
                Assert.Equal(payload.Length - 1, walked);
                Assert.Equal(0, payload[^1]);
                compressed++;
            }
        }

        Assert.Equal(7749, compressed);
    }

    /// <summary>
    /// How many bytes of this payload are complete MPEG audio frames, walking
    /// header to header. Deliberately a second, independent implementation of
    /// the frame arithmetic: SdtArchive reads only the FIRST header, so a test
    /// that reused its code could not catch a payload that starts well and ends
    /// badly.
    /// </summary>
    private static int MpegFrameBytes(ReadOnlySpan<byte> payload)
    {
        int[] rates = [44100, 48000, 32000];
        int[] layer1V1 = [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448];
        int[] layer2V1 = [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384];
        int[] layer1V2 = [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256];
        int[] layer2V2 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];

        int at = 0;
        while (at + 4 <= payload.Length)
        {
            uint header = BinaryPrimitives.ReadUInt32BigEndian(payload[at..]);
            if ((header & 0xffe00000) != 0xffe00000) break;

            int version = (int)((header >> 19) & 3);
            int layer = (int)((header >> 17) & 3);
            int bitrateIndex = (int)((header >> 12) & 0xf);
            int rateIndex = (int)((header >> 10) & 3);
            int padding = (int)((header >> 9) & 1);
            if (version == 1 || layer == 0 || rateIndex == 3
                || bitrateIndex is 0 or 15) break;

            int baseRate = rates[rateIndex];
            int sampleRate = version switch { 3 => baseRate, 2 => baseRate / 2, _ => baseRate / 4 };
            int[] table = version == 3
                ? layer == 3 ? layer1V1 : layer2V1
                : layer == 3 ? layer1V2 : layer2V2;
            int kbps = table[bitrateIndex];

            int frame = layer == 3
                ? ((12 * kbps * 1000 / sampleRate) + padding) * 4
                : (144 * kbps * 1000 / sampleRate) + padding;
            if (frame <= 0 || at + frame > payload.Length) break;
            at += frame;
        }

        return at;
    }

    private static byte[] Bank(params Sample[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((uint)samples.Length);
        int offset = checked(4 + samples.Length * 4);
        foreach (Sample sample in samples)
        {
            writer.Write((uint)offset);
            offset = checked(offset + 40 + sample.Data.Length);
        }
        foreach (Sample sample in samples)
        {
            writer.Write(40u);
            writer.Write((uint)sample.Data.Length);
            byte[] name = new byte[16];
            Encoding.ASCII.GetBytes(sample.Name.AsSpan(0, Math.Min(16, sample.Name.Length)), name);
            writer.Write(name);
            writer.Write(sample.SampleRate);
            writer.Write(sample.BitsPerSample);
            writer.Write(sample.Type);
            writer.Write(0u);
            writer.Write(sample.SampleCountField);
            writer.Write(0u);
            writer.Write(sample.Data);
        }
        return stream.ToArray();
    }

    private static ushort U16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));

    private static uint U32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    private sealed record Sample(string Name, ushort SampleRate, byte BitsPerSample,
        byte Type, uint SampleCountField, byte[] Data);
}
