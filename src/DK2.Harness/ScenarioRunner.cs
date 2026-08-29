using System.Globalization;
using System.Text;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

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
            unsupported.Add($"setup.rooms ({scenario.Setup.Rooms.Count}) - rooms are not simulated yet");
        if (scenario.Setup.Creatures.Count > 0)
            unsupported.Add($"setup.creatures ({scenario.Setup.Creatures.Count}) - creatures are not simulated until M4");

        foreach (string observation in scenario.Observe)
            if (observation is not ("state_hash" or "events"))
                unsupported.Add($"observe.{observation} - nothing produces this yet");

        if (scenario.Work.Count > 0)
        {
            unsupported.Add(
                $"work ({scenario.Work.Count} entries) is injected by the harness. In DK2 a working " +
                "imp produces these; no creature exists yet, so this run says nothing about who " +
                "would do the work, whether they could reach the tile, or how long it would take");
            unsupported.Add(
                "the rate of work is UNKNOWN (Q19/Q1): the health deltas carry no unit, so tick " +
                "counts in this run are not seconds and must not be read as timings");
        }

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

        // Registration order is part of the simulation's definition: tags are
        // applied before the work that reads them, so a tile marked on tick N is
        // marked for the whole of tick N.
        var simulation = new GameSimulation(
            _database, world, scenario.Seed,
            [new TileTagSystem(), new TileWorkSystem()]);

        var sink = new ListEventSink();
        simulation.ObserveWith(sink);

        Dictionary<int, InputFrame> input = BuildInput(scenario, unsupported);
        List<ScenarioWork> work = ResolveWork(scenario, world, unsupported);

        var hashes = new List<ulong>(scenario.DurationTicks + 1) { simulation.StateHash() };
        for (int t = 0; t < scenario.DurationTicks; t++)
        {
            InjectWork(world, work, t);
            simulation.Tick(input.TryGetValue(t, out InputFrame frame) ? frame : InputFrame.Empty);
            hashes.Add(simulation.StateHash());
        }

        if (simulation.Systems.Count == 0)
            unsupported.Add("no simulation systems are registered - the world cannot change");

        return new ScenarioResult(
            scenario, scenario.DurationTicks, simulation.StateHash(), hashes, sink.Events, unsupported);
    }

    /// <summary>Turns the scenario's scheduled commands into one input frame per tick.</summary>
    private static Dictionary<int, InputFrame> BuildInput(Scenario scenario, List<string> unsupported)
    {
        var byTick = new Dictionary<int, List<PlayerCommand>>();

        foreach (ScenarioCommand c in scenario.Commands)
        {
            if (c.AtTick < 0 || c.AtTick >= scenario.DurationTicks)
            {
                unsupported.Add(
                    $"commands entry at_tick={c.AtTick} falls outside the run (0..{scenario.DurationTicks - 1})");
                continue;
            }

            if (!byTick.TryGetValue(c.AtTick, out List<PlayerCommand>? list))
                byTick[c.AtTick] = list = [];

            if (c.Tag.Count > 0) list.Add(new TagTilesCommand(c.Player, ToTiles(c.Tag), true));
            if (c.Untag.Count > 0) list.Add(new TagTilesCommand(c.Player, ToTiles(c.Untag), false));
            if (c.Tag.Count == 0 && c.Untag.Count == 0)
                unsupported.Add($"commands entry at_tick={c.AtTick} names no tag or untag tiles");
        }

        return byTick.ToDictionary(kv => kv.Key, kv => new InputFrame(kv.Value));
    }

    private static List<TilePos> ToTiles(List<List<int>> pairs) =>
        pairs.Where(p => p.Count == 2).Select(p => new TilePos(p[0], p[1])).ToList();

    /// <summary>Validates the scenario's work entries, dropping and reporting bad ones.</summary>
    private static List<ScenarioWork> ResolveWork(
        Scenario scenario, WorldState world, List<string> unsupported)
    {
        var usable = new List<ScenarioWork>();

        foreach (ScenarioWork w in scenario.Work)
        {
            if (!Enum.TryParse(w.Kind, ignoreCase: true, out TileWorkKind _))
            {
                unsupported.Add(
                    $"work kind '{w.Kind}' is not one of {string.Join(", ", Enum.GetNames<TileWorkKind>())}");
                continue;
            }

            if (w.At.Count != 2 || !world.Contains(w.At[0], w.At[1]))
            {
                unsupported.Add($"work entry at [{string.Join(",", w.At)}] is not a tile on this map");
                continue;
            }

            if (w.Repeat < 1)
            {
                unsupported.Add($"work entry at_tick={w.AtTick} has repeat={w.Repeat}");
                continue;
            }

            usable.Add(w);
        }

        return usable;
    }

    private static void InjectWork(WorldState world, List<ScenarioWork> work, int tick)
    {
        foreach (ScenarioWork w in work)
        {
            int last = w.UntilTick < w.AtTick ? w.AtTick : w.UntilTick;
            if (tick < w.AtTick || tick > last) continue;

            var kind = Enum.Parse<TileWorkKind>(w.Kind, ignoreCase: true);
            for (int i = 0; i < w.Repeat; i++)
                world.QueueWork(w.At[0], w.At[1], kind, w.Player);
        }
    }

    /// <summary>
    /// Resolves a scenario's map field.
    ///
    /// <c>campaign:level1</c> loads a shipped map. <c>flat_24x20</c> builds a
    /// synthetic dungeon - impenetrable border, dirt path inside - and
    /// <c>rock_24x20</c> the same thing filled with diggable rock, which is what
    /// a dig experiment needs. Both are testing conveniences and neither is a
    /// DK2 layout.
    /// </summary>
    private LevelMap ResolveMap(string spec)
    {
        const string campaign = "campaign:";
        const string flat = "flat_";
        const string rock = "rock_";

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

        foreach (string prefix in new[] { flat, rock })
        {
            if (!spec.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            string[] parts = spec[prefix.Length..].Split('x', 'X');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                || w < 3 || h < 3)
                throw new InvalidDataException(
                    $"Map spec '{spec}' is not of the form {prefix}WxH (min 3x3).");

            return SyntheticMap(w, h, solid: prefix == rock);
        }

        throw new InvalidDataException(
            $"Unrecognised map spec '{spec}'. Use 'campaign:<level>', 'flat_WxH' or 'rock_WxH'.");
    }

    private LevelMap SyntheticMap(int width, int height, bool solid)
    {
        byte border = FindTerrain(t => t.IsImpenetrable);
        byte fill = solid
            ? FindTerrain(t => t.IsSolid && t.IsTaggable && !t.IsImpenetrable && t.GoldValue == 0)
            : FindTerrain(t => t.Name.Equals("Dirt Path", StringComparison.OrdinalIgnoreCase));

        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                tiles[(y * width) + x] = new MapTile(edge ? border : fill, 0, 0, 0);
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
