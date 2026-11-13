// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>A hungry creature goes to a hatchery and eats a chicken, the eating half
/// of the room <see cref="HatcherySystem"/> stocks (ADR-0066).</summary>
/// <remarks>Follows the original's <c>HungerState.cpp</c> states one for one
/// (docs/research/eating.md): the walk is to a room and not to a bird, the nearest
/// reachable hatchery of the creature's own keeper; the bird is chosen on arrival
/// (<see cref="EatingRules.NearestChickenIn"/>) and nothing is reserved, so two
/// hungry creatures may race for one and the loser looks again (Q172, owner). An
/// empty hatchery is not a failure — it lays. Prisoners eat by their own states, one
/// bird paying off a whole <c>HungerFill</c> (<see cref="PrisonerEatSystem"/>). Ours:
/// composing the walk with the in-room search; trying each candidate hatchery in turn where
/// DK2's finder returns one room (Q790); the retry cadence living here; and no post-meal wander round the hatchery
/// (the original's state 0x5F, cosmetic — Q175).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/eating.md")]
public sealed class CreatureEatSystem : ICreatureUpdateStep
{
    private const string SystemName = "CreatureEat";

    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];

    public CreatureEatSystem() : this(new GridPathfinder()) { }

    public CreatureEatSystem(IPathfinder pathfinder) =>
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));

    public string Name => SystemName;

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    /// <summary>The hunger branch of the needs routine (<c>0x00477C20</c>) and the eating states,
    /// in the creature's update before its state handler.</summary>
    public void Update(SimulationContext context, CreatureState creature)
    {
        GameDatabase database = context.Database;
        if (creature.Possessed) return;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (!CreatureHealthRules.CanGetHungry(definition)) return;

        // The clock first, ahead of the attempt and of every state handler (0x00477C20 under 0x00476FC0).
        if (!CreatureDeathRules.IsDying(creature) && !CreatureDeathRules.IsCorpse(creature))
            Clock(context, definition, creature);

        if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Held | CreatureGuards.Imprisoned | CreatureGuards.Down)) return;

        // A creature walking out for good does not stop for lunch, for the
        // same reason it does not stop for a bed.
        if (creature.Activity == CreatureActivity.Leaving) return;
        if (Combat.CombatRules.Preoccupied(creature)) return;

        if (creature.Dk2StateId == EatingRules.AfterMealState)
        {
            if (context.Tick.Value < creature.ActionUntilTick) return;
            creature.ActionUntilTick = 0;
            if (creature.FoodOwedAfterMeal) BackToTheSearch(context, creature);
            else GiveUp(context, creature);
            return;
        }

        switch (creature.Activity)
        {
            case CreatureActivity.FetchingChicken:
                Fetching(context, creature, definition);
                break;

            case CreatureActivity.SeekingFood:
                Seeking(context, creature, definition);
                break;

            default:
                // Asleep is left asleep: the original's needs routine
                // refuses outright while the creature is in the sleeping
                // state category (Q176, read from the binary).
                if (creature.Activity is CreatureActivity.Sleeping
                    or CreatureActivity.GoingToSleep) break;

                // An idle creature goes looking, and so does one at a job whose state
                // admits Eating: the needs routine's mask test (NeedRules, ADR-0198).
                if (!NeedRules.MayAsk(creature, CreatureStateCategory.Eating)) break;
                if (!EatingRules.IsHungry(database, definition, creature)) break;

                // At most one attempt a game second, the original's
                // LastAttemptedHunger test.
                if (context.Tick.Value < creature.LastFoodAttemptTick + EatingRules.RetryTicks) break;
                creature.LastFoodAttemptTick = context.Tick.Value;

                // The busy test comes after the stamp, so a busy creature spends the try.
                if (NeedRules.IsBusy(context.World, creature, context.Tick.Value)) break;

                TrySetOutForAHatchery(context, creature, definition);
                break;
        }
    }

    /// <summary>The hunger clock: a helping added once <c>HungerRate</c> has passed since the stamp.
    /// Group 7 and the clock flags defer the update, not elapsed time (ADR-0579).</summary>
    public static void Clock(SimulationContext context, CreatureDef definition, CreatureState creature)
    {
        if (!NeedRules.HungerClockRuns(creature)) return;

        // The clock ADDS a helping; it does not raise a flag. A creature with nowhere to eat
        // therefore accumulates, and owes every helping it missed (docs/research/eating.md).
        if (!EatingRules.Tick(context.Database, definition, creature, context.Tick.Value)) return;

        context.Emit(SystemName, "creature_hungry",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} need={creature.HungerNeed} " +
            $"after={EatingRules.HungerRateSeconds(definition)}s");
    }

    /// <summary>Chooses a hatchery and sets off for it, or says why not.</summary>
    /// <remarks>Only a hatchery holding a chicken is a candidate
    /// (<see cref="EatingRules.HoldsAChicken"/>). Rooms are visited nearest first by squared
    /// distance to the room's centre, and the first this creature can reach wins; equal
    /// distances fall back to the lowest tile index (ADR-0001). Ours: DK2 measures to the
    /// room's first slab and breaks ties by its room list, and tries one room once, where ours
    /// draws a point again for each candidate whose point or route fails (Q790). The finder runs before the
    /// already-inside shortcut, as <c>Hunger_TryToEat</c> calls it before state 91, so a
    /// creature in an empty hatchery does not enter the chain. A failure posts the original's
    /// 41, 42 or 43 by its own gates (<see cref="RoomNeedRules.NoHatcheryFor"/>).</remarks>
    private bool TrySetOutForAHatchery(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var candidates = new List<(long Distance, int FirstTile, RoomInstance Room)>();
        int hatcheries = 0;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!EatingRules.IsHatcheryOf(room, creature.OwnerPlayerId)) continue;
            if (room.TileIndices.Count == 0) continue;
            hatcheries++;

            if (!EatingRules.HoldsAChicken(world, room)) continue;

            candidates.Add((
                EatingRules.DistanceSquared(world, creature.Tile, CentreTileIndex(world, room)),
                room.TileIndices[0], room));
        }

        candidates.Sort((a, b) =>
            a.Distance != b.Distance
                ? a.Distance.CompareTo(b.Distance)
                : a.FirstTile.CompareTo(b.FirstTile));

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        RoomInstance? chosen = null;
        int arrival = -1;

        foreach ((_, _, RoomInstance room) in candidates)
        {
            if (EatingRules.Contains(room, here))
            {
                chosen = room;
                break;
            }

            // 0x004634B0 walks through the room helper 0x00471A70, whose point is 0x004E4C10's
            // random(size) along the slab chain (Room.cpp:1509), as every room walk's is.
            if (RoomWalkRules.ArrivalTile(context, room) is not int target) continue;
            var at = new TilePos(target % world.Width, target / world.Width);
            if (!context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.Loose)) continue;

            chosen = room;
            arrival = target;
            break;
        }

        if (chosen is null)
        {
            context.Emit(SystemName, "food_attempt_failed",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} at=({creature.Tile.X},{creature.Tile.Y}) " +
                $"need={creature.HungerNeed} " +
                $"reason={(hatcheries == 0 ? "no_hatchery"
                    : candidates.Count == 0 ? "hatchery_empty" : "unreachable")} " +
                $"hatcheries={hatcheries} with_chickens={candidates.Count}");
            RoomNeedRules.NoHatcheryFor(context, creature, definition);
            return false;
        }

        if (arrival < 0)
        {
            // The room helper's own shortcut: already inside means no walk,
            // SetState(1) and then the resume state (travel-states.md 2.2).
            EnterTheChain(context, creature);
            Shadow(context, creature, CreatureStateMachine.Idle);
            Shadow(context, creature, EatingRules.InHatcheryState);
            creature.StopMoving();
            // 91's handler only sets 92; 92's handler runs on the next turn (0x00471A70).
            return true;
        }

        creature.TargetObjectId = EatingRules.NoChicken;
        EnterTheChain(context, creature);
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToRoom, EatingRules.InHatcheryState);

        context.Emit(SystemName, "food_sought",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} at=({creature.Tile.X},{creature.Tile.Y}) " +
            $"need={creature.HungerNeed} tiles={chosen.TileCount} " +
            $"to=({arrival % world.Width},{arrival / world.Width}) steps={_route.Count} " +
            $"candidates={candidates.Count}");
        return true;
    }

    /// <summary>
    /// Walking to the hatchery, and then standing in it looking for a bird.
    ///
    /// <para>Nothing happens while there is route left. When it runs out the
    /// tile underfoot decides: not a hatchery of this keeper's (sold, captured
    /// or a blocked route) ends the state and leaves the creature idle to try
    /// again; a hatchery with a chicken in it starts the short walk to that
    /// chicken; a hatchery with none leaves the creature standing there,
    /// hungry, to ask again next second — which is the original's own
    /// answer.</para>
    /// </summary>
    private void Seeking(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.HasRoute) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;

        RoomInstance? room = EatingRules.HatcheryUnder(world, database, creature);
        if (room is null)
        {
            context.Emit(SystemName, "food_attempt_failed",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) " +
                $"need={creature.HungerNeed} reason=not_in_hatchery");
            GiveUp(context, creature);
            return;
        }

        // The walk is over and the room is still this keeper's hatchery: state 7's arrival,
        // which hands over to 92. A creature already standing in 92 asking again is not
        // arriving, so this happens once per walk.
        if (StateShadow.IsTravelling(creature)) Arrive(context, creature);

        // Nothing left to pay off — fed on the way, or the last bird went down
        // last tick. Note this is StillOwesFood and not IsHungry: it takes a
        // whole helping to set out and none at all to keep eating.
        if (!EatingRules.StillOwesFood(creature))
        {
            GiveUp(context, creature);
            return;
        }

        ObjectState? chicken = EatingRules.NearestChickenIn(world, database, room, creature.PosX, creature.PosY);
        if (chicken is null)
        {
            // Standing in an empty hatchery is the original's holding pattern,
            // not a failure. It stays in the state and asks again.
            return;
        }

        SetOffForTheChicken(context, creature, definition, chicken);
    }

    /// <summary>
    /// Walking to one chosen chicken, and eating it on arrival.
    ///
    /// <para>The bird is re-checked every tick, because nothing reserved it: it
    /// may have been eaten by somebody faster, picked up by an imp, or have
    /// stopped being in this room. Any of those sends the creature back to
    /// <see cref="CreatureActivity.SeekingFood"/> to search again rather than
    /// to idle, because it is standing in a hatchery and still hungry.</para>
    /// </summary>
    private void Fetching(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        ObjectState? chicken = creature.TargetObjectId >= 0
            ? world.ObjectById(creature.TargetObjectId)
            : null;

        if (chicken is null || !EatingRules.IsEdibleChicken(chicken))
        {
            context.Emit(SystemName, "chicken_lost",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} chicken={creature.TargetObjectId}");
            BackToTheSearch(context, creature);
            return;
        }

        // Still walking: state 6 hands over to 93 only at its arrival.
        if (creature.HasRoute) return;

        // A route that ran out short of the bird (it moved, or a waypoint stopped being walkable)
        // is a failed approach, and the search starts again from where the creature now stands.
        if (EatingRules.DistanceSquared(world, creature.Tile, chicken.TileIndex)
            > EatingRules.ReachTilesSquared)
        {
            BackToTheSearch(context, creature);
            return;
        }

        // Within reach: state 6's arrival hands over to 93, which eats.
        if (StateShadow.IsTravelling(creature)) Arrive(context, creature);
        Swallow(context, creature, definition, chicken);
    }

    private void SetOffForTheChicken(
        SimulationContext context, CreatureState creature,
        CreatureDef definition, ObjectState chicken)
    {
        WorldState world = context.World;

        creature.StopMoving();

        // 92 hands the bird's own point to the position helper 0x00471890(point, 93, 0xE8): state 6,
        // resuming in 93. It has no already-there shortcut, so even a bird underfoot is a walk, and
        // its arrival reaches 93 (travel-states.md 2.1, 8). A failed route returns 0 (0x004636B1), and
        // the dispatcher drops a handler's 0 to state 1 (0x004772B0), as 92's other failures do.
        if (!TunnelRules.RouteOrTunnel(context, SystemName, creature, chicken.PosX, chicken.PosY,
                StateShadow.TravelToPoint, EatingRules.EatState, Navigation.MoveOptions.ExactSpot,
                chicken.PosX >> 12, chicken.PosY >> 12))
        {
            GiveUp(context, creature);
            return;
        }

        // Then the bird is prepared (0x004636B3-0x004636E4): its fear marker (0x0049CE70(1), +0x152),
        // process 8, whose first turn draws the reaction timer and then holds it still, and both scale
        // words at 1.0. Ours keeps no +0x58/+0x5C, which it also clears.
        creature.FoodOwedAfterMeal = false;     // +0xAB
        creature.TargetObjectId = chicken.Id;   // +0xAF
        chicken.ChickenScared = true;
        ChickenMovementRules.SetProcess(chicken, ChickenProcess.Reaction);
        chicken.ChickenScale = new Fp32(Fp32.One);

        int x = chicken.TileIndex % world.Width;
        int y = chicken.TileIndex / world.Width;
        context.Emit(SystemName, "chicken_chosen",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} chicken={chicken.Id} " +
            $"at=({x},{y}) steps={creature.Body.PathTotalCount}");
    }

    /// <summary>One bird eaten: one tick's work once the creature is within a tile.
    /// Health up by <c>min(HpFromChicken, missing)</c>, <c>HungerNeed</c> down by one
    /// helping, the clock restarted and the bird destroyed.</summary>
    private static void Swallow(
        SimulationContext context, CreatureState creature,
        CreatureDef definition, ObjectState chicken)
    {
        GameDatabase database = context.Database;

        // State 93 asks for the chicken's own sound first (0x00463859, the bird's category and voice, at the
        // bird), then, once the meal is paid off, the eater's (0x0046398D).
        ThingSoundRules.Request(context, SystemName, chicken, ThingSoundEvent.ChickenEaten);
        // Then the hunger grievance is wiped, every bird and before the meal (0x00463760, ADR-0651).
        CreatureAngerRules.Forget(context.World, definition, creature, AngerBucket.Hunger);
        int before = creature.Health;
        int gained = EatingRules.Eat(database, definition, creature, context.Tick.Value);
        ThingSoundRules.Request(context, SystemName, creature, ThingSoundEvent.Ate);
        context.World.RemoveObject(chicken.Id);

        context.Emit(SystemName, "chicken_eaten",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} chicken={chicken.Id} " +
            $"health={before}->{creature.Health} gained={gained} " +
            $"need={creature.HungerNeed}");

        // 93 has eaten; 94 waits out the meal and decides.
        creature.FoodOwedAfterMeal = EatingRules.StillOwesFood(creature);
        creature.TargetObjectId = EatingRules.NoChicken;
        creature.StopMoving();
        creature.ActionUntilTick = context.Tick.Value;
        if (AnimationTimingRules.ResolvesPresent(definition, 13))
        {
            CreatureAnimationTiming animation = definition.ArtSlot(AnimationTimingRules.ResolveArtSlot(definition, 13));
            creature.ActionUntilTick += AnimationTimingRules.FirstCycleHoldTicks(animation);
        }
        Shadow(context, creature, EatingRules.AfterMealState);
    }

    /// <summary>Standing in the hatchery, hungry, with no bird chosen: state 92.</summary>
    private static void BackToTheSearch(SimulationContext context, CreatureState creature)
    {
        creature.TargetObjectId = EatingRules.NoChicken;
        Shadow(context, creature, EatingRules.InHatcheryState);
        creature.StopMoving();
    }

    /// <summary>Out of the eating states entirely, and idle: state 1.</summary>
    private static void GiveUp(SimulationContext context, CreatureState creature)
    {
        creature.TargetObjectId = EatingRules.NoChicken;
        Shadow(context, creature, CreatureStateMachine.Idle);
        creature.StopMoving();
    }

    /// <summary>From idle into state 91, the needs routine's entry into eating
    /// (<c>eating.md</c> §3).</summary>
    private static void EnterTheChain(SimulationContext context, CreatureState creature)
    {
        // Taken off a job: 0x004633D0 sets 91 from the job's own state.
        if (NeedRules.IsAtAJob(creature)) NeedRules.EndJob(creature);
        Shadow(context, creature, EatingRules.FindHatcheryState);
    }

    private static void Arrive(SimulationContext context, CreatureState creature) =>
        StateShadow.Arrive(context, SystemName, creature);

    private static void Shadow(SimulationContext context, CreatureState creature, int state) =>
        StateShadow.Move(context, SystemName, creature, state);

    /// <summary>The room's centre tile, rounded down — the same shape a lair uses.</summary>
    private static int CentreTileIndex(WorldState world, RoomInstance room) =>
        ((room.MinY + ((room.MaxY - room.MinY) / 2)) * world.Width)
        + room.MinX + ((room.MaxX - room.MinX) / 2);
}
