// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdMapWriterTests
{
    [Fact]
    public void EachTypedTileByteEditsOnlyThatByteAndReadsBack()
    {
        byte[] source = [1, 2, 0xA3, 0xD4];
        MapTile original = Read(source);
        MapTile[] changed = [original with { TerrainId = 77 }, original with { PlayerId = 7 },
            original with { Flags = 0xE1 }, original with { Unknown = 0xB2 }];
        for (int index = 0; index < changed.Length; index++)
        {
            byte[] written = MapGrid.WriteTile(changed[index], new KwdRecordImage(KwdRecordType.Map, source));
            Assert.Equal(changed[index], Read(written));
            Assert.NotEqual(source[index], written[index]);
            for (int i = 0; i < 4; i++) if (i != index) Assert.Equal(source[i], written[i]);
        }
    }

    [InstallFact]
    public void EveryShippedMapTileTypedOverlayIsByteIdentical()
    {
        string maps = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "maps");
        int files = 0, tiles = 0;
        foreach (string file in Directory.GetFiles(maps, "*Map.kld"))
        {
            KwdChunk chunk = KwdReader.ReadFile(file).Single();
            LevelMap map = MapGrid.Parse([chunk])!;
            List<KwdRecordImage> images = KwdRecordRoundTrip.Split(chunk);
            Assert.Equal(map.Tiles.Count, images.Count);
            for (int i = 0; i < images.Count; i++)
                Assert.Equal(images[i].ToBytes(), MapGrid.WriteTile(map.Tiles[i], images[i]));
            files++; tiles += images.Count;
        }
        Assert.Equal(66, files);
        Assert.Equal(345694, tiles);
    }

    private static MapTile Read(byte[] content)
    {
        byte[] header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 1);
        return MapGrid.Parse([new KwdChunk { Type = KwdRecordType.Map, ItemCount = 0,
            Content = content, HeaderBlock = header }])!.Tiles.Single();
    }
}
