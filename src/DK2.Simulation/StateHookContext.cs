// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a category hook may touch when <see cref="CreatureStateMachine"/> runs it:
/// the world (rooms, the map cell under the creature, the players' gold), the database, and
/// the level's trigger executor and effect requests. Passed by <c>in</c>, so a state
/// change allocates nothing (ADR-0308).</summary>
/// <remarks>A system has one in <see cref="SimulationContext.Hooks"/>. Code that holds only a
/// world and a database builds one from them, which has no trigger executor, as a world with no
/// triggers has none in its simulation context either.</remarks>
public readonly struct StateHookContext
{
    public StateHookContext(WorldState world, GameDatabase database)
        : this(world, database, null)
    {
    }

    internal StateHookContext(WorldState world, GameDatabase database, TriggerExecutor? triggers,
        Action<string, ViewEffectRequest>? requestEffect = null)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Database = database ?? throw new ArgumentNullException(nameof(database));
        Triggers = triggers;
        _requestEffect = requestEffect;
    }

    public WorldState World { get; }
    public GameDatabase Database { get; }

    /// <summary>Null in a world without triggers, as <see cref="SimulationContext"/>'s is.</summary>
    internal TriggerExecutor? Triggers { get; }
    private readonly Action<string, ViewEffectRequest>? _requestEffect;

    public void RequestEffect(string system, ViewEffectRequest request) => _requestEffect?.Invoke(system, request);

    /// <summary>A creature trigger event, on the turn <see cref="SimulationContext.FireCreatureEvent"/>
    /// would give it: the world's tick is the context's while a tick runs.</summary>
    public void FireCreatureEvent(CreatureState creature, CreatureTriggerEvent type) =>
        Triggers?.FireCreatureEvent(creature, type, unchecked((int)(World.CurrentTick.Value + 1)));
}
