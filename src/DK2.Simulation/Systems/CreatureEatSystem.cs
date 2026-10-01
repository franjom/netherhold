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
/// composing the walk with the in-room search, the walking routine being unestablished
/// (Q174); the retry cadence living here; and no post-meal wander round the hatchery
/// (the original's state 0x5F, cosmetic — Q175).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/eating.md")]
public sealed class CreatureEatSystem : ISimulationSystem
{
    private const string SystemName = "CreatureEat";

    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];

    public CreatureEatSystem() : this(new GridPathfinder()) { }

    public CreatureEatSystem(IPathfinder pathfinder) =>
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
            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);


            if (!CreatureHealthRules.CanGetHungry(definition)) continue;
            if (HandRules.IsHeld(creature)) continue;
            if (PrisonRules.IsImprisoned(creature)) continue;
            if (CreatureDeathRules.IsDown(creature)) continue;

            // A creature walking out for good does not stop for lunch, for the
            // same reason it does not stop for a bed.
            if (creature.Activity == CreatureActivity.Leaving) continue;
            if (Combat.CombatRules.Preoccupied(creature)) continue;

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
                    creature.FoodAttemptTicks--;
                    if (creature.FoodAttemptTicks > 0) break;
                    creature.FoodAttemptTicks = EatingRules.RetryTicks;

                    // The busy test comes after the stamp, so a busy creature spends the try.
                    if (NeedRules.IsBusy(context.World, creature, context.Tick.Value)) break;

                    // Standing in one already? Then there is no walk to make and
                    // the search runs here and now.
                    if (EatingRules.HatcheryUnder(world, database, creature) is not null)
                    {
                        // The room helper's own shortcut: already inside means no walk,
                        // SetState(1) and then the resume state (travel-states.md 2.2).
                        EnterTheChain(context, creature);
                        Shadow(context, creature, CreatureStateMachine.Idle);
                        Shadow(context, creature, EatingRules.InHatcheryState);
                        creature.StopMoving();
                        Seeking(context, creature, definition);
                        break;
                    }

                    TrySetOutForAHatchery(context, creature, definition);
                    break;
            }
        }
    }

    /// <summary>Chooses a hatchery and sets off for it, or says why not.</summary>
    /// <remarks>Rooms are visited nearest first by squared distance to the room's
    /// centre, and the first this creature can actually reach over its own
    /// <see cref="WorldPathGrid"/> wins — so a hatchery behind water is reachable for
    /// a goblin and not for a vampire, exactly as a lair is. Equal distances fall back
    /// to the lowest tile index, <see cref="RoomIndex.Build"/>'s own order (ADR-0001).
    /// A hatchery with no birds in it is still a hatchery and the finder does not look
    /// at the flock; what the original tests is whether the room holds any object at
    /// all (<see cref="EatingRules.HoldsAnyObject"/>), which a coop satisfies, so the
    /// one thing it rules out is a room too small to earn a coop and therefore too
    /// small ever to lay.</remarks>
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

            // The original's own gate: a hatchery with nothing in it is not
            // walked to. Its object list, not its flock - a coop counts - so
            // this excludes exactly one thing, a room too small to earn a coop
            // and therefore too small ever to lay (EatingRules.HoldsAnyObject).
            if (!EatingRules.HoldsAnyObject(world, room)) continue;

            candidates.Add((
                EatingRules.DistanceSquared(world, creature.Tile, CentreTileIndex(world, room)),
                room.TileIndices[0], room));
        }

        candidates.Sort((a, b) =>
            a.Distance != b.Distance
                ? a.Distance.CompareTo(b.Distance)
                : a.FirstTile.CompareTo(b.FirstTile));

        var grid = new WorldPathGrid(database, world, definition);
        RoomInstance? chosen = null;
        int arrival = -1;

        foreach ((_, _, RoomInstance room) in candidates)
        {
            int target = ArrivalTile(world, room);
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
                $"hatcheries={hatcheries} with_objects={candidates.Count}");
            return false;
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

        ObjectState? chicken = EatingRules.NearestChickenIn(world, room, creature.Tile);
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

        if (EatingRules.DistanceSquared(world, creature.Tile, chicken.TileIndex)
            > EatingRules.ReachTilesSquared)
        {
            // Still walking. A route that ran out short of the bird — a waypoint
            // that stopped being walkable — is a failed approach, and the search
            // starts again from where the creature now stands.
            if (creature.HasRoute) return;

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
        GameDatabase database = context.Database;

        creature.TargetObjectId = chicken.Id;
        creature.StopMoving();

        // 92 hands the bird to the position helper: state 6, resuming in 93. That helper has
        // no already-there shortcut, unlike the room one - so even a bird underfoot is a
        // walk, and it is its arrival that reaches 93 (travel-states.md 2.1, 8).
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, EatingRules.EatState);

        int x = chicken.TileIndex % world.Width;
        int y = chicken.TileIndex / world.Width;

        // Already on it or beside it: eat this tick, no walk at all.
        if (EatingRules.DistanceSquared(world, creature.Tile, chicken.TileIndex)
            <= EatingRules.ReachTilesSquared)
        {
            if (StateShadow.IsTravelling(creature)) Arrive(context, creature);
            Swallow(context, creature, definition, chicken);
            return;
        }

        context.Navigation.TryRoute(creature, new TilePos(x, y), Navigation.MoveOptions.ExactSpot);

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

        int before = creature.Health;
        int gained = EatingRules.Eat(database, definition, creature);
        context.World.RemoveObject(chicken.Id);

        context.Emit(SystemName, "chicken_eaten",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} chicken={chicken.Id} " +
            $"health={before}->{creature.Health} gained={gained} " +
            $"need={creature.HungerNeed}");

        // 93 has eaten; 94 waits out the meal and decides.
        Shadow(context, creature, EatingRules.AfterMealState);

        if (EatingRules.StillOwesFood(creature))
        {
            // Still owing birds: back into the room's search. The original
            // stores exactly this question — HungerNeed != 0 — in a byte on the
            // state object the moment a chicken goes down, and its eating states
            // branch on that byte to go round again.
            BackToTheSearch(context, creature);
            return;
        }

        GiveUp(context, creature);
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

    /// <summary>
    /// Which tile of the room to walk at: its centre when that tile is part of
    /// the room, and its lowest tile index otherwise — a hatchery need not be
    /// rectangular, and an L-shaped one has a centre that is not in it.
    /// </summary>
    private static int ArrivalTile(WorldState world, RoomInstance room)
    {
        int centre = CentreTileIndex(world, room);
        return EatingRules.Contains(room, centre) ? centre : room.TileIndices[0];
    }
}
