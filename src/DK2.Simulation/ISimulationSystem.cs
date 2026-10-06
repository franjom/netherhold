// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Everything a system may touch during one tick.
///
/// Deliberately narrow: a system gets the static database, the mutable world,
/// the tick's input, the shared RNG and an event sink — and nothing else. No
/// clock, no file system, no ambient state. That is what makes the run
/// reproducible.
/// </summary>
public sealed class SimulationContext
{
    internal SimulationContext(GameDatabase database, WorldState world, SimRandom rng, ISimulationEventSink events)
    {
        Database = database;
        World = world;
        Rng = rng;
        Events = events;
        Navigation = new Navigation.WorldSteering(database, world);
    }

    public GameDatabase Database { get; }
    public WorldState World { get; }
    public SimRandom Rng { get; }
    // Recomputed each recovery pass; never an input to the following turn.
    internal HashSet<CreatureState> TremorRecoveryCompletedThisTick { get; } = [];
    // Cleared before every turn; suppresses a second mood selection later in that turn.
    internal HashSet<CreatureState> TremorMoodDispatchedThisTick { get; } = [];
    internal HashSet<CreatureState> TremorRebellionStartedThisTick { get; } = [];

    /// <summary>The mesh, the router and the steering probes over <see cref="World"/>. Systems
    /// request routes through it; the movement system steps bodies against it.</summary>
    public Navigation.WorldSteering Navigation { get; }
    public ISimulationEventSink Events { get; internal set; }

    /// <summary>The tick currently being executed.</summary>
    public GameTick Tick { get; internal set; }

    /// <summary>Commands issued for the tick currently being executed.</summary>
    public InputFrame Input
    {
        get => _input;
        internal set
        {
            _input = value;
            _commands.Reset(value.All);
        }
    }

    private InputFrame _input;
    private readonly CommandIndex _commands = new();

    /// <summary>The tick's commands of type <typeparamref name="T"/>, in input order. A system that
    /// handles one command type reads this; one whose types must keep their order among each other
    /// (the hand, keeper spells, blueprints, possession) reads <see cref="Input"/>.</summary>
    public IReadOnlyList<T> Commands<T>() where T : PlayerCommand => _commands.Of<T>();

    public void Emit(string system, string kind, string detail) =>
        Events.Emit(new SimEvent(Tick, system, kind, detail));

    /// <summary>The level's trigger executor, when the simulation has one: the systems fire DK2's
    /// creature and player trigger events through it at the moment they happen, as the binary
    /// does. Null in a world without triggers, and the firings are no-ops.</summary>
    internal TriggerExecutor? Triggers { get; set; }

    /// <summary>DK2's turn number for a trigger walk fired now: the executor's own
    /// <c>Run</c> takes <c>CurrentTick + 1</c>, and an event during this tick belongs to that turn.</summary>
    private int TriggerTurn => unchecked((int)(Tick.Value + 1));

    /// <summary>What <see cref="CreatureStateMachine"/> hands a category hook: this context's
    /// world, database and trigger executor, made on the stack.</summary>
    public StateHookContext Hooks => new(World, Database, Triggers);

    public void FireCreatureEvent(CreatureState creature, CreatureTriggerEvent type) =>
        Triggers?.FireCreatureEvent(creature, type, TriggerTurn);

    public void FirePlayerEvent(PlayerId playerId, PlayerTriggerEvent type, byte targetType = 0, byte targetId = 0) =>
        Triggers?.FirePlayerEvent(playerId, type, TriggerTurn, targetType, targetId);
}

/// <summary>One unit of per-tick simulation work.</summary>
/// <remarks>
/// <para>Systems run in a fixed registration order, once per tick, single-threaded. Order is
/// part of the simulation's definition — two systems that touch the same state in a different
/// order produce a different game — so whoever builds the <see cref="GameSimulation"/> declares
/// it explicitly; it is never inferred, sorted or parallelised.</para>
/// <para>UNKNOWN: the order in which the original updates its own subsystems.</para>
/// </remarks>
public interface ISimulationSystem
{
    /// <summary>Stable name, used in event logs and divergence reports.</summary>
    string Name { get; }

    void Execute(SimulationContext context);
}
