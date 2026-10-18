// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>The job states: choosing in state 10, walking to the job, and working it.</summary>
public sealed partial class ImpTaskSystem
{
    private void UpdateIdle(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        if (creature.Dk2StateId == CreatureStateMachine.Idle) creature.Fleeing = false;
        creature.TimerTicks--;
        if (creature.TimerTicks > 0) return;

        // State 1 hands a worker to 10 (0x004773D0), and 10 looks on its own next update: both
        // captures show one tick in 1 and one in 10 before every walk or rest (mining-cadence.md
        // §6). A worker whose job ended is already in 10 and looks at once.
        if (creature.Dk2StateId is CreatureStateMachine.Idle or CreatureStateMachine.NoState)
        {
            StateShadow.Move(context, SystemName, creature, ImpStates.TaskSelection);
            return;
        }

        // DK2's state 10 hands a worker that found nothing to 12 at once (ADR-0224).
        if (creature.OwnerPlayerId != PlayerIds.Neutral && !creature.JustDropped && Throttled(context, creature))
            return;
        if (creature.OwnerPlayerId != PlayerIds.Neutral) creature.WorkerIdle = false;
        if (!TryTakeJob(context, creature, definition, grid)) ToRest(context, creature);
    }

    /// <summary>State 10's cap (<c>0x00463E2C</c>–<c>0x00463E9C</c>): a keeper's fifth worker to
    /// choose in one tick rests in 12 until now + 20 + rand(80) instead, the draw taken only once
    /// 12 has accepted it; otherwise the count goes up and the choice runs. A just-dropped
    /// worker skips the cap. The binary also skips it when the worker's last evaluation stamp
    /// is 40 or more ticks in the future, which a stamp written from the current tick never is.</summary>
    private bool Throttled(SimulationContext context, CreatureState creature)
    {
        long now = context.Tick.Value;
        PlayerId owner = (byte)creature.OwnerPlayerId;
        if (_selectionTick[owner.Value] != now)
        {
            _selectionTick[owner.Value] = now;
            _selections[owner.Value] = 0;
        }
        if (_selections[owner.Value] >= SelectionsPerTick
            && StateShadow.TryMove(context, SystemName, creature, ImpStates.Resting))
        {
            creature.StateDeadlineTick = now + ThrottleBaseTicks + context.Rng.NextInt(ThrottleRandomTicks);
            context.Emit(SystemName, "throttled",
                $"imp={creature.Id} player={creature.OwnerPlayerId} until={creature.StateDeadlineTick}");
            return true;
        }
        _selections[owner.Value]++;
        return false;
    }

    private void UpdateMoving(SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        // State 6 resumes only when the route's progress is exactly 1.0 after the tick's move
        // (0x00470C80, 0x004D54F0): the point reached, or within a sixteenth of it, never on
        // entering the tile (imp-task-objects-and-walks.md §4, Q490, ADR-0297).
        bool walked = creature.Dk2StateId != StateShadow.TravelToPoint
            || Steering.FollowPathRules.Progress(creature.Body) == Steering.Fixed16.One;
        if (walked && IsInWorkingPosition(context, creature, grid))
        {
            (int entry, int work) = ImpStates.For(creature.TargetWork);
            if (creature.Dk2StateId == entry)
            {
                if (IsExcavation(creature.TargetWork)
                    || creature.TargetWork is TileWorkKind.Reinforce or TileWorkKind.RepairWall)
                {
                    var target = new TilePos(creature.TargetTileIndex % context.World.Width,
                        creature.TargetTileIndex / context.World.Width);
                    int heading = creature.Heading;
                    int remaining = Steering.Bearing.TurnToward(ref heading,
                        Steering.Bearing.Of(CreatureState.CentreOf(target.X) - creature.PosX,
                            CreatureState.CentreOf(target.Y) - creature.PosY), definition.TurnRate);
                    creature.Heading = heading;
                    if (remaining != 0) return;
                }
                // Only the completed turn counts toward the first work application.
                StateShadow.Move(context, SystemName, creature, work);
                creature.TimerTicks--;
                return;
            }
            creature.StopMoving();
            creature.TimerTicks = WorkCadence.FirstApplicationTicks;
            AtWork(context, creature);
            return;
        }

        if (!walked || creature.HasRoute) return;

        // Walked the whole route and the target is still not next to it: the
        // world changed under the route. Drop the job rather than re-planning
        // from here, so an imp cannot get stuck chasing a moving answer.
        context.Emit(SystemName, "job_abandoned",
            $"imp={creature.Id} {Describe(context.World, creature.TargetTileIndex)} " +
            $"{creature.TargetWork} reason=out_of_reach");
        StateShadow.ToIdle(context, SystemName, creature);
        GoIdle(creature);
    }

    private void UpdateWorking(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        if (!IsInWorkingPosition(context, creature, grid))
        {
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        // Decrement first, then act on zero. The timer is WorkCadence's: three ticks
        // from arrival, a kind-dependent gap, then every tick (ADR-0280).
        creature.TimerTicks--;
        if (creature.TimerTicks > 0) return;

        // Before another strike, full hands try the store chooser (0x00465870: 0x00464360(1),
        // then 0x00464400); an imp with nowhere to take the load keeps mining (Q31).
        if (MinersHandsFull(context, creature, definition)
            && TryRouteToSomewhereItFits(context, creature))
        {
            // 26 walks to the store in 6 resuming in 28.
            BeginDelivering(creature);
            ToTheStore(context, creature);
            return;
        }

        // A miner not going to deliver gives its slot up once the pile in the slot's cell holds
        // more than MaxGoldPileOutsideTreasury, and state 26 falls to 10 (0x00465870; its overflow
        // lands in that cell, TileWorkSystem.HandTo). The chooser may then give it the face's next
        // slot, which is how a seam gets mined out with nowhere to take the gold (Q140).
        if (creature.TargetWork == TileWorkKind.MineGold
            && FloorGold.PileInCell(context.World, creature.Tile.Y * context.World.Width + creature.Tile.X,
                creature.WorkSlot == MiningSlots.None ? MiningSlots.AnyCell : MiningSlots.Cell(creature.WorkSlot))
                > FloorGold.MaxPile(context.Database))
        {
            context.Emit(SystemName, "job_abandoned",
                $"imp={creature.Id} {Describe(context.World, creature.TargetTileIndex)} " +
                $"{creature.TargetWork} reason=pile_underfoot_full");
            WorkEnded(context, creature);
            GoIdle(creature);
            return;
        }

        context.World.QueueWork(new TileWorkOrder(
            creature.TargetTileIndex, creature.TargetWork, creature.OwnerPlayerId, creature.Id));
        creature.TimerTicks = WorkCadence.AfterApplication(
            creature.TargetWork, context.Tick.Value, creature.StateEnteredTick);

        if (creature.TargetWork != TileWorkKind.MineGold) return;
        _tileWork.Execute(context);
        // Native action work precedes the state/hands check. An exhausted seam
        // keeps the work state this turn; its remaining action lifetime is separate.
        if (!JobStillStands(context, creature)) return;
        if (HandsFull(context, creature, definition) && TryRouteToSomewhereItFits(context, creature))
        {
            BeginDelivering(creature);
            ToTheStore(context, creature);
        }
    }

    /// <summary>
    /// State 26's hands test before another strike. Whether the load can be
    /// stored is checked separately (ADR-0296).
    /// </summary>
    private static bool MinersHandsFull(
        SimulationContext context, CreatureState creature, CreatureDef definition) =>
        creature.TargetWork == TileWorkKind.MineGold && HandsFull(context, creature, definition);
}
