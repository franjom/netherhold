// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Editing;

public sealed partial class KwdLevelFileSet
{
    public void InsertThing(KwdRecordLocation location,ThingRecord row,KwdRecordImage template)
    {
        KwdChunk chunk=RequireType(location,KwdRecordType.Things);
        byte[] bytes=KwdThings.WriteRecord(row,template);
        foreach(KwdTriggerId root in Roots(row))
        {
            if(root==KwdTriggerId.None) continue;
            RequireCompleteManifest();
            if(TriggerRows().Count(t=>new KwdTriggerId(t.TriggerId)==root)!=1)
                throw new InvalidDataException($"New thing trigger root {root.Value} does not resolve uniquely.");
        }
        InsertTagged(location,chunk,new KwdRecordImage(KwdRecordType.Things,bytes,row.Tag));
    }

    public void RemoveThing(KwdRecordLocation location)
    {
        KwdChunk chunk=RequireType(location,KwdRecordType.Things);
        RemoveTagged(location,chunk);
    }

    public void InsertTrigger(KwdRecordLocation location,TriggerRecord row,KwdRecordImage template)
    {
        KwdChunk chunk=RequireType(location,KwdRecordType.Triggers);
        RequireCompleteManifest();
        var id=new KwdTriggerId(row.TriggerId);
        if(id==KwdTriggerId.None || TriggerRows().Any(t=>new KwdTriggerId(t.TriggerId)==id)
            || ReferenceIds().Contains(id) || new KwdTriggerId(row.NextTriggerId)!=KwdTriggerId.None
            || new KwdTriggerId(row.ChildTriggerId)!=KwdTriggerId.None)
            throw new InvalidDataException("New triggers require a unique, unreferenced nonzero id and no tree links.");
        byte[] bytes=KwdTriggers.WriteRecord(row,template);
        InsertTagged(location,chunk,new KwdRecordImage(KwdRecordType.Triggers,bytes,row.Tag));
    }

    public void RemoveTrigger(KwdRecordLocation location)
    {
        KwdChunk chunk=RequireType(location,KwdRecordType.Triggers);
        RequireCompleteManifest();
        var id=new KwdTriggerId(ReadRecord<TriggerRecord>(location).TriggerId);
        if(id==KwdTriggerId.None || TriggerRows().Count(t=>new KwdTriggerId(t.TriggerId)==id)!=1)
            throw new InvalidDataException("Only uniquely identified trigger nodes can be removed.");
        if(ReferenceIds().Contains(id))
            throw new InvalidDataException($"Trigger {id.Value} is referenced by a tree link or root and cannot be removed.");
        RemoveTagged(location,chunk);
    }

    private IEnumerable<KwdTriggerId> ReferenceIds()
    {
        foreach(TriggerRecord trigger in TriggerRows())
        {
            yield return new KwdTriggerId(trigger.NextTriggerId);
            yield return new KwdTriggerId(trigger.ChildTriggerId);
        }
        foreach(KwdChunk chunk in _files.Values.SelectMany(c=>c)
            .Where(c=>c.Type is KwdRecordType.Things or KwdRecordType.Players or KwdRecordType.Level))
        foreach(object row in KwdTypedRecords.Read(chunk))
        foreach(KwdTriggerId root in Roots(row)) yield return root;
    }

    private KwdChunk RequireType(KwdRecordLocation location,KwdRecordType type)
    {
        KwdChunk chunk=Chunk(location);
        if(chunk.Type!=type) throw new InvalidDataException($"This operation requires a {type} chunk.");
        return chunk;
    }

    private void InsertTagged(KwdRecordLocation location,KwdChunk chunk,KwdRecordImage image)
    {
        var images=KwdRecordRoundTrip.Split(chunk);
        if((uint)location.RecordIndex>(uint)images.Count) throw new ArgumentOutOfRangeException(nameof(location));
        images.Insert(location.RecordIndex,image);
        Replace(location,TaggedChunk(chunk,images));
    }

    private void RemoveTagged(KwdRecordLocation location,KwdChunk chunk)
    {
        var images=KwdRecordRoundTrip.Split(chunk);
        if((uint)location.RecordIndex>=(uint)images.Count) throw new ArgumentOutOfRangeException(nameof(location));
        images.RemoveAt(location.RecordIndex);
        Replace(location,TaggedChunk(chunk,images));
    }

    private static KwdChunk TaggedChunk(KwdChunk source,IReadOnlyList<KwdRecordImage> images)
    {
        byte[] header=source.HeaderBlock.ToArray();
        if(source.Type==KwdRecordType.Things) BinaryPrimitives.WriteInt32LittleEndian(header,images.Count);
        else if(source.Type==KwdRecordType.Triggers)
        {
            BinaryPrimitives.WriteInt32LittleEndian(header,images.Count(i=>i.Tag==213));
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4),images.Count(i=>i.Tag==214));
        }
        else throw new InvalidDataException("The chunk has no established tagged editing layout.");
        KwdChunk result=source with { HeaderBlock=header,ItemCount=images.Count,Content=images.SelectMany(i=>i.ToBytes()).ToArray() };
        _=KwdTaggedRecords.Split(result);
        return result;
    }

    private IEnumerable<TriggerRecord> TriggerRows() => _files.Values.SelectMany(c=>c)
        .Where(c=>c.Type==KwdRecordType.Triggers).SelectMany(c=>KwdTriggers.Parse([c]));

    private static IEnumerable<KwdTriggerId> Roots(object row)
    {
        if(RootTrigger(row) is KwdTriggerId root) yield return root;
        if(row is HeroPartyThing party)
            foreach(GoodCreatureThing member in party.Members) yield return new KwdTriggerId(member.TriggerId);
    }

    private void RequireCompleteManifest()
    {
        KwdChunk[] levels=_files.Values.SelectMany(c=>c).Where(c=>c.Type==KwdRecordType.Level).ToArray();
        if(levels.Length!=1) throw new InvalidDataException("Trigger-reference operations require one complete LEVEL file set.");
        foreach(KwdLevelFilePath path in KwdLevel.Parse(levels)!.Paths)
        {
            string relative=Normalize(path.Path);
            if(Path.GetExtension(relative).Length==0) relative+=".kwd";
            if(!_files.ContainsKey(relative)) throw new InvalidDataException("Trigger-reference operations require every LEVEL file reference to be loaded.");
        }
    }
}
