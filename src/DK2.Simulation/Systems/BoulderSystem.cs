// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>Every rolling boulder's update (object process state 17, <c>0x004A1390</c>), after the shots
/// and before the traps, as DK2's thing pass runs its class lists: shots (1), objects (2), traps (3).
/// A boulder that breaks, and an object it breaks, is removed this turn. Ours walks the boulders
/// oldest first; DK2's order within the object list is unread.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/boulder.md")]
public sealed class BoulderSystem : ISimulationSystem
{
    private WorldSight? _sight;
    private WorldState? _sightWorld;
    private readonly List<ObjectState> _boulders = [];
    private readonly List<ObjectState> _destroyed = [];

    public string Name => "boulders";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        _boulders.Clear();
        foreach (ObjectState thing in world.Objects)
            if (thing.ObjectTypeId == BoulderRules.ObjectId && thing.ProcessState == BoulderRules.ProcessState) _boulders.Add(thing);
        if (_boulders.Count == 0) return;

        TrapRules.RelinkCreatures(world);
        context.Navigation.Refresh();
        if (!ReferenceEquals(_sightWorld, world))
        {
            _sight = new WorldSight(context.Navigation, world, context.Database);
            _sightWorld = world;
        }
        _destroyed.Clear();
        foreach (ObjectState boulder in _boulders)
        {
            if (_destroyed.Contains(boulder)) continue;
            if (BoulderRules.Update(context, _sight!, boulder, _destroyed)) _destroyed.Add(boulder);
        }
        foreach (ObjectState gone in _destroyed) world.RemoveObject(gone.Id);
    }
}
