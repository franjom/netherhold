// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>DK2's trap update (<c>0x004A84E0</c>) for every trap, newest first as the thing pass
/// walks a class list (<c>0x004B4A10</c>), after the tile chain has taken in this turn's moves
/// (traps.md).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/traps.md")]
public sealed class TrapSystem : ISimulationSystem
{
    private WorldSight? _sight;
    private WorldState? _sightWorld;
    private readonly RoomCache _rooms = new();

    public string Name => "traps";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        TrapRules.RelinkCreatures(world);
        if (world.Traps.Count == 0) return;
        context.Navigation.Refresh();
        if (!ReferenceEquals(_sightWorld, world))
        {
            _sight = new WorldSight(context.Navigation, world, context.Database);
            _sightWorld = world;
        }
        IReadOnlyList<TrapState> traps = world.Traps;
        for (int i = traps.Count - 1; i >= 0; i--)
        {
            TrapState trap = traps[i];
            if (context.Database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition))
                TrapRules.Update(context, _sight!, trap, definition, _rooms);
        }
    }
}

/// <summary>The thing pass's second walk for the traps (<c>0x004B7830</c>): one whose health is
/// spent (<c>0x004B5C30</c>) is removed this turn through <c>0x004A93E0</c> and the cleanup
/// <c>0x004A7B20</c>.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/traps.md")]
public sealed class TrapRemovalSystem : ISimulationSystem
{
    public string Name => "trap_removal";

    public void Execute(SimulationContext context)
    {
        IReadOnlyList<TrapState> traps = context.World.Traps;
        for (int i = traps.Count - 1; i >= 0; i--)
            if (TrapRules.IsDead(traps[i])) TrapRules.Remove(context, traps[i]);
    }
}
