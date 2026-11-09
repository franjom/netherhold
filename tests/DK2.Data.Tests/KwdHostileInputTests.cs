// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

/// <summary>
/// Custom maps arrive from other players (ADR-0724 R5). Whatever the bytes, reading a KWD file and
/// its MAP grid either succeeds or throws <see cref="InvalidDataException"/>, and never allocates
/// far beyond the file's own size.
/// </summary>
public class KwdHostileInputTests
{
    private const long MaxBytesPerSyntheticRead = 1024 * 1024;

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(0x40000000)]
    [InlineData(0)]
    [InlineData(1)]
    public void AnyWordOverwritten_ReadsOrFailsInAControlledWay(int value)
    {
        byte[] file = SyntheticFile();
        for (int at = 0; at + 4 <= file.Length; at++)
        {
            byte[] input = (byte[])file.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(at), value);
            AssertControlled(input, MaxBytesPerSyntheticRead, $"word at {at} = {value}");
        }
    }

    [Fact]
    public void AnyTruncation_ReadsOrFailsInAControlledWay()
    {
        byte[] file = SyntheticFile();
        for (int length = 0; length < file.Length; length++)
            AssertControlled(file[..length], MaxBytesPerSyntheticRead, $"cut at {length}");
    }

    [Fact]
    public void RandomBitFlips_ReadOrFailInAControlledWay()
    {
        byte[] file = SyntheticFile();
        var random = new Random(7245);
        for (int i = 0; i < 20_000; i++)
        {
            byte[] input = (byte[])file.Clone();
            for (int f = random.Next(1, 5); f > 0; f--)
                input[random.Next(input.Length)] ^= (byte)(1 << random.Next(8));
            AssertControlled(input, MaxBytesPerSyntheticRead, $"case {i}");
        }
    }

    /// <summary>The same property on every shipped map, with words overwritten at random.</summary>
    [InstallFact]
    public void ShippedMaps_WithWordsOverwritten_ReadOrFailInAControlledWay()
    {
        string maps = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "maps");
        var random = new Random(72450);
        int[] values = [int.MaxValue, -1, 0x40000000, 0];
        string[] files = Directory.GetFiles(maps, "*Map.kld");
        Assert.True(files.Length > 50, $"expected every shipped MAP file, found {files.Length}");
        foreach (string path in files)
        {
            byte[] file = File.ReadAllBytes(path);
            for (int i = 0; i < 200; i++)
            {
                byte[] input = (byte[])file.Clone();
                int at = random.Next(file.Length - 3);
                BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(at), values[i % values.Length]);
                AssertControlled(input, 8L * file.Length + MaxBytesPerSyntheticRead,
                    $"{Path.GetFileName(path)} word at {at}");
            }
        }
    }

    private static void AssertControlled(byte[] input, long maxBytes, string what)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            List<KwdChunk> chunks = KwdReader.Read(new MemoryStream(input));
            MapGrid.Parse(chunks);
        }
        catch (InvalidDataException)
        {
        }
        catch (Exception e)
        {
            Assert.Fail($"{e.GetType().Name} with {what}: {e.Message}");
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < maxBytes, $"{what} allocated {allocated} bytes");
    }

    /// <summary>A 3x2 MAP chunk followed by a general (VARIABLES-shaped) chunk.</summary>
    private static byte[] SyntheticFile()
    {
        byte[] mapHeader = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(mapHeader, 3);
        BinaryPrimitives.WriteInt32LittleEndian(mapHeader.AsSpan(4), 2);
        byte[] tiles = Enumerable.Range(0, 3 * 2 * 4).Select(i => (byte)i).ToArray();
        byte[] generalHeader = new byte[28];
        BinaryPrimitives.WriteInt32LittleEndian(generalHeader, 2);
        byte[] records = Enumerable.Range(0, 16).Select(i => (byte)(i * 7)).ToArray();
        return KwdWriter.ToBytes([Chunk(KwdRecordType.Map, mapHeader, tiles), Chunk(KwdRecordType.Variables, generalHeader, records)]);
    }

    private static KwdChunk Chunk(KwdRecordType type, byte[] header, byte[] content) => new()
    {
        Type = type,
        ItemCount = 0,
        HeaderBlock = header,
        Content = content,
        ContainerHeader = new KwdContainerHeader((uint)(28 + header.Length + content.Length),
            (uint)type + 2, (uint)content.Length, content.Length),
    };
}
