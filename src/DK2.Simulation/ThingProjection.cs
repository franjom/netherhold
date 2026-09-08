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
    int RoomsAlreadyInTheGrid,
    int NotYetProjected)
{
    public int Placed => Creatures + Objects;
}

/// <summary>
/// Turns a level's placed THINGS records into world contents.
///
/// <para><b>What a THING's coordinates are.</b> Bare tile indices. They are
/// 32-bit ints and the simulation carries positions in fixed-point subtile
/// units (<see cref="Fp32"/>, 4096 to the tile), so the natural reading is that
/// they are the same units — and they are not. Across all 66 shipped levels the
/// coordinates run x 0..118 and y 0..102 against a largest map of 121x105, and
/// not one of the 6279 records lands outside its own level's grid; under the
/// subtile reading every single one would. <c>dk2tools kwd things</c> with no
/// argument re-checks it.</para>
///
/// <para><b>What this places, and what it deliberately does not.</b> Creatures
/// and objects, because <see cref="WorldState"/> already holds both and both
/// are live simulation entities. Action points, doors, traps, hero parties and
/// effect generators are read from the file and counted, but the world has
/// nowhere to put them yet — see the remarks on
/// <see cref="ThingProjectionResult.NotYetProjected"/> below and Q222–Q224.
/// Nothing here adds state to the world hash, so this changes no
/// <c>HashVersion</c>.</para>
/// </summary>
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

        int creatures = 0, objects = 0, rooms = 0, skipped = 0;

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

        return new ThingProjectionResult(creatures, objects, rooms, skipped);
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

        // No floor on the result, deliberately. A creature that rounded to zero
        // health would die on the tick it arrived, so the obvious move is to
        // clamp to 1 — but the clamp could never fire: the smallest Hp in the
        // shipped table is 240, the smallest placed percentage is 5, and the
        // level curve only ever scales health up. A branch that cannot run is a
        // branch nobody maintains, so the guarantee is asserted in
        // PlacedThingsOnRealDataTests instead of being defended against here.
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
}
