// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// A hungry creature goes to a hatchery and eats a chicken — the half of the
/// room that had been missing since <see cref="HatcherySystem"/> started laying
/// (ADR-0066), and the answer to the oldest "cannot" in the project: a creature
/// got hungry and nothing could be done about it.
///
/// <para><b>Read from the original's own <c>HungerState.cpp</c></b>
/// (<c>docs/research/eating.md</c>), whose states this follows one for one:</para>
///
/// <list type="number">
/// <item><b>The need is asked once a game second</b>, from the same routine
/// that asks about sleep and pay — and <b>before</b> either of them, which is
/// the order the original's routine has and the reason this system is
/// registered above <see cref="CreatureSleepSystem"/>. A creature that is
/// already asleep is left asleep; that narrowing is ours and is named
/// below.</item>
/// <item><b>The walk is to a room, not to a bird</b>
/// (<see cref="CreatureActivity.SeekingFood"/>): the nearest reachable
/// hatchery of the creature's own keeper, an ally's being no use to it.</item>
/// <item><b>The bird is chosen on arrival</b>, from inside the room, as the
/// nearest chicken by straight-line squared distance
/// (<see cref="EatingRules.NearestChickenIn"/>). An empty hatchery is not a
/// failure: the creature stands in it and asks again a second later, which is
/// exactly what the original's state does when its search comes back empty.
/// The hatchery is laying, after all.</item>
/// <item><b>Nothing is reserved</b>, so two hungry creatures may set off for
/// one chicken and the second to arrive finds it gone and looks again
/// (<see cref="CreatureActivity.FetchingChicken"/>). The owner watched exactly
/// this in a prison (Q172): <i>"they rush toward that chicken, the first one to
/// it gets it"</i>.</item>
/// <item><b>Eating is one tick's work</b> once the creature is within a tile:
/// health up by <c>min(HpFromChicken, missing)</c>, <c>HungerNeed</c> down by
/// one, the clock restarted and the bird destroyed.</item>
/// </list>
///
/// <para><b>What is ours and stated as such.</b> The original reaches its
/// in-room search state directly and its handler simply fails when the creature
/// is not standing in a hatchery; which routine walks it there in the ordinary
/// case is <b>not established</b> — state 0x5A does exactly that walk, and the
/// only caller found for it is a fallback for one particular player id
/// (Q174). This system composes the two, which produces the same observable
/// behaviour by the shortest honest route. Also ours: the retry cadence being
/// driven from this system rather than from a needs routine shared with sleep,
/// and the absence of the post-meal wander around the hatchery (the original's
/// state 0x5F, which picks a random adjacent tile — cosmetic here, and Q175).
/// </para>
///
/// <para><b>A sleeping creature is left asleep, and that is the original's rule
/// rather than a guess</b> (Q176). Its needs routine refuses outright when the
/// current state's category byte is 7, and the three sleep states carry 7 —
/// corroborated by the sleep block's own refusal to fire when the creature is
/// already in the sleeping category. So a hungry creature does <b>not</b> get up
/// to eat. This system skips <see cref="CreatureActivity.Sleeping"/> and
/// <see cref="CreatureActivity.GoingToSleep"/>, and a test pins it.</para>
///
/// <para><b>Prisoners are skipped here too.</b> They eat by a different state
/// in the original — one bird pays off the whole of <c>HungerFill</c> rather
/// than one (<see cref="EatingRules.EatInPrison"/>) — and they cannot move yet
/// (Q172), so their second still belongs to <see cref="PrisonSystem"/>.</para>
/// </summary>
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

                    // Only an idle creature goes looking. A worker mid-job keeps
                    // its job; DK2 gates the same way, on a state-category byte
                    // rather than on this list.
                    if (creature.Activity != CreatureActivity.Idle) break;
                    if (!EatingRules.IsHungry(database, definition, creature)) break;

                    // At most one attempt a game second, the original's
                    // LastAttemptedHunger test.
                    creature.FoodAttemptTicks--;
                    if (creature.FoodAttemptTicks > 0) break;
                    creature.FoodAttemptTicks = EatingRules.RetryTicks;

                    // Standing in one already? Then there is no walk to make and
                    // the search runs here and now.
                    if (EatingRules.HatcheryUnder(world, database, creature) is not null)
                    {
                        creature.Activity = CreatureActivity.SeekingFood;
                        creature.Path.Clear();
                        creature.PathIndex = 0;
                        Seeking(context, creature, definition);
                        break;
                    }

                    TrySetOutForAHatchery(context, creature, definition);
                    break;
            }
        }
    }

    /// <summary>
    /// Choose a hatchery and set off for it, or say why not.
    ///
    /// <para>Rooms are visited nearest first by squared distance to the room's
    /// centre, and the first one this creature can actually reach over its own
    /// <see cref="WorldPathGrid"/> wins — so a hatchery behind water is
    /// reachable for a goblin and not for a vampire, exactly as a lair is.
    /// Equal distances fall back to the lowest tile index, which is
    /// <see cref="RoomIndex.Build"/>'s own order and is reproducible
    /// (ADR-0001).</para>
    ///
    /// <para><b>A hatchery with no birds in it is still a hatchery</b>, and the
    /// finder does not look at the flock — the room lays, so an empty one will
    /// have a chicken shortly. What the original <i>does</i> test is whether the
    /// room holds any object at all (<see cref="EatingRules.HoldsAnyObject"/>),
    /// which a coop satisfies; the one thing it rules out is a hatchery too
    /// small to earn a coop, and therefore too small ever to lay.</para>
    /// </summary>
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
            if (!_pathfinder.TryFindPath(grid, creature.Tile, at, _route)) continue;

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

        creature.Path.Clear();
        creature.Path.AddRange(_route);
        creature.PathIndex = 0;
        creature.TargetObjectId = EatingRules.NoChicken;
        creature.Activity = CreatureActivity.SeekingFood;

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
        if (creature.PathIndex < creature.Path.Count) return;

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
            GiveUp(creature);
            return;
        }

        // Nothing left to pay off — fed on the way, or the last bird went down
        // last tick. Note this is StillOwesFood and not IsHungry: it takes a
        // whole helping to set out and none at all to keep eating.
        if (!EatingRules.StillOwesFood(creature))
        {
            GiveUp(creature);
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
            BackToTheSearch(creature);
            return;
        }

        if (EatingRules.DistanceSquared(world, creature.Tile, chicken.TileIndex)
            > EatingRules.ReachTilesSquared)
        {
            // Still walking. A route that ran out short of the bird — a waypoint
            // that stopped being walkable — is a failed approach, and the search
            // starts again from where the creature now stands.
            if (creature.PathIndex < creature.Path.Count) return;

            BackToTheSearch(creature);
            return;
        }

        Swallow(context, creature, definition, chicken);
    }

    private void SetOffForTheChicken(
        SimulationContext context, CreatureState creature,
        CreatureDef definition, ObjectState chicken)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        creature.TargetObjectId = chicken.Id;
        creature.Activity = CreatureActivity.FetchingChicken;
        creature.Path.Clear();
        creature.PathIndex = 0;

        int x = chicken.TileIndex % world.Width;
        int y = chicken.TileIndex / world.Width;

        // Already on it or beside it: eat this tick, no walk at all.
        if (EatingRules.DistanceSquared(world, creature.Tile, chicken.TileIndex)
            <= EatingRules.ReachTilesSquared)
        {
            Swallow(context, creature, definition, chicken);
            return;
        }

        var grid = new WorldPathGrid(database, world, definition);
        if (_pathfinder.TryFindPath(grid, creature.Tile, new TilePos(x, y), _route))
        {
            creature.Path.AddRange(_route);
        }

        context.Emit(SystemName, "chicken_chosen",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} chicken={chicken.Id} " +
            $"at=({x},{y}) steps={creature.Path.Count}");
    }

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

        if (EatingRules.StillOwesFood(creature))
        {
            // Still owing birds: back into the room's search. The original
            // stores exactly this question — HungerNeed != 0 — in a byte on the
            // state object the moment a chicken goes down, and its eating states
            // branch on that byte to go round again.
            BackToTheSearch(creature);
            return;
        }

        GiveUp(creature);
    }

    /// <summary>Standing in the hatchery, hungry, with no bird chosen.</summary>
    private static void BackToTheSearch(CreatureState creature)
    {
        creature.TargetObjectId = EatingRules.NoChicken;
        creature.Activity = CreatureActivity.SeekingFood;
        creature.Path.Clear();
        creature.PathIndex = 0;
    }

    /// <summary>Out of the eating states entirely, and idle.</summary>
    private static void GiveUp(CreatureState creature)
    {
        creature.TargetObjectId = EatingRules.NoChicken;
        creature.Activity = CreatureActivity.Idle;
        creature.Path.Clear();
        creature.PathIndex = 0;
    }

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
