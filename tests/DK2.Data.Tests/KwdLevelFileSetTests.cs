// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Editing;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdLevelFileSetTests
{
    [Fact]
    public void TypedEditsAreIsolatedAndSaveReloadWithOtherBytesUnchanged()
    {
        byte[] payload=Enumerable.Repeat((byte)0x5A,24).ToArray();
        KwdChunk source=Fixture(KwdRecordType.Things,KwdThingsWriterTests.Image(194,payload).ToBytes(),1);
        var editor=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> { ["things.kld"]=[source] });
        var location=new KwdRecordLocation("things.kld",ChunkIndex:0,RecordIndex:0);
        editor.EditRecord<ObjectThing>(location,row=>row with { MoneyAmount=13 });
        Assert.Equal(13,editor.ReadRecord<ObjectThing>(location).MoneyAmount);
        byte[] edited=editor.Chunks("things.kld")[0].Content;
        for(int i=0;i<edited.Length;i++) if(i<24 || i>=28) Assert.Equal(source.Content[i],edited[i]);
        Assert.Equal(payload,source.Content[8..]);
        var snapshot=editor.Chunks("things.kld"); snapshot[0].Content[0]=0;
        Assert.Equal(194,editor.Chunks("things.kld")[0].Content[0]);
        string root=Path.Combine(Path.GetTempPath(),"dk2-writers-"+Guid.NewGuid().ToString("N"));
        try
        {
            editor.Save(root);
            Assert.Equal(editor.ToBytes("things.kld"),File.ReadAllBytes(Path.Combine(root,"things.kld")));
            Assert.Equal(13,((ObjectThing)KwdThings.Parse(KwdReader.ReadFile(Path.Combine(root,"things.kld"))).Single()).MoneyAmount);
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }

    [Fact]
    public void LoadFollowsLevelManifestAndRetainsEveryFileByte()
    {
        string root=Path.Combine(Path.GetTempPath(),"dk2-level-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            KwdChunk level=Level(["defs","things.kld"]);
            byte[] doors=KwdWriter.ToBytes([Fixture(KwdRecordType.Doors,new byte[616],1)]);
            byte[] things=KwdWriter.ToBytes([Fixture(KwdRecordType.Things,KwdThingsWriterTests.Image(194,new byte[24]).ToBytes(),1)]);
            File.WriteAllBytes(Path.Combine(root,"level.kwd"),KwdWriter.ToBytes([level]));
            File.WriteAllBytes(Path.Combine(root,"defs.kwd"),doors);
            File.WriteAllBytes(Path.Combine(root,"things.kld"),things);
            var editor=KwdLevelFileSet.Load(Path.Combine(root,"level.kwd"),root);
            Assert.Equal(3,editor.Files.Count);
            Assert.Equal(doors,editor.ToBytes("defs.kwd")); Assert.Equal(things,editor.ToBytes("things.kld"));
            editor.EditRecord<DoorDef>(new("defs.kwd",0,0),row=>row with { Health=13 });
            Assert.Equal(13,editor.ReadRecord<DoorDef>(new("defs.kwd",0,0)).Health);
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }

    [Fact]
    public void ReferenceChangesInvalidLocationsAndEscapingPathsAreRefused()
    {
        var editor=KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> {
            ["things.kld"]=[Fixture(KwdRecordType.Things,KwdThingsWriterTests.Image(194,new byte[24]).ToBytes(),1)] });
        byte[] original=editor.ToBytes("things.kld");
        Assert.Throws<InvalidDataException>(()=>editor.EditRecord<ObjectThing>(new("things.kld",0,0),row=>row with { TriggerId=13 }));
        Assert.Throws<InvalidDataException>(()=>editor.ReadRecord<DoorDef>(new("things.kld",0,0)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>editor.ReadRecord<ObjectThing>(new("things.kld",0,1)));
        Assert.Throws<InvalidDataException>(()=>KwdLevelFileSet.Create(new Dictionary<string,IReadOnlyList<KwdChunk>> { ["../escape.kwd"]=[] }));
        Assert.Equal(original,editor.ToBytes("things.kld"));
    }

    [InstallFact]
    public void EveryShippedLevelFileSetLoadsWithByteIdenticalOutput()
    {
        string[] levels=Directory.GetFiles(Path.Combine(Dk2InstallGate.Path,"Data","editor","maps"),"*.kwd");
        Assert.Equal(66,levels.Length);
        foreach(string level in levels)
        {
            var editor=KwdLevelFileSet.Load(level,Dk2InstallGate.Path);
            foreach(string file in editor.Files) Assert.Equal(File.ReadAllBytes(Path.Combine(Dk2InstallGate.Path,file)),editor.ToBytes(file));
        }
    }

    internal static KwdChunk Fixture(KwdRecordType type,byte[] content,int count,int width=1,int height=1)
    {
        byte[] header=new byte[type==KwdRecordType.Map ? 8 : type==KwdRecordType.Triggers ? 32 : 28];
        if(type==KwdRecordType.Map)
        {
            BinaryPrimitives.WriteInt32LittleEndian(header,width); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4),height);
        }
        else if(type==KwdRecordType.Level) BinaryPrimitives.WriteUInt16LittleEndian(header,(ushort)count);
        else BinaryPrimitives.WriteInt32LittleEndian(header,count);
        return new KwdChunk { Type=type,ItemCount=count,Content=content,HeaderBlock=header,
            ContainerHeader=new KwdContainerHeader((uint)(28+header.Length+content.Length),(uint)type+(type==KwdRecordType.Level?3u:2u),(uint)content.Length,content.Length) };
    }

    internal static KwdChunk Level(string[] paths)
    {
        byte[] content=new byte[25603+8+paths.Length*72];
        BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(25603),222);
        BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(25607),paths.Length*72);
        for(int i=0;i<paths.Length;i++) Encoding.ASCII.GetBytes(paths[i]).CopyTo(content,25611+i*72+8);
        return Fixture(KwdRecordType.Level,content,paths.Length);
    }
}
