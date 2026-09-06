// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;
using DK2.Data.Text;

namespace DK2.Data.Tests;

public class Dk2StringTableTests
{
    [Fact]
    public void SyntheticTableResolvesIdsCharactersAndParameters()
    {
        byte[] codePage = CodePage('A', 'B', 'C');
        byte[] first = Entry(StringChunk(1, 2, 3));
        byte[] second = Entry(StringChunk(3), ParameterChunk(0), StringChunk(2));
        byte[] data = Table(400, first, second);

        Dk2StringTable table = Dk2StringTable.Read(data, codePage);

        Assert.Equal(400u, table.FileId);
        Assert.Equal("ABC", table.Resolve(0));
        Assert.Equal("C%1B", table.Resolve(1));
        Assert.False(table.TryResolve(2, out _));
        Assert.Throws<KeyNotFoundException>(() => table.Resolve(2));
    }

    [Fact]
    public void StoredOffsetsAreRelativeToTheEndOfTheTwelveByteHeader()
    {
        byte[] data = Table(10, Entry(StringChunk(1)));

        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)));
        Assert.Equal(16u, 12u + BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)));
        Assert.Equal("A", Dk2StringTable.Read(data, CodePage('A')).Resolve(0));
    }

    [Fact]
    public void MultiByteCharacterIndexesUseTheCodePageThreshold()
    {
        char[] characters = Enumerable.Repeat('x', 254).ToArray();
        characters[253] = '\u03a9';
        byte[] codePage = CodePage(characters);
        codePage[4] = 2; // threshold = 253; indexes from here consume two bytes

        Dk2StringTable table = Dk2StringTable.Read(
            Table(1, Entry(StringChunk(254, 1))), codePage);

        Assert.Equal("\u03a9", table.Resolve(0));
    }

    [Fact]
    public void MalformedChunksAndOffsetsAreRejected()
    {
        byte[] badOffset = Table(10, Entry(StringChunk(1)));
        BinaryPrimitives.WriteUInt32LittleEndian(badOffset.AsSpan(12), 0);
        Assert.Throws<InvalidDataException>(() =>
            Dk2StringTable.Read(badOffset, CodePage('A'), "bad-offset.str"));

        byte[] noEnd = Table(10, Chunk(1, [1]));
        Assert.Throws<InvalidDataException>(() =>
            Dk2StringTable.Read(noEnd, CodePage('A'), "no-end.str"));
    }

    [Fact]
    public void ShippedDefinitionStringIdsResolveToTheirNames()
    {
        string? install = Dk2Install.Find();
        if (install is null) return;

        GameDatabase database = GameDatabaseLoader.Load(install);
        Dk2StringTable text = Dk2StringTable.Open(
            Path.Combine(install, "Data", "Text", "Default", "Text.str"));

        CreatureDef goblin = database.CreatureByName("Goblin")!;
        Assert.Equal(goblin.Name, text.Resolve(goblin.NameStringId));

        RoomDef room = database.RoomByName("Treasury")!;
        Assert.Equal(room.Name, text.Resolve(room.NameStringId));

        ObjectDef gameObject = database.Objects.Values.Single(
            definition => definition.Name == "Chicken");
        Assert.Equal(gameObject.Name, text.Resolve(gameObject.NameStringId));

        TerrainDef terrain = database.Terrain.Values.Single(
            definition => definition.Name == "Rock");
        Assert.Equal(terrain.Name, text.Resolve(terrain.NameStringId));
    }

    [Fact]
    public void EveryShippedTableDecodesEveryEntryAndHasTheConfirmedTotals()
    {
        string? install = Dk2Install.Find();
        if (install is null) return;

        string textRoot = Path.Combine(install, "Data", "Text", "Default");
        string[] files = Directory.GetFiles(textRoot, "*.str", SearchOption.TopDirectoryOnly);
        Assert.Equal(92, files.Length);

        int entries = 0;
        long bytes = 0;
        foreach (string file in files)
        {
            Dk2StringTable table = Dk2StringTable.Open(file);
            entries += table.Entries.Count;
            bytes += new FileInfo(file).Length;
            Assert.Equal((uint)(table.Entries.Count * sizeof(uint)),
                table.Entries.Min(entry => entry.StoredOffset));
            Assert.Equal(table.Entries.Select(entry => entry.StoredOffset).OrderBy(value => value),
                table.Entries.Select(entry => entry.StoredOffset));
            Assert.All(table.Entries, entry => Assert.True(table.TryResolve(entry.Id, out _)));

            if (Path.GetFileName(file).Equals("Text.str", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(1091u, table.FileId);
                Assert.Equal(3086, table.Entries.Count);
            }
        }

        Assert.Equal(7024, entries);
        Assert.Equal(353104, bytes);
    }

    private static byte[] CodePage(params char[] characters)
    {
        byte[] data = new byte[8 + characters.Length * 2];
        "BFMU"u8.CopyTo(data);
        data[4] = 0;
        for (int i = 0; i < characters.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8 + i * 2), characters[i]);
        return data;
    }

    private static byte[] Table(uint baseId, params byte[][] entries)
    {
        int tableSize = entries.Length * 4;
        byte[] data = new byte[12 + tableSize + entries.Sum(entry => entry.Length)];
        "BFST"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), baseId);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)entries.Length);
        int storedOffset = tableSize;
        int destination = 12 + tableSize;
        for (int i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12 + i * 4), (uint)storedOffset);
            entries[i].CopyTo(data, destination);
            storedOffset += entries[i].Length;
            destination += entries[i].Length;
        }
        return data;
    }

    private static byte[] Entry(params byte[][] chunks) =>
        chunks.Append(Chunk(0, [])).SelectMany(chunk => chunk).ToArray();

    private static byte[] StringChunk(params byte[] bytes) => Chunk(1, bytes);
    private static byte[] ParameterChunk(int zeroBasedParameter) => Chunk(2, [], zeroBasedParameter);

    private static byte[] Chunk(int type, byte[] payload, int? encodedLength = null)
    {
        int size = 4 + payload.Length;
        byte[] chunk = new byte[(size + 3) & ~3];
        uint header = ((uint)(encodedLength ?? payload.Length) << 8) | (uint)type;
        BinaryPrimitives.WriteUInt32LittleEndian(chunk, header);
        payload.CopyTo(chunk, 4);
        return chunk;
    }
}
