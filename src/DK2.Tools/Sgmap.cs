// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;

namespace DK2.Tools;

public static class SgmapCommand
{
    public const string EnvironmentVariable = "DK2_SGMAP";
    private const string FileName = "DKII_EXE_v170.sgmap";

    public static int Run(string[] arguments, string? repositoryRoot,
        TextWriter? output = null, TextWriter? error = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;

        var remaining = new List<string>(arguments);
        string? explicitPath = TakePathOption(remaining, error);
        if (explicitPath == string.Empty) return 2;
        if (remaining.Count == 0) return Usage(error);

        string? path = ResolvePath(explicitPath, repositoryRoot);
        if (path is null)
        {
            string? configured = explicitPath ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
                error.WriteLine($"Symbol map not found at '{Path.GetFullPath(configured)}'.");
            else
                error.WriteLine($"Symbol map not found. Pass --sgmap <path>, set "
                    + $"{EnvironmentVariable}, or place the Flame checkout beside the repository.");
            return 2;
        }

        SgmapDatabase database;
        try
        {
            using var reader = File.OpenText(path);
            database = SgmapDatabase.Parse(reader);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            error.WriteLine($"Could not read symbol map '{path}': {ex.Message}");
            return 2;
        }

        try
        {
            return RunQuery(database, remaining, output, error);
        }
        catch (ArgumentException ex)
        {
            error.WriteLine(ex.Message);
            return 2;
        }
    }

    public static int RunQuery(SgmapDatabase database, IReadOnlyList<string> arguments,
        TextWriter output, TextWriter error)
    {
        switch (arguments)
        {
            case ["struct", string name]:
                return PrintStruct(database, name, output, error);
            case ["find", string text]:
                return PrintFind(database, text, output);
            case ["global", string address]:
                return PrintGlobal(database, ParseHex(address), output, error);
            case ["at", string name, string offset]:
                return PrintAt(database, name, ParseHex(offset), output, error);
            default:
                return Usage(error);
        }
    }

    public static string? ResolvePath(string? explicitPath, string? repositoryRoot)
    {
        string? selected = explicitPath;
        if (string.IsNullOrWhiteSpace(selected))
            selected = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            string full = Path.GetFullPath(selected);
            return File.Exists(full) ? full : null;
        }

        if (repositoryRoot is null) return null;
        DirectoryInfo? parent = Directory.GetParent(Path.GetFullPath(repositoryRoot));
        if (parent is null) return null;
        string candidate = Path.Combine(parent.FullName, "Flame", "mapping", FileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? TakePathOption(List<string> arguments, TextWriter error)
    {
        int index = arguments.IndexOf("--sgmap");
        if (index < 0) return null;
        if (index + 1 >= arguments.Count || arguments.Count(a => a == "--sgmap") != 1)
        {
            error.WriteLine("--sgmap requires exactly one path.");
            return string.Empty;
        }

        string path = arguments[index + 1];
        arguments.RemoveRange(index, 2);
        return path;
    }

    private static int PrintStruct(SgmapDatabase database, string name,
        TextWriter output, TextWriter error)
    {
        SgmapStruct? structure = database.FindStruct(name, error);
        if (structure is null) return 1;

        output.WriteLine($"struct {structure.Name} size=0x{structure.Size:X}");
        foreach (SgmapFieldLocation field in database.Expand(structure))
            output.WriteLine($"  0x{field.Offset:X4}  0x{field.Size:X4}  "
                + $"{field.Path} : {database.Format(field.Type)}");
        return 0;
    }

    private static int PrintFind(SgmapDatabase database, string text, TextWriter output)
    {
        int count = 0;
        foreach (SgmapStruct structure in database.Structs)
        {
            if (structure.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                string path = structure.Path is null ? string.Empty : $"  path={structure.Path}";
                output.WriteLine($"struct  {structure.Name}  size=0x{structure.Size:X}{path}");
                count++;
            }

            foreach (SgmapFieldLocation field in database.DirectFields(structure))
            {
                if (!field.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
                output.WriteLine($"field   {structure.Name}+0x{field.Offset:X}  "
                    + $"{field.Name} : {database.Format(field.Type)}");
                count++;
            }
        }

        foreach (SgmapGlobal global in database.Globals)
        {
            if (!global.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
            string owner = global.MemberOfId is null ? string.Empty
                : $"  in {database.StructName(global.MemberOfId)}";
            output.WriteLine($"global  0x{global.Address:X8}  {global.Name} : "
                + database.Format(global.Type) + owner);
            count++;
        }

        output.WriteLine($"-- {count} match(es)");
        return 0;
    }

    private static int PrintGlobal(SgmapDatabase database, uint address,
        TextWriter output, TextWriter error)
    {
        SgmapGlobal? global = database.GlobalAtOrBefore(address);
        if (global is null)
        {
            error.WriteLine($"No global exists at or before 0x{address:X8}.");
            return 1;
        }

        uint delta = address - global.Address;
        string relation = delta == 0 ? "exact"
            : delta < global.Size ? $"contains +0x{delta:X}"
            : $"preceding, +0x{delta:X}";
        output.WriteLine($"0x{global.Address:X8}  size=0x{global.Size:X}  "
            + $"{global.Name} : {database.Format(global.Type)}  [{relation}]");
        return 0;
    }

    private static int PrintAt(SgmapDatabase database, string name, uint offset,
        TextWriter output, TextWriter error)
    {
        SgmapStruct? structure = database.FindStruct(name, error);
        if (structure is null) return 1;
        if (offset >= structure.Size)
        {
            error.WriteLine($"Offset 0x{offset:X} is outside {structure.Name} "
                + $"(size 0x{structure.Size:X}).");
            return 1;
        }

        IReadOnlyList<SgmapFieldLocation> fields = database.FieldsAt(structure, offset);
        if (fields.Count == 0)
        {
            error.WriteLine($"No named field covers {structure.Name}+0x{offset:X}.");
            return 1;
        }

        foreach (SgmapFieldLocation field in fields)
        {
            uint delta = offset - field.Offset;
            string within = delta == 0 ? string.Empty : $" +0x{delta:X}";
            output.WriteLine($"{structure.Name}+0x{offset:X} -> {field.Path}{within} "
                + $"(starts 0x{field.Offset:X}, size 0x{field.Size:X}, "
                + $"{database.Format(field.Type)})");
        }
        return 0;
    }

    private static uint ParseHex(string value)
    {
        string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..] : value;
        if (uint.TryParse(digits, NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture, out uint parsed)) return parsed;
        throw new ArgumentException($"Invalid hexadecimal value '{value}'.");
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("usage: dk2tools sgmap [--sgmap <path>] struct <name>");
        error.WriteLine("       dk2tools sgmap [--sgmap <path>] find <substring>");
        error.WriteLine("       dk2tools sgmap [--sgmap <path>] global <hexAddr>");
        error.WriteLine("       dk2tools sgmap [--sgmap <path>] at <struct> <hexOff>");
        return 2;
    }
}

public sealed class SgmapDatabase
{
    private readonly Dictionary<string, SgmapStruct> _structsById;

    private SgmapDatabase(List<SgmapStruct> structs, List<SgmapGlobal> globals)
    {
        Structs = structs;
        Globals = globals.OrderBy(g => g.Address).ToArray();
        _structsById = structs.ToDictionary(s => s.Id, StringComparer.Ordinal);
        ValidateReferences();
    }

    public IReadOnlyList<SgmapStruct> Structs { get; }
    public IReadOnlyList<SgmapGlobal> Globals { get; }

    public static SgmapDatabase Parse(TextReader reader)
    {
        List<SgmapNode> roots = SgmapSyntax.Parse(reader);
        var structs = new List<SgmapStruct>();
        var globals = new List<SgmapGlobal>();

        foreach (SgmapNode node in roots)
        {
            if (node.Key == "struct") structs.Add(ParseStruct(node));
            else if (node.Key == "global") globals.Add(ParseGlobal(node));
            else throw node.Error($"Unknown top-level entry '{node.Key}'");
        }
        return new SgmapDatabase(structs, globals);
    }

    public SgmapStruct? FindStruct(string name, TextWriter error)
    {
        SgmapStruct[] matches = Structs.Where(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Id, name, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length == 0) error.WriteLine($"Structure '{name}' was not found.");
        else error.WriteLine($"Structure name '{name}' is ambiguous ({matches.Length} matches); "
            + "query by its map id.");
        return null;
    }

    public IEnumerable<SgmapFieldLocation> DirectFields(SgmapStruct structure)
    {
        uint offset = OwnFieldOffset(structure);
        foreach (SgmapField field in structure.Fields)
        {
            uint size = SizeOf(field.Type);
            yield return new SgmapFieldLocation(field.Name, field.Name, offset, size, field.Type);
            if (!structure.IsUnion) offset = checked(offset + size);
        }
    }

    public IEnumerable<SgmapFieldLocation> Expand(SgmapStruct structure)
    {
        foreach (SgmapFieldLocation field in AllFields(structure))
        {
            yield return field;
            foreach (SgmapFieldLocation nested in ExpandType(
                field.Type, field.Offset, field.Path, new HashSet<string>(StringComparer.Ordinal)))
                yield return nested;
        }
    }

    public IReadOnlyList<SgmapFieldLocation> FieldsAt(SgmapStruct structure, uint offset)
    {
        var found = new List<SgmapFieldLocation>();
        foreach (SgmapFieldLocation field in AllFields(structure))
        {
            if (offset < field.Offset || offset >= checked(field.Offset + field.Size)) continue;
            SgmapFieldLocation leaf = DeepestAt(field, offset);
            found.Add(leaf);
        }
        return found;
    }

    public SgmapGlobal? GlobalAtOrBefore(uint address)
    {
        int low = 0, high = Globals.Count - 1, answer = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (Globals[middle].Address <= address)
            {
                answer = middle;
                low = middle + 1;
            }
            else high = middle - 1;
        }
        return answer < 0 ? null : Globals[answer];
    }

    public string StructName(string id) => StructById(id).Name;

    public string Format(SgmapType type)
    {
        string? Child(string key) => type.Children.FirstOrDefault(c => c.Label == key)?.Type is { } child
            ? Format(child) : null;
        return type.Kind switch
        {
            "void" => "void",
            "int" => $"{(type.Bool("signed") ? "int" : "uint")}{type.UInt("size") * 8}",
            "float" => $"float{type.UInt("size") * 8}",
            "struct" => StructById(type.Required("id")).Name,
            "ptr" => $"{Child("type") ?? "void"}*",
            "array" => string.Concat(Child("type") ?? "?", "[", type.UInt("count"), "]"),
            "function" => FormatFunction(type),
            "winapi" => type.Required("name"),
            _ => type.Kind,
        };
    }

    private IEnumerable<SgmapFieldLocation> AllFields(SgmapStruct structure)
    {
        if (structure.SuperId is not null)
            foreach (SgmapFieldLocation field in AllFields(StructById(structure.SuperId)))
                yield return field;
        foreach (SgmapFieldLocation field in DirectFields(structure)) yield return field;
    }

    private IEnumerable<SgmapFieldLocation> ExpandType(SgmapType type, uint offset,
        string path, HashSet<string> ancestors)
    {
        if (type.Kind == "array")
        {
            SgmapType element = type.Child("type");
            uint elementSize = SizeOf(element);
            for (uint i = 0; i < type.UInt("count"); i++)
            {
                uint elementOffset = checked(offset + i * elementSize);
                string elementPath = $"{path}[{i}]";
                yield return new SgmapFieldLocation(elementPath, elementPath,
                    elementOffset, elementSize, element);
                foreach (SgmapFieldLocation nested in ExpandType(
                    element, elementOffset, elementPath, ancestors)) yield return nested;
            }
            yield break;
        }
        if (type.Kind != "struct") yield break;

        SgmapStruct nestedStruct = StructById(type.Required("id"));
        if (!ancestors.Add(nestedStruct.Id)) yield break;
        foreach (SgmapFieldLocation nested in AllFields(nestedStruct))
        {
            var concrete = nested with
            {
                Name = nested.Name,
                Path = $"{path}.{nested.Path}",
                Offset = checked(offset + nested.Offset),
            };
            yield return concrete;
            foreach (SgmapFieldLocation child in ExpandType(
                nested.Type, concrete.Offset, concrete.Path, ancestors)) yield return child;
        }
        ancestors.Remove(nestedStruct.Id);
    }

    private SgmapFieldLocation DeepestAt(SgmapFieldLocation field, uint target)
    {
        SgmapType type = field.Type;
        if (type.Kind == "array")
        {
            SgmapType element = type.Child("type");
            uint elementSize = SizeOf(element);
            uint index = (target - field.Offset) / elementSize;
            var indexed = new SgmapFieldLocation(field.Name, $"{field.Path}[{index}]",
                checked(field.Offset + index * elementSize), elementSize, element);
            return DeepestAt(indexed, target);
        }
        if (type.Kind != "struct") return field;

        SgmapStruct nested = StructById(type.Required("id"));
        uint relative = target - field.Offset;
        foreach (SgmapFieldLocation child in AllFields(nested))
        {
            if (relative < child.Offset || relative >= checked(child.Offset + child.Size)) continue;
            var concrete = child with
            {
                Path = $"{field.Path}.{child.Path}",
                Offset = checked(field.Offset + child.Offset),
            };
            return DeepestAt(concrete, target);
        }
        return field;
    }

    private uint OwnFieldOffset(SgmapStruct structure)
    {
        uint offset = structure.VtableId is null ? 0u : 4u;
        if (structure.SuperId is null) return offset;
        SgmapStruct parent = StructById(structure.SuperId);
        return checked(offset + parent.Size - (parent.VtableId is null ? 0u : 4u));
    }

    private uint SizeOf(SgmapType type) => type.Kind switch
    {
        "int" or "float" or "winapi" => type.UInt("size"),
        "ptr" => 4,
        "struct" => StructById(type.Required("id")).Size,
        "array" => checked(type.UInt("count") * SizeOf(type.Child("type"))),
        _ => throw new FormatException($"Type '{type.Kind}' has no storage size."),
    };

    private string FormatFunction(SgmapType type)
    {
        string result = type.Children.FirstOrDefault(c => c.Label == "ret")?.Type is { } ret
            ? Format(ret) : "void";
        string args = string.Join(", ", type.Children.Where(c => c.Label == "arg")
            .Select(c => Format(c.Type)));
        string convention = type.Properties.GetValueOrDefault("declspec", "function");
        return $"{convention} ({args}) -> {result}";
    }

    private SgmapStruct StructById(string id) => _structsById.TryGetValue(id, out SgmapStruct? value)
        ? value : throw new FormatException($"Structure id '{id}' was not declared.");

    private void ValidateReferences()
    {
        foreach (SgmapStruct structure in Structs)
        {
            if (structure.SuperId is not null) StructById(structure.SuperId);
            if (structure.VtableId is not null) StructById(structure.VtableId);
            foreach (SgmapField field in structure.Fields) ValidateType(field.Type);
        }
        foreach (SgmapGlobal global in Globals) ValidateType(global.Type);
        foreach (SgmapGlobal global in Globals)
            if (global.MemberOfId is not null) StructById(global.MemberOfId);
    }

    private void ValidateType(SgmapType type)
    {
        if (type.Kind == "struct") StructById(type.Required("id"));
        foreach (SgmapTypeChild child in type.Children) ValidateType(child.Type);
    }

    private static SgmapStruct ParseStruct(SgmapNode node)
    {
        var fields = new List<SgmapField>();
        foreach (SgmapNode child in node.Children)
        {
            if (child.Key != "field") continue;
            SgmapNode type = child.Children.SingleOrDefault(c => c.Key == "type")
                ?? throw child.Error("Field has no type");
            fields.Add(new SgmapField(child.Required("name"), ParseType(type)));
        }
        return new SgmapStruct(node.Required("id"), node.Required("name"), node.UInt("size"),
            node.Optional("path"), node.Optional("vtable"), node.Optional("super"),
            node.Bool("is_union"), fields);
    }

    private static SgmapGlobal ParseGlobal(SgmapNode node)
    {
        SgmapNode type = node.Children.SingleOrDefault(c => c.Key == "type")
            ?? throw node.Error("Global has no type");
        return new SgmapGlobal(node.Hex("va"), node.Required("name"),
            node.UInt("size"), ParseType(type), node.Optional("member_of"));
    }

    private static SgmapType ParseType(SgmapNode node)
    {
        var children = new List<SgmapTypeChild>();
        foreach (SgmapNode child in node.Children)
        {
            if (child.Key is "type" or "ret" or "arg")
                children.Add(new SgmapTypeChild(child.Key, ParseType(child)));
        }
        return new SgmapType(node.Required("kind"), node.Properties, children);
    }
}

public sealed record SgmapStruct(string Id, string Name, uint Size, string? Path,
    string? VtableId, string? SuperId, bool IsUnion, IReadOnlyList<SgmapField> Fields);
public sealed record SgmapField(string Name, SgmapType Type);
public sealed record SgmapGlobal(uint Address, string Name, uint Size, SgmapType Type,
    string? MemberOfId);
public sealed record SgmapFieldLocation(string Name, string Path, uint Offset, uint Size,
    SgmapType Type);
public sealed record SgmapTypeChild(string Label, SgmapType Type);

public sealed record SgmapType(string Kind, IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<SgmapTypeChild> Children)
{
    public string Required(string key) => Properties.TryGetValue(key, out string? value)
        ? value : throw new FormatException($"Type '{Kind}' has no '{key}' property.");
    public uint UInt(string key) => uint.TryParse(Required(key), NumberStyles.None,
        CultureInfo.InvariantCulture, out uint value) ? value
        : throw new FormatException($"Type '{Kind}' has invalid '{key}'.");
    public bool Bool(string key) => Properties.TryGetValue(key, out string? value)
        && bool.TryParse(value, out bool parsed) && parsed;
    public SgmapType Child(string label) => Children.SingleOrDefault(c => c.Label == label)?.Type
        ?? throw new FormatException($"Type '{Kind}' has no '{label}' child.");
}

internal sealed class SgmapNode(
    string key, Dictionary<string, string> properties, int lineNumber)
{
    public string Key { get; } = key;
    public IReadOnlyDictionary<string, string> Properties => properties;
    public List<SgmapNode> Children { get; } = [];

    public string Required(string name) => properties.TryGetValue(name, out string? value)
        ? value : throw Error($"Missing property '{name}'");
    public string? Optional(string name) => properties.GetValueOrDefault(name);
    public uint UInt(string name) => uint.TryParse(Required(name), NumberStyles.None,
        CultureInfo.InvariantCulture, out uint value) ? value
        : throw Error($"Invalid unsigned integer in '{name}'");
    public uint Hex(string name) => uint.TryParse(Required(name), NumberStyles.AllowHexSpecifier,
        CultureInfo.InvariantCulture, out uint value) ? value
        : throw Error($"Invalid hexadecimal integer in '{name}'");
    public bool Bool(string name) => properties.TryGetValue(name, out string? value)
        && bool.TryParse(value, out bool parsed) && parsed;
    public FormatException Error(string message) => new($"Line {lineNumber}: {message}.");
}

internal static class SgmapSyntax
{
    public static List<SgmapNode> Parse(TextReader reader)
    {
        var roots = new List<SgmapNode>();
        var parents = new List<SgmapNode>();
        int lineNumber = 0;
        for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            int spaces = line.TakeWhile(c => c == ' ').Count();
            if (spaces % 2 != 0) throw new FormatException(
                $"Line {lineNumber}: indentation must use pairs of spaces.");
            int level = spaces / 2;
            SgmapNode node = ParseLine(line[spaces..], lineNumber);
            if (level == 0) roots.Add(node);
            else
            {
                if (level > parents.Count) throw new FormatException(
                    $"Line {lineNumber}: indentation skipped a level.");
                parents[level - 1].Children.Add(node);
            }
            if (parents.Count > level) parents.RemoveRange(level, parents.Count - level);
            parents.Add(node);
        }
        return roots;
    }

    private static SgmapNode ParseLine(string line, int lineNumber)
    {
        int separator = line.IndexOf(':');
        if (separator <= 0) throw new FormatException($"Line {lineNumber}: missing entry key.");
        string key = line[..separator].Trim();
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        string rest = line[(separator + 1)..].Trim();
        if (rest.Length > 0)
        {
            foreach (string part in rest.Split(','))
            {
                int equals = part.IndexOf('=');
                if (equals <= 0) throw new FormatException(
                    $"Line {lineNumber}: malformed property '{part}'.");
                string name = part[..equals].Trim();
                string value = part[(equals + 1)..].Trim();
                if (!properties.TryAdd(name, value)) throw new FormatException(
                    $"Line {lineNumber}: duplicate property '{name}'.");
            }
        }
        return new SgmapNode(key, properties, lineNumber);
    }
}
