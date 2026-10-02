// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>Every Jack's update (jack-in-the-box.md): one standing in state 273 runs its handler, one
/// walking pays the travel state's toll and hands back to 273 when the route is done, and one whose
/// health is spent explodes. DK2 explodes it inside the health routine that kills it; ours does so at
/// its next update when anything else killed it.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/jack-in-the-box.md")]
public sealed class JackInTheBoxSystem : ISimulationSystem
{
    private const string SystemName = "jack_in_the_box";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState jack = creatures[i];
            if (!JackInTheBoxRules.IsJack(jack) || jack.DeathShotFired) continue;
            if (!CreatureDeathRules.IsDown(jack) && !HandRules.IsHeld(jack))
            {
                if (StateShadow.IsTravelling(jack))
                {
                    JackInTheBoxRules.Travelling(context, jack);
                    if (jack.UtilityNewState == JackInTheBoxRules.SpringState && !jack.HasRoute) StateShadow.Arrive(context, SystemName, jack);
                }
                else if (jack.Dk2StateId == JackInTheBoxRules.SpringState) JackInTheBoxRules.Handle(context, jack);
            }
            if (jack.Health <= 0) JackInTheBoxRules.Explode(context, jack);
        }
    }
}
