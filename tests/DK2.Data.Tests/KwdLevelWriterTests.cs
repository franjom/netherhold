// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdLevelWriterTests
{
    [Fact]
    public void EveryDecodedScalarRoundTripsWithoutChangingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.Level,25683+8+72+8,
            bytes=>KwdLevel.Parse([Chunk(bytes,25683)])!,
            (row,image)=>KwdLevel.WriteRecord(row,Chunk(image.ToBytes(),25683)),
            "Name:0:128 Description:80:2048 Author:880:128 Email:900:128 Information:980:2048 " +
            "TriggerId:1180:2 TicksPerSecond:1182:2 Unknown_1184:1184:520 Flags:638C:2 SoundCategory:638E:32 " +
            "TalismanPieces:63AE:1 PreviousRewards:63AF:4 NextRewards:63B3:4 SoundTrack:63B7:1 TextTableId:63B8:1 " +
            "TitleTextId:63B9:2 PlotTextId:63BB:2 DebriefTextId:63BD:2 ObjectiveTextId:63BF:2 Unknown_63B9:63C1:2 " +
            "SubObjective1TextId:63C3:2 SubObjective2TextId:63C5:2 SubObjective3TextId:63C7:2 SpecialLevelIndex:63C9:2 " +
            "IntroductionOverrideObjects:63CB:8 IntroductionOverrideTextIds:63D3:16 TerrainPath:63E3:32 Unknown_6403:6403:80",
            ["Messages","Paths","UnknownPathTail"]);

    [Theory]
    [InlineData(25603)]
    [InlineData(25683)]
    public void EveryMessagePathAndTailSlotRetainsItsOwnBytes(int metadata)
    {
        byte[] bytes=Enumerable.Repeat((byte)0x5A,metadata+8+72+8).ToArray();
        KwdChunk chunk=Chunk(bytes,metadata); LevelDef row=KwdLevel.Parse([chunk])!;
        Assert.Equal(bytes,KwdLevel.WriteRecord(row,chunk));
        for(int slot=0;slot<512;slot++)
        {
            string[] messages=row.Messages.ToArray(); messages[slot]="changed é";
            byte[] edited=KwdLevel.WriteRecord(row with { Messages=messages },chunk);
            Assert.Equal(messages,KwdLevel.Parse([chunk with { Content=edited }])!.Messages);
            Outside(bytes,edited,5004+40*slot,40);
        }
        for(int slot=0;slot<2;slot++)
        {
            uint[] tail=row.UnknownPathTail.ToArray(); tail[slot]=13;
            byte[] edited=KwdLevel.WriteRecord(row with { UnknownPathTail=tail },chunk);
            Assert.Equal(tail,KwdLevel.Parse([chunk with { Content=edited }])!.UnknownPathTail);
            Outside(bytes,edited,metadata+80+4*slot,4);
        }
        KwdScalarOverlayChecks.Check(KwdRecordType.Level,72,ReadPath,KwdLevel.WritePathRecord,
            "RecordType:0:4 Unknown_0004:4:4 Path:8:64");
    }

    [Fact]
    public void GlobalsIsWrittenOnlyAsTheKnownLevelPathDiscriminator()
    {
        byte[] path=new byte[72]; path[8]=(byte)'x';
        KwdLevelFilePath row=ReadPath(path);
        Assert.Equal(KwdRecordType.Globals,row.RecordType);
        byte[] result=KwdLevel.WritePathRecord(row with { Path="GlobalVariables.kwd" },new KwdRecordImage(KwdRecordType.Level,path));
        Assert.Equal("GlobalVariables.kwd",ReadPath(result).Path);
        Assert.Throws<InvalidDataException>(()=>KwdLevel.WritePathRecord(row,new KwdRecordImage(KwdRecordType.Globals,path)));
    }

    [Fact]
    public void ShapeChangesAndStringsThatCannotRoundTripAreRefused()
    {
        KwdChunk chunk=Chunk(new byte[25603+8+72+8],25603); LevelDef row=KwdLevel.Parse([chunk])!;
        Assert.Throws<InvalidDataException>(()=>KwdLevel.WriteRecord(row with { Paths=[] },chunk));
        Assert.Throws<InvalidDataException>(()=>KwdLevel.WriteRecord(row with { UnknownPathTail=[] },chunk));
        Assert.Throws<InvalidDataException>(()=>KwdLevel.WriteRecord(row with { Messages=[] },chunk));
        Assert.Throws<InvalidDataException>(()=>KwdLevel.WriteRecord(row with { Name=" padded " },chunk));
        Assert.Throws<InvalidDataException>(()=>KwdLevel.WriteRecord(row with { Name="a\0b" },chunk));
        Assert.Throws<InvalidDataException>(()=>KwdLevel.WriteRecord(row with { Name=new string('a',65) },chunk));
        Assert.Throws<InvalidDataException>(()=>KwdLevel.WriteRecord(row with { Name="\uD800" },chunk));
        Assert.Throws<InvalidDataException>(()=>KwdLevel.WriteRecord(row with { Unknown_6403=new byte[80] },chunk));
    }

    [InstallFact]
    public void EveryShippedLevelTypedOverlayIsByteIdentical()
    {
        string[] files=Directory.GetFiles(Path.Combine(Dk2InstallGate.Path,"Data","editor","maps"),"*.kwd");
        Assert.Equal(66,files.Length);
        foreach(string file in files)
        foreach(KwdChunk chunk in KwdReader.ReadFile(file).Where(c=>c.Type==KwdRecordType.Level))
            Assert.Equal(chunk.Content,KwdLevel.WriteRecord(KwdLevel.Parse([chunk])!,chunk));
    }

    private static KwdLevelFilePath ReadPath(byte[] path)
    {
        byte[] bytes=new byte[25603+8+72+8]; path.CopyTo(bytes,25611);
        return KwdLevel.Parse([Chunk(bytes,25603)])!.Paths.Single();
    }

    private static KwdChunk Chunk(byte[] bytes,int metadata)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(metadata),222);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(metadata+4),80);
        byte[] header=new byte[28]; BinaryPrimitives.WriteUInt16LittleEndian(header,1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2),2);
        return new KwdChunk { Type=KwdRecordType.Level,ItemCount=1,Content=bytes,HeaderBlock=header };
    }

    private static void Outside(byte[] source,byte[] edited,int at,int size)
    {
        for(int i=0;i<source.Length;i++) if(i<at || i>=at+size) Assert.Equal(source[i],edited[i]);
    }
}
