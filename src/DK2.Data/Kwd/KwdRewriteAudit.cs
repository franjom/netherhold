// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections;
using System.Reflection;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Typed file rewriting shared by the command and install-gated compatibility test.</summary>
public static class KwdRewriteAudit
{
    public sealed record Difference(int Offset,int? ChunkIndex,KwdRecordType? ChunkType,
        int? RecordIndex,string Field,byte? Expected,byte? Actual);
    public sealed record FileResult(string RelativePath,int Chunks,int Records,Difference? Difference);
    public sealed record Result(IReadOnlyList<FileResult> Files,IReadOnlyDictionary<KwdRecordType,int> RecordCounts);

    public static Result Run(string input,string outputDirectory)
    {
        string source=Path.GetFullPath(input),output=Path.GetFullPath(outputDirectory);
        RefuseLinkedPath(source); RefuseLinkedPath(output);
        bool directory=Directory.Exists(source);
        if(!directory && !File.Exists(source)) throw new FileNotFoundException("Rewrite input does not exist.",source);
        if(directory && Within(output,source)) throw new InvalidDataException("Rewrite output must be outside the input directory.");
        string root=directory ? source : Path.GetDirectoryName(source)!;
        string[] files=directory ? EnumeratePlainFiles(source)
            .Where(IsKwd).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray() : [source];
        if(files.Any(f=>!IsKwd(f))) throw new InvalidDataException("Rewrite accepts KWD/KLD files.");
        foreach(string file in files)
        {
            string destination=Path.Combine(output,Path.GetRelativePath(root,file));
            RefuseLinkedPath(destination);
            if(string.Equals(file,destination,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Rewrite output would overwrite its source file.");
            if(File.Exists(destination) || Directory.Exists(destination))
                throw new InvalidDataException($"Rewrite destination already exists: {destination}");
        }
        var results=new List<FileResult>(); var counts=new Dictionary<KwdRecordType,int>();
        foreach(string file in files)
        {
            byte[] original=File.ReadAllBytes(file);
            using var stream=new MemoryStream(original,false);
            var chunks=KwdReader.Read(stream,file); var rewritten=new List<KwdChunk>(); int records=0;
            foreach(KwdChunk chunk in chunks)
            {
                int count=KwdRecordRoundTrip.Split(chunk).Count;
                records=checked(records+count); counts.TryGetValue(chunk.Type,out int old);
                counts[chunk.Type]=checked(old+count);
                rewritten.Add(KwdTypedRecords.Rewrite(chunk));
            }
            byte[] actual=KwdWriter.ToBytes(rewritten);
            string relative=Path.GetRelativePath(root,file),destination=Path.Combine(output,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using(var target=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                target.Write(actual,0,actual.Length);
            results.Add(new(relative,chunks.Count,records,DescribeDifference(original,actual,chunks)));
        }
        return new(results.ToArray(),counts);
    }

    public static Difference? DescribeDifference(byte[] expected,byte[] actual,IReadOnlyList<KwdChunk> chunks)
    {
        int at=0;
        while(at<expected.Length && at<actual.Length && expected[at]==actual[at]) at++;
        if(at==expected.Length && at==actual.Length) return null;
        byte? before=at<expected.Length ? expected[at] : null,after=at<actual.Length ? actual[at] : null;
        int start=0;
        for(int index=0;index<chunks.Count;index++)
        {
            KwdChunk chunk=chunks[index]; int contentAt=28+chunk.HeaderBlock.Length;
            int length=checked(contentAt+chunk.Content.Length),relative=at-start;
            if(relative<length)
            {
                if(relative<contentAt)
                    return new(at,index,chunk.Type,null,HeaderField(chunk,relative),before,after);
                int recordAt=0,payloadAt=relative-contentAt;
                var images=KwdRecordRoundTrip.Split(chunk);
                for(int record=0;record<images.Count;record++)
                {
                    KwdRecordImage image=images[record];
                    if(payloadAt<recordAt+image.ConsumedLength)
                    {
                        int recordOffset=payloadAt-recordAt;
                        string field=image.Tag is not null && recordOffset<8
                            ? recordOffset<4 ? "tag" : "payloadSize"
                            : DecodedField(chunk,record,payloadAt,after) ?? $"raw +0x{recordOffset:X}";
                        return new(at,index,chunk.Type,record,field,before,after);
                    }
                    recordAt+=image.ConsumedLength;
                }
                return new(at,index,chunk.Type,null,"content",before,after);
            }
            start=checked(start+length);
        }
        return new(at,null,null,null,"fileLength",before,after);
    }

    private static string HeaderField(KwdChunk chunk,int offset)
    {
        if(offset<20) return (offset/4) switch {
            0=>"signature",1=>"byteSize",2=>"chunkEndOffset",3=>"checkOne",_=>"headerBlockSize" };
        int at=offset-20,length=chunk.HeaderBlock.Length;
        if(at>=length) return at<length+4 ? "checkTwo" : "declaredContentSize";
        if(chunk.Type==KwdRecordType.Map) return at<4 ? "width" : at<8 ? "height" : $"header raw +0x{at:X}";
        if(chunk.Type==KwdRecordType.Level && at<4) return at<2 ? "itemCount" : "height";
        if(chunk.Type==KwdRecordType.Triggers)
            return at<4 ? "genericCount" : at<8 ? "actionCount" : at<12 ? "unknown"
                : at<22 ? "created" : at<32 ? "modified" : $"header raw +0x{at:X}";
        return at<4 ? "itemCount" : at<8 ? "unknown" : at<18 ? "created" : at<28 ? "modified" : $"header raw +0x{at:X}";
    }

    private static string? DecodedField(KwdChunk chunk,int record,int offset,byte? value)
    {
        if(value is null) return null;
        byte[] changed=(byte[])chunk.Content.Clone(); changed[offset]=value.Value;
        try
        {
            object before=KwdTypedRecords.Read(chunk)[record];
            object after=KwdTypedRecords.Read(chunk with { Content=changed })[record];
            return ChangedProperty(before,after,"",0);
        }
        catch(Exception e) when(e is InvalidDataException or ArgumentException or OverflowException
            or IndexOutOfRangeException or TargetInvocationException) { return null; }
    }

    private static string? ChangedProperty(object? before,object? after,string path,int depth)
    {
        if(Equals(before,after)) return null;
        if(before is null || after is null || before.GetType()!=after.GetType() || depth>12)
            return path.Length>0 ? path : before is KwdVariableEntry && after is KwdVariableEntry ? "Id" : "recordType";
        if(before is ReadOnlyMemory<byte> oldBytes && after is ReadOnlyMemory<byte> newBytes)
            return oldBytes.Span.SequenceEqual(newBytes.Span) ? null : path;
        Type type=before.GetType();
        if(type.IsPrimitive || type.IsEnum || before is string or decimal) return path;
        if(before is IReadOnlyDictionary<string,long> oldFields && after is IReadOnlyDictionary<string,long> newFields)
        {
            foreach(string key in oldFields.Keys.OrderBy(k=>k,StringComparer.Ordinal))
                if(!newFields.TryGetValue(key,out long value) || oldFields[key]!=value) return path+"."+key;
            return null;
        }
        if(before is IEnumerable oldItems && after is IEnumerable newItems)
        {
            IEnumerator left=oldItems.GetEnumerator(),right=newItems.GetEnumerator(); int index=0;
            while(left.MoveNext())
            {
                if(!right.MoveNext()) return path;
                string? difference=ChangedProperty(left.Current,right.Current,$"{path}[{index++}]",depth+1);
                if(difference is not null) return difference;
            }
            return right.MoveNext() ? path : null;
        }
        foreach(PropertyInfo property in type.GetProperties().Where(p=>p.CanRead && p.GetIndexParameters().Length==0 && p.Name!="ConsumedLength")
            .OrderBy(p=>p.Name=="Parameters" ? 0 : p.Name.StartsWith("Body",StringComparison.Ordinal) ? 2 : 1)
            .ThenBy(p=>p.Name,StringComparer.Ordinal))
        {
            string name=path.Length==0 ? property.Name : path+"."+property.Name;
            string? difference=ChangedProperty(property.GetValue(before),property.GetValue(after),name,depth+1);
            if(difference is not null) return difference;
        }
        return null;
    }

    private static IEnumerable<string> EnumeratePlainFiles(string root)
    {
        var pending=new Stack<string>(); pending.Push(root);
        while(pending.Count>0)
        {
            string directory=pending.Pop(); RefuseLinkedPath(directory);
            foreach(string file in Directory.EnumerateFiles(directory))
                if(IsKwd(file)) { RefuseLinkedPath(file); yield return file; }
            foreach(string child in Directory.EnumerateDirectories(directory)) pending.Push(child);
        }
    }

    private static void RefuseLinkedPath(string path)
    {
        for(string? current=path;current is not null;current=Path.GetDirectoryName(current))
        {
            try
            {
                if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException($"Rewrite refuses linked paths: {current}");
            }
            catch(FileNotFoundException) { }
            catch(DirectoryNotFoundException) { }
        }
    }

    private static bool IsKwd(string file)=>Path.GetExtension(file).Equals(".kwd",StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(file).Equals(".kld",StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path,string root)=>string.Equals(path,root,StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
