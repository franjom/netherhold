// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>The creature update's previous-position copy (<c>0x00489FE6</c>-<c>0x0048A00E</c>): after the fight
/// scan and before the dispatcher (<c>0x00476FC0</c> at <c>0x0048A030</c>), the position goes into <c>+0xF6</c>,
/// the word <c>vtable+0x2C</c> returns and a spell's lead reads (shots.md section 1). A handler that moves the
/// creature this turn, a knock-back's, therefore moves it away from this copy, not into it.</summary>
/// <remarks>The update's tile-entry flag (<c>0x00488F61</c>-<c>0x00488F96</c>: the position's tile against the
/// copy's) is taken before this copy, and the room attack reads it (<c>0x00488FB6</c>); ours' hero handler runs
/// after this step, so the step keeps the pair it replaced in <see cref="CreatureState.LastPosBeforeCopy"/> until
/// the update ends. The flag's claim on the way (<c>0x0048905E</c>) runs in ours after the mover and reads this
/// copy: the pair native compares at the next update's entry, one update early (as before). A rule that sets
/// the position later in the update (a landing, a fall) sets the copy with it. Ours keeps x and y here; native
/// also copies z into <c>+0xFE</c> (<see cref="CreatureState.LastPosZ"/> stays with the rules that move height)
/// and the heading word <c>+0xF4</c> into <c>+0x102</c>, which ours does not hold.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/functions/00488930.md")]
public sealed class PreviousPositionStep : ICreatureUpdateStep
{
    public string Name => "previous position";

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void Update(SimulationContext context, CreatureState creature)
    {
        creature.LastPosBeforeCopy = (creature.LastPosX, creature.LastPosY);
        creature.LastPosX = creature.PosX;
        creature.LastPosY = creature.PosY;
    }
}
