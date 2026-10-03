// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Tools;

namespace DK2.Tools.Tests;

public class KwdRewriteCommandTests
{
    [Fact]
    public void SingleFileCommandReportsIdentityAndWritesTheFile()
    {
        string root=Path.Combine(Path.GetTempPath(),"dk2-rewrite-command-"+Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root); string input=Path.Combine(root,"map.kld"),output=Path.Combine(root,"out");
            byte[] header=new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(header,1);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4),1);
            KwdChunk chunk=new() { Type=KwdRecordType.Map,ItemCount=0,HeaderBlock=header,Content=[1,2,3,4],
                ContainerHeader=new(40,(uint)KwdRecordType.Map+2,4,4) };
            byte[] source=KwdWriter.ToBytes([chunk]); File.WriteAllBytes(input,source);
            using var stdout=new StringWriter(); using var stderr=new StringWriter();
            Assert.Equal(0,KwdRewriteCommand.Run([input,output],stdout,stderr));
            Assert.Contains("map.kld: identical",stdout.ToString()); Assert.Equal("",stderr.ToString());
            Assert.Equal(source,File.ReadAllBytes(Path.Combine(output,"map.kld")));
            Assert.Equal(2,KwdRewriteCommand.Run([input,root],stdout,stderr));
            Assert.Contains("overwrite",stderr.ToString()); Assert.Equal(source,File.ReadAllBytes(input));
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public void MissingArgumentsAndMissingInputReturnAnActionableError()
    {
        using var stdout=new StringWriter(); using var stderr=new StringWriter();
        Assert.Equal(2,KwdRewriteCommand.Run([],stdout,stderr));
        Assert.Contains("kwd rewrite <file|dir> <outdir>",stderr.ToString());
        Assert.Equal(2,KwdRewriteCommand.Run([Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".kwd"),Path.GetTempPath()],stdout,stderr));
        Assert.Contains("does not exist",stderr.ToString());
    }

    [Fact]
    public void DifferenceReportIncludesFileByteChunkRecordFieldAndBothValues()
    {
        var file=new KwdRewriteAudit.FileResult("doors.kwd",1,2,
            new(123,0,KwdRecordType.Doors,1,"Health",13,14));
        string line=KwdRewriteCommand.Format(file);
        Assert.Contains("doors.kwd",line); Assert.Contains("123",line); Assert.Contains("chunk 0 (Doors)",line);
        Assert.Contains("record 1",line); Assert.Contains("Health",line); Assert.Contains("0D -> 0E",line);
        Assert.Contains("EOF",KwdRewriteCommand.Format(file with { Difference=new(123,null,null,null,"fileLength",13,null) }));
    }
}
