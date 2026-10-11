// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>A hungry prisoner looks round its cell for food, walks to the nearest it
/// finds, and eats it.</summary>
/// <remarks>The first to reach a chicken eats it and the bird is never reserved, so
/// it is a race — CONFIRMED (owner, 2026-09-05, Q172); read from DK2's prison states
/// 115 and 117-119 under ADR-0072's corrected frame (docs/research/prison.md §7). A
/// prisoner never leaves the prison states to eat: 117-119 carry the same label,
/// group and category as 114-116, so <see cref="CreatureActivity.Imprisoned"/> stands
/// and the walk is marked with <see cref="CreatureState.TargetObjectId"/> instead.
/// That is why <see cref="PrisonSystem"/> keeps draining a prisoner walking to its
/// dinner, and why a keeper can lift one out mid-stride.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison.md")]
public sealed class PrisonerEatSystem : ISimulationSystem
{
    private const string SystemName = "PrisonerEat";

    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];

    public PrisonerEatSystem() : this(new GridPathfinder()) { }

    public PrisonerEatSystem(IPathfinder pathfinder) =>
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (creature.Possessed) continue;
            if (!PrisonRules.IsImprisoned(creature)) continue;
            if (TortureRules.IsBeingTortured(creature)) continue;   // on a device, not in a cell
            if (JailBreakRules.IsEscaping(creature)) continue;      // walking out, in 121 (ADR-0229)
            if (creature.Dk2StateId == PrisonRules.ReturningState) continue;   // 126 asks no hunger (ADR-0400)
            if (SlapRules.IsReaction(creature.Dk2StateId)) continue;          // 120's reaction (ADR-0401)

            // In the keeper's hand it is out of the dungeon (ADR-0062); down,
            // it is not walking anywhere and PrisonSystem owns its second.
            if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Held | CreatureGuards.Down)) continue;

            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
            if (!CreatureHealthRules.CanGetHungry(definition))
            {
                creature.TargetObjectId = EatingRules.NoChicken;
                continue;
            }

            // Already walking to one: the walk that resumes in 118.
            if (creature.TargetObjectId >= 0)
            {
                Fetching(context, creature, definition);
                continue;
            }

            // 119 holds action 26's first cycle, then searches again in 117.
            if (creature.Dk2StateId == PrisonRules.AfterEatingState)
            {
                if (context.Tick.Value < creature.ActionUntilTick) continue;
                creature.ActionUntilTick = 0;
                To(context, creature, PrisonRules.SeekingFoodState);
                continue;
            }

            // 117 searches every turn it runs, with no hunger test and no retry wait:
            // both belong to 115, and 118 comes back here directly (0x00473750).
            if (creature.Dk2StateId == PrisonRules.SeekingFoodState)
            {
                Search(context, creature, definition);
                continue;
            }

            // State 115's own hunger test. It is HungerNeed >= HungerFill, the
            // same entry test the general needs routine uses, and it is asked
            // before the wander - which is why this system is registered above
            // PrisonerWanderSystem.
            if (!EatingRules.IsHungry(database, definition, creature)) continue;

            // At most one attempt a game second. The general path has the 4 as a
            // literal; the prison reads TimeBetweenReattemptingHungerSeconds,
            // which is 4, and four DK2 ticks is one game second (Q1/Q132). So
            // the number to use is one game second and not the variable's own
            // value read as seconds, which would be four times too slow.
            creature.FoodAttemptTicks--;
            if (creature.FoodAttemptTicks > 0) continue;
            creature.FoodAttemptTicks = EatingRules.RetryTicks;

            Search(context, creature, definition);
        }
    }

    /// <summary>State 117: look round the cell, and either set off for the nearest
    /// thing worth eating or grow angry that there is none.</summary>
    /// <remarks>The anger is a quarter of the tabled figure: the creature loader
    /// divides every <c>angerNo*</c> field by DK2's four ticks a second as it loads
    /// it, and 117 adds one of those per attempt rather than per tick
    /// (<see cref="PrisonRules.AngerPerFailedFoodSearch"/>).</remarks>
    private void Search(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        RoomInstance? cell = RoomIndex.RoomAt(
            world, database, (creature.Tile.Y * world.Width) + creature.Tile.X);

        // Standing on something that has stopped being a room. PrisonSystem
        // frees it on the same second; there is nothing to search.
        if (cell is null) return;

        // 115 decides it is hungry and moves to 117 (ADR-0191); a captive 118 sent
        // back is in 117 already.
        if (StateShadow.IsTravelling(creature)) StateShadow.Arrive(context, SystemName, creature);
        if (creature.Dk2StateId != PrisonRules.SeekingFoodState) To(context, creature, PrisonRules.DecidingState);
        To(context, creature, PrisonRules.SeekingFoodState);

        ObjectState? food = EatingRules.NearestFoodIn(world, database, cell, creature.Tile);
        if (food is null)
        {
            int anger = PrisonRules.AngerPerFailedFoodSearch(definition);
            if (anger != 0)
            {
                CreatureAngerRules.Apply(
                    world, definition, creature, AngerBucket.Hunger, anger, AngerComplaint.NoFood);
                context.Emit(SystemName, "prisoner_no_food",
                    $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} " +
                    $"at=({creature.Tile.X},{creature.Tile.Y}) " +
                    $"need={creature.HungerNeed} anger=+{anger} " +
                    $"total={creature.Anger.Hunger}");
            }

            To(context, creature, PrisonRules.InTheCellState);
            return;
        }

        creature.TargetObjectId = food.Id;

        int x = food.TileIndex % world.Width;
        int y = food.TileIndex / world.Width;

        // Already on it or beside it: eat this tick, with no walk at all.
        if (EatingRules.DistanceSquared(world, creature.Tile, food.TileIndex)
            <= EatingRules.ReachTilesSquared)
        {
            Swallow(context, creature, definition, food);
            return;
        }

        creature.StopMoving();

        context.Navigation.TryRoute(creature, new TilePos(x, y), Navigation.MoveOptions.CaptiveSpot);
        if (CreatureStateMachine.HasState(creature))
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, PrisonRules.EatingState);

        context.Emit(SystemName, "prisoner_food_chosen",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} object={food.Id} " +
            $"at=({x},{y}) steps={creature.Body.PathTotalCount}");
    }

    /// <summary>
    /// States 118 and 119: walking to the chosen thing, and eating it on
    /// arrival — or finding it gone, which is what losing the race looks like
    /// from the inside.
    /// </summary>
    private static void Fetching(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        ObjectState? food = world.ObjectById(creature.TargetObjectId);
        if (food is null
            || food.CarriedByCreatureId >= 0
            || !EatingRules.IsFood(database, food))
        {
            context.Emit(SystemName, "prisoner_food_lost",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} object={creature.TargetObjectId}");
            BackToTheSearch(context, creature);
            return;
        }

        if (EatingRules.DistanceSquared(world, creature.Tile, food.TileIndex)
            > EatingRules.ReachTilesSquared)
        {
            // Still walking. A route that ran out short of the bird is a failed
            // approach and the search starts again from where it stands.
            if (creature.HasRoute) return;

            BackToTheSearch(context, creature);
            return;
        }

        Swallow(context, creature, definition, food);
    }

    private static void Swallow(
        SimulationContext context, CreatureState creature,
        CreatureDef definition, ObjectState food)
    {
        // At the bird: 118, straight from 117 when it was already within reach.
        if (creature.Dk2StateId == PrisonRules.SeekingFoodState)
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, PrisonRules.EatingState);
        if (StateShadow.IsTravelling(creature)) StateShadow.Arrive(context, SystemName, creature);

        // On the food, before it eats, 118 wipes the hunger grievance (0x00486F80(1)).
        CreatureAngerRules.Forget(context.World, definition, creature, AngerBucket.Hunger);

        int before = creature.Health;
        int gained = EatingRules.EatInPrison(context.Database, definition, creature);
        context.World.RemoveObject(food.Id);

        context.Emit(SystemName, "prisoner_ate",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} object={food.Id} " +
            $"at=({creature.Tile.X},{creature.Tile.Y}) " +
            $"health={before}->{creature.Health} gained={gained} " +
            $"need={creature.HungerNeed}");

        // The hunger routine returns the need still owed: owing, 118 goes straight back
        // to 117 for another; filled, 119 waits out the meal. One bird pays a whole
        // level-scaled HungerFill, so owing means helpings built up over a long stay.
        if (EatingRules.StillOwesFood(creature))
        {
            BackToTheSearch(context, creature);
            return;
        }

        creature.TargetObjectId = EatingRules.NoChicken;
        creature.StopMoving();
        creature.ActionUntilTick = context.Tick.Value;
        if (AnimationTimingRules.ResolvesPresent(definition, 13))
        {
            CreatureAnimationTiming animation = definition.ArtSlot(AnimationTimingRules.ResolveArtSlot(definition, 13));
            creature.ActionUntilTick += AnimationTimingRules.FirstCycleHoldTicks(animation);
        }
        To(context, creature, PrisonRules.AfterEatingState);
    }

    /// <summary>
    /// Back into state 117, which searches again on the next turn with neither the
    /// retry wait nor the hunger test: both are 115's, and 118 sends a captive whose
    /// bird is gone, or who still owes food after one, straight to 117
    /// (<c>0x00473750</c>, re-read 2026-09-30).
    /// </summary>
    private static void BackToTheSearch(SimulationContext context, CreatureState creature)
    {
        creature.TargetObjectId = EatingRules.NoChicken;
        creature.StopMoving();

        if (StateShadow.IsTravelling(creature)) StateShadow.Arrive(context, SystemName, creature);
        To(context, creature, PrisonRules.SeekingFoodState);
    }

    /// <summary>A captive state change, for a captive on state ids; a repeat is no move.</summary>
    private static void To(SimulationContext context, CreatureState creature, int state)
    {
        if (!CreatureStateMachine.HasState(creature) || creature.Dk2StateId == state) return;
        StateShadow.Move(context, SystemName, creature, state);
    }
}
