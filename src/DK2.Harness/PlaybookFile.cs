// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Security.Cryptography;
using DK2.AI.Players;
using DK2.Data.Model;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DK2.Harness;

/// <summary>
/// Reads a level's playbook (campaign bot stage 3 plan, Task 3b) from <c>knowledge/playbooks/&lt;level&gt;.yaml</c>
/// (outside <c>scenarios/</c>, where every yaml is a scenario) into a reviewed <see cref="Playbook"/>.
/// Strict: an unknown key, an unreadable item, room or provenance, and every problem
/// <see cref="Playbook.Review"/> finds refuse the file, all named at once; warnings are returned.
/// </summary>
public static class PlaybookFile
{
    public const string DirectoryName = "playbooks";

    public sealed record Loaded(Playbook Playbook, IReadOnlyList<string> Warnings, string Sha256);

    private static IDeserializer Yaml => new DeserializerBuilder().WithNamingConvention(HyphenatedNamingConvention.Instance).Build();

    /// <summary>The playbook at <paramref name="path"/>, reviewed against <paramref name="hashVersion"/>.</summary>
    public static Loaded Load(string path, ulong hashVersion)
    {
        string text = File.ReadAllText(path);
        Document document;
        try { document = Yaml.Deserialize<Document>(text) ?? new Document(); }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidDataException($"playbook '{path}' is refused: {ex.Message}", ex);
        }
        var problems = new List<string>();
        Playbook playbook = Read(document, problems);
        PlaybookReview review = playbook.Review(hashVersion);
        problems.AddRange(review.Problems);
        if (problems.Count > 0) throw new InvalidDataException($"playbook '{path}' is refused: " + string.Join("; ", problems));
        return new Loaded(playbook, review.Warnings, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
    }

    /// <summary>Only the caps, for <c>watch</c>, which sets each run's wall limit before the run loads the playbook.</summary>
    public static PlaybookCaps Caps(string path)
    {
        try { return Caps((Yaml.Deserialize<Document>(File.ReadAllText(path)) ?? new Document()).Caps); }
        catch (YamlDotNet.Core.YamlException ex) { throw new InvalidDataException($"playbook '{path}' is refused: {ex.Message}", ex); }
    }

    private static PlaybookCaps Caps(CapsEntry? caps) =>
        caps is null ? new PlaybookCaps() : new PlaybookCaps(caps.MaxTicks, caps.MaxActions, caps.WallSeconds, caps.OutputMib);

    private static Playbook Read(Document d, List<string> problems)
    {
        List<PlaybookItem> Items(string what, List<string>? names)
        {
            var items = new List<PlaybookItem>();
            foreach (string name in names ?? [])
                if (PlaybookItem.TryParse(name, out PlaybookItem item)) items.Add(item);
                else problems.Add($"{what}: '{name}' is not kind:Name (room, spell, door, trap, creature)");
            return items;
        }

        List<RoomTypeId>? Rooms(string what, List<string>? names)
        {
            if (names is null) return null;
            var rooms = new List<RoomTypeId>();
            foreach (string name in names)
                if (Enum.GetNames<RoomTypeId>().Contains(name, StringComparer.Ordinal)) rooms.Add(Enum.Parse<RoomTypeId>(name));
                else problems.Add($"plan {what}: '{name}' is not a room type name");
            return rooms;
        }

        if (d.Available is null) problems.Add("no available section (generate it with playbook-available)");
        var changes = new List<PlaybookChange>();
        foreach (ChangeEntry change in d.Available?.MayChange ?? [])
        {
            if (!PlaybookItem.TryParse(change.Item, out PlaybookItem item)) { problems.Add($"may-change: '{change.Item}' is not kind:Name"); continue; }
            if (change.To is not ("available" or "unavailable")) { problems.Add($"may-change {item}: to '{change.To}' is not available or unavailable"); continue; }
            changes.Add(new PlaybookChange(item, change.To == "available", change.By ?? ""));
        }
        var tunables = new List<PlaybookTunable>();
        foreach (TunableEntry t in d.Tunable ?? [])
        {
            PlaybookProvenance? provenance = t.Provenance switch { "researched" => PlaybookProvenance.Researched, "tuned" => PlaybookProvenance.Tuned, _ => null };
            if (provenance is null) { problems.Add($"tunable {t.Path}: provenance '{t.Provenance}' is not researched or tuned"); continue; }
            if (t.Path is null || t.Value is not int value || t.Min is not int min || t.Max is not int max)
            { problems.Add($"tunable {t.Path ?? "(no path)"}: path, value, min and max are all required"); continue; }
            tunables.Add(new PlaybookTunable(t.Path, value, min, max, provenance.Value, t.Source, t.ProvenOn, t.HashVersion));
        }
        return new Playbook
        {
            Level = d.Level ?? "",
            Start = Items("available start", d.Available?.Start),
            Creatures = Items("available creatures", d.Available?.Creatures),
            MayChange = changes,
            Rooms = Rooms("rooms", d.Plan?.Rooms),
            ExtraRooms = Rooms("extra-rooms", d.Plan?.ExtraRooms),
            Rules = d.Plan?.Rules ?? [],
            Tunables = tunables,
            Notes = [.. (d.Notes ?? []).Select(Note)],
            Events = [.. (d.Events ?? []).Select(Event)],
            Caps = Caps(d.Caps),
        };
    }

    private static PlaybookNote Note(NoteEntry n, int index)
    {
        if (n.At is { Count: not 2 }) throw new InvalidDataException($"note {index + 1}: at is [x, y]");
        string at = n.At is null ? "" : $" at ({n.At[0]}, {n.At[1]})";
        return new PlaybookNote(n.Kind ?? "", (n.Text ?? "") + at + (n.Provenance is null ? "" : $" ({n.Provenance})"), n.Source ?? "");
    }

    private static PlaybookNote Event(EventEntry e, int index)
    {
        if (string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.When) || string.IsNullOrWhiteSpace(e.Expect))
            throw new InvalidDataException($"event {index + 1}: id, when and expect are all required");
        return new PlaybookNote(e.Id, $"{e.When}: {e.Expect}", e.Source ?? "");
    }

    private sealed class Document
    {
        public string? Level { get; set; }
        public AvailableEntry? Available { get; set; }
        public PlanEntry? Plan { get; set; }
        public List<NoteEntry>? Notes { get; set; }
        public List<EventEntry>? Events { get; set; }
        public List<TunableEntry>? Tunable { get; set; }
        public CapsEntry? Caps { get; set; }
    }

    private sealed class AvailableEntry
    {
        public long ReadAtTick { get; set; }
        public List<string>? Start { get; set; }
        public List<string>? Creatures { get; set; }
        public List<ChangeEntry>? MayChange { get; set; }
    }

    private sealed class ChangeEntry
    {
        public string? Item { get; set; }
        public string? To { get; set; }
        public string? By { get; set; }
    }

    private sealed class PlanEntry
    {
        public List<string>? Rooms { get; set; }
        public List<string>? ExtraRooms { get; set; }
        public List<string>? Rules { get; set; }
    }

    private sealed class NoteEntry
    {
        public string? Kind { get; set; }
        public string? Text { get; set; }
        public List<int>? At { get; set; }
        public string? Provenance { get; set; }
        public string? Source { get; set; }
    }

    private sealed class EventEntry
    {
        public string? Id { get; set; }
        public string? When { get; set; }
        public string? Expect { get; set; }
        public string? Source { get; set; }
    }

    private sealed class TunableEntry
    {
        public string? Path { get; set; }
        public int? Value { get; set; }
        public int? Min { get; set; }
        public int? Max { get; set; }
        public string? Provenance { get; set; }
        public string? Source { get; set; }
        public string? ProvenOn { get; set; }
        public ulong? HashVersion { get; set; }
    }

    private sealed class CapsEntry
    {
        public int? MaxTicks { get; set; }
        public int? MaxActions { get; set; }
        public int? WallSeconds { get; set; }
        public int? OutputMib { get; set; }
    }
}
