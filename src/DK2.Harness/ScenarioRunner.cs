// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.AI;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Harness;

public sealed record KeeperCommandEmission(int InputTick, PlayerCommand Command);

/// <summary>What a scenario run produced, and what it could not do.</summary>
public sealed record ScenarioResult(
    Scenario Scenario,
    int TicksRun,
    ulong FinalStateHash,
    IReadOnlyList<ulong> HashPerTick,
    IReadOnlyList<SimEvent> Events,
    IReadOnlyList<string> Unsupported)
{
    /// <summary>Every expectation the run did not meet, one line each. Empty is the
    /// only passing value; a scenario with no expectations produces an empty list too,
    /// which is why the count of scenarios carrying any is tracked separately.</summary>
    public IReadOnlyList<string> ExpectationFailures { get; init; } = [];

    /// <summary>
    /// The world at the end of the run, drawn as text through the same
    /// presentation boundary a Unity view uses.
    ///
    /// <para>Worth having in a scenario report for a reason the hash series
    /// cannot cover: a hash tells you two runs differed, and a picture tells you
    /// <i>where</i>. When the imp-corridor scenario's hash moved after the work
    /// stance changed (ADR-0028), the question was always "moved how" — and
    /// answering it meant reading the event log line by line.</para>
    ///
    /// <para>Empty when the run rendered nothing, which is not an error.</para>
    /// </summary>
    public string Picture { get; init; } = "";

    /// <summary>
    /// Where the run's art came from — normally "all placeholders", and saying
    /// so is the point (plan §7.2).
    /// </summary>
    public string AssetSummary { get; init; } = "";

    /// <summary>
    /// Which balance table produced these numbers.
    ///
    /// <para>Not in <see cref="Unsupported"/>, because it is not a shortfall —
    /// it is provenance. A campaign level ships its own VARIABLES and 26 ids are
    /// overridden by at least one shipped level, so "which table" is a real
    /// question about any run on a campaign map, and a reader of the report
    /// should not have to guess.</para>
    /// </summary>
    public string BalanceTable { get; init; } = "GlobalVariables.kwd";

    /// <summary>Commands emitted by computer keepers, before the next-tick input delay.</summary>
    public IReadOnlyList<PlayerCommand> EmittedKeeperCommands { get; init; } = [];
    public IReadOnlyList<KeeperCommandEmission> KeeperCommandLog { get; init; } = [];

    /// <summary>Each computer keeper's own state at the end of the run, by player id: what it
    /// decided, which no command shows (the attack target, the flags, the stored tunnels).</summary>
    public IReadOnlyDictionary<PlayerId, KeeperAIState> KeeperStates { get; init; } = new Dictionary<PlayerId, KeeperAIState>();

    /// <summary>Where the run's wall-clock time went. Not part of the report: see <see cref="RunTimings"/>.</summary>
    public RunTimings? Timings { get; init; }
}

/// <summary>
/// Runs a scenario headlessly against the simulation and writes an evidence
/// trail: a per-tick state-hash series, the event log, and a report naming
/// every part of the scenario this build could not honour.
/// </summary>
public sealed class ScenarioRunner
{
    /// <summary>How a scenario names one of the original campaign maps.</summary>
    internal const string CampaignPrefix = "campaign:";

    private readonly GameDatabase _database;
    private readonly string? _installDir;

    public ScenarioRunner(GameDatabase database, string? installDir)
    {
        _database = database;
        _installDir = installDir;
    }

    /// <summary>
    /// Runs a scenario, on the balance table the level it names actually uses.
    ///
    /// <para>A campaign level ships its own VARIABLES, and 26 ids are overridden
    /// by at least one shipped level — so a run on <c>campaign:level1</c> must
    /// use level 1's numbers, not the global defaults. Rather than thread a
    /// second database through every helper, the level's database is resolved
    /// first and a runner built around it does the work; every rule below then
    /// reads the one <c>_database</c> that is correct for this run.</para>
    /// </summary>
    public ScenarioResult Run(Scenario scenario)
    {
        GlobalVariables? levelVariables = ResolveLevelVariables(scenario.Map);
        string? levelName = LevelNameOf(scenario.Map);
        LevelDef? level = levelName is null || _installDir is null
            ? null
            : GameDatabaseLoader.LoadCampaignLevel(_installDir, levelName);
        return levelVariables is null && level is null
            ? RunAgainstThisDatabase(scenario, null)
            : new ScenarioRunner(_database.ForLevel(levelVariables, level), _installDir)
                .RunAgainstThisDatabase(scenario, levelVariables is null ? null : levelName);
    }

    /// <summary>The level's own VARIABLES, for a scenario that names a campaign map.</summary>
    private GlobalVariables? ResolveLevelVariables(string mapSpec)
    {
        string? level = LevelNameOf(mapSpec);
        return level is null || _installDir is null
            ? null
            : GameDatabaseLoader.LoadCampaignVariables(_installDir, level);
    }

    private static string? LevelNameOf(string mapSpec) =>
        mapSpec.StartsWith(CampaignPrefix, StringComparison.OrdinalIgnoreCase)
            ? mapSpec[CampaignPrefix.Length..].Trim()
            : null;

    private ScenarioResult RunAgainstThisDatabase(Scenario scenario, string? campaignLevel)
    {
        var clock = new RunClock();
        var unsupported = new List<string>();

        // Which balance table this run used, recorded as provenance rather than
        // as a shortfall. A run that silently used the global defaults on a
        // level that overrides them would be evidence about a game nobody plays.
        string balanceTable = campaignLevel is null
            ? "GlobalVariables.kwd"
            : $"GlobalVariables.kwd + {campaignLevel}Variables.kld " +
              $"({_database.Variables.Misc.Count} values, " +
              $"{Count(_database.Variables.Availabilities.Count, "availability entry", "availability entries")}, " +
              $"{Count(_database.Variables.CreaturePool.Count, "creature-pool entry", "creature-pool entries")})";

        static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

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
                "how many swings a tile takes and how long they take are both known now (Q47, " +
                "Q1): an application is a fifth of the tabled delta - the worker's Efficiency of " +
                "80% over the four ticks of a game second, a stat DK2 prints on its status bar " +
                "and one a slap or a well-aimed drop raises to 90% - and one lands every tick, " +
                "four a second, a second being a real second. So a tick count here IS a wall-clock " +
                "timing at 250 ms a tick");
        }

        // A run that used a number the game's own files do not contain has to
        // say which, every time. There is exactly one (ADR-0023), and this
        // enumerates rather than naming it so a second would report itself.
        List<string> corrected = Enum.GetValues<TileWorkKind>()
            .Select(TileWorkRules.Variable)
            .Where(v => ObservedCorrections.IsCorrected(_database, v))
            .Select(v => $"{v} (shipped {_database[v]}, " +
                         $"used {ObservedCorrections.TileWorkPerSecond(_database, v)})")
            .ToList();

        if (corrected.Count > 0)
            unsupported.Add(
                $"this run used {corrected.Count} corrected balance value(s) rather than what the " +
                $"game ships: {string.Join(", ", corrected)}. Each is a case where the shipped " +
                "number demonstrably cannot produce behaviour measured in the original, the " +
                "balance table was searched for an alternative and came back empty, and the " +
                "correction is guarded on the exact shipped value. See ObservedCorrections and " +
                "ADR-0023");

        var builder = new ScenarioWorldBuilder(_database, _installDir);
        var inputs = new ScenarioInputBuilder(_database, _installDir);
        LevelMap map = builder.ResolveMap(scenario.Map);

        IEnumerable<byte> playerIds = scenario.Setup.Players.Count > 0
            ? scenario.Setup.Players.Select(p => p.Id)
            : map.Tiles.Select(t => t.PlayerId).Distinct();
        if (campaignLevel is not null && _installDir is not null)
            playerIds = ActivationOrder(playerIds, GameDatabaseLoader.LoadCampaignPlayers(_installDir, campaignLevel));

        WorldState world = WorldState.FromMap(
            _database, map, playerIds, fogOfWarEnabled: scenario.FogOfWar);

        // The level's own contents before the scenario's, so a scenario that
        // asks for both is adding to the shipped level rather than racing it.
        HashSet<byte> goldPaidByLevel = builder.PlaceShippedThings(scenario, world, unsupported);

        // Rooms first, gold second: gold has to be stored somewhere, and a
        // treasury is the only somewhere there is.
        builder.PlaceRooms(scenario, world, unsupported);
        builder.PlaceResources(scenario, world, unsupported, goldPaidByLevel);
        builder.SpawnCreatures(scenario, world, unsupported);

        TriggerTree? triggers = builder.ResolveShippedTriggers(scenario, campaignLevel);

        // Registration order is part of the simulation's definition, and there
        // is exactly one of it — see SimulationSystems. A harness that ran the
        // systems in a different order from the test suite would be producing
        // evidence about neither.
        var simulation = new GameSimulation(
            _database, world, scenario.CreateRandom(), SimulationSystems.Default(), triggers);

        var sink = new ListEventSink();
        simulation.ObserveWith(sink);
        simulation.ProfileWith(clock);

        Dictionary<int, InputFrame> input = inputs.BuildInput(scenario, unsupported);
        List<ScenarioWork> work = ScenarioInputBuilder.ResolveWork(scenario, world, unsupported);
        List<KeeperAI> keepers = inputs.CreateComputerKeepers(scenario, campaignLevel, world, unsupported);
        var emitted = new List<PlayerCommand>();
        var commandLog = new List<KeeperCommandEmission>();
        IReadOnlyList<PlayerCommand> pending = [];

        var hashes = new List<ulong>(scenario.DurationTicks + 1) { simulation.StateHash() };
        for (int t = 0; t < scenario.DurationTicks; t++)
        {
            ScenarioInputBuilder.InjectWork(world, work, t);
            InputFrame scripted = input.TryGetValue(t, out InputFrame frame) ? frame : InputFrame.Empty;
            clock.BeginTick(t);
            simulation.Tick(pending.Count == 0 ? scripted :
                new InputFrame([.. scripted.All, .. pending]));
            clock.Begin(RunClock.StateHash);
            hashes.Add(simulation.StateHash());
            clock.End(RunClock.StateHash);
            if (t + 1 == scenario.DurationTicks)
            {
                clock.EndTick();
                break;
            }
            var next = new List<PlayerCommand>();
            clock.Begin(RunClock.KeeperAi);
            foreach (KeeperAI keeper in keepers)
                next.AddRange(keeper.Think(_database, world));
            clock.End(RunClock.KeeperAi);
            clock.EndTick();
            emitted.AddRange(next);
            foreach (PlayerCommand command in next)
                commandLog.Add(new KeeperCommandEmission(t + 1, command));
            pending = next;
        }

        if (simulation.Systems.Count == 0)
            unsupported.Add("no simulation systems are registered - the world cannot change");

        AddManaCaveats(world, unsupported);

        // Draw the finished world through the presentation boundary. Runs with
        // no assets installed, which is every run: see the summary line.
        AssetProviderChain assets = AssetProviderChain.Standard(
            replacementRoot: null, convertedRoot: ConvertedAssetRoot);
        var renderer = new TextRenderer(new WorldView(_database, scenario.ViewingPlayer), assets);
        string picture = renderer.Render(world);

        return new ScenarioResult(
            scenario, scenario.DurationTicks, simulation.StateHash(), hashes, sink.Events, unsupported)
        {
            ExpectationFailures = CheckExpectations(scenario, sink.Events),
            Picture = picture,
            AssetSummary = assets.Summarise(),
            BalanceTable = balanceTable,
            EmittedKeeperCommands = emitted,
            KeeperCommandLog = commandLog,
            KeeperStates = keepers.ToDictionary(k => k.PlayerId, k => k.State),
            Timings = clock.Stop(),
        };
    }

    /// <summary>
    /// Where the converter puts its output (plan §7.1). Named here rather than
    /// inline because it is the one path the harness knows, and it is a
    /// directory that normally does not exist — which is exactly the "no assets"
    /// case the placeholder covers.
    /// </summary>
    public const string ConvertedAssetRoot = "build/converted";

    /// <summary>Tests every <see cref="ScenarioExpectation"/> against the events the run
    /// produced, and returns one line per unmet claim. Reports the count it did see, because
    /// "expected 531, saw 530" is a different problem from "expected 531, saw none".</summary>
    private static IReadOnlyList<string> CheckExpectations(
        Scenario scenario, IReadOnlyList<SimEvent> events)
    {
        var failures = new List<string>();
        foreach (ScenarioExpectation expectation in scenario.Expect)
        {
            int seen = 0;
            foreach (SimEvent observed in events)
            {
                if (observed.Kind != expectation.Event) continue;
                if (expectation.Detail.Length > 0
                    && !observed.Detail.Contains(expectation.Detail, StringComparison.Ordinal))
                    continue;
                if (expectation.AtTick is int tick && observed.Tick.Value != tick) continue;
                seen++;
            }

            bool met = expectation.Absent
                ? seen == 0
                : expectation.Count is int want ? seen == want : seen > 0;

            if (!met) failures.Add($"expected {expectation}, saw {seen}");
        }

        return failures;
    }

    /// <summary>The order the world activates its players in: DK2 activates one player per PLAYERS
    /// record, in file order (<c>0x0056DAD0</c> calls <c>0x0051EF80</c> per record), which sets the
    /// active list the verdict walks. Ids with no record keep their given order, in front: a
    /// harness convention (DK2 never activates a player without a record), so they end the list.</summary>
    public static IReadOnlyList<byte> ActivationOrder(IEnumerable<byte> playerIds, IReadOnlyList<PlayerDef>? records)
    {
        if (records is null) return playerIds.ToList();
        List<byte> recordOrder = records.Select(p => p.PlayerId).ToList();
        return playerIds.OrderBy(id => recordOrder.IndexOf(id)).ToList();
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
            "mana here has both halves of the economy. Income is land and " +
            "a dungeon heart; outgoings are the keeper spells' cast costs and upkeep (every " +
            "spell is built, ADR-0350 and ADR-0352 (Sight of Evil pays and makes no region): a summoned Horned Reaper costs 2000 a " +
            "second, Possession its creature's charge), the 7 a second every imp charges its " +
            "keeper (Q53) and a keeper's trap upkeep (Q59). A scenario pays only for what it " +
            "casts, so a mana total in this run is what a keeper would have only if it casts " +
            "what that keeper would");
        unsupported.Add(
            "mana is paid once a game second, and a game second is four ticks of 250 ms - read " +
            "from the binary and stopwatched by the owner, so Q1 is settled and the wall-clock " +
            "rate these totals imply is now a claim this project stands behind");

    }

    /// <summary>Writes the run's evidence trail under the given directory.</summary>
    public static void WriteReport(ScenarioResult result, string outputDir)
        => ScenarioReportWriter.Write(result, outputDir);
}
