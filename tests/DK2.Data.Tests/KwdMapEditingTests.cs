// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Core;
using DK2.Data.Editing;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdMapEditingTests
{
    [Fact]
    public void RepaintChangesOnlyTheSelectedFourBytes()
    {
        KwdChunk chunk=KwdLevelFileSetTests.Fixture(KwdRecordType.Map,Enumerable.Range(1,24).Select(i=>(byte)i).ToArray(),0,3,2);
        var editor=Set(chunk); var location=new KwdChunkLocation("map.kld",0);
        editor.RepaintMap(location,new TilePos(1,1),new MapTile(13,14,15,16));
        KwdChunk edited=KwdThingEditingTests.Reload(editor.ToBytes("map.kld"));
        Assert.Equal(chunk.HeaderBlock,edited.HeaderBlock);
        for(int i=0;i<24;i++) if(i<16 || i>=20) Assert.Equal(chunk.Content[i],edited.Content[i]);
        Assert.Equal(new MapTile(13,14,15,16),editor.ReadMap(location)[1,1]);
    }

    [Fact]
    public void GrowingAndCroppingRetainCoordinatesAndUseExplicitFillImages()
    {
        KwdChunk chunk=KwdLevelFileSetTests.Fixture(KwdRecordType.Map,Enumerable.Range(1,16).Select(i=>(byte)i).ToArray(),0,2,2);
        var editor=Set(chunk); var location=new KwdChunkLocation("map.kld",0);
        var fill=new KwdRecordImage(KwdRecordType.Map,[0xFA,0xFB,0xFC,0xFD]);
        LevelMap original=MapGrid.Parse([chunk])!;
        editor.ResizeMap(location,new KwdMapDimensions(3,3),fill);
        LevelMap result=editor.ReadMap(location);
        for(int y=0;y<3;y++) for(int x=0;x<3;x++)
            Assert.Equal(x<2 && y<2 ? original[x,y] : new MapTile(0xFA,0xFB,0xFC,0xFD),result[x,y]);
        KwdChunk reread=KwdThingEditingTests.Reload(editor.ToBytes("map.kld"));
        Assert.Equal(0,reread.ItemCount); Assert.Equal(36,reread.Content.Length);
        Assert.Equal(3,BinaryPrimitives.ReadInt32LittleEndian(reread.HeaderBlock));
        Assert.Equal(3,BinaryPrimitives.ReadInt32LittleEndian(reread.HeaderBlock.AsSpan(4)));
        Assert.Equal(chunk.ContainerHeader!.ChunkEndOffset+20,reread.ContainerHeader!.ChunkEndOffset);
        Assert.Equal(36u,reread.ContainerHeader.DeclaredContentSize);
        editor.ResizeMap(location,new KwdMapDimensions(1,2),fill);
        Assert.Equal(new[]{original[0,0],original[0,1]},editor.ReadMap(location).Tiles);
    }

    [Fact]
    public void IdentityResizeAndInvalidEditsLeaveAllBytesUnchanged()
    {
        KwdChunk chunk=KwdLevelFileSetTests.Fixture(KwdRecordType.Map,new byte[]{1,2,3,4},0);
        var editor=Set(chunk); var location=new KwdChunkLocation("map.kld",0);
        var fill=new KwdRecordImage(KwdRecordType.Map,[13,14,15,16]);
        byte[] original=editor.ToBytes("map.kld");
        editor.ResizeMap(location,new KwdMapDimensions(1,1),fill); Assert.Equal(original,editor.ToBytes("map.kld"));
        Assert.Throws<ArgumentOutOfRangeException>(()=>editor.RepaintMap(location,new TilePos(-1,0),default));
        Assert.Throws<ArgumentOutOfRangeException>(()=>editor.RepaintMap(location,new TilePos(1,0),default));
        Assert.Throws<ArgumentOutOfRangeException>(()=>editor.ResizeMap(location,new KwdMapDimensions(0,1),fill));
        Assert.Throws<OverflowException>(()=>editor.ResizeMap(location,new KwdMapDimensions(int.MaxValue,2),fill));
        Assert.Throws<InvalidDataException>(()=>editor.ResizeMap(location,new KwdMapDimensions(2,2),new KwdRecordImage(KwdRecordType.Doors,new byte[4])));
        Assert.Equal(original,editor.ToBytes("map.kld"));
    }

    [Fact]
    public void InconsistentSourceDimensionsAreRefused()
    {
        var editor=Set(KwdLevelFileSetTests.Fixture(KwdRecordType.Map,new byte[8],0));
        Assert.Throws<InvalidDataException>(()=>editor.ReadMap(new("map.kld",0)));
    }

    [InstallFact]
    public void EveryShippedMapRepaintsAndGrowsBackToItsExactSourceBytes()
    {
        foreach(string level in Directory.GetFiles(Path.Combine(Dk2InstallGate.Path,"Data","editor","maps"),"*.kwd"))
        {
            var editor=KwdLevelFileSet.Load(level,Dk2InstallGate.Path);
            string file=editor.Files.Single(f=>editor.Chunks(f).Any(c=>c.Type==KwdRecordType.Map));
            var location=new KwdChunkLocation(file,0); LevelMap map=editor.ReadMap(location);
            byte[] original=editor.ToBytes(file);
            MapTile tile=map[map.Width-1,map.Height-1];
            editor.RepaintMap(location,new TilePos(map.Width-1,map.Height-1),tile with { TerrainId=(byte)(tile.TerrainId^1) });
            Assert.Equal((byte)(tile.TerrainId^1),editor.ReadMap(location)[map.Width-1,map.Height-1].TerrainId);
            editor.RepaintMap(location,new TilePos(map.Width-1,map.Height-1),tile);
            var fill=KwdRecordRoundTrip.Split(editor.Chunks(file).Single(c=>c.Type==KwdRecordType.Map))[0];
            editor.ResizeMap(location,new KwdMapDimensions(map.Width+1,map.Height+1),fill);
            editor.ResizeMap(location,new KwdMapDimensions(map.Width,map.Height),fill);
            Assert.Equal(original,editor.ToBytes(file));
        }
    }

    private static KwdLevelFileSet Set(KwdChunk map)=>KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> { ["map.kld"]=[map] });
}
