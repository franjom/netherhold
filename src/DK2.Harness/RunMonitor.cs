// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Harness;

/// <summary>What a run should watch besides its hashes (ADR-0552 Stage C). All of it only observes:
/// none of it changes the state hash or draws from the simulation's random stream.</summary>
public sealed record RunOptions
{
    public IReadOnlyList<ScenarioController> Controllers { get; init; } = [];

    /// <summary>Where to write the input journal, or null for none.</summary>
    public Stream? Journal { get; init; }

    /// <summary>Record trigger records reached, creature-ticks per DK2 state and event kinds.</summary>
    public bool Coverage { get; init; }

    /// <summary>Sample each player's progress every this-many ticks; 0 for never.</summary>
    public int ProgressEvery { get; init; }

    /// <summary>A controlled player whose indicators stay still this long is a stall candidate.</summary>
    public int StallWindowTicks { get; init; } = 1200;

    /// <summary>Every this-many ticks, save and restore the world and run the copy beside the live
    /// one for <see cref="SnapshotCheckTicks"/> ticks; 0 for never.</summary>
    public int SnapshotCheckEvery { get; init; }

    public int SnapshotCheckTicks { get; init; } = 40;
}

/// <summary>One progress sample for one player: what a stall is judged on.</summary>
public sealed record ProgressSample(int Tick, byte Player, byte Status, int OwnedTiles, int RoomTiles, int Gold, int Mana, int Creatures);

/// <summary>What a run touched: trigger records reached, creature-ticks per DK2 state id, events per kind.</summary>
public sealed record RunCoverage(
    int TriggerRecordsReached, IReadOnlyDictionary<int, long> CreatureTicksByState, IReadOnlyDictionary<string, int> EventsByKind);

/// <summary>
/// The run's observers. Progress is read from <see cref="WorldState"/> directly: the monitor is
/// privileged test code, separate from what any controller sees. A stall is a candidate, never a
/// verdict. The snapshot check is the one invariant with a failure verdict, and its source is the
/// project's own contract: a restored world continues with identical hashes (<c>WorldSnapshot</c>,
/// <c>SnapshotTests</c>).
/// </summary>
internal sealed class RunMonitor(
    RunOptions options, GameDatabase database, Func<TriggerTree?> freshTriggers, List<ScenarioWork> work, int durationTicks)
{
    private readonly HashSet<ushort> _reached = [];
    private readonly Dictionary<int, long> _states = [];
    private readonly Dictionary<byte, int> _lastCommandTick = [];
    private readonly Dictionary<byte, (int Since, ProgressSample Sample)> _still = [];
    private readonly HashSet<byte> _flagged = [];
    private GameSimulation? _shadow;
    private int _shadowUntil;

    public List<ProgressSample> Progress { get; } = [];
    public List<string> StallCandidates { get; } = [];
    public List<string> InvariantFailures { get; } = [];

    /// <summary>How many snapshot checks started, so a clean run can be told from one that never checked.</summary>
    public int SnapshotChecks { get; private set; }

    public void Start(GameSimulation simulation)
    {
        if (options.Coverage) simulation.TrackTriggerReach = true;
    }

    public void ControllerIssued(PlayerId player, int inputTick) => _lastCommandTick[player.Value] = inputTick;

    /// <summary>After tick <paramref name="tick"/> ran with <paramref name="given"/> and the live
    /// world's hash is <paramref name="liveHash"/>.</summary>
    public void AfterTick(GameSimulation live, int tick, in InputFrame given, ulong liveHash)
    {
        int done = tick + 1;
        if (_shadow is not null) StepShadow(tick, given, liveHash);
        // A check started on the last tick could never step, so none is.
        if (options.SnapshotCheckEvery > 0 && _shadow is null && done % options.SnapshotCheckEvery == 0 && done < durationTicks)
            StartShadow(live, done);

        if (options.Coverage)
        {
            if (live.LastTriggerRun?.Reached is HashSet<ushort> reached) _reached.UnionWith(reached);
            foreach (CreatureState creature in live.World.Creatures)
                _states[creature.Dk2StateId] = _states.GetValueOrDefault(creature.Dk2StateId) + 1;
        }

        if (options.ProgressEvery > 0 && done % options.ProgressEvery == 0) Sample(live.World, done);
    }

    public RunCoverage? Coverage(IReadOnlyList<SimEvent> events) => options.Coverage
        ? new RunCoverage(_reached.Count, _states,
            events.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal))
        : null;

    private void StartShadow(GameSimulation live, int done)
    {
        _shadowUntil = done + options.SnapshotCheckTicks;
        SnapshotChecks++;
        try
        {
            using var stream = new MemoryStream();
            WorldSnapshot.Write(stream, live);
            stream.Position = 0;
            _shadow = WorldSnapshot.Read(stream, database, SimulationSystems.Default(), freshTriggers());
        }
        catch (Exception ex)
        {
            // A save or restore that throws is the failure this check exists to find, not a crash.
            InvariantFailures.Add($"snapshot at tick {done}: {ex.GetType().FullName}: {ex.Message}");
            _shadow = null;
            return;
        }
        if (_shadow.StateHash() != live.StateHash())
        {
            InvariantFailures.Add($"snapshot at tick {done}: the restored world hashes differently before any tick");
            _shadow = null;
        }
    }

    private void StepShadow(int tick, in InputFrame given, ulong liveHash)
    {
        try
        {
            ScenarioInputBuilder.InjectWork(_shadow!.World, work, tick);
            _shadow.Tick(given);
        }
        catch (Exception ex)
        {
            InvariantFailures.Add(
                $"snapshot continuation: the world restored at tick {_shadowUntil - options.SnapshotCheckTicks} " +
                $"threw at tick {tick + 1}: {ex.GetType().FullName}: {ex.Message}");
            _shadow = null;
            return;
        }
        if (_shadow.StateHash() != liveHash)
        {
            InvariantFailures.Add(
                $"snapshot continuation: the world restored at tick {_shadowUntil - options.SnapshotCheckTicks} " +
                $"diverged from the live run after tick {tick + 1}");
            _shadow = null;
        }
        else if (tick + 1 >= _shadowUntil) _shadow = null;
    }

    private void Sample(WorldState world, int done)
    {
        // One pass over tiles and creatures for all players, indexed by player id.
        var owned = new int[256];
        var roomTiles = new int[256];
        var creatures = new int[256];
        for (int i = 0; i < world.TileCount; i++)
        {
            TileState tile = world.TileAtIndex(i);
            owned[tile.OwnerPlayerId]++;
            if (database.TerrainById(tile.TerrainId).IsRoom) roomTiles[tile.OwnerPlayerId]++;
        }
        foreach (CreatureState c in world.Creatures)
            if (c.Activity != CreatureActivity.Dead) creatures[c.OwnerPlayerId]++;
        foreach (PlayerId id in world.PlayerIds)
        {
            var sample = new ProgressSample(done, id.Value, world.Player(id).Status, owned[id.Value], roomTiles[id.Value],
                GoldStore.Total(world, id), world.Player(id).Mana, creatures[id.Value]);
            Progress.Add(sample);
            JudgeStall(sample);
        }
    }

    /// <summary>Two signals, kept apart (plan §10): a controller that issued nothing in the window
    /// is idle; one that issued commands while territory, rooms, gold and creatures all stood still
    /// is a stall candidate. Only controlled players are judged, once each.</summary>
    private void JudgeStall(ProgressSample sample)
    {
        if (!options.Controllers.Any(c => c.PlayerId.Value == sample.Player) || _flagged.Contains(sample.Player)) return;
        if (!_still.TryGetValue(sample.Player, out var still) || Moved(still.Sample, sample))
        {
            _still[sample.Player] = (sample.Tick, sample);
            return;
        }
        if (sample.Tick - still.Since < options.StallWindowTicks) return;
        _flagged.Add(sample.Player);
        bool issued = _lastCommandTick.TryGetValue(sample.Player, out int last) && last > still.Since;
        StallCandidates.Add(issued
            ? $"player {sample.Player}: commands issued but territory, rooms, gold and creatures unchanged from tick {still.Since} to {sample.Tick}"
            : $"player {sample.Player}: controller idle (no command) from tick {still.Since} to {sample.Tick}");
    }

    private static bool Moved(ProgressSample a, ProgressSample b) =>
        a.OwnedTiles != b.OwnedTiles || a.RoomTiles != b.RoomTiles || a.Gold != b.Gold || a.Creatures != b.Creatures || a.Status != b.Status;
}

/// <summary>A fuzzer asked for on the command line; the seed defaults to the scenario's.</summary>
public sealed record FuzzSpec(PlayerId Player, ulong? Seed, int IntervalTicks);

/// <summary>Run options from a command line, applied per scenario (the fuzz seed and the journal differ).</summary>
public sealed record RunTemplate(FuzzSpec? Fuzz, bool Coverage, int ProgressEvery, int SnapshotCheckEvery)
{
    public RunOptions For(Scenario scenario, Stream journal) => new()
    {
        Controllers = Fuzz is null
            ? []
            : [LegalActionFuzzer.Create(Fuzz.Player, Fuzz.Seed ?? scenario.Seed, Fuzz.IntervalTicks)],
        Journal = journal,
        Coverage = Coverage,
        ProgressEvery = ProgressEvery,
        SnapshotCheckEvery = SnapshotCheckEvery,
    };
}
