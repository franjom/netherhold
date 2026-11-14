// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Security.Cryptography;
using DK2.AI.Players;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DK2.Harness;

/// <summary>
/// Loads the campaign player's knowledge (stage 3 plan, Task 3a) from <c>knowledge/</c>: the generated
/// room facts (<c>rooms.yaml</c>, by <see cref="KeeperKnowledgeCommand"/>) and the written rules
/// (<c>rules.yaml</c>). A rule acts only when its status is not <c>refuted</c>, it applies
/// <c>always</c>, and its form is one the strategy reads (<c>attraction-target</c>, <c>tile-target</c>);
/// the others are kept for the playbooks and the tuner. Every problem is reported at once.
/// </summary>
public static class KeeperKnowledgeFile
{
    public const string DirectoryName = "knowledge", RoomsFile = "rooms.yaml", RulesFile = "rules.yaml";
    public const string None = "none";

    private static readonly string[] Statuses = ["proposed", "tested", "held", "refuted"];
    private static readonly string[] Applies = ["always", "playbook"];
    private static readonly string[] Kinds = ["attraction-target", "tile-target", "research-shelves", "built-in", "manual"];

    /// <summary>What was loaded: the facts, the targets the acting rules set, which rules act, and the
    /// two files' SHA-256 for the run's record.</summary>
    public sealed record Loaded(KeeperKnowledge Knowledge, IReadOnlyDictionary<RoomTypeId, int> AttractionTargets,
        IReadOnlyDictionary<RoomTypeId, int> TileTargets, IReadOnlyList<string> ActingRules, string RoomsSha256, string RulesSha256)
    {
        /// <summary>Floor-slot targets: a research-shelves rule's room, sized for the level's research.</summary>
        public IReadOnlyDictionary<RoomTypeId, int> SlotTargets { get; init; } = new Dictionary<RoomTypeId, int>();
    }

    /// <summary>The floor slots a Library needs for <paramref name="researchable"/> items (Task 3a): every finished
    /// item is a book on a floor slot, which holds fewer than <see cref="ResearchTreeRules.ShelfCapacity"/> books
    /// and Specials (<c>0x004E1AC0</c>), and an upgrade, which makes no book, still needs a slot with room
    /// (research-tree.md section 4, library-and-research.md section 7). So the smallest count with room for every
    /// book and one more. Specials share the slots and are not counted: a Library that shelves them needs more.</summary>
    public static int ResearchSlots(int researchable) => researchable / ResearchTreeRules.ShelfCapacity + 1;

    /// <summary>How many items <paramref name="player"/> may research on a campaign level: the entries of the
    /// research list (GlobalVariables, research-tree.md section 1) that <c>0x004C3000</c>'s first pass can
    /// choose, RESEARCHABLE (<see cref="ResearchTreeRules.IsResearchable"/>) at the start the simulation builds
    /// from the level's own files. A researchable item off the list is never booked, so it is not counted.
    /// Definition data a player knows from the level; null for a map that is not a campaign level.</summary>
    public static int? ResearchableItems(string installDirectory, string mapSpec, PlayerId player)
    {
        const string prefix = "campaign:";
        if (!mapSpec.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string level = mapSpec[prefix.Length..].Trim();
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(installDirectory, level);
        if (map is null) return null;
        GameDatabase database = GameDatabaseLoader.Load(installDirectory)
            .ForLevel(GameDatabaseLoader.LoadCampaignVariables(installDirectory, level), GameDatabaseLoader.LoadCampaignLevel(installDirectory, level));
        WorldState world = WorldState.FromMap(database, map, map.Tiles.Select(t => t.PlayerId).Append(player.Value).Distinct());
        return database.Variables.Research
            .Where(e => ResearchTreeRules.AvailabilityKindOf(e.Kind) != AvailabilityKind.None && ResearchTreeRules.IsResearchable(world, player, e))
            .Select(e => (e.Kind, e.TypeId)).Distinct().Count();
    }

    /// <summary>The repository's <c>knowledge/</c> above <paramref name="scenarioPath"/>, or null outside one.</summary>
    public static string? DefaultDirectory(string scenarioPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(scenarioPath.Length > 0 ? scenarioPath : ".")) ?? ".");
        for (; dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DK2.slnx")))
            {
                string knowledge = Path.Combine(dir.FullName, DirectoryName);
                return Directory.Exists(knowledge) ? knowledge : null;
            }
        return null;
    }

    /// <summary>Loads the knowledge; <paramref name="researchable"/> is the level's research count
    /// (<see cref="ResearchableItems"/>), without which a research-shelves rule does not act.</summary>
    public static Loaded Load(string directory, int? researchable = null)
    {
        var problems = new List<string>();
        string roomsPath = Path.Combine(directory, RoomsFile), rulesPath = Path.Combine(directory, RulesFile);
        IDeserializer yaml = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties().Build();

        RoomsDocument rooms = yaml.Deserialize<RoomsDocument>(File.ReadAllText(roomsPath)) ?? new RoomsDocument();
        if (rooms.HashVersion != GameSimulation.CurrentHashVersion)
            problems.Add($"{RoomsFile} was generated at hash {rooms.HashVersion}, this build is {GameSimulation.CurrentHashVersion}: regenerate it with `keeper-knowledge --out {DirectoryName}/{RoomsFile}`");
        var facts = new List<RoomFacts>();
        foreach (RoomEntry room in rooms.Rooms ?? [])
        {
            if (!KnownRoom(room.Room, out RoomTypeId type)) { problems.Add($"{RoomsFile}: unknown room '{room.Room}'"); continue; }
            if (room.Sizes is not { Count: > 0 }) { problems.Add($"{RoomsFile}: {type} has no sizes"); continue; }
            if (room.CostPerTile < 0) { problems.Add($"{RoomsFile}: {type} has a negative cost_per_tile"); continue; }
            if (room.Sizes.Any(s => s.Side <= 0 || s.FloorSlots < 0 || s.Attraction < 0 || s.Gold < 0) || room.Sizes.Select(s => s.Side).Distinct().Count() != room.Sizes.Count)
            { problems.Add($"{RoomsFile}: {type} has a size with a bad or repeated side or a negative value"); continue; }
            if (room.Attracts is null) { problems.Add($"{RoomsFile}: {type} has no attracts list"); continue; }
            facts.Add(new RoomFacts(type, room.CostPerTile,
                room.Sizes.Select(s => new RoomSizeFacts(s.Side, s.FloorSlots, s.Attraction, s.Gold)),
                capsCreatures: room.Attracts.Any(a => a.CappedByThisRoom && a.Portal)));
        }
        if (facts.Select(f => f.Room).Distinct().Count() != facts.Count) problems.Add($"{RoomsFile} describes a room twice");
        if (facts.Count == 0) problems.Add($"{RoomsFile} describes no room");

        RulesDocument rules = yaml.Deserialize<RulesDocument>(File.ReadAllText(rulesPath)) ?? new RulesDocument();
        var attraction = new Dictionary<RoomTypeId, int>();
        var tiles = new Dictionary<RoomTypeId, int>();
        var slots = new Dictionary<RoomTypeId, int>();
        var shelfRooms = new HashSet<RoomTypeId>();
        var acting = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (RuleEntry rule in rules.Rules ?? [])
        {
            string id = rule.Id ?? "(no id)";
            if (rule.Id is null || !ids.Add(rule.Id)) problems.Add($"{RulesFile}: rule id '{id}' is missing or repeated");
            if (string.IsNullOrWhiteSpace(rule.Rule)) problems.Add($"{RulesFile}: {id} has no sentence");
            if (string.IsNullOrWhiteSpace(rule.Source)) problems.Add($"{RulesFile}: {id} names no source");
            if (!Statuses.Contains(rule.Status)) problems.Add($"{RulesFile}: {id} status '{rule.Status}' is not one of {string.Join(", ", Statuses)}");
            if (!Applies.Contains(rule.Applies)) problems.Add($"{RulesFile}: {id} applies '{rule.Applies}' is not one of {string.Join(", ", Applies)}");
            string? kind = rule.Form?.GetValueOrDefault("kind");
            if (!Kinds.Contains(kind)) { problems.Add($"{RulesFile}: {id} form kind '{kind}' is not one of {string.Join(", ", Kinds)}"); continue; }
            if (kind == "research-shelves")
            {
                if (!KnownRoom(rule.Form!.GetValueOrDefault("room"), out RoomTypeId shelves)) { problems.Add($"{RulesFile}: {id} names no known room"); continue; }
                if (!facts.Exists(f => f.Room == shelves)) { problems.Add($"{RulesFile}: {id} targets {shelves}, which {RoomsFile} does not describe"); continue; }
                if (rule.Status == "refuted" || rule.Applies != "always") continue;
                // A second rule for one room is refused whatever the level, not only when a research count is given.
                if (!shelfRooms.Add(shelves)) { problems.Add($"{RulesFile}: {id} sets {shelves} a second time"); continue; }
                if (researchable is not int count || count <= 0) continue;
                slots.Add(shelves, ResearchSlots(count));
                acting.Add(id);
                continue;
            }
            if (kind is not ("attraction-target" or "tile-target")) continue;
            string valueKey = kind == "attraction-target" ? "value" : "tiles";
            if (!KnownRoom(rule.Form!.GetValueOrDefault("room"), out RoomTypeId target))
            { problems.Add($"{RulesFile}: {id} names no known room"); continue; }
            if (kind == "attraction-target" && !facts.Exists(f => f.Room == target))
            { problems.Add($"{RulesFile}: {id} targets {target}, which {RoomsFile} does not describe"); continue; }
            if (!int.TryParse(rule.Form!.GetValueOrDefault(valueKey), out int value) || value <= 0)
            { problems.Add($"{RulesFile}: {id} {valueKey} is not a positive whole number"); continue; }
            if (rule.Status == "refuted" || rule.Applies != "always") continue;
            Dictionary<RoomTypeId, int> into = kind == "attraction-target" ? attraction : tiles;
            if (!into.TryAdd(target, value)) { problems.Add($"{RulesFile}: {id} sets {target} a second time"); continue; }
            acting.Add(id);
        }
        if (problems.Count > 0) throw new InvalidDataException($"knowledge in '{directory}' is refused: " + string.Join("; ", problems));
        return new Loaded(new KeeperKnowledge(facts), attraction, tiles, acting, Sha256(roomsPath), Sha256(rulesPath)) { SlotTargets = slots };
    }

    /// <summary>A room named as the enum names it; a number is not a name.</summary>
    /// <summary>An exact room type name: no number, no comma list, no padding (<see cref="Enum.TryParse{TEnum}(string?, out TEnum)"/> takes all three).</summary>
    private static bool KnownRoom(string? name, out RoomTypeId room)
    {
        room = default;
        return name is not null && Enum.GetNames<RoomTypeId>().Contains(name, StringComparer.Ordinal) && Enum.TryParse(name, out room);
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class RoomsDocument
    {
        public ulong HashVersion { get; set; }
        public List<RoomEntry>? Rooms { get; set; }
    }

    private sealed class RoomEntry
    {
        public string Room { get; set; } = "";
        public int CostPerTile { get; set; }
        public List<SizeEntry>? Sizes { get; set; }
        public List<AttractEntry>? Attracts { get; set; }
    }

    private sealed class SizeEntry
    {
        public int Side { get; set; }
        public int FloorSlots { get; set; }
        public int Attraction { get; set; }
        public int Gold { get; set; }
    }

    private sealed class AttractEntry
    {
        public bool CappedByThisRoom { get; set; }
        public bool Portal { get; set; }
    }

    private sealed class RulesDocument
    {
        public List<RuleEntry>? Rules { get; set; }
    }

    private sealed class RuleEntry
    {
        public string? Id { get; set; }
        public string? Rule { get; set; }
        public string? Source { get; set; }
        public string Status { get; set; } = "";
        public Dictionary<string, string>? Form { get; set; }
        public string Applies { get; set; } = "";
    }
}
