// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Systems;

/// <summary>Every Jack's update (jack-in-the-box.md): one standing in state 273 runs its handler, one
/// walking pays the travel state's toll and hands back to 273 when the route is done, and one whose
/// health is spent explodes. DK2 explodes it inside the health routine that kills it; ours does so at
/// its next update when anything else killed it.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/jack-in-the-box.md")]
public sealed class JackInTheBoxSystem : ICreatureUpdateStep
{
    private const string SystemName = "jack_in_the_box";

    public string Name => SystemName;

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void Update(SimulationContext context, CreatureState jack)
    {
        if (!JackInTheBoxRules.IsJack(jack) || jack.DeathShotFired) return;
        if (!CreatureDeathRules.IsDown(jack) && !HandRules.IsHeld(jack))
        {
            if (StateShadow.IsTravelling(jack))
            {
                JackInTheBoxRules.Travelling(context, jack);
                // State 6 resumes at a progress of exactly 1.0 (0x00470C80), which counts the last
                // point's distance in sixteenths of a tile: under 1/16 is there, before Follow_Path's
                // half-radius arrival (Q711: 0x8C8 short of 0x810 on M7Traps). Its wait while a kind-4
                // action (a hit's recoil) runs is not modelled: nothing can attack a Jack (ADR-0615).
                if (jack.UtilityNewState == JackInTheBoxRules.SpringState && FollowPathRules.Progress(jack.Body) == Fixed16.One)
                {
                    jack.StopMoving();
                    StateShadow.Arrive(context, SystemName, jack);
                }
            }
            else if (jack.Dk2StateId == JackInTheBoxRules.SpringState) JackInTheBoxRules.Handle(context, jack);
        }
        if (jack.Health <= 0) JackInTheBoxRules.Explode(context, jack);
    }
}
