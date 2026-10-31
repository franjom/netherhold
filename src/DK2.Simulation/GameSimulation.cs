// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>The entire game. Deterministic, headless, serialisable, Unity-free.</summary>
/// <remarks>
/// <para>Contract: <b>same database + same initial world + same seed + same
/// input frames, in the same order, produce the same <see cref="StateHash"/> at
/// every tick — on any machine, in any process.</b></para>
/// <para>Enforced by <c>DeterminismTests</c>, whose pinned hash CI's determinism
/// gate checks on Linux and Windows. Anything that would break it — wall-clock time, unordered
/// collections, floating point in gameplay maths,
/// <see cref="object.GetHashCode"/> over strings, ambient randomness — is a
/// defect, not a trade-off.</para>
/// </remarks>
public sealed class GameSimulation
{
    private const ulong HashVersion = 348;

    /// <summary><c>HashVersion</c>, read at run time: a const would be compiled into a caller and
    /// go stale when this assembly is replaced without it (journal headers, ADR-0552).</summary>
    public static ulong CurrentHashVersion => HashVersion;

    private readonly List<ISimulationSystem> _systems;
    private readonly SimulationMarker[] _systemMarkers;
    private readonly SimulationContext _context;
    private readonly TriggerExecutor _triggers;
    private readonly bool _scheduledTriggers;
    private TriggerTurnCsvEmitter? _triggerEmitter;

    public GameSimulation(
        GameDatabase database,
        WorldState world,
        ulong seed,
        IEnumerable<ISimulationSystem>? systems = null,
        TriggerTree? triggers = null)
        : this(database, world, new SimRandom(seed), systems, triggers)
    {
    }

    public GameSimulation(
        GameDatabase database,
        WorldState world,
        SimRandom rng,
        IEnumerable<ISimulationSystem>? systems = null,
        TriggerTree? triggers = null)
        : this(database, world, rng, systems, triggers, levelStart: true)
    {
    }

    /// <summary><paramref name="levelStart"/> false is a restore (<see cref="WorldSnapshot"/>): the
    /// trigger executor skips the level initialiser's party sweep and creature event 3.</summary>
    internal GameSimulation(
        GameDatabase database,
        WorldState world,
        SimRandom rng,
        IEnumerable<ISimulationSystem>? systems,
        TriggerTree? triggers,
        bool levelStart)
    {
        _systems = systems is null ? [] : [.. systems];
        _scheduledTriggers = ScheduledTriggers(_systems);
        Database = database;
        World = world;
        Rng = rng;
        world.BindCreatureIdentityRandom(rng);
        _systemMarkers = [.. _systems.Select(s => SimulationMarker.Named(s.Name))];
        _context = new SimulationContext(database, world, rng, NullEventSink.Instance);
        if (levelStart) HatcheryRules.InitializeLevelStart(world, database);
        _triggers = triggers is null
            ? new TriggerExecutor(world.Triggers)
            : new TriggerExecutor(triggers, world.Triggers, world, database, rng, levelStart);
        _context.Triggers = _triggers;
        _triggers.EffectRequested = _context.RequestEffect;
        _triggers.ViewRequested = request => _context.Events.Emit(new SimEvent(_context.Tick,
            nameof(TriggerExecutor), "scripted_view",
            $"player={request.Player} kind={request.Kind} value={request.Value} tile=({request.Tile.X},{request.Tile.Y}) flag={request.Flag}")
        {
            ScriptedView = request,
        });
        world.GameEnded = (player, status) => FinaleRules.OnVerdict(_context, player, status);
    }

    /// <summary>Whether the list walks the trigger roots inside the turn (ADR-0568). A list that
    /// schedules them must hold all three walkers: one alone would drop the others' roots silently.</summary>
    private static bool ScheduledTriggers(IReadOnlyList<ISimulationSystem> systems)
    {
        bool areas = false, players = false, creatures = false;
        foreach (ISimulationSystem system in systems)
        {
            areas |= system is ActionPointTriggerSystem;
            players |= system is PlayerTriggerSystem;
            creatures |= system is CreatureUpdateSystem;
        }
        if (!areas && !players) return false;
        if (areas && players && creatures) return true;
        string missing = string.Join(", ", new[]
        {
            areas ? null : nameof(ActionPointTriggerSystem),
            players ? null : nameof(PlayerTriggerSystem),
            creatures ? null : nameof(CreatureUpdateSystem),
        }.Where(name => name is not null));
        throw new ArgumentException(
            $"A system list that schedules trigger roots needs {nameof(ActionPointTriggerSystem)}, "
            + $"{nameof(PlayerTriggerSystem)} and {nameof(CreatureUpdateSystem)}; missing: {missing}.",
            nameof(systems));
    }

    public GameDatabase Database { get; }
    public WorldState World { get; }
    public SimRandom Rng { get; }

    /// <summary>
    /// Where to send observation events. Assigning a recorder must not change
    /// behaviour — see <see cref="SimEvent"/>.
    /// </summary>
    public ISimulationEventSink Events => _context.Events;

    public IReadOnlyList<ISimulationSystem> Systems => _systems;

    /// <summary>The context the systems run in, for a tool that edits the world between ticks
    /// (<see cref="WorldResync"/>).</summary>
    internal SimulationContext Context => _context;

    /// <summary>The mesh, the router and the steering probes the systems route through. The mesh
    /// is edited in place as tiles change (ADR-0461), so it is state: saved and hashed.</summary>
    public Navigation.WorldSteering Navigation => _context.Navigation;
    public TriggerRunResult? LastTriggerRun { get; private set; }

    /// <summary>Records which trigger records each turn's walk reached, so that
    /// <see cref="TriggerReach"/> can say what a declined condition foreclosed. Analysis
    /// only, and off by default because it costs a set per turn. Assigning it must not
    /// change behaviour — the same contract as <see cref="ObserveWith"/> — and
    /// <c>TriggerReachTests.TrackingReachDoesNotChangeTheStateHash</c> pins that.
    /// </summary>
    public bool TrackTriggerReach
    {
        get => _triggers.TrackReach;
        set => _triggers.TrackReach = value;
    }

    /// <summary>The next tick to be executed.</summary>
    public GameTick CurrentTick => World.CurrentTick;

    /// <summary>Starts sending observation events to <paramref name="events"/>. Behaviour-neutral.</summary>
    public void ObserveWith(ISimulationEventSink events) => _context.Events = events;

    /// <summary>Starts timing ticks, systems and the spans below them with <paramref name="profiler"/>.
    /// Behaviour-neutral, as <see cref="ObserveWith"/> is.</summary>
    public void ProfileWith(ISimulationProfiler profiler) =>
        _context.Profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));

    public void EmitTriggerTurnsTo(TextWriter writer) => _triggerEmitter = new TriggerTurnCsvEmitter(writer);

    /// <summary>
    /// Advances exactly one game tick. The ONLY way simulation time moves.
    ///
    /// Systems execute in registration order for tick N, then the world's tick
    /// becomes N+1. (Our convention, not a claim about the original's internal
    /// ordering, which is UNKNOWN — see <see cref="ISimulationSystem"/>.)
    /// </summary>
    public void Tick(in InputFrame input)
    {
        ISimulationProfiler profiler = _context.Profiler;
        using ProfileScope tick = profiler.Scope(SimulationMarkers.Tick);
        _context.Tick = World.CurrentTick;
        _context.Input = input;
        World.TileHighlights.AdvanceTick();
        _context.TremorMoodDispatchedThisTick.Clear();
        _context.AngryChooserRanThisTick.Clear();
        _context.IdledByCombatHandlerThisTick.Clear();
        _context.TremorRebellionStartedThisTick.Clear();

        // Close the previous turn's observation window before this turn's producers.
        // This preserves zero-timeout messages for an external keeper (ADR-0245).
        using (profiler.Scope(SimulationMarkers.Messages))
        {
            foreach (Commands.AcknowledgePlayerMessageCommand ack in _context.Commands<Commands.AcknowledgePlayerMessageCommand>())
                World.Messages.Acknowledge(ack.PlayerId, ack.MessageId);
            World.Messages.Sweep(World, Database);
        }

        // The key handler runs before the turn's walk, so a skip is seen by this turn's condition 72.
        foreach (Commands.SkipCameraPathCommand skip in _context.Commands<Commands.SkipCameraPathCommand>())
        {
            FinaleRules.SkipGlide(ref World.Player(skip.PlayerId), World.CurrentTick);
            if (CameraPathRules.Skip(ref World.Player(skip.PlayerId), World.CurrentTick))
                _triggers.ViewRequested?.Invoke(new ScriptedViewRequest(
                    ScriptedViewKind.CameraPathSkipped, skip.PlayerId, 0, default, false));
        }
        // Actions 134 and 75 after a result, in the order the packet carries them.
        foreach (PlayerCommand command in _context.Input.All)
        {
            if (command is Commands.ContinueAfterResultCommand go) FinaleRules.Continue(_context, go.PlayerId);
            else if (command is Commands.RevealMapCommand reveal) FinaleRules.RevealMap(World, reveal.PlayerId);
        }

        // DK2's first game turn is 1; our next-tick clock starts at 0.
        int dk2Turn = unchecked((int)(World.CurrentTick.Value + 1));
        // The per-turn world update (0x0050A450) counts the level time limit down at 0x0050A64E,
        // before its trigger walk at 0x0050A745.
        if (World.Triggers.CountDownLevelTimeLimit())
        {
            _context.Emit(nameof(TriggerExecutor), "level_time_up", $"turn={dk2Turn}");
            // 0x0050A67A: in skirmish and multiplayer every keeper loses, in active-list order.
            if (World.GameMode is GameMode.Skirmish or GameMode.Multiplayer)
                foreach (PlayerId keeper in World.ActivePlayerOrder)
                    if (Data.Model.PlayerIds.IsKeeper(keeper.Value))
                        VerdictRules.End(World, Database, keeper, PlayerStatus.Lost);
        }
        using (profiler.Scope(SimulationMarkers.Triggers))
        {
            if (_scheduledTriggers) _triggers.BeginTurn(new GameTick(dk2Turn));
            else LastTriggerRun = _triggers.Run(dk2Turn);
        }
        for (int i = 0; i < _systems.Count; i++)
        {
            using (profiler.Scope(_systemMarkers[i]))
                _systems[i].Execute(_context);
        }

        if (_scheduledTriggers) LastTriggerRun = _triggers.EndTurn();
        foreach (BlockedTrigger done in LastTriggerRun!.CreatureActions)
            _context.Emit(
                nameof(TriggerExecutor), "trigger_performed",
                $"tag={done.Tag} type={done.Type} id={done.TriggerId} context={done.Context}");
        foreach (BlockedTrigger partial in LastTriggerRun.Partial)
        {
            _context.Emit(
                nameof(TriggerExecutor), "trigger_partial",
                $"tag={partial.Tag} type={partial.Type} id={partial.TriggerId} "
                + $"context={partial.Context}");
        }

        _triggerEmitter?.Emit(dk2Turn, World.Triggers, _triggers.Tree);

        World.CurrentTick = World.CurrentTick.Next;
    }

    public void Tick() => Tick(InputFrame.Empty);

    /// <summary>Runs <paramref name="ticks"/> ticks with no input.</summary>
    public void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++) Tick();
    }

    /// <summary>
    /// Structural hash of all simulation state: the world, and the RNG's
    /// position in its stream. Two runs that agree on this at every tick are
    /// behaviourally identical.
    ///
    /// The RNG state is included deliberately: two runs can hold identical
    /// world state having consumed different numbers of draws, and without this
    /// they would look identical now and diverge later.
    /// </summary>
    public ulong StateHash()
    {
        Hash64 hash = Hash64.Start();
        hash.Add(HashVersion);
        World.HashInto(ref hash);
        _context.Navigation.HashInto(ref hash);

        Rng.HashInto(ref hash);

        return hash.Value;
    }
}
