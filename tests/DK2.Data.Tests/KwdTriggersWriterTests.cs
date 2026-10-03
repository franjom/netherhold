// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdTriggersWriterTests
{
    [Theory]
    [InlineData(213)]
    [InlineData(214)]
    public void EveryDecodedFieldAndEveryParameterShapeRoundTrips(int tag)
    {
        string fields="Body:0:8 TriggerId:8:2 NextTriggerId:A:2 ChildTriggerId:C:2 RepeatCount:F:1";
        fields+=tag==213 ? " BodyValue:4:4" : " BodyWord4:4:2 BodyWord6:6:2 BodyDword0:0:4 BodyDword4:4:4";
        KwdScalarOverlayChecks.Check(KwdRecordType.Triggers,16,bytes=>Read(tag,bytes),
            (row,image)=>KwdTriggers.WriteRecord(row,Image(tag,image.ToBytes()))[8..],fields,["Type","Parameters"]);
        for(int type=0;type<=255;type++)
        {
            byte[] payload=Enumerable.Repeat((byte)0x5A,16).ToArray(); payload[14]=(byte)type;
            TriggerRecord original=Read(tag,payload);
            foreach(string name in original.Parameters.Keys)
            {
                var parameters=original.Parameters.ToDictionary(p=>p.Key,p=>p.Value); parameters[name]=13;
                byte[] written=KwdTriggers.WriteRecord(original with { Parameters=parameters },Image(tag,payload));
                Assert.Equal(13L,Read(tag,written[8..]).Parameters[name]);
                Assert.Equal(payload[8..],written[16..]);
                Assert.Equal(Image(tag,payload).ToBytes()[..8],written[..8]);
            }
        }
    }

    [Fact]
    public void ConflictingAliasesUnknownParametersAndTypeChangesAreRefused()
    {
        byte[] payload=new byte[16]; payload[14]=1;
        var row=(ActionTriggerRecord)Read(214,payload);
        Assert.Throws<InvalidDataException>(()=>KwdTriggers.WriteRecord(row with { BodyWord4=1,BodyDword4=2 },Image(214,payload)));
        Assert.Throws<InvalidDataException>(()=>KwdTriggers.WriteRecord(row with { BodyWord4=1,BodyDword4=65536 },Image(214,payload)));
        Assert.Throws<InvalidDataException>(()=>KwdTriggers.WriteRecord(row with { Type=2 },Image(214,payload)));
        Assert.Throws<InvalidDataException>(()=>KwdTriggers.WriteRecord(row with { Parameters=new Dictionary<string,long>{{"invented",1}} },Image(214,payload)));
        var outOfRange=row.Parameters.ToDictionary(p=>p.Key,p=>p.Value); outOfRange["CreatureId"]=256;
        Assert.Throws<InvalidDataException>(()=>KwdTriggers.WriteRecord(row with { Parameters=outOfRange },Image(214,payload)));
        Assert.Throws<InvalidDataException>(()=>KwdTriggers.WriteRecord(row with { Body=new byte[7] },Image(214,payload)));
    }

    [Fact]
    public void AgreeingAliasesMergeAndRawBodyEditsRetainUnchangedAliases()
    {
        byte[] payload=new byte[16]; payload[14]=1;
        var row=(ActionTriggerRecord)Read(214,payload);
        byte[] result=KwdTriggers.WriteRecord(row with { BodyWord4=13,BodyDword4=13 },Image(214,payload));
        Assert.Equal(13,Read(214,result[8..]).Parameters["PosX"]);
        byte[] body=[13,0,0,0,0,0,0,0];
        result=KwdTriggers.WriteRecord(row with { Body=body },Image(214,payload));
        Assert.Equal(13,Read(214,result[8..]).Parameters["CreatureId"]);
    }

    [InstallFact]
    public void EveryShippedTriggerTypedOverlayIsByteIdentical()
    {
        int count=0;
        foreach(string file in Directory.EnumerateFiles(Path.Combine(Dk2InstallGate.Path,"Data"),"*",SearchOption.AllDirectories)
            .Where(p=>p.EndsWith(".kwd",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".kld",StringComparison.OrdinalIgnoreCase)))
        foreach(KwdChunk chunk in KwdReader.ReadFile(file).Where(c=>c.Type==KwdRecordType.Triggers))
        {
            var rows=KwdTriggers.Parse([chunk]); var images=KwdRecordRoundTrip.Split(chunk);
            for(int i=0;i<rows.Count;i++) Assert.Equal(images[i].ToBytes(),KwdTriggers.WriteRecord(rows[i],images[i]));
            count+=rows.Count;
        }
        Assert.Equal(9161,count);
    }

    private static TriggerRecord Read(int tag,byte[] payload)=>KwdTriggers.Parse([
        new KwdChunk { Type=KwdRecordType.Triggers,ItemCount=1,HeaderBlock=[],Content=Image(tag,payload).ToBytes() }]).Single();

    private static KwdRecordImage Image(int tag,byte[] payload)
    {
        byte[] bytes=new byte[24]; BinaryPrimitives.WriteInt32LittleEndian(bytes,tag);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),16); payload.CopyTo(bytes,8);
        return new KwdRecordImage(KwdRecordType.Triggers,bytes,tag);
    }
}
