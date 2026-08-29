using System.Globalization;
using System.Text;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Harness;

/// <summary>What a scenario run produced, and what it could not do.</summary>
public sealed record ScenarioResult(
    Scenario Scenario,
    int TicksRun,
    ulong FinalStateHash,
    IReadOnlyList<ulong> HashPerTick,
    IReadOnlyList<SimEvent> Events,
    IReadOnlyList<string> Unsupported);

/// <summary>
/// Runs a scenario headlessly against the simulation and writes an evidence
/// trail: a per-tick state-hash series, the event log, and a report naming
/// every part of the scenario this build could not honour.
/// </summary>
public sealed class ScenarioRunner
{
    private readonly GameDatabase _database;
    private readonly string? _installDir;

    public ScenarioRunner(GameDatabase database, string? installDir)
    {
        _database = database;
        _installDir = installDir;
    }

    public ScenarioResult Run(Scenario scenario)
    {
        var unsupported = new List<string>();

        foreach (string key in scenario.UnknownKeys)
            unsupported.Add($"unknown key '{key}' in the scenario file");
        if (scenario.Setup.Rooms.Count > 0)
            unsupported.Add($"setup.rooms ({scenario.Setup.Rooms.Count}) - rooms are not simulated until M3");
        if (scenario.Setup.Creatures.Count > 0)
            unsupported.Add($"setup.creatures ({scenario.Setup.Creatures.Count}) - creatures are not simulated until M4");

        foreach (string observation in scenario.Observe)
            if (observation is not ("state_hash" or "events"))
                unsupported.Add($"observe.{observation} - nothing produces this yet");

        LevelMap map = ResolveMap(scenario.Map);

        IEnumerable<byte> playerIds = scenario.Setup.Players.Count > 0
            ? scenario.Setup.Players.Select(p => p.Id)
            : map.Tiles.Select(t => t.PlayerId).Distinct();

        WorldState world = WorldState.FromMap(_database, map, playerIds);
        foreach (ScenarioPlayer p in scenario.Setup.Players)
        {
            world.Player(p.Id).Gold = p.Gold;
            world.Player(p.Id).Mana = p.Mana;
        }

        // No systems are registered yet: M2 delivers the loop, not the rules.
        // The run is therefore a determinism and plumbing check, and the report
        // says so rather than implying the scenario's question was answered.
        var simulation = new GameSimulation(_database, world, scenario.Seed);
        var sink = new ListEventSink();
        simulation.ObserveWith(sink);

        var hashes = new List<ulong>(scenario.DurationTicks + 1) { simulation.StateHash() };
        for (int t = 0; t < scenario.DurationTicks; t++)
        {
            simulation.Tick();
            hashes.Add(simulation.StateHash());
        }

        if (simulation.Systems.Count == 0)
            unsupported.Add("no simulation systems are registered - the world cannot change yet (M2)");

        return new ScenarioResult(
            scenario, scenario.DurationTicks, simulation.StateHash(), hashes, sink.Events, unsupported);
    }

    /// <summary>
    /// Resolves a scenario's map field. "campaign:level1" loads a shipped map;
    /// "flat_24x20" builds a synthetic dungeon - solid impenetrable border,
    /// dirt path inside - which is a testing convenience, not a DK2 layout.
    /// </summary>
    private LevelMap ResolveMap(string spec)
    {
        const string campaign = "campaign:";
        const string flat = "flat_";

        if (spec.StartsWith(campaign, StringComparison.OrdinalIgnoreCase))
        {
            string level = spec[campaign.Length..].Trim();
            if (_installDir is null)
                throw new InvalidOperationException(
                    $"Scenario needs campaign map '{level}' but no DK2 installation was found. Set DK2_DIR.");

            return GameDatabaseLoader.LoadCampaignMap(_installDir, level)
                ?? throw new InvalidOperationException(
                    $"No campaign map named '{level}' under the installation's Data/editor/maps.");
        }

        if (spec.StartsWith(flat, StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = spec[flat.Length..].Split('x', 'X');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                || w < 3 || h < 3)
                throw new InvalidDataException($"Map spec '{spec}' is not of the form flat_WxH (min 3x3).");

            return FlatMap(w, h);
        }

        throw new InvalidDataException(
            $"Unrecognised map spec '{spec}'. Use 'campaign:<level>' or 'flat_WxH'.");
    }

    private LevelMap FlatMap(int width, int height)
    {
        byte border = FindTerrain(t => t.IsImpenetrable);
        byte floor = FindTerrain(t => t.Name.Equals("Dirt Path", StringComparison.OrdinalIgnoreCase));

        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                tiles[(y * width) + x] = new MapTile(edge ? border : floor, 0, 0, 0);
            }
        return new LevelMap(width, height, tiles);
    }

    private byte FindTerrain(Func<TerrainDef, bool> predicate)
    {
        TerrainDef? match = _database.Terrain.Values.FirstOrDefault(predicate);
        return match?.TerrainId
            ?? throw new InvalidOperationException("The loaded database has no terrain matching that requirement.");
    }

    /// <summary>Writes the run's evidence trail under the given directory.</summary>
    public static void WriteReport(ScenarioResult result, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        File.WriteAllLines(
            Path.Combine(outputDir, "hashes.tsv"),
            result.HashPerTick.Select((h, i) => i.ToString(CultureInfo.InvariantCulture) + "\t" + h.ToString("x16", CultureInfo.InvariantCulture)));

        File.WriteAllLines(
            Path.Combine(outputDir, "events.log"),
            result.Events.Select(e => e.ToString()));

        var report = new StringBuilder();
        report.AppendLine($"# Scenario run: {result.Scenario.Id}");
        report.AppendLine();
        report.AppendLine($"- source: {result.Scenario.SourcePath}");
        report.AppendLine($"- map: {result.Scenario.Map}");
        report.AppendLine($"- seed: {result.Scenario.Seed}");
        report.AppendLine($"- ticks run: {result.TicksRun}");
        report.AppendLine($"- final state hash: {result.FinalStateHash:x16}");
        report.AppendLine($"- events recorded: {result.Events.Count}");
        report.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.Scenario.Question))
        {
            report.AppendLine("## Question");
            report.AppendLine();
            report.AppendLine(result.Scenario.Question.Trim());
            report.AppendLine();
        }

        report.AppendLine("## Not answered by this run");
        report.AppendLine();
        if (result.Unsupported.Count == 0)
        {
            report.AppendLine("Nothing - every part of the scenario was applied.");
        }
        else
        {
            report.AppendLine("This build could not act on the following. Any conclusion drawn");
            report.AppendLine("from this run covers the remainder only.");
            report.AppendLine();
            foreach (string item in result.Unsupported)
                report.AppendLine($"- {item}");
        }

        File.WriteAllText(Path.Combine(outputDir, "report.md"), report.ToString());
    }
}
