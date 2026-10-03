// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Editing;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdTriggerEditingTests
{
    [Theory]
    [InlineData(213)]
    [InlineData(214)]
    public void DetachedInsertAndRemovePreserveBothCountsAndAllOtherHeaderBytes(int tag)
    {
        KwdChunk source=TriggerChunk(KwdThingEditingTests.Trigger(213,13).Concat(KwdThingEditingTests.Trigger(214,14)).ToArray());
        Array.Fill(source.HeaderBlock,(byte)0x5A,8,24);
        var editor=Set(source); byte[] before=editor.ToBytes("triggers.kld");
        byte[] bytes=KwdThingEditingTests.Trigger(tag,15); TriggerRecord row=Read(bytes);
        editor.InsertTrigger(new("triggers.kld",0,1),row,new KwdRecordImage(KwdRecordType.Triggers,bytes,tag));
        KwdChunk result=KwdThingEditingTests.Reload(editor.ToBytes("triggers.kld"));
        Assert.Equal(3,result.ItemCount); Assert.Equal(72,result.Content.Length);
        Assert.Equal(tag==213?2:1,BinaryPrimitives.ReadInt32LittleEndian(result.HeaderBlock));
        Assert.Equal(tag==214?2:1,BinaryPrimitives.ReadInt32LittleEndian(result.HeaderBlock.AsSpan(4)));
        Assert.Equal(source.HeaderBlock[8..],result.HeaderBlock[8..]);
        Assert.Equal(source.ContainerHeader!.ChunkEndOffset+24,result.ContainerHeader!.ChunkEndOffset);
        Assert.Equal(source.ContainerHeader.DeclaredContentSize+24,result.ContainerHeader.DeclaredContentSize);
        Assert.Equal(new ushort[]{13,15,14},KwdTriggers.Parse([result]).Select(r=>r.TriggerId));
        editor.RemoveTrigger(new("triggers.kld",0,1)); Assert.Equal(before,editor.ToBytes("triggers.kld"));
    }

    [Theory]
    [InlineData("next")]
    [InlineData("child")]
    [InlineData("level")]
    [InlineData("thing")]
    [InlineData("member")]
    [InlineData("player")]
    public void ReferencedNodesCannotBeRemoved(string reference)
    {
        KwdChunk level=KwdLevelFileSetTests.Level(["triggers.kld","things.kld","players.kld"]);
        if(reference=="level") BinaryPrimitives.WriteUInt16LittleEndian(level.Content.AsSpan(4480),13);
        byte[] linked=KwdThingEditingTests.Trigger(213,14,next:reference=="next"?(ushort)13:(ushort)0,child:reference=="child"?(ushort)13:(ushort)0);
        byte[] thing=reference=="member" ? new byte[555] : new byte[24];
        if(reference=="member") BinaryPrimitives.WriteUInt16LittleEndian(thing.AsSpan(43+24),13);
        if(reference=="thing") BinaryPrimitives.WriteUInt16LittleEndian(thing.AsSpan(20),13);
        KwdChunk player=KwdLevelFileSetTests.Fixture(KwdRecordType.Players,new byte[205],1);
        PlayerDef playerRow=KwdPlayers.Parse([player]).Single();
        if(reference=="player") player=player with { Content=KwdPlayers.WriteRecord(playerRow with { TriggerId=13 },new KwdRecordImage(KwdRecordType.Players,player.Content)) };
        var editor=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> {
            ["level.kwd"]=[level], ["triggers.kld"]=[TriggerChunk(KwdThingEditingTests.Trigger(213,13).Concat(linked).ToArray())],
            ["things.kld"]=[KwdLevelFileSetTests.Fixture(KwdRecordType.Things,KwdThingsWriterTests.Image(reference=="member"?201:194,thing).ToBytes(),1)],
            ["players.kld"]=[player] });
        byte[] original=editor.ToBytes("triggers.kld");
        Assert.Throws<InvalidDataException>(()=>editor.RemoveTrigger(new("triggers.kld",0,0)));
        Assert.Equal(original,editor.ToBytes("triggers.kld"));
    }

    [Fact]
    public void DuplicateReservedLinkedAndPreviouslyReferencedNewIdsAreRefused()
    {
        var editor=Set(TriggerChunk(KwdThingEditingTests.Trigger(213,13,next:14)));
        byte[] original=editor.ToBytes("triggers.kld");
        foreach(byte[] bytes in new[]{KwdThingEditingTests.Trigger(213,13),KwdThingEditingTests.Trigger(213,0),
            KwdThingEditingTests.Trigger(213,14),KwdThingEditingTests.Trigger(214,15,next:13),KwdThingEditingTests.Trigger(213,15,child:13)})
            Assert.Throws<InvalidDataException>(()=>editor.InsertTrigger(new("triggers.kld",0,1),Read(bytes),
                new KwdRecordImage(KwdRecordType.Triggers,bytes,BinaryPrimitives.ReadInt32LittleEndian(bytes))));
        Assert.Throws<InvalidDataException>(()=>editor.EditRecord<TriggerRecord>(new("triggers.kld",0,0),r=>r with { NextTriggerId=15 }));
        Assert.Equal(original,editor.ToBytes("triggers.kld"));
        var incomplete=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> { ["triggers.kld"]=[TriggerChunk(KwdThingEditingTests.Trigger(213,13))] });
        Assert.Throws<InvalidDataException>(()=>incomplete.RemoveTrigger(new("triggers.kld",0,0)));
        var duplicate=Set(TriggerChunk(KwdThingEditingTests.Trigger(213,13).Concat(KwdThingEditingTests.Trigger(214,13)).ToArray()));
        Assert.Throws<InvalidDataException>(()=>duplicate.RemoveTrigger(new("triggers.kld",0,0)));
    }

    [InstallFact]
    public void EveryShippedLevelSupportsExplicitDetachedTemplatesIncludingEmptyTriggerChunks()
    {
        foreach(string level in Directory.GetFiles(Path.Combine(Dk2InstallGate.Path,"Data","editor","maps"),"*.kwd"))
        {
            var editor=KwdLevelFileSet.Load(level,Dk2InstallGate.Path);
            string file=editor.Files.Single(f=>f.EndsWith("Triggers.kld",StringComparison.OrdinalIgnoreCase));
            KwdChunk chunk=editor.Chunks(file).Single(c=>c.Type==KwdRecordType.Triggers);
            var rows=KwdTriggers.Parse([chunk]);
            byte[] fallback=KwdThingEditingTests.Trigger(213,1);
            var image=rows.Count==0 ? new KwdRecordImage(KwdRecordType.Triggers,fallback,213) : KwdRecordRoundTrip.Split(chunk)[0];
            TriggerRecord prototype=rows.Count==0 ? Read(fallback) : rows[0];
            byte[] original=editor.ToBytes(file);
            var used=editor.Files.SelectMany(f=>editor.Chunks(f))
                .Where(c=>c.Type is KwdRecordType.Triggers or KwdRecordType.Things or KwdRecordType.Players or KwdRecordType.Level)
                .SelectMany(c=>KwdTypedRecords.Read(c)).SelectMany(ReferenceIds).ToHashSet();
            ushort fresh=Enumerable.Range(1,ushort.MaxValue).Select(i=>(ushort)i).First(id=>!used.Contains(id));
            TriggerRecord row=prototype with { TriggerId=fresh,NextTriggerId=0,ChildTriggerId=0 };
            editor.InsertTrigger(new(file,0,rows.Count),row,image);
            Assert.Equal(rows.Count+1,KwdThingEditingTests.Reload(editor.ToBytes(file)).ItemCount);
            editor.RemoveTrigger(new(file,0,rows.Count)); Assert.Equal(original,editor.ToBytes(file));
        }
    }

    private static KwdLevelFileSet Set(KwdChunk triggers)=>KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> {
        ["level.kwd"]=[KwdLevelFileSetTests.Level(["triggers.kld"])],["triggers.kld"]=[triggers] });

    private static KwdChunk TriggerChunk(byte[] content)
    {
        KwdChunk chunk=KwdLevelFileSetTests.Fixture(KwdRecordType.Triggers,content,content.Length/24);
        var records=KwdTaggedRecords.Split(chunk);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.HeaderBlock,records.Count(r=>r.Tag==213));
        BinaryPrimitives.WriteInt32LittleEndian(chunk.HeaderBlock.AsSpan(4),records.Count(r=>r.Tag==214));
        return chunk;
    }

    private static TriggerRecord Read(byte[] bytes)=>KwdTriggers.Parse([TriggerChunk(bytes)]).Single();

    private static IEnumerable<ushort> ReferenceIds(object row)
    {
        if(row.GetType().GetProperty("TriggerId")?.GetValue(row) is ushort id) yield return id;
        if(row is TriggerRecord trigger) { yield return trigger.NextTriggerId; yield return trigger.ChildTriggerId; }
        if(row is HeroPartyThing party)
            foreach(GoodCreatureThing member in party.Members) yield return member.TriggerId;
    }
}
