// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Media;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class EaMovieTests
{
    [Fact]
    public void AStereoAdpcmGroupTakesHighNibblesLeftAndScalesByTwentyMinusShift()
    {
        // Coefficient index 0 and shift 0 on both channels: new = (nibble << 20 + 0x80) >> 8.
        var payload = new byte[12 + 30];
        payload[14] = 0x1F; // first sample: L nibble 1, R nibble -1
        var stereo = new short[2 * 28];

        int frames = EaAudio.DecodeBlock(payload, channels: 2, compression: 7, stereo);

        Assert.Equal(28, frames);
        Assert.Equal(new short[] { 4096, -4096, 0, 0 }, stereo[..4]);
    }

    [Fact]
    public void AdpcmPredictsFromTheBlockHeadersCurrentAndPreviousSamples()
    {
        // Index 1 (c1 240, c2 0): new = (0 + 240 * cur + 0x80) >> 8 from the header's current sample.
        var payload = new byte[12 + 30];
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(4), 1000);
        payload[12] = 0x10;
        var stereo = new short[2 * 28];

        EaAudio.DecodeBlock(payload, 2, 7, stereo);

        Assert.Equal((240 * 1000 + 0x80) >> 8, stereo[0]);
    }

    [Fact]
    public void MonoAdpcmGroupsStartAfterTheTwelveByteHeaderAsDk2sCodeReadsThem_NoShippedMovieIsMono()
    {
        // 0x005A8430: (len - 12) / 15 groups from payload+12; index high nibble, shift low nibble.
        var payload = new byte[12 + 15];
        payload[13] = 0x10; // first data byte: high nibble 1 then low nibble 0
        var stereo = new short[2 * 28];

        int frames = EaAudio.DecodeBlock(payload, channels: 1, compression: 7, stereo);

        Assert.Equal(28, frames);
        Assert.Equal(new short[] { 4096, 4096, 0, 0 }, stereo[..4]);
    }

    [Fact]
    public void AtQuantiser107TheAcMatrixIsZeroAndOnlyDcSurvives()
    {
        // (107 - q) / 16 = 0 scales every AC entry to 0; M[0] stays 0x10000 (0x005AAB10).
        var bits = new BitWriter();
        bits.Put("1111110"); bits.Put("10000000"); bits.Put("000001"); bits.Put("000000"); bits.Put("00101000"); bits.Put("10");
        for (int i = 0; i < 3; i++) { bits.Put("100"); bits.Put("10"); }
        for (int i = 0; i < 2; i++) { bits.Put("11111110"); bits.Put("10000000"); bits.Put("10"); }
        var rgba = new byte[16 * 16 * 4];

        new TqiDecoder().Decode(Frame(16, 16, 107, bits), rgba);

        for (int i = 0; i < 256; i++)
            Assert.Equal(new byte[] { 115, 113, 115, 255 }, rgba.AsSpan(i * 4, 4).ToArray());
    }

    [Fact]
    public void AOneByteHeaderValueIsSignExtended()
    {
        // 0x005A8910 sign-extends 1-3 byte values: a rate byte of 0xFF reads as -1.
        byte[] header = [(byte)'P', (byte)'T', 0, 0, 0xFD, 0x84, 0x01, 0xFF, 0x8A];
        byte[] file = [.. Chunk("SCHl", header), .. Chunk("SCEl", [])];

        Assert.Equal(-1, EaMovie.Read(file).SampleRate);
    }

    [Fact]
    public void ADcOnlyFrameAtMidChromaIsTheBinarysGreyOfTwoVMinusThirteen()
    {
        // Each predictor reaches 128, so v = 128 * 0x10000 >> 17 = 64: R = B = 2v - 13, G = 2v - 15.
        var bits = new BitWriter();
        bits.Put("1111110"); bits.Put("10000000"); bits.Put("10"); // Y0: size 8, +128, end of block
        for (int i = 0; i < 3; i++) { bits.Put("100"); bits.Put("10"); } // Y1-Y3: size 0
        for (int i = 0; i < 2; i++) { bits.Put("11111110"); bits.Put("10000000"); bits.Put("10"); } // Cb, Cr
        var rgba = new byte[16 * 16 * 4];

        int used = new TqiDecoder().Decode(Frame(16, 16, 100, bits), rgba);

        Assert.Equal(bits.Count, used);
        for (int i = 0; i < 256; i++)
            Assert.Equal(new byte[] { 115, 113, 115, 255 }, rgba.AsSpan(i * 4, 4).ToArray());
    }

    [Fact]
    public void ThePlainZigzagsSecondCoefficientIsHorizontal()
    {
        // An escape (run 0, level 40, no sign bit) lands on natural index 1, so a row brightens to the left.
        var bits = new BitWriter();
        bits.Put("1111110"); bits.Put("10000000"); bits.Put("000001"); bits.Put("000000"); bits.Put("00101000"); bits.Put("10");
        for (int i = 0; i < 3; i++) { bits.Put("100"); bits.Put("10"); }
        for (int i = 0; i < 2; i++) { bits.Put("11111110"); bits.Put("10000000"); bits.Put("10"); }
        var rgba = new byte[16 * 16 * 4];

        new TqiDecoder().Decode(Frame(16, 16, 100, bits), rgba);

        for (int y = 0; y < 8; y++)
            Assert.True(rgba[(y * 16) * 4] > rgba[(y * 16 + 7) * 4], $"row {y} should fall left to right");
    }

    [Fact]
    public void TheAudioHeaderReadsOnlyElementsAfterFdAndStopsAt8A()
    {
        byte[] header = [(byte)'P', (byte)'T', 0, 0, 0x82, 0x01, 0x05, 0xFD, 0x82, 0x01, 0x02, 0x83, 0x01, 0x07, 0x84, 0x02, 0x56, 0x22, 0x8A, 0x82, 0x01, 0x09];
        byte[] file = [.. Chunk("SCHl", header), .. Chunk("SCEl", [])];

        EaMovie movie = EaMovie.Read(file);

        Assert.Equal(2, movie.Channels);
        Assert.Equal(7, movie.Compression);
        Assert.Equal(22050, movie.SampleRate);
    }

    [InstallFact]
    public void BullfrogIntroDecodesEveryFrameToItsLastWordAndEveryAudioBlock()
    {
        EaMovie movie = EaMovie.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Movies", "BullfrogIntro.tgq"));
        var decoder = new TqiDecoder();
        var rgba = new byte[320 * 480 * 4];
        var stereo = new short[2 * 4096];
        int frames = 0, blocks = 0;

        foreach (EaMovieChunk chunk in movie.Chunks)
        {
            ReadOnlySpan<byte> payload = movie.Payload(chunk);
            if (chunk.Kind == EaMovieChunkKind.Video)
            {
                Assert.Equal((320, 480), TqiDecoder.FrameSize(payload));
                int used = decoder.Decode(payload, rgba);
                int available = (payload.Length - 8) * 8;
                Assert.InRange(used, available - 32, available);
                frames++;
            }
            else if (chunk.Kind == EaMovieChunkKind.Audio)
            {
                Assert.True(EaAudio.DecodeBlock(payload, movie.Channels, movie.Compression, stereo) > 0);
                blocks++;
            }
        }

        Assert.Equal((2, 22050, 7), (movie.Channels, movie.SampleRate, movie.Compression));
        Assert.Equal(189, frames);
        Assert.Equal(187, blocks);
    }

    [InstallFact]
    public void BullfrogIntrosLogoFrameIsRedOnDark_ASanityCheckNotAReference()
    {
        // No DK2-rendered frame has been captured to compare against; this only catches a decoder
        // that produces noise or swaps channels.
        EaMovie movie = EaMovie.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Movies", "BullfrogIntro.tgq"));
        EaMovieChunk frame = movie.Chunks.Where(c => c.Kind == EaMovieChunkKind.Video).ElementAt(120);
        var rgba = new byte[320 * 480 * 4];

        new TqiDecoder().Decode(movie.Payload(frame), rgba);

        long r = 0, g = 0, b = 0;
        for (int i = 0; i < rgba.Length; i += 4) { r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; }
        Assert.True(r > 2 * g && r > 2 * b, $"mean R {r} G {g} B {b}");
    }

    private static byte[] Frame(int width, int height, int quantiser, BitWriter bits)
    {
        byte[] stream = bits.ToWords();
        var frame = new byte[8 + stream.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)height);
        frame[4] = (byte)quantiser;
        frame[7] = 3;
        stream.CopyTo(frame, 8);
        return frame;
    }

    private static byte[] Chunk(string tag, byte[] payload)
    {
        var chunk = new byte[8 + payload.Length];
        Encoding.ASCII.GetBytes(tag).CopyTo(chunk, 0);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), chunk.Length);
        payload.CopyTo(chunk, 8);
        return chunk;
    }

    /// <summary>Bits most significant first, packed into little-endian 32-bit words as DK2 reads them.</summary>
    private sealed class BitWriter
    {
        private readonly StringBuilder _bits = new();
        public int Count => _bits.Length;
        public void Put(string bits) => _bits.Append(bits);

        public byte[] ToWords()
        {
            string all = _bits.ToString().PadRight((_bits.Length + 31) / 32 * 32, '0');
            var bytes = new byte[all.Length / 8];
            for (int w = 0; w < all.Length / 32; w++)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(w * 4), Convert.ToUInt32(all.Substring(w * 32, 32), 2));
            return bytes;
        }
    }
}
