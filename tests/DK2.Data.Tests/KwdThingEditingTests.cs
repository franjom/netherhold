// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Editing;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

public class KwdThingEditingTests
{
    [Fact]
    public void InsertAndRemoveKeepCountsSizesOffsetsOrderingAndUnknownHeaderBytes()
    {
        byte[] trap=KwdThingsWriterTests.Image(195,new byte[16]).ToBytes();
        byte[] body=new byte[24]; body[8]=0x5A;
        KwdRecordImage template=KwdThingsWriterTests.Image(194,body);
        KwdChunk chunk=KwdLevelFileSetTests.Fixture(KwdRecordType.Things,trap,1);
        Array.Fill(chunk.HeaderBlock,(byte)0x5A,4,24);
        var editor=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> { ["things.kld"]=[chunk] });
        byte[] before=editor.ToBytes("things.kld");
        editor.InsertThing(new("things.kld",0,0),((ObjectThing)KwdThings.Parse(194,body)) with { MoneyAmount=13 },template);
        KwdChunk result=Reload(editor.ToBytes("things.kld"));
        Assert.Equal(2,result.ItemCount); Assert.Equal(56,result.Content.Length);
        Assert.Equal(chunk.HeaderBlock[4..],result.HeaderBlock[4..]);
        Assert.Equal(new[]{194,195},KwdTaggedRecords.Split(result).Select(r=>r.Tag));
        Assert.Equal(13,((ObjectThing)KwdThings.Parse([result])[0]).MoneyAmount);
        Assert.Equal(chunk.ContainerHeader!.ChunkEndOffset+32,result.ContainerHeader!.ChunkEndOffset);
        Assert.Equal(chunk.ContainerHeader.DeclaredContentSize+32,result.ContainerHeader.DeclaredContentSize);
        Assert.Equal(trap,result.Content[32..]); Assert.Equal(0x5A,result.Content[16]);
        editor.RemoveThing(new("things.kld",0,0)); Assert.Equal(before,editor.ToBytes("things.kld"));
        editor.RemoveThing(new("things.kld",0,0)); Assert.Empty(Reload(editor.ToBytes("things.kld")).Content);
    }

    [Fact]
    public void NewRootsMustResolveAndInvalidInsertionLeavesTheSetUnchanged()
    {
        var editor=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> {
            ["level.kwd"]=[KwdLevelFileSetTests.Level(["things.kld","triggers.kld"])],
            ["things.kld"]=[KwdLevelFileSetTests.Fixture(KwdRecordType.Things,[],0)],
            ["triggers.kld"]=[KwdLevelFileSetTests.Fixture(KwdRecordType.Triggers,Trigger(213,13),1)] });
        byte[] original=editor.ToBytes("things.kld"); byte[] body=new byte[24];
        var row=(ObjectThing)KwdThings.Parse(194,body);
        var template=KwdThingsWriterTests.Image(194,body);
        Assert.Throws<InvalidDataException>(()=>editor.InsertThing(new("things.kld",0,0),row with { TriggerId=14 },template));
        Assert.Throws<ArgumentOutOfRangeException>(()=>editor.InsertThing(new("things.kld",0,1),row,template));
        Assert.Equal(original,editor.ToBytes("things.kld"));
        editor.InsertThing(new("things.kld",0,0),row with { TriggerId=13 },template);
        Assert.Equal(13,editor.ReadRecord<ObjectThing>(new("things.kld",0,0)).TriggerId);
    }

    internal static byte[] Trigger(int tag,ushort id,ushort next=0,ushort child=0)
    {
        byte[] bytes=new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes,tag); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16),id);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18),next);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20),child);
        return bytes;
    }

    internal static KwdChunk Reload(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes); return KwdReader.Read(stream).Single();
    }
}
