// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Editing;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.LevelGen;

/// <summary>A shipped level loaded for editing, saved under a new name: map, things and variables
/// edited through <see cref="KwdLevelFileSet"/>, the LEVEL manifest re-pointed at the renamed files.
/// Nothing is written to the install; <see cref="SaveAs"/> writes a staging folder.</summary>
public sealed class LevelBuilder
{
    public string Install { get; }
    public string BaseName { get; }
    private readonly KwdLevelFileSet _set;
    private readonly KwdChunkLocation _map;
    private readonly string _things, _variables;

    public LevelBuilder(string install, string baseName)
    {
        Install = install;
        BaseName = baseName;
        _set = KwdLevelFileSet.Load(Path.Combine(MapsDir(install), baseName + ".kwd"), install);
        string mapFile = Own("Map.kld");
        _map = new KwdChunkLocation(mapFile, ChunkOf(mapFile, KwdRecordType.Map));
        _things = Own("Things.kld");
        _variables = Own("Variables.kld");
    }

    public static string MapsDir(string install) => Path.Combine(install, "Data", "editor", "maps");

    public LevelMap Map => _set.ReadMap(_map);
    public void Paint(int x, int y, MapTile tile) => _set.RepaintMap(_map, new TilePos(x, y), tile);

    /// <summary>The level's THINGS in file order; indices are what <see cref="EditThing{T}"/> and
    /// <see cref="RemoveThing"/> take, and they shift after a removal.</summary>
    public IReadOnlyList<object> Things => KwdTypedRecords.Read(_set.Chunks(_things)[ChunkOf(_things, KwdRecordType.Things)]);
    public void EditThing<T>(int index, Func<T, T> edit) =>
        _set.EditRecord(new KwdRecordLocation(_things, ChunkOf(_things, KwdRecordType.Things), index), edit);
    public void RemoveThing(int index) =>
        _set.RemoveThing(new KwdRecordLocation(_things, ChunkOf(_things, KwdRecordType.Things), index));

    /// <summary>The level's own VARIABLES overrides (they beat GlobalVariables.kwd).</summary>
    public IReadOnlyList<object> Variables => KwdTypedRecords.Read(_set.Chunks(_variables)[ChunkOf(_variables, KwdRecordType.Variables)]);

    /// <summary>Sets an override the level already carries; returns false when it carries none.
    /// Adding a new one is not supported here.</summary>
    public bool SetVariable(VariableTypeId id, int value)
    {
        int chunk = ChunkOf(_variables, KwdRecordType.Variables);
        var rows = Variables;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not MiscVariable v || v.Id != id) continue;
            _set.EditRecord<MiscVariable>(new KwdRecordLocation(_variables, chunk, i), r => r with { Value = value });
            return true;
        }
        return false;
    }

    /// <summary>The value GlobalVariables.kwd gives, for resetting a level override to campaign play.</summary>
    public int GlobalValue(VariableTypeId id)
    {
        string global = Path.Combine(Install, "Data", "editor", "GlobalVariables.kwd");
        return KwdReader.ReadFile(global).SelectMany(c => KwdTypedRecords.Read(c)).OfType<MiscVariable>().First(v => v.Id == id).Value;
    }

    /// <summary>Writes <c>stage/maps/&lt;name&gt;.kwd</c> and its five <c>.kld</c> files: the base level's
    /// own files renamed, and the LEVEL record's name and paths re-pointed at them. Returns that folder.</summary>
    public string SaveAs(string name, string stage)
    {
        string raw = Path.Combine(stage, "raw");
        _set.Save(raw);
        string outMaps = Path.Combine(stage, "maps");
        Directory.CreateDirectory(outMaps);
        string rawMaps = Path.GetDirectoryName(Path.Combine(raw, Own("Map.kld")))!;

        var chunks = KwdReader.ReadFile(Path.Combine(rawMaps, BaseName + ".kwd")).ToList();
        int li = chunks.FindIndex(c => c.Type == KwdRecordType.Level);
        LevelDef level = KwdLevel.Parse([chunks[li]])!;
        string from = @"\Maps\" + BaseName, to = @"\Maps\" + name;
        var paths = level.Paths.Select(p => p.Path.Contains(from, StringComparison.OrdinalIgnoreCase)
            ? p with { Path = p.Path.Replace(from, to, StringComparison.OrdinalIgnoreCase) } : p).ToList();
        LevelDef renamed = level with { Name = level.Name.Replace(BaseName, name, StringComparison.OrdinalIgnoreCase), Paths = paths };
        chunks[li] = chunks[li] with { Content = KwdLevel.WriteRecord(renamed, chunks[li]) };
        KwdWriter.WriteFile(Path.Combine(outMaps, name + ".kwd"), chunks);
        foreach (string f in Directory.GetFiles(rawMaps, BaseName + "*.kld"))
            File.Copy(f, Path.Combine(outMaps, name + Path.GetFileName(f)[BaseName.Length..]), overwrite: true);
        return outMaps;
    }

    /// <summary>Loads a saved level back from a root laid out like the install (its files plus the
    /// install's definition files), the way DK2 resolves it, and prints the map with its traps and
    /// creatures. Throws if the manifest does not resolve.</summary>
    public static void Verify(string install, string stage, string name)
    {
        string check = Path.Combine(stage, "check");
        string checkMaps = MapsDir(check);
        Directory.CreateDirectory(checkMaps);
        foreach (string f in Directory.GetFiles(Path.Combine(stage, "maps"))) File.Copy(f, Path.Combine(checkMaps, Path.GetFileName(f)), true);
        foreach (string f in Directory.GetFiles(Path.Combine(install, "Data", "editor"), "*.kwd"))
            File.Copy(f, Path.Combine(check, "Data", "editor", Path.GetFileName(f)), true);
        var back = KwdLevelFileSet.Load(Path.Combine(checkMaps, name + ".kwd"), check);
        string own = back.Files.Single(f => f.EndsWith(name + "Map.kld", StringComparison.OrdinalIgnoreCase));
        LevelMap map = back.ReadMap(new KwdChunkLocation(own, back.Chunks(own).ToList().FindIndex(c => c.Type == KwdRecordType.Map)));
        string thingsFile = back.Files.Single(f => f.EndsWith(name + "Things.kld", StringComparison.OrdinalIgnoreCase));
        var things = back.Chunks(thingsFile).SelectMany(c => KwdTypedRecords.Read(c)).ToList();
        Console.WriteLine($"{name}: {map.Width}x{map.Height}, files {string.Join(", ", back.Files.Where(f => f.Contains(name)).Select(Path.GetFileName))}");
        Console.Write(Render(map, things));
        Console.WriteLine($"things: {string.Join(", ", things.GroupBy(t => t.GetType().Name).Select(g => g.Key + " " + g.Count()))}");
    }

    /// <summary>The map as text: <c>#</c> rock, <c>.</c> path, <c>W</c> wall, <c>H</c> heart, <c>~</c> lava or
    /// the map's edge, <c>T</c> a trap, <c>c</c> a hero, <c>k</c> a keeper's creature, other terrain by its id mod 10.</summary>
    public static string Render(LevelMap map, IEnumerable<object> things)
    {
        var marks = new Dictionary<(int, int), char>();
        foreach (object t in things)
        {
            switch (t)
            {
                case TrapThing tr: marks[(tr.X, tr.Y)] = 'T'; break;
                case GoodCreatureThing g: marks[(g.X, g.Y)] = 'c'; break;
                case KeeperCreatureThing k: marks[(k.X, k.Y)] = 'k'; break;
            }
        }
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < map.Height; y++)
        {
            sb.Append($"{y,3} ");
            for (int x = 0; x < map.Width; x++)
                sb.Append(marks.TryGetValue((x, y), out char ch) ? ch : map[x, y].TerrainId switch
                {
                    1 or 2 => '#', 3 or 8 => '.', 9 => 'W', 14 => 'H', 5 or 30 => '~', var o => (char)('0' + o % 10),
                });
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string Own(string suffix) => _set.Files.Single(f => f.EndsWith(BaseName + suffix, StringComparison.OrdinalIgnoreCase));
    private int ChunkOf(string file, KwdRecordType type) => _set.Chunks(file).ToList().FindIndex(c => c.Type == type);
}
