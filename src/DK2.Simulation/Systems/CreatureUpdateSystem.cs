// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>A system whose work is one creature at a time: its part of DK2's creature update
/// (<c>0x00488930</c>). Inside a <see cref="CreatureUpdateSystem"/> it runs for each creature
/// between the steps declared before and after it; run alone, it sweeps the creature list itself
/// in the same order (<see cref="CreatureUpdateSystem.RunAlone"/>).</summary>
public interface ICreatureUpdateStep : ISimulationSystem
{
    /// <summary>Once a tick, before the first creature's update.</summary>
    void BeginTurn(SimulationContext context) { }

    /// <summary>This step's part of one creature's update.</summary>
    void Update(SimulationContext context, CreatureState creature);

    /// <summary>Once a tick, after the last creature's update.</summary>
    void EndTurn(SimulationContext context) { }
}

/// <summary>DK2's creature list in the thing sweep (<c>0x004B7830</c>): newest first, and one
/// creature's whole update before the next one's begins (functions/004B7830.md). Inside that
/// update the fight scan precedes the state dispatcher, whose needs routine precedes the
/// choosers and the state handler, and the steering step comes last (<c>0x00488930</c>,
/// <c>0x00476FC0</c>); the steps run in the order they are declared, which is that order.</summary>
/// <remarks>The list is in id order and ids only climb, so the walk goes by id: a creature
/// removed during it shifts nothing onto a second update, and one added during it waits for the
/// next tick, as a thing linked at the head of DK2's list does. A creature removed by one of its
/// own steps takes none of the rest.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/functions/004B7830.md")]
public sealed class CreatureUpdateSystem : ISimulationSystem
{
    private readonly ICreatureUpdateStep[] _steps;

    public CreatureUpdateSystem(params ICreatureUpdateStep[] steps) =>
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));

    public string Name => "CreatureUpdate";

    /// <summary>The steps, in the order each creature takes them.</summary>
    public IReadOnlyList<ICreatureUpdateStep> Steps => _steps;

    public void Execute(SimulationContext context)
    {
        foreach (ICreatureUpdateStep step in _steps) step.BeginTurn(context);
        Sweep(context, _steps, walkTriggers: true);
        foreach (ICreatureUpdateStep step in _steps) step.EndTurn(context);
    }

    /// <summary>Steps on their own, over the whole list in the sweep's order: what a step's
    /// <see cref="ISimulationSystem.Execute"/> does in a host that runs it without the others.</summary>
    public static void RunAlone(SimulationContext context, params ICreatureUpdateStep[] steps)
    {
        foreach (ICreatureUpdateStep step in steps) step.BeginTurn(context);
        Sweep(context, steps);
        foreach (ICreatureUpdateStep step in steps) step.EndTurn(context);
    }

    private static void Sweep(SimulationContext context, ReadOnlySpan<ICreatureUpdateStep> steps, bool walkTriggers = false)
    {
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;
        int below = int.MaxValue;
        for (int i = creatures.Count - 1; ; i--)
        {
            if (i >= creatures.Count) i = creatures.Count - 1;
            while (i >= 0 && creatures[i].Id >= below) i--;
            if (i < 0) break;
            CreatureState creature = creatures[i];
            below = creature.Id;
            context.UpdateStartStateId = creature.Dk2StateId;
            foreach (ICreatureUpdateStep step in steps)
            {
                step.Update(context, creature);
                if (creature.LinkWorld is null) break;
            }
            if (walkTriggers && creature.LinkWorld is not null) context.Triggers?.RunCreature(creature);
        }
    }
}
