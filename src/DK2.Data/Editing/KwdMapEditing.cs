// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Core;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Editing;

public readonly record struct KwdChunkLocation(string RelativePath, int ChunkIndex);

public sealed partial class KwdLevelFileSet
{
    public LevelMap ReadMap(KwdChunkLocation location)
    {
        KwdChunk chunk=RequireType(new(location.RelativePath,location.ChunkIndex,0),KwdRecordType.Map);
        if(chunk.HeaderBlock.Length<8) throw new InvalidDataException("MAP needs an explicit width/height header.");
        int width=BinaryPrimitives.ReadInt32LittleEndian(chunk.HeaderBlock);
        int height=BinaryPrimitives.ReadInt32LittleEndian(chunk.HeaderBlock.AsSpan(4));
        int expected;
        try { expected=checked(width*height*4); }
        catch(OverflowException exception) { throw new InvalidDataException("MAP dimensions exceed the supported image size.",exception); }
        if(width<=0 || height<=0 || chunk.Content.Length!=expected)
            throw new InvalidDataException("MAP dimensions must describe the complete tile image exactly.");
        return MapGrid.Parse([chunk])!;
    }

    public void RepaintMap(KwdChunkLocation location,TilePos position,MapTile tile)
    {
        LevelMap map=ReadMap(location);
        if(!map.Contains(position.X,position.Y)) throw new ArgumentOutOfRangeException(nameof(position));
        var record=new KwdRecordLocation(location.RelativePath,location.ChunkIndex,0);
        KwdChunk chunk=Chunk(record);
        int at=(position.Y*map.Width+position.X)*4;
        byte[] content=chunk.Content.ToArray();
        MapGrid.WriteTile(tile,new KwdRecordImage(KwdRecordType.Map,content.AsSpan(at,4).ToArray())).CopyTo(content,at);
        Replace(record,chunk with { Content=content });
    }

    public void ResizeMap(KwdChunkLocation location,KwdMapDimensions dimensions,KwdRecordImage fillTemplate)
    {
        if(dimensions.Width<=0 || dimensions.Height<=0) throw new ArgumentOutOfRangeException(nameof(dimensions));
        int size=checked(dimensions.Width*dimensions.Height*4);
        byte[] fill=KwdOverlay.Image(fillTemplate,KwdRecordType.Map,4);
        LevelMap map=ReadMap(location);
        var record=new KwdRecordLocation(location.RelativePath,location.ChunkIndex,0);
        KwdChunk chunk=Chunk(record);
        byte[] content=new byte[size];
        for(int at=0;at<content.Length;at+=4) fill.CopyTo(content,at);
        int rowBytes=Math.Min(map.Width,dimensions.Width)*4;
        for(int y=0;y<Math.Min(map.Height,dimensions.Height);y++)
            chunk.Content.AsSpan(y*map.Width*4,rowBytes).CopyTo(content.AsSpan(y*dimensions.Width*4,rowBytes));
        byte[] header=chunk.HeaderBlock.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(header,dimensions.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4),dimensions.Height);
        Replace(record,chunk with { HeaderBlock=header,Content=content });
    }
}
