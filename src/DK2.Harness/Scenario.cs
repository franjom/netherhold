using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DK2.Harness;

/// <summary>Starting resources for one player. Applied directly to PlayerState.</summary>
public sealed class ScenarioPlayer
{
    public byte Id { get; set; }
    public int Gold { get; set; }
    public int Mana { get; set; }
}

/// <summary>
/// One creature placed on the map before the run starts.
///
/// <b>Placement is the harness's, not DK2's.</b> The original's imps come out of
/// a dungeon heart or a spell and cost mana; none of that machinery exists, so a
/// scenario that wants workers says where they stand and the report says that is
/// what happened.
/// </summary>
public sealed class ScenarioCreature
{
    /// <summary>A CREATURES record name, e.g. <c>Imp</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>Where it starts, as <c>[x, y]</c>.</summary>
    public List<int> At { get; set; } = [];

    /// <summary>Whose creature it is.</summary>
    public byte Player { get; set; }
}

/// <summary>
/// Initial conditions. <see cref="Players"/> and <see cref="Creatures"/> are
/// applied; <see cref="Rooms"/> is parsed so that scenarios can be written now
/// and reported as unsupported rather than silently ignored.
/// </summary>
public sealed class ScenarioSetup
{
    public List<ScenarioPlayer> Players { get; set; } = [];

    /// <summary>Not applied yet (rooms arrive later in M3).</summary>
    public List<Dictionary<string, object>> Rooms { get; set; } = [];

    /// <summary>Creatures to place before tick 0.</summary>
    public List<ScenarioCreature> Creatures { get; set; } = [];
}

/// <summary>
/// A player command scheduled for one tick. Real input: it goes through the
/// same <see cref="DK2.Simulation.InputFrame"/> the game would use.
/// </summary>
public sealed class ScenarioCommand
{
    public int AtTick { get; set; }
    public byte Player { get; set; }

    /// <summary>Tiles to mark for digging, as <c>[[x, y], …]</c>.</summary>
    public List<List<int>> Tag { get; set; } = [];

    /// <summary>Tiles to unmark, as <c>[[x, y], …]</c>.</summary>
    public List<List<int>> Untag { get; set; } = [];
}

/// <summary>
/// A work application injected straight onto the world's queue at a given tick.
///
/// <b>This is a harness affordance, not a player command and not DK2.</b> In the
/// original a working imp produces these; no creature exists yet, so a scenario
/// that wants to watch a tile get dug has to stand in for one. Every run that
/// uses this says so under "Not answered by this run", because a dig timed by a
/// scenario file tells you nothing about how long DK2 takes to dig.
/// </summary>
public sealed class ScenarioWork
{
    public int AtTick { get; set; }
    public byte Player { get; set; }

    /// <summary>A <see cref="DK2.Simulation.TileWorkKind"/> name.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Target tile, as <c>[x, y]</c>.</summary>
    public List<int> At { get; set; } = [];

    /// <summary>How many applications to queue on that tick. Defaults to one.</summary>
    public int Repeat { get; set; } = 1;

    /// <summary>Repeat the same injection every tick until this one, inclusive.</summary>
    public int UntilTick { get; set; } = -1;
}

/// <summary>
/// A declarative experiment: the same file is meant to eventually drive both
/// our simulation and the original game (DK2_REIMPLEMENTATION_PLAN.md 6.2).
///
/// Loading is deliberately strict about its own ignorance: any key it does not
/// recognise, and any key it recognises but cannot yet act on, is reported by
/// name. A scenario that quietly does less than it says would poison the
/// regression corpus, which is the one artifact this project cannot afford to
/// have lying to it.
/// </summary>
public sealed class Scenario
{
    public string Id { get; set; } = "";

    /// <summary>What this scenario is trying to find out. Prose, for humans.</summary>
    public string Question { get; set; } = "";

    /// <summary>
    /// Either <c>campaign:&lt;level&gt;</c> to load a shipped map, or
    /// <c>flat_WxH</c> for a synthetic open dungeon.
    /// </summary>
    public string Map { get; set; } = "";

    public ulong Seed { get; set; }

    public int DurationTicks { get; set; }

    public ScenarioSetup Setup { get; set; } = new();

    /// <summary>Player commands, scheduled by tick.</summary>
    public List<ScenarioCommand> Commands { get; set; } = [];

    /// <summary>Harness-injected tile work, standing in for a creature that does not exist yet.</summary>
    public List<ScenarioWork> Work { get; set; } = [];

    /// <summary>What the run should record. Unrecognised entries are reported.</summary>
    public List<string> Observe { get; set; } = [];

    /// <summary>Top-level keys in the file that this build does not understand.</summary>
    [YamlIgnore]
    public IReadOnlyList<string> UnknownKeys { get; private set; } = [];

    [YamlIgnore]
    public string SourcePath { get; private set; } = "";

    private static readonly string[] KnownKeys =
        ["id", "question", "map", "seed", "duration_ticks", "setup", "commands", "work", "observe"];

    public static Scenario Load(string path)
    {
        string text = File.ReadAllText(path);

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        Scenario scenario = deserializer.Deserialize<Scenario>(text)
            ?? throw new InvalidDataException($"'{path}' is empty or not a scenario.");

        // Second pass purely to find keys we silently dropped above.
        var raw = deserializer.Deserialize<Dictionary<string, object>>(text) ?? [];
        scenario.UnknownKeys = raw.Keys
            .Where(k => !KnownKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        scenario.SourcePath = path;
        scenario.Validate();
        return scenario;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidDataException($"'{SourcePath}': scenario needs an id.");
        if (string.IsNullOrWhiteSpace(Map))
            throw new InvalidDataException($"'{SourcePath}': scenario '{Id}' needs a map.");
        if (DurationTicks <= 0)
            throw new InvalidDataException($"'{SourcePath}': scenario '{Id}' needs a positive duration_ticks.");
    }
}
