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
/// every level record in it either succeeds or throws <see cref="InvalidDataException"/>, and never allocates
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

    /// <summary>The same property on every shipped level file (MAP, PLAYERS, THINGS, TRIGGERS,
    /// VARIABLES and the LEVEL header), with words overwritten at random.</summary>
    [InstallFact]
    public void ShippedLevelFiles_WithWordsOverwritten_ReadOrFailInAControlledWay()
    {
        string maps = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "maps");
        var random = new Random(72450);
        int[] values = [int.MaxValue, -1, 0x40000000, 0];
        string[] files = Directory.GetFiles(maps, "*.kld").Concat(Directory.GetFiles(maps, "*.kwd")).ToArray();
        Assert.True(files.Length > 300, $"expected every shipped level file, found {files.Length}");
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
            ParseLevel(KwdReader.Read(new MemoryStream(input)));
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

    /// <summary>Every parser a transferred level reaches, as the level loader calls them.</summary>
    private static void ParseLevel(List<KwdChunk> chunks)
    {
        MapGrid.Parse(chunks);
        KwdPlayers.Parse(chunks);
        KwdThings.Parse(chunks);
        KwdTriggers.Parse(chunks);
        KwdVariables.Parse(chunks);
        KwdLevel.Parse(chunks);
    }

    /// <summary>Mutations start from a file every parser accepts, so they reach past the first check.</summary>
    [Fact]
    public void SyntheticFile_ParsesWhole()
    {
        List<KwdChunk> chunks = KwdReader.Read(new MemoryStream(SyntheticFile()));

        Assert.Equal(6, MapGrid.Parse(chunks)!.Tiles.Count);
        Assert.Single(KwdPlayers.Parse(chunks));
        Assert.Equal(2, KwdThings.Parse(chunks).Count);
        Assert.Equal(2, KwdTriggers.Parse(chunks).Count);
    }

    /// <summary>A 3x2 MAP grid, one VARIABLES record, one PLAYERS record, two THINGS and two
    /// TRIGGERS. LEVEL is left to the install test: its 25 KB of metadata would make the
    /// every-word sweep here slow.</summary>
    private static byte[] SyntheticFile()
    {
        byte[] mapHeader = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(mapHeader, 3);
        BinaryPrimitives.WriteInt32LittleEndian(mapHeader.AsSpan(4), 2);
        byte[] tiles = Enumerable.Range(0, 3 * 2 * 4).Select(i => (byte)i).ToArray();
        byte[] variables = Enumerable.Range(0, 16).Select(i => (byte)(i * 7)).ToArray();
        byte[] player = Enumerable.Range(0, KwdPlayers.RecordSize).Select(i => (byte)(i * 3)).ToArray();
        byte[] things = [.. Tagged(194, 24), .. Tagged(197, 56)];
        byte[] triggers = [.. Tagged(213, 16), .. Tagged(214, 16)];
        return KwdWriter.ToBytes([
            Chunk(KwdRecordType.Map, mapHeader, tiles),
            Chunk(KwdRecordType.Variables, Counts(1), variables),
            Chunk(KwdRecordType.Players, Counts(1), player),
            Chunk(KwdRecordType.Things, Counts(2), things),
            Chunk(KwdRecordType.Triggers, Counts(1, 1), triggers),
        ]);
    }

    private static byte[] Counts(params int[] counts)
    {
        byte[] header = new byte[28];
        for (int i = 0; i < counts.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(i * 4), counts[i]);
        return header;
    }

    private static byte[] Tagged(int tag, int size)
    {
        byte[] record = new byte[KwdTaggedRecords.RecordHeaderSize + size];
        BinaryPrimitives.WriteInt32LittleEndian(record, tag);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), size);
        for (int i = KwdTaggedRecords.RecordHeaderSize; i < record.Length; i++) record[i] = (byte)(i * 5);
        return record;
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
