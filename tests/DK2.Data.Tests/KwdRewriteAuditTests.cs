// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdRewriteAuditTests
{
    [Fact]
    public void DirectoryRewriteRetainsRelativePathsAndUsesTypedRecords()
    {
        string root=Path.Combine(Path.GetTempPath(),"dk2-rewrite-"+Guid.NewGuid());
        try
        {
            string input=Path.Combine(root,"input"),output=Path.Combine(root,"output");
            Directory.CreateDirectory(Path.Combine(input,"nested"));
            byte[] bytes=KwdWriter.ToBytes([DoorChunk()]);
            File.WriteAllBytes(Path.Combine(input,"nested","doors.KWD"),bytes);
            File.WriteAllText(Path.Combine(input,"ignored.txt"),"not a game file");
            var result=KwdRewriteAudit.Run(input,output);
            var file=Assert.Single(result.Files);
            Assert.Null(file.Difference); Assert.Equal(1,file.Chunks); Assert.Equal(2,file.Records);
            Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(output,"nested","doors.KWD")));
            Assert.False(File.Exists(Path.Combine(output,"ignored.txt")));
            Assert.Throws<InvalidDataException>(()=>KwdRewriteAudit.Run(input,Path.Combine(input,"out")));
            Assert.Throws<InvalidDataException>(()=>KwdRewriteAudit.Run(Path.Combine(input,"nested","doors.KWD"),Path.Combine(input,"nested")));
            Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(input,"nested","doors.KWD")));
        }
        finally { Directory.Delete(root,true); }
    }

    [Theory]
    [InlineData(8,"chunkEndOffset",null)]
    [InlineData(20,"itemCount",null)]
    [InlineData(24,"unknown",null)]
    [InlineData(26,"unknown",null)]
    [InlineData(28,"created",null)]
    [InlineData(38,"modified",null)]
    [InlineData(48,"checkTwo",null)]
    [InlineData(52,"declaredContentSize",null)]
    [InlineData(56+616+0x22C,"Health",1)]
    public void FirstDifferenceNamesTheHeaderOrDecodedField(int offset,string field,int? record)
    {
        KwdChunk chunk=DoorChunk(); byte[] source=KwdWriter.ToBytes([chunk]),actual=(byte[])source.Clone();
        actual[offset]^=1;
        var diff=KwdRewriteAudit.DescribeDifference(source,actual,[chunk])!;
        Assert.Equal(offset,diff.Offset); Assert.Equal(0,diff.ChunkIndex);
        Assert.Equal(KwdRecordType.Doors,diff.ChunkType); Assert.Equal(record,diff.RecordIndex);
        Assert.Contains(field,diff.Field); Assert.Equal(source[offset],diff.Expected); Assert.Equal(actual[offset],diff.Actual);
    }

    [Fact]
    public void DiagnosticsLocateTheSecondChunkAndTaggedRecordHeader()
    {
        byte[] body=KwdThingsWriterTests.Image(194,new byte[24]).ToBytes();
        KwdChunk chunk=KwdLevelFileSetTests.Fixture(KwdRecordType.Things,body.Concat(body).ToArray(),2);
        KwdChunk first=DoorChunk(); byte[] source=KwdWriter.ToBytes([first,chunk]),actual=(byte[])source.Clone();
        int offset=KwdWriter.ToBytes([first]).Length+56+32;
        actual[offset]^=1;
        var diff=KwdRewriteAudit.DescribeDifference(source,actual,[first,chunk])!;
        Assert.Equal(1,diff.ChunkIndex); Assert.Equal(1,diff.RecordIndex); Assert.Equal("tag",diff.Field);
    }

    [Fact]
    public void UndecodedCreatureTailAndFileLengthAreReportedHonestly()
    {
        KwdChunk chunk=KwdLevelFileSetTests.Fixture(KwdRecordType.Creatures,new byte[5537],1);
        byte[] source=KwdWriter.ToBytes([chunk]),actual=(byte[])source.Clone(); actual[56+5449]=1;
        var diff=KwdRewriteAudit.DescribeDifference(source,actual,[chunk])!;
        Assert.Equal(0,diff.RecordIndex); Assert.StartsWith("raw",diff.Field);
        diff=KwdRewriteAudit.DescribeDifference(source,source[..^1],[chunk])!;
        Assert.Equal(source.Length-1,diff.Offset); Assert.Null(diff.Actual);
        diff=KwdRewriteAudit.DescribeDifference(source,source.Concat(new byte[]{13}).ToArray(),[chunk])!;
        Assert.Equal(source.Length,diff.Offset); Assert.Equal("fileLength",diff.Field); Assert.Null(diff.Expected);
        Assert.Null(KwdRewriteAudit.DescribeDifference(source,source,[chunk]));
    }

    [InstallFact]
    public void SharedRewriteAuditWritesEveryShippedFileByteIdentically()
    {
        string output=Path.Combine(Path.GetTempPath(),"dk2-rewrite-corpus-"+Guid.NewGuid());
        try
        {
            string input=Path.Combine(Dk2InstallGate.Path,"Data","editor");
            var result=KwdRewriteAudit.Run(input,output);
            Assert.Equal(408,result.Files.Count); Assert.Equal(409,result.Files.Sum(f=>f.Chunks));
            Assert.Equal(371126,result.Files.Sum(f=>f.Records));
            Assert.Equal(17,result.RecordCounts.Count);
            foreach(var file in result.Files)
            {
                Assert.Null(file.Difference);
                Assert.Equal(File.ReadAllBytes(Path.Combine(input,file.RelativePath)),File.ReadAllBytes(Path.Combine(output,file.RelativePath)));
            }
        }
        finally { if(Directory.Exists(output)) Directory.Delete(output,true); }
    }

    private static KwdChunk DoorChunk()=>KwdLevelFileSetTests.Fixture(KwdRecordType.Doors,new byte[616*2],2);
}
