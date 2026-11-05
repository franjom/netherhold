// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>The position step of <c>Creature_UpdateCarriedThing</c> (<c>0x0048B5F0</c>), called from the
/// creature update at <c>0x0048A4AE</c> whenever the dragging word <c>+0x3C5</c> is set, in every state: after
/// the steering step and before the animation chooser <c>0x0048BE50</c>. The thing it names takes the carrier's
/// position and a heading half a turn from the carrier's. Ours keeps that word and <c>+0x3C3</c> in
/// <see cref="CreatureState.CarriedObjectId"/>, so the Portal Gem (<c>0x0048B350</c>, which this step never
/// reaches) is told by its type. The carrying sound that follows is <see cref="CarriedThingSoundSystem"/>.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/functions/0048B5F0.md")]
public sealed class CarriedThingAttachSystem : ICreatureUpdateStep
{
    public string Name => "carried thing attach";

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void Update(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        if (creature.CarriedObjectId >= 0 && world.ObjectById(creature.CarriedObjectId) is { } thing
            && ObjectCarryRules.IsAttachable(thing))
            ObjectCarryRules.FollowCarrier(world, creature, thing);
        if (creature.CarriedCreatureId >= 0 && world.CreatureById(creature.CarriedCreatureId) is { } body)
            CarryRules.FollowCarrier(creature, body);
    }
}
