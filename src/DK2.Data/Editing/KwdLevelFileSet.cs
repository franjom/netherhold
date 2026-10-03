// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Editing;

public readonly record struct KwdRecordLocation(string RelativePath, int ChunkIndex, int RecordIndex);

/// <summary>Owned file images and transactional typed edits for a level and its referenced data.</summary>
public sealed partial class KwdLevelFileSet
{
    private readonly Dictionary<string,KwdChunk[]> _files=new(StringComparer.OrdinalIgnoreCase);
    private KwdLevelFileSet() { }
    public IReadOnlyList<string> Files => _files.Keys.ToArray();

    public static KwdLevelFileSet Create(IReadOnlyDictionary<string,IReadOnlyList<KwdChunk>> files)
    {
        var result=new KwdLevelFileSet();
        foreach(var file in files)
        {
            string path=Normalize(file.Key);
            KwdChunk[] chunks=file.Value.Select(Clone).ToArray();
            foreach(KwdChunk chunk in chunks)
            {
                if(chunk.ContainerHeader is null) throw new InvalidDataException("Editing requires explicit container templates.");
                _=KwdRecordRoundTrip.Split(chunk);
            }
            if(result._files.ContainsKey(path)) throw new InvalidDataException("Duplicate file path.");
            result._files.Add(path,chunks);
        }
        return result;
    }

    public static KwdLevelFileSet Load(string levelFile,string contentRoot)
    {
        string root=Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var files=new Dictionary<string,IReadOnlyList<KwdChunk>>(StringComparer.OrdinalIgnoreCase);
        string Relative(string full)
        {
            full=Path.GetFullPath(full);
            if(!full.StartsWith(root,Path.DirectorySeparatorChar=='\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Level files must remain within the explicit content root.");
            return Normalize(full[root.Length..]);
        }
        string levelPath=Relative(levelFile);
        IReadOnlyList<KwdChunk> levelChunks=KwdReader.ReadFile(levelFile);
        files.Add(levelPath,levelChunks);
        LevelDef level=KwdLevel.Parse(levelChunks) ?? throw new InvalidDataException("The file has no LEVEL manifest.");
        foreach(KwdLevelFilePath entry in level.Paths)
        {
            string path=Normalize(entry.Path);
            if(Path.GetExtension(path).Length==0) path+=".kwd";
            string actual=Resolve(root,path);
            string relative=Relative(actual);
            if(!files.ContainsKey(relative)) files.Add(relative,KwdReader.ReadFile(actual));
        }
        return Create(files);
    }

    public IReadOnlyList<KwdChunk> Chunks(string relativePath) => FileChunks(relativePath).Select(Clone).ToArray();
    public byte[] ToBytes(string relativePath) => KwdWriter.ToBytes(FileChunks(relativePath));

    public T ReadRecord<T>(KwdRecordLocation location)
    {
        KwdChunk chunk=Chunk(location);
        IReadOnlyList<object> rows=KwdTypedRecords.Read(chunk);
        if((uint)location.RecordIndex>=(uint)rows.Count) throw new ArgumentOutOfRangeException(nameof(location));
        if(rows[location.RecordIndex] is not T row) throw new InvalidDataException("The requested typed record does not match the file image.");
        return row;
    }

    public void EditRecord<T>(KwdRecordLocation location,Func<T,T> edit)
    {
        KwdChunk chunk=Chunk(location);
        T original=ReadRecord<T>(location);
        T changed=edit(original);
        if(changed is null || changed.GetType()!=original!.GetType())
            throw new InvalidDataException("A typed edit must retain its record type.");
        RefuseReferenceChanges(original,changed);
        var images=KwdRecordRoundTrip.Split(chunk);
        KwdRecordImage template=images[location.RecordIndex];
        images[location.RecordIndex]=new KwdRecordImage(chunk.Type,KwdTypedRecords.Write(changed,template,chunk),template.Tag);
        Replace(location,chunk with { Content=KwdRecordRoundTrip.Join(chunk,images) });
    }

    public void Save(string outputRoot)
    {
        var output=_files.Select(file=>(Path:Path.Combine(outputRoot,file.Key),Bytes:KwdWriter.ToBytes(file.Value))).ToArray();
        foreach(var file in output)
        {
            string? parent=Path.GetDirectoryName(file.Path);
            if(!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllBytes(file.Path,file.Bytes);
        }
    }

    private KwdChunk[] FileChunks(string path) => _files.TryGetValue(Normalize(path),out KwdChunk[]? chunks)
        ? chunks : throw new InvalidDataException("The file is not part of this level set.");

    private KwdChunk Chunk(KwdRecordLocation location)
    {
        KwdChunk[] chunks=FileChunks(location.RelativePath);
        if((uint)location.ChunkIndex>=(uint)chunks.Length) throw new ArgumentOutOfRangeException(nameof(location));
        return chunks[location.ChunkIndex];
    }

    private void Replace(KwdRecordLocation location,KwdChunk chunk) => FileChunks(location.RelativePath)[location.ChunkIndex]=chunk;
    private static KwdChunk Clone(KwdChunk chunk) => chunk with { Content=chunk.Content.ToArray(),HeaderBlock=chunk.HeaderBlock.ToArray() };

    private static string Normalize(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':'))
            throw new InvalidDataException("File paths must be relative to the level set's content root.");
        string[] parts=path.Replace('\\','/').Split('/');
        if(parts.Any(part=>part is "" or "." or "..")) throw new InvalidDataException("File paths cannot escape the content root.");
        return string.Join(Path.DirectorySeparatorChar.ToString(),parts);
    }

    private static string Resolve(string root,string path)
    {
        string current=root;
        foreach(string part in path.Split(Path.DirectorySeparatorChar))
        {
            string exact=Path.Combine(current,part);
            if(File.Exists(exact) || Directory.Exists(exact)) { current=exact; continue; }
            string[] matches=Directory.EnumerateFileSystemEntries(current)
                .Where(p=>Path.GetFileName(p).Equals(part,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1) throw new FileNotFoundException("A LEVEL reference cannot be resolved uniquely.",exact);
            current=matches[0];
        }
        return current;
    }

    private static ushort? RootTrigger(object row) => row switch
    {
        ObjectThing r=>r.TriggerId, DoorThing r=>r.TriggerId, ActionPointThing r=>r.TriggerId,
        NeutralCreatureThing r=>r.TriggerId, GoodCreatureThing r=>r.TriggerId, KeeperCreatureThing r=>r.TriggerId,
        HeroPartyThing r=>r.TriggerId, PlayerDef r=>r.TriggerId, LevelDef r=>r.TriggerId,
        _=>null,
    };

    private static void RefuseReferenceChanges(object original,object changed)
    {
        if(RootTrigger(original)!=RootTrigger(changed))
            throw new InvalidDataException("Trigger root changes require a validated reference-edit operation.");
        if(original is TriggerRecord a && changed is TriggerRecord b
            && (a.TriggerId!=b.TriggerId || a.NextTriggerId!=b.NextTriggerId || a.ChildTriggerId!=b.ChildTriggerId))
            throw new InvalidDataException("Trigger identifiers and tree links cannot be rewritten through a field edit.");
        if(original is HeroPartyThing p && changed is HeroPartyThing q
            && !p.Members.Select(m=>m.TriggerId).SequenceEqual(q.Members.Select(m=>m.TriggerId)))
            throw new InvalidDataException("Hero-party member trigger roots cannot be rewritten through a field edit.");
        if(original is LevelDef x && changed is LevelDef y && !x.Paths.SequenceEqual(y.Paths))
            throw new InvalidDataException("LEVEL file-reference edits require an explicit file-set import operation.");
    }
}
