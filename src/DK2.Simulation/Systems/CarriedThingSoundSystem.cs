// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>The last of a creature's update that touches a carried thing (<c>Creature_UpdateCarriedThing</c>,
/// <c>0x0048B5F0</c>, called at <c>0x0048A4AE</c>, after the steering step): a creature whose carried-thing word
/// (<c>+0x3C5</c>) names a live thing asks for the carrying sound <c>0x10D</c> with its own voice, in its own category,
/// at its position, on every update. Only the sound is here; following the carrier is the carrying states' (ours
/// moves the thing where the imp's handler runs). The request is made whatever the thing is: a body, a spell
/// book, a crate, a special or the portal gem.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects.md#10-imp-jobs")]
public sealed class CarriedThingSoundSystem : ICreatureUpdateStep
{
    public string Name => "carried thing sound";

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void Update(SimulationContext context, CreatureState creature)
    {
        if (creature.CarriedObjectId < 0 && creature.CarriedCreatureId < 0) return;
        // A word that names no live thing is cleared and sounds nothing (0x0048B60A).
        WorldState world = context.World;
        bool live = creature.CarriedObjectId >= 0
            ? world.ObjectById(creature.CarriedObjectId) is not null
            : world.CreatureById(creature.CarriedCreatureId) is not null;
        if (live) ThingSoundRules.Request(context, Name, creature, ThingSoundEvent.CarriedThing);
    }
}
