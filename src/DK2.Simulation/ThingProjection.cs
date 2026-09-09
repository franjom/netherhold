// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// What one level's THINGS placed, and what it did not.
///
/// <para>Returned rather than logged so a caller can assert on it. A projection
/// that silently ignores two thirds of a file looks identical to one that
/// works, and the counts are the only thing that tells them apart.</para>
/// </summary>
public readonly record struct ThingProjectionResult(
    int Creatures,
    int Objects,
    int ActionPoints,
    int RoomsAlreadyInTheGrid,
    int NotYetProjected)
{
    public int Placed => Creatures + Objects + ActionPoints;
}

/// <summary>Turns a level's placed THINGS records into world contents.</summary>
/// <remarks>
/// <para>A THING's coordinates are bare tile indices, not the fixed-point subtile units the
/// simulation carries positions in (<see cref="Fp32"/>, 4096 to the tile). Across all 66 shipped
/// levels they run x 0..118 and y 0..102 against a largest map of 121x105, and not one of the 6279
/// records lands outside its own level's grid; under the subtile reading every one would.
/// <c>dk2tools kwd things</c> re-checks it.</para>
/// <para>Places creatures, objects and action points, because <see cref="WorldState"/> holds each
/// as simulation state (action points are hashed state, ADR-0103). Not implemented: doors, traps,
/// hero parties and effect generators are read and counted but the world has nowhere to put them
/// (<see cref="ThingProjectionResult.NotYetProjected"/>, Q222–Q224).</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/placed-things.md")]
public static class ThingProjection
{
    /// <summary>
    /// A creature or room record's <c>InitialHealth</c> is a <b>percentage of
    /// the entity's maximum</b>, not an absolute hit-point count.
    ///
    /// <para>Read off the shipped corpus: of 2891 placed creatures 2851 carry
    /// exactly 100, the rest carry 5, 10, 15, 20, 25, 30, 45, 50, 60, 70 or 80, and
    /// <b>nothing anywhere carries more than 100</b>. An absolute reading would
    /// make every creature in the game a 100 hp creature regardless of its
    /// record — which is close enough to plausible for a Goblin that it could
    /// have survived a long time unnoticed.</para>
    /// </summary>
    public const int FullHealthPercent = 100;

    /// <summary>
    /// Places every THING this projection understands into <paramref name="world"/>.
    ///
    /// <para>Order is the file's order, and that is deliberate: creature update
    /// order is behaviour (see <see cref="WorldState.Creatures"/>), so two imps
    /// that reach for the same job must resolve it the way the shipped level
    /// lists them, not the way a sort would.</para>
    /// </summary>
    public static ThingProjectionResult Place(
        WorldState world, GameDatabase database, IEnumerable<ThingRecord> things)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (things is null) throw new ArgumentNullException(nameof(things));

        int creatures = 0, objects = 0, actionPoints = 0, rooms = 0, skipped = 0;

        foreach (ThingRecord record in things)
        {
            switch (record)
            {
                case KeeperCreatureThing t:
                    PlaceCreature(world, database, t, t.CreatureId, t.PlayerId, t.InitialHealth);
                    creatures++;
                    break;

                // Neither the neutral nor the good creature record carries a
                // player field, because their side is fixed by which record
                // they are. PlayerIds documents both ids as CONFIRMED.
                case NeutralCreatureThing t:
                    PlaceCreature(world, database, t, t.CreatureId, PlayerIds.Neutral, t.InitialHealth);
                    creatures++;
                    break;

                case GoodCreatureThing t:
                    PlaceCreature(world, database, t, t.CreatureId, PlayerIds.Good, t.InitialHealth);
                    creatures++;
                    break;

                case ObjectThing t:
                    PlaceObject(world, database, t);
                    objects++;
                    break;

                case ActionPointThing t:
                    PlaceActionPoint(world, t);
                    actionPoints++;
                    break;

                // A ROOM record does not place a room: the MAP grid already
                // carries the room's terrain, and WorldState.FromMap has
                // already laid it down. Checked rather than assumed — across
                // the corpus all 275 of them agree with the terrain id under
                // them, with no exceptions. Projecting one as a placement would
                // build every dungeon heart and hero gate twice.
                case RoomThing:
                    rooms++;
                    break;

                default:
                    skipped++;
                    break;
            }
        }

        return new ThingProjectionResult(creatures, objects, actionPoints, rooms, skipped);
    }

    private static void PlaceCreature(
        WorldState world,
        GameDatabase database,
        CreatureThing thing,
        byte creatureTypeId,
        byte ownerPlayerId,
        int initialHealthPercent)
    {
        CreatureState state = world.SpawnCreature(
            database, creatureTypeId, ownerPlayerId, new TilePos(thing.X, thing.Y));

        // Level before health, because maximum health is the record's Hp scaled
        // by the level curve. Setting health first and the level after would
        // leave a level-10 creature standing at a level-1 creature's maximum.
        state.Level = CreatureLevelRules.Clamp(thing.Level);
        state.GoldHeld = thing.GoldHeld;

        // No floor on the result, deliberately: a creature that rounded to zero health would die on
        // the tick it arrived, but the clamp could never fire — the smallest Hp in the shipped table
        // is 240, the smallest placed percentage is 5, and the level curve only scales health up. A
        // branch that cannot run is a branch nobody maintains, so the guarantee is asserted in
        // PlacedThingsOnRealDataTests instead.
        CreatureDef def = database.CreatureById(creatureTypeId);
        int max = CreatureLevelRules.MaxHealth(database, def, state.Level);
        state.Health = max * initialHealthPercent / FullHealthPercent;
    }

    private static void PlaceObject(WorldState world, GameDatabase database, ObjectThing thing)
    {
        ObjectState state = world.SpawnObject(
            database, thing.ObjectId, thing.PlayerId, (thing.Y * world.Width) + thing.X);

        // MoneyAmount is what a placed gold pile is worth. It is zero on every
        // object that is not gold, so this needs no type test.
        state.GoldHeld = thing.MoneyAmount;
    }

    private static void PlaceActionPoint(WorldState world, ActionPointThing thing)
    {
        // DK2 copies coordinate words, copies only five bits out of Flags, and
        // scales WaitDelay once at load. Unchecked casts reproduce those
        // 16-bit stores rather than adding validation the original lacks.
        world.AddActionPoint(new ActionPointState
        {
            Id = thing.ActionPointId,
            StartX = unchecked((ushort)thing.StartX),
            StartY = unchecked((ushort)thing.StartY),
            EndX = unchecked((ushort)thing.EndX),
            EndY = unchecked((ushort)thing.EndY),
            TriggerId = thing.TriggerId,
            NextWaypointId = thing.NextWaypointId,
            Flags = (byte)(thing.Flags & ActionPointState.LoadedFlagMask),
            PassageCount = 0,
            WaitTimerTicks = unchecked((ushort)(
                thing.WaitDelay * SimulationTiming.OriginalTicksPerGameSecond)),
        });
    }
}
