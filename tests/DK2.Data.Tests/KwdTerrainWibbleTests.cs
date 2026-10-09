// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>The terrain lattice's eight bytes at file 0x1CE..0x1D5 (terrain-lattice.md §4).</summary>
public class KwdTerrainWibbleTests
{
    private const int RecordSize = 552, Wibble = 0x1CE;

    private static byte[] Source()
    {
        byte[] source = Enumerable.Repeat((byte)0x5A, RecordSize).ToArray();
        source[0] = (byte)'a'; source[1] = 0;
        for (int i = 0; i < 8; i++) source[Wibble + i] = (byte)(10 + i);
        return source;
    }

    private static TerrainDef Read(byte[] source) => KwdTerrainRooms.ParseTerrain([
        new KwdChunk { Type = KwdRecordType.Terrain, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();

    [Fact]
    public void TheEightBytesReadInOrderAndWriteBackUnchanged()
    {
        byte[] source = Source();
        TerrainDef terrain = Read(source);

        Assert.Equal(new TerrainWibble(10, 11, 12, 13, 14, 15, 16, 17), terrain.Wibble);
        Assert.Equal(source, KwdTerrainRooms.WriteTerrainRecord(terrain, new KwdRecordImage(KwdRecordType.Terrain, source)));
    }

    [Fact]
    public void AnEditedWibbleChangesOnlyItsBytesAndANullOneLeavesTheTemplate()
    {
        byte[] source = Source();
        TerrainDef terrain = Read(source);

        byte[] edited = KwdTerrainRooms.WriteTerrainRecord(
            terrain with { Wibble = new TerrainWibble(1, 2, 3, 4, 5, 6, 7, 8) },
            new KwdRecordImage(KwdRecordType.Terrain, source));
        for (int i = 0; i < RecordSize; i++)
            Assert.Equal(i is >= Wibble and < Wibble + 8 ? (byte)(i - Wibble + 1) : source[i], edited[i]);

        Assert.Equal(source, KwdTerrainRooms.WriteTerrainRecord(
            terrain with { Wibble = null }, new KwdRecordImage(KwdRecordType.Terrain, source)));
    }
}
