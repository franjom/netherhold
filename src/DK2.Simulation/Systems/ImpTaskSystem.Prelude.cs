// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

public sealed partial class ImpTaskSystem
{
    private ICreatureUpdateStep? _prelude;

    /// <summary>Reinforcement's claim/pickup block (0048904E), before the action poll at00489A68.</summary>
    public ICreatureUpdateStep Prelude => _prelude ??= new WorkerPrelude(this);

    internal sealed class WorkerPrelude(ImpTaskSystem owner) : ICreatureUpdateStep
    {
        public string Name => "WorkerPrelude";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature)
        {
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            if (!creature.ReinforcementAction.Active && !ReinforcementActionRules.UsesClock(creature, definition)
                && creature.ClaimAction.Kind == ClaimActionKind.None && !ClaimActionRules.UsesClock(creature, definition)) return;
            if (creature.Possessed || HeroJobRules.IsHero(creature) || !((definition.Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker)
                || context.TremorRecoveryCompletedThisTick.Contains(creature)
                || CreatureEligibility.IsExcluded(creature,
                    CreatureGuards.Held | CreatureGuards.Imprisoned | CreatureGuards.Down | CreatureGuards.Preoccupied)) return;
            owner.RunWorkerPrelude(context, creature, definition);
        }
    }

    private bool RunWorkerPrelude(SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid? grid = null)
    {
        if (!context.WorkerPreludeProcessedThisTick.Add(creature))
            return context.WorkerPreludeClaimedThisTick.Contains(creature);
        bool claimed = false;
        if (CreatureStateMachine.HasState(creature)
            && NeedRules.ResolvedCategory(creature) == CreatureStateCategory.WorkerJob
            && creature.CarriedObjectId < 0 && creature.CarriedCreatureId < 0)
        {
            using (context.Profiler.Scope(SimulationMarkers.ImpClaimOnTheWay))
                claimed = TryClaimOnTheWay(context, creature, grid ?? new WorldPathGrid(context.Database, context.World, definition));
        }
        PickUpGoldInPassing(context, creature, definition);
        if (claimed) context.WorkerPreludeClaimedThisTick.Add(creature);
        return claimed;
    }
}
