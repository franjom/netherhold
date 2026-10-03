// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using DK2.Data.Editing;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

public class KwdEditingOwnershipTests
{
    [Fact]
    public void DecodedRawSlicesCannotMutateTheOwnedFile()
    {
        var editor=LevelSet(); byte[] original=editor.ToBytes("level.kwd");
        LevelDef row=editor.ReadRecord<LevelDef>(new("level.kwd",0,0));
        Assert.True(MemoryMarshal.TryGetArray(row.Unknown_1184,out ArraySegment<byte> segment));
        segment.Array![segment.Offset]=13;
        Assert.Equal(original,editor.ToBytes("level.kwd"));
    }

    [Fact]
    public void AThrowingCallbackCannotLeaveRawMutationsBehind()
    {
        var editor=LevelSet(); byte[] original=editor.ToBytes("level.kwd");
        Assert.Throws<InvalidOperationException>(()=>editor.EditRecord<LevelDef>(new("level.kwd",0,0),row=>
        {
            Assert.True(MemoryMarshal.TryGetArray(row.Unknown_1184,out ArraySegment<byte> segment));
            segment.Array![segment.Offset]=13;
            throw new InvalidOperationException("abort edit");
        }));
        Assert.Equal(original,editor.ToBytes("level.kwd"));
    }

    [Fact]
    public void MutatingACallbackPathListCannotBypassManifestRefusal()
    {
        var editor=LevelSet(); byte[] original=editor.ToBytes("level.kwd");
        Assert.Throws<InvalidDataException>(()=>editor.EditRecord<LevelDef>(new("level.kwd",0,0),row=>
        {
            var paths=Assert.IsAssignableFrom<IList<KwdLevelFilePath>>(row.Paths);
            paths[0]=paths[0] with { Path="other.kwd" };
            return row;
        }));
        Assert.Equal(original,editor.ToBytes("level.kwd"));
    }

    [Fact]
    public void MutatingACallbackPartyListCannotBypassTriggerRootRefusal()
    {
        KwdChunk chunk=KwdLevelFileSetTests.Fixture(KwdRecordType.Things,KwdThingsWriterTests.Image(201,new byte[555]).ToBytes(),1);
        var editor=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> { ["things.kld"]=[chunk] });
        byte[] original=editor.ToBytes("things.kld");
        Assert.Throws<InvalidDataException>(()=>editor.EditRecord<HeroPartyThing>(new("things.kld",0,0),row=>
        {
            var members=Assert.IsAssignableFrom<IList<GoodCreatureThing>>(row.Members);
            members[0]=members[0] with { TriggerId=13 };
            return row;
        }));
        Assert.Equal(original,editor.ToBytes("things.kld"));
    }

    [Fact]
    public void ReentrantStructuralEditsCannotRestoreADanglingTriggerRoot()
    {
        byte[] body=new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(20),13);
        var editor=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> {
            ["level.kwd"]=[KwdLevelFileSetTests.Level(["things.kld","triggers.kld"])],
            ["things.kld"]=[KwdLevelFileSetTests.Fixture(KwdRecordType.Things,KwdThingsWriterTests.Image(194,body).ToBytes(),1)],
            ["triggers.kld"]=[KwdLevelFileSetTests.Fixture(KwdRecordType.Triggers,KwdThingEditingTests.Trigger(213,13),1)] });
        byte[] things=editor.ToBytes("things.kld"),triggers=editor.ToBytes("triggers.kld");
        Assert.Throws<InvalidOperationException>(()=>editor.EditRecord<ObjectThing>(new("things.kld",0,0),row=>
        {
            editor.RemoveThing(new("things.kld",0,0));
            editor.RemoveTrigger(new("triggers.kld",0,0));
            return row with { MoneyAmount=13 };
        }));
        Assert.Equal(things,editor.ToBytes("things.kld")); Assert.Equal(triggers,editor.ToBytes("triggers.kld"));
    }

    [Fact]
    public void NestedFieldEditsAreRefusedAndTheGuardResetsAfterTheCallback()
    {
        var location=new KwdRecordLocation("things.kld",0,0);
        var editor=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> {
            ["things.kld"]=[KwdLevelFileSetTests.Fixture(KwdRecordType.Things,KwdThingsWriterTests.Image(194,new byte[24]).ToBytes(),1)] });
        editor.EditRecord<ObjectThing>(location,row=>
        {
            Assert.Throws<InvalidOperationException>(()=>editor.EditRecord<ObjectThing>(location,inner=>inner with { MoneyAmount=12 }));
            return row with { MoneyAmount=13 };
        });
        Assert.Equal(13,editor.ReadRecord<ObjectThing>(location).MoneyAmount);
        editor.EditRecord<ObjectThing>(location,row=>row with { MoneyAmount=14 });
        Assert.Equal(14,editor.ReadRecord<ObjectThing>(location).MoneyAmount);
    }

    [Fact]
    public void OpaqueLevelEditsKeepTheirMetadataBoundaryAndRoundTrip()
    {
        byte[] bytes=new byte[25683+8+72];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(25683),222);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(25687),72);
        var editor=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> {
            ["level.kwd"]=[KwdLevelFileSetTests.Fixture(KwdRecordType.Level,bytes,1)] });
        byte[] raw=new byte[80];
        BinaryPrimitives.WriteUInt32LittleEndian(raw,222);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4),72);
        editor.EditRecord<LevelDef>(new("level.kwd",0,0),row=>row with { Unknown_6403=raw });
        Assert.Equal(raw,editor.ReadRecord<LevelDef>(new("level.kwd",0,0)).Unknown_6403.ToArray());
        byte[] result=editor.Chunks("level.kwd")[0].Content;
        for(int i=0;i<bytes.Length;i++)
            if(i<25603 || i>=25683) Assert.Equal(bytes[i],result[i]);
    }

    private static KwdLevelFileSet LevelSet()=>KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> {
        ["level.kwd"]=[KwdLevelFileSetTests.Level(["file.kwd"])] });
}
