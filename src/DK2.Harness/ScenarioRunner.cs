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

        foreach (string observation in scenario.Observe)
            if (observation is not ("state_hash" or "events"))
                unsupported.Add($"observe.{observation} - nothing produces this yet");

        if (scenario.Work.Count > 0)
        {
            unsupported.Add(
                $"work ({scenario.Work.Count} entries) is injected by the harness, bypassing the " +
                "worker that would produce it. Nothing has decided that a creature wanted this job, " +
                "could reach the tile or was standing beside it, so this run says nothing about " +
                "who would do the work or how long it would take. Use setup.creatures and a tag " +
                "command instead to watch a worker do it");
            unsupported.Add(
                "the rate of work is UNKNOWN (Q19/Q1): the health deltas carry no unit, so tick " +
                "counts in this run are not seconds and must not be read as timings");
        }

        LevelMap map = ResolveMap(scenario.Map);

        IEnumerable<byte> playerIds = scenario.Setup.Players.Count > 0
            ? scenario.Setup.Players.Select(p => p.Id)
            : map.Tiles.Select(t => t.PlayerId).Distinct();

        WorldState world = WorldState.FromMap(_database, map, playerIds);

        // Rooms first, gold second: gold has to be stored somewhere, and a
        // treasury is the only somewhere there is.
        PlaceRooms(scenario, world, unsupported);
        PlaceResources(scenario, world, unsupported);
        SpawnCreatures(scenario, world, unsupported);

        // Registration order is part of the simulation's definition, and there
        // is exactly one of it — see SimulationSystems. A harness that ran the
        // systems in a different order from the test suite would be producing
        // evidence about neither.
        var simulation = new GameSimulation(
            _database, world, scenario.Seed, SimulationSystems.Default());

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

        AddManaCaveats(world, unsupported);

        return new ScenarioResult(
            scenario, scenario.DurationTicks, simulation.StateHash(), hashes, sink.Events, unsupported);
    }

    /// <summary>
    /// Mana accrues in every run now, so every run has to say what that number
    /// is not. Only said when somebody actually earned some — a run with no
    /// claimed land has nothing to disclaim.
    /// </summary>
    private void AddManaCaveats(WorldState world, List<string> unsupported)
    {
        int[] gain = ManaRules.GainPerSecondByPlayerIndex(world, _database);
        if (gain.All(g => g == 0)) return;

        unsupported.Add(
            "mana here is income only: nothing spends it. The Create Imp spell, keeper spells, " +
            "possession and research all cost mana in DK2 and none of them exists yet, so a mana " +
            "total in this run is what a keeper would have earned, not what they would have left");
        unsupported.Add(
            "mana is paid once a game second and a game second is TicksPerSecond ticks, which is a " +
            "placeholder (Q1). The totals are right; the wall-clock rate they imply is not a claim");

        byte? heartTerrain = ManaRules.DungeonHeartTerrainId(_database);
        if (heartTerrain is null) return;

        for (int i = 0; i < world.TileCount; i++)
        {
            if (world.TileAtIndex(i).TerrainId != heartTerrain) continue;

            unsupported.Add(
                "a dungeon heart on this map is terrain and a mana source and nothing else. In DK2 " +
                "losing it is defeat, and defeat is not implemented: a keeper whose heart is beaten " +
                "down here simply stops earning the heart's 30 a second and plays on");
            return;
        }
    }

    /// <summary>
    /// Lays down the scenario's rooms, and says what laying them down does not
    /// amount to.
    /// </summary>
    private void PlaceRooms(Scenario scenario, WorldState world, List<string> unsupported)
    {
        int placed = 0;

        foreach (ScenarioRoom r in scenario.Setup.Rooms)
        {
            RoomDef? room = _database.RoomByName(r.Type);
            if (room is null)
            {
                unsupported.Add($"setup.rooms entry '{r.Type}' is not a room in the database");
                continue;
            }

            if (r.At.Count != 2)
            {
                unsupported.Add($"setup.rooms entry '{r.Type}' needs at: [x, y]");
                continue;
            }

            int w = r.Size.Count == 2 ? r.Size[0] : 1;
            int h = r.Size.Count == 2 ? r.Size[1] : 1;
            if (w < 1 || h < 1)
            {
                unsupported.Add($"setup.rooms entry '{r.Type}' has size [{w},{h}]");
                continue;
            }

            int refused = 0;
            for (int y = r.At[1]; y < r.At[1] + h; y++)
            {
                for (int x = r.At[0]; x < r.At[0] + w; x++)
                {
                    if (!world.Contains(x, y))
                    {
                        refused++;
                        continue;
                    }

                    ref TileState tile = ref world.Tile(x, y);
                    if (!RoomRules.IsSuitableGround(_database, tile, room))
                    {
                        refused++;
                        continue;
                    }

                    RoomRules.Place(_database, ref tile, room, r.Player);
                    placed++;
                }
            }

            if (refused > 0)
            {
                unsupported.Add(
                    $"setup.rooms entry '{r.Type}' at [{r.At[0]},{r.At[1]}] size [{w},{h}]: " +
                    $"{refused} of its {w * h} tiles are not ground this room can sit on and were skipped");
            }
        }

        if (placed == 0) return;

        unsupported.Add(
            "setup.rooms lays room terrain down directly: nothing is charged for it, no imp builds " +
            "it, and no room does anything a room does beyond being terrain its owner holds. A " +
            "treasury stores gold because a variable says how much per tile; a library, a lair and " +
            "a hatchery are inert");
    }

    /// <summary>
    /// Applies starting mana, and stores starting gold — which can only go where
    /// there is room for it.
    /// </summary>
    private void PlaceResources(Scenario scenario, WorldState world, List<string> unsupported)
    {
        foreach (ScenarioPlayer p in scenario.Setup.Players)
        {
            world.Player(p.Id).Mana = p.Mana;

            if (p.Gold <= 0) continue;

            int stored = GoldStore.Deposit(world, _database, p.Id, p.Gold);
            if (stored == p.Gold) continue;

            unsupported.Add(
                $"setup.players[{p.Id}].gold: {p.Gold - stored} of {p.Gold} had nowhere to go. A " +
                "keeper's gold lives in treasury and dungeon-heart tiles and nowhere else here; " +
                "DK2 would drop the rest on the floor as gold piles, which are objects and are not " +
                "implemented (Q31). Give the player a treasury in setup.rooms to hold it");
        }
    }

    /// <summary>
    /// Places the scenario's creatures, and says what having them still does not
    /// buy the run.
    /// </summary>
    private void SpawnCreatures(Scenario scenario, WorldState world, List<string> unsupported)
    {
        int workers = 0;

        foreach (ScenarioCreature c in scenario.Setup.Creatures)
        {
            CreatureDef? definition = _database.CreatureByName(c.Type);
            if (definition is null)
            {
                unsupported.Add($"setup.creatures entry '{c.Type}' is not a creature in the database");
                continue;
            }

            if (c.At.Count != 2 || !world.Contains(c.At[0], c.At[1]))
            {
                unsupported.Add(
                    $"setup.creatures entry '{c.Type}' at [{string.Join(",", c.At)}] is not a tile on this map");
                continue;
            }

            world.SpawnCreature(definition.CreatureId, c.Player, new TilePos(c.At[0], c.At[1]));
            if (definition.Flags.HasFlag(CreatureFlags.IsWorker)) workers++;
        }

        if (scenario.Setup.Creatures.Count == 0) return;

        unsupported.Add(
            "setup.creatures places creatures directly. DK2 makes imps from a dungeon heart, from " +
            "the Create Imp spell at a mana cost, and from level specials - none of which exists " +
            "yet, so nothing here says how many workers a keeper would actually have");

        if (workers == 0)
        {
            unsupported.Add(
                "none of the placed creatures is a worker, and only workers do anything at all " +
                "yet: no creature has needs, moods, combat or a job of its own");
            return;
        }

        unsupported.Add(
            "workers only do work the keeper marked. Claiming floor, reinforcing walls, carrying " +
            "gold to a treasury and fleeing a fight are all real imp jobs that are not " +
            "implemented, and the priority order between them is UNKNOWN (Q2/Q24)");
        unsupported.Add(
            "routing is ours, not DK2's (Q5): a four-connected shortest-path search with uniform " +
            "step costs. Travel times in this run are a consequence of that choice");
    }

    /// <summary>Turns the scenario's scheduled commands into one input frame per tick.</summary>
    private Dictionary<int, InputFrame> BuildInput(Scenario scenario, List<string> unsupported)
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

            bool built = c.Build is not null && TryBuild(c, list, unsupported);

            if (c.Tag.Count == 0 && c.Untag.Count == 0 && !built)
                unsupported.Add($"commands entry at_tick={c.AtTick} names nothing this build can do");
        }

        return byTick.ToDictionary(kv => kv.Key, kv => new InputFrame(kv.Value));
    }

    private bool TryBuild(ScenarioCommand c, List<PlayerCommand> into, List<string> unsupported)
    {
        ScenarioBuild build = c.Build!;
        RoomDef? room = _database.RoomByName(build.Type);
        if (room is null)
        {
            unsupported.Add(
                $"commands entry at_tick={c.AtTick} builds '{build.Type}', which is not a room in the database");
            return false;
        }

        List<TilePos> tiles = ToTiles(build.Tiles);
        if (tiles.Count == 0)
        {
            unsupported.Add($"commands entry at_tick={c.AtTick} builds '{build.Type}' on no tiles");
            return false;
        }

        into.Add(new BuildRoomCommand(c.Player, room.RoomId, tiles));
        return true;
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
    /// synthetic dungeon - impenetrable border, dirt path inside;
    /// <c>rock_24x20</c> the same thing filled with diggable rock, which is what
    /// a dig experiment needs; and <c>chamber_24x20</c> solid rock with a small
    /// open chamber at the centre, which is what an experiment with workers
    /// needs, since a worker has to stand somewhere. All three are testing
    /// conveniences and none is a DK2 layout.
    /// </summary>
    private LevelMap ResolveMap(string spec)
    {
        const string campaign = "campaign:";
        const string flat = "flat_";
        const string rock = "rock_";
        const string chamber = "chamber_";

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

        foreach (string prefix in new[] { flat, rock, chamber })
        {
            if (!spec.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            string[] parts = spec[prefix.Length..].Split('x', 'X');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                || w < 3 || h < 3)
                throw new InvalidDataException(
                    $"Map spec '{spec}' is not of the form {prefix}WxH (min 3x3).");

            if (prefix == chamber && (w < 7 || h < 7))
                throw new InvalidDataException(
                    $"Map spec '{spec}' needs at least 7x7 to hold a chamber and a wall around it.");

            return SyntheticMap(w, h, solid: prefix != flat, chamber: prefix == chamber);
        }

        throw new InvalidDataException(
            $"Unrecognised map spec '{spec}'. Use 'campaign:<level>', 'flat_WxH', 'rock_WxH' or 'chamber_WxH'.");
    }

    private LevelMap SyntheticMap(int width, int height, bool solid, bool chamber = false)
    {
        byte border = FindTerrain(t => t.IsImpenetrable);
        byte floor = FindTerrain(t => t.Name.Equals("Dirt Path", StringComparison.OrdinalIgnoreCase));
        byte fill = solid
            ? FindTerrain(t => t.IsSolid && t.IsTaggable && !t.IsImpenetrable && t.GoldValue == 0)
            : floor;

        // A 3x3 opening at the centre. Small on purpose: it is a place to stand,
        // not a dungeon, and anything larger would start to look like a claim
        // about how DK2 lays a level out.
        int chamberX = width / 2;
        int chamberY = height / 2;

        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool open = chamber
                    && Math.Abs(x - chamberX) <= 1
                    && Math.Abs(y - chamberY) <= 1;

                byte terrain = edge ? border : open ? floor : fill;
                tiles[(y * width) + x] = new MapTile(terrain, 0, 0, 0);
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
