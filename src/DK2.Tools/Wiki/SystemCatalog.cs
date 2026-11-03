// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DK2.Tools.Wiki;

public sealed record SystemEntry(string Id, string Title, string Scope);

/// <summary>docs/systems.yaml: the closed list of systems the wiki indexes by (ADR-0643), in the wiki's order.</summary>
public sealed partial class SystemCatalog
{
    public const string RelativePath = "docs/systems.yaml";

    readonly Dictionary<string, int> order;

    SystemCatalog(IReadOnlyList<SystemEntry> systems)
    {
        Systems = systems;
        order = systems.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
    }

    public IReadOnlyList<SystemEntry> Systems { get; }

    public bool Contains(string id) => order.ContainsKey(id);

    /// <summary>The catalogue position of <paramref name="id"/>, for sorting system lists; unknown ids sort last.</summary>
    public int OrderOf(string id) => order.TryGetValue(id, out int i) ? i : int.MaxValue;

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")] private static partial Regex IdRx();

    /// <summary>Parses the YAML; <see cref="InvalidDataException"/> on a duplicate id, a malformed id or a missing field.</summary>
    public static SystemCatalog Parse(string yaml)
    {
        File? file;
        try
        {
            file = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build()
                .Deserialize<File>(yaml);
        }
        catch (YamlException e)
        {
            throw new InvalidDataException($"{RelativePath}: {e.Message}", e);
        }

        if (file?.Systems is not { Count: > 0 } raw)
            throw new InvalidDataException($"{RelativePath}: no systems");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var systems = new List<SystemEntry>(raw.Count);
        foreach (Entry e in raw)
        {
            if (string.IsNullOrWhiteSpace(e.Id) || !IdRx().IsMatch(e.Id))
                throw new InvalidDataException($"{RelativePath}: id '{e.Id}' is not lower-case words joined by hyphens");
            if (!seen.Add(e.Id))
                throw new InvalidDataException($"{RelativePath}: id '{e.Id}' is listed twice");
            if (string.IsNullOrWhiteSpace(e.Title) || string.IsNullOrWhiteSpace(e.Scope))
                throw new InvalidDataException($"{RelativePath}: '{e.Id}' needs a title and a scope");
            systems.Add(new SystemEntry(e.Id, e.Title.Trim(), e.Scope.Trim()));
        }
        return new SystemCatalog(systems);
    }

    public static SystemCatalog Load(string root)
        => Parse(System.IO.File.ReadAllText(Path.Combine(root, RelativePath.Replace('/', Path.DirectorySeparatorChar))));

    sealed class File
    {
        public List<Entry>? Systems { get; set; }
    }

    sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Scope { get; set; } = "";
    }
}
