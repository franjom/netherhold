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
/// Initial conditions. Only <see cref="Players"/> is applied today; the rest
/// are parsed so that scenarios can be written now and reported as unsupported
/// rather than silently ignored.
/// </summary>
public sealed class ScenarioSetup
{
    public List<ScenarioPlayer> Players { get; set; } = [];

    /// <summary>Not applied yet (rooms arrive with M3).</summary>
    public List<Dictionary<string, object>> Rooms { get; set; } = [];

    /// <summary>Not applied yet (creatures arrive with M4).</summary>
    public List<Dictionary<string, object>> Creatures { get; set; } = [];
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

    /// <summary>What the run should record. Unrecognised entries are reported.</summary>
    public List<string> Observe { get; set; } = [];

    /// <summary>Top-level keys in the file that this build does not understand.</summary>
    [YamlIgnore]
    public IReadOnlyList<string> UnknownKeys { get; private set; } = [];

    [YamlIgnore]
    public string SourcePath { get; private set; } = "";

    private static readonly string[] KnownKeys =
        ["id", "question", "map", "seed", "duration_ticks", "setup", "observe"];

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
