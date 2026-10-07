// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The entire game. Deterministic, headless, serialisable, Unity-free.</summary>
/// <remarks>
/// <para>Contract: <b>same database + same initial world + same seed + same
/// input frames, in the same order, produce the same <see cref="StateHash"/> at
/// every tick — on any machine, in any process.</b></para>
/// <para>Enforced by <c>DeterminismTests</c>, whose pinned hash is the gate
/// (there is no CI). Anything that would break it — wall-clock time, unordered
/// collections, floating point in gameplay maths,
/// <see cref="object.GetHashCode"/> over strings, ambient randomness — is a
/// defect, not a trade-off.</para>
/// </remarks>
public sealed class GameSimulation
{
    private const ulong HashVersion = 203;

    private readonly List<ISimulationSystem> _systems;
    private readonly SimulationContext _context;
    private readonly TriggerExecutor _triggers;
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
    {
        Database = database;
        World = world;
        Rng = rng;
        _systems = systems is null ? [] : [.. systems];
        _context = new SimulationContext(database, world, rng, NullEventSink.Instance);
        _triggers = triggers is null
            ? new TriggerExecutor(world.Triggers)
            : new TriggerExecutor(triggers, world.Triggers, world, database, rng);
        _context.Triggers = _triggers;
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
        _context.Tick = World.CurrentTick;
        _context.Input = input;
        _context.TremorMoodDispatchedThisTick.Clear();
        _context.TremorRebellionStartedThisTick.Clear();

        // Close the previous turn's observation window before this turn's producers.
        // This preserves zero-timeout messages for an external keeper (ADR-0245).
        foreach (Commands.AcknowledgePlayerMessageCommand ack in _context.Commands<Commands.AcknowledgePlayerMessageCommand>())
            World.Messages.Acknowledge(ack.PlayerId, ack.MessageId);
        World.Messages.Sweep(World, Database);

        // DK2's first game turn is 1; our next-tick clock starts at 0.
        int dk2Turn = unchecked((int)(World.CurrentTick.Value + 1));
        LastTriggerRun = _triggers.Run(dk2Turn);
        // The same per-turn world update (0x0050A450) counts the level time limit down.
        if (World.Triggers.CountDownLevelTimeLimit())
            _context.Emit(nameof(TriggerExecutor), "level_time_up", $"turn={dk2Turn}");
        foreach (BlockedTrigger done in LastTriggerRun.CreatureActions)
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

        foreach (ISimulationSystem system in _systems)
            system.Execute(_context);

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

        Rng.HashInto(ref hash);

        return hash.Value;
    }
}
