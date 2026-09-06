// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Hunger and the chicken that answers it — the creature half of the hatchery,
/// read from the original's own <c>HungerState.cpp</c>
/// (<c>docs/research/eating.md</c>).
///
/// <para><b>The original's shape, and every number in it is read rather than
/// guessed.</b> DK2 keeps a creature's hunger as an accumulator it calls
/// <c>HungerNeed</c> — its own debug dump prints the name — and drives it from
/// the same routine that drives sleep and pay:</para>
///
/// <list type="number">
/// <item><b>The clock adds, it does not set.</b> Every <c>HungerRate</c> (a
/// creature record field in seconds, which the original multiplies by ticks-per-
/// second as it loads it) the routine does <c>HungerNeed += HungerFill</c> and
/// stamps <c>LastAteTime</c>. Nothing caps it, so a creature with nowhere to eat
/// keeps accumulating and owes that many chickens when a hatchery finally
/// appears.</item>
/// <item><b>Hungry means <c>HungerNeed &gt;= HungerFill</c></b> — the same
/// number, level-scaled through creature stat row 9 both times. So the first
/// period makes a creature hungry and each chicken pays one off.</item>
/// <item><b>The attempt runs at most once a game second</b> — the original
/// compares <c>LastAttemptedHunger + 4</c> against the game tick, and four ticks
/// is one game second (Q1/Q132). <c>TimeBetweenReattemptingHungerSeconds</c> is
/// 4 and the prison's copy of this test reads it; the general path has the 4 as
/// a literal. The two agree.</item>
/// <item><b>A chicken restores <c>min(HpFromChicken, MaxHealth − Health)</c></b>
/// — the original computes the missing health from the <i>level-scaled</i>
/// maximum and takes the smaller. <c>HpFromChicken</c> itself is <b>not</b>
/// level-scaled; only the ceiling is.</item>
/// <item><b>One chicken pays off exactly one</b> in a hatchery
/// (<see cref="Eat"/>), and <b>the whole of <c>HungerFill</c></b> in a prison
/// (<see cref="EatInPrison"/>). That asymmetry is in the binary and is
/// reproduced rather than tidied: a caged Bile Demon is filled by one bird
/// where a free one eats five.</item>
/// </list>
///
/// <para><b>Finding the bird.</b> Inside a hatchery the original walks the
/// room's own tile chain, takes the first object on each tile <b>whose object
/// type id is 9</b>, and keeps the one with the smallest straight-line squared
/// distance — not a path length, and not a ring search. Nothing is reserved
/// anywhere: the chosen bird's id is written on the creature and on nothing
/// else, which is exactly what the owner watched in a prison (Q172):
/// <i>"they rush toward that chicken, the first one to it gets it"</i>. The
/// loser arrives to find it gone and searches again.</para>
///
/// <para><b>Id or flag, and the difference is real.</b> The hatchery search
/// matches on the object <i>id</i> 9; the prison search matches on the
/// <see cref="ObjectFlags.TypeFood"/> bit. Those are not the same set — <b>two
/// of the 145 OBJECTS records carry TypeFood, the Chicken and the Torture
/// Pendulum</b> — so a pendulum standing in a prison is food to a prisoner and
/// one standing in a hatchery is not. This class matches the id, because that
/// is what the hatchery path does; <see cref="IsFood"/> exists for the prison
/// path to use when prisoners can move.</para>
///
/// <para><b>In reach</b> is a squared distance below <c>0x1001</c> in the
/// original's fixed point, whose tile is 4096 — so one tile, inclusive of its
/// corner-to-corner boundary by exactly one unit. Ours compares whole tiles,
/// because every object here stands at a tile centre.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/eating.md")]
public static class EatingRules
{
    /// <summary>No chicken chosen — <see cref="CreatureState.TargetObjectId"/>'s idle value.</summary>
    public const int NoChicken = -1;

    /// <summary>
    /// How near a creature must be to eat: one tile, squared.
    ///
    /// <para>The original's test is <c>dx² + dy² &lt; 0x1001</c> on fixed-point
    /// positions with 4096 to the tile, so a bird exactly one tile away is in
    /// reach and one any further is not. Ours is in whole tiles because objects
    /// stand at tile centres, and the two agree on every position ours can
    /// produce.</para>
    /// </summary>
    public const int ReachTilesSquared = 1;

    /// <summary>
    /// How long between attempts to go and eat: <b>one game second</b>.
    ///
    /// <para>The original stamps <c>LastAttemptedHunger</c> and refuses another
    /// attempt until four game ticks have passed. Four ticks is one game second
    /// on every shipped level (Q1/Q132), and the prison's copy of the same test
    /// reads <c>TimeBetweenReattemptingHungerSeconds</c>, which is 4. The two
    /// paths therefore retry at the same rate whichever way you read the
    /// unit.</para>
    /// </summary>
    public static int RetryTicks => SimulationTiming.TicksPerSecond;

    /// <summary>
    /// How much hunger one period adds, and the level of it a creature will not
    /// tolerate: the record's <c>HungerFill</c> scaled by creature stat row 9.
    ///
    /// <para><b>Row 9 is 100% at every level in the shipped table</b>, so this
    /// is <c>HungerFill</c> exactly and the scaling is invisible. It is applied
    /// anyway because the original applies it: the observation that would show
    /// a difference is a mod that edits row 9, and there is no reason for this
    /// project to be the thing that breaks it.</para>
    /// </summary>
    public static int HungerFill(GameDatabase database, CreatureDef creature, int level) =>
        CreatureLevelRules.Scale(
            creature.HungerFill,
            CreatureLevelRules.Percent(database, CreatureStatId.HungerFill, level));

    /// <summary>
    /// Seconds between one helping of hunger and the next — the record's
    /// <c>HungerRate</c>, which is 180 for a Goblin and 220 for a Bile Demon.
    ///
    /// <para>The original converts this to ticks as it loads the record
    /// (<c>value × ticks-per-second &gt;&gt; 12</c>, the shift undoing the
    /// field's fixed point), which is the same arithmetic
    /// <see cref="SimulationTiming.TicksFromSeconds"/> does here — and is
    /// incidental corroboration that variable 19 is ticks per game second,
    /// since <c>TimeAwake</c> and <c>TimeSleep</c> get the same multiply
    /// without the shift.</para>
    /// </summary>
    public static int HungerRateSeconds(CreatureDef creature) =>
        creature.HungerRate.Raw / Fp32.One;

    /// <summary>
    /// Whether this creature will <b>set out</b> to eat:
    /// <c>HungerNeed &gt;= HungerFill</c>, which is the original's own entry
    /// test in the routine that drives needs.
    /// </summary>
    ///
    /// <para><b>Not the same question as <see cref="StillOwesFood"/></b>, and
    /// the pair of them is a hysteresis rather than an accident. Going to eat
    /// costs a walk, so it takes a full helping to start one; once a creature is
    /// standing over the birds it eats until it owes nothing. That is what makes
    /// <c>HungerFill</c> mean what the data has always been read to mean —
    /// <i>chickens needed to be full</i> — when each bird pays off only
    /// one.</para>
    public static bool IsHungry(
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        int fill = HungerFill(database, creature, state.Level);
        return fill > 0 && state.HungerNeed >= fill;
    }

    /// <summary>
    /// Whether this creature has anything left to pay off at all:
    /// <c>HungerNeed &gt; 0</c>.
    ///
    /// <para>The original computes exactly this the moment a chicken goes down —
    /// it stores <c>HungerNeed != 0</c> in a byte on the state object — and the
    /// eating states branch on that byte to go round again. So a Bile Demon,
    /// which is given five helpings at a time and pays off one a bird, eats
    /// five birds in a row rather than one and a very long wait. <b>Which state
    /// re-enters which is not fully established</b> (Q174); that the loop is
    /// keyed on this number is.</para>
    /// </summary>
    public static bool StillOwesFood(CreatureState state) => state.HungerNeed > 0;

    /// <summary>
    /// Whether this object is food by the <b>flag</b> — the test the original's
    /// <i>prison</i> search uses, and the one that lets a Torture Pendulum
    /// count. Not used by the hatchery path; see the type summary.
    /// </summary>
    public static bool IsFood(GameDatabase database, ObjectState thing) =>
        database.ObjectById(thing.ObjectTypeId)?.IsFood == true;

    /// <summary>
    /// Whether this object is a chicken a creature could walk up to and eat: the
    /// hatchery search's own test (object id 9) plus "not in somebody's hands",
    /// which the original applies at the moment of eating rather than of
    /// choosing.
    /// </summary>
    public static bool IsEdibleChicken(ObjectState thing) =>
        thing.ObjectTypeId == HatcheryRules.ChickenObjectId
        && thing.CarriedByCreatureId < 0
        && !thing.InHand;

    /// <summary>
    /// The hatchery this creature is standing in, or null. The original's test
    /// is the tile underfoot: its room must be type 4 <b>and</b> owned by the
    /// creature's own keeper — an allied hatchery does not feed you, because the
    /// "allow allied" argument is passed as false.
    /// </summary>
    public static RoomInstance? HatcheryUnder(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        RoomInstance? room = RoomIndex.RoomAt(world, database, here);
        return IsHatcheryOf(room, creature.OwnerPlayerId) ? room : null;
    }

    /// <summary>A hatchery of this keeper's, and not of an ally's.</summary>
    public static bool IsHatcheryOf(RoomInstance? room, int playerId) =>
        room is not null
        && room.Definition.TypeId == RoomTypeId.Hatchery
        && room.OwnerPlayerId == playerId;

    /// <summary>
    /// The nearest chicken in this room by <b>straight-line squared distance</b>
    /// — the original's ranking exactly, and deliberately not a path length.
    ///
    /// <para>Ties go to the lowest object id, which is spawn order. The original
    /// takes the first it meets at an equal distance, walking the room's tile
    /// chain in its own order; ours walks <see cref="RoomInstance.TileIndices"/>
    /// and settles ties by id so that the answer is reproducible whatever order
    /// the room was built in (ADR-0001).</para>
    /// </summary>
    public static ObjectState? NearestChickenIn(
        WorldState world, RoomInstance room, TilePos from)
    {
        ObjectState? best = null;
        long bestDistance = long.MaxValue;

        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            ObjectState thing = objects[i];
            if (!IsEdibleChicken(thing)) continue;
            if (!Contains(room, thing.TileIndex)) continue;

            int x = thing.TileIndex % world.Width;
            int y = thing.TileIndex / world.Width;
            long dx = x - from.X;
            long dy = y - from.Y;
            long distance = (dx * dx) + (dy * dy);

            if (best is not null
                && (distance > bestDistance
                    || (distance == bestDistance && thing.Id >= best.Id)))
            {
                continue;
            }

            best = thing;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// The nearest <b>food-flagged</b> object in this room — the <i>prison</i>
    /// search, and a different question from <see cref="NearestChickenIn"/>.
    ///
    /// <para>DK2's state 117 walks the prison's own tile chain, and on each
    /// tile walks that tile's object list, keeping every object whose kind is
    /// <c>2</c> and whose definition carries the food flag
    /// (<see cref="ObjectFlags.TypeFood"/>). It ranks them by <b>straight-line
    /// squared distance</b> in its fixed point and takes the smallest, and the
    /// comparison is strict — so at equal distance the <i>first</i> it meets
    /// wins, walking the room in the room's own order.</para>
    ///
    /// <para><b>The flag, not the id.</b> Two of the 145 OBJECTS records carry
    /// it, the Chicken and the Torture Pendulum, so a pendulum standing in a
    /// prison is food to a prisoner where one standing in a hatchery is not.
    /// That asymmetry is the original's and is reproduced rather than
    /// tidied.</para>
    ///
    /// <para><b>Ties go to the lowest object id here</b>, which is spawn order,
    /// for the reason <see cref="NearestChickenIn"/> gives: the original's
    /// answer depends on the order its room happens to hold its tiles in, and
    /// ours may not (ADR-0001).</para>
    ///
    /// <para><b>A carried bird is not a candidate</b>, which is ours in form and
    /// the original's in effect: DK2 takes an object out of its tile's list
    /// while it is carried, so it is not on any tile to be found. Ours keeps a
    /// carried object's tile following its carrier, so the test has to be
    /// written down.</para>
    /// </summary>
    public static ObjectState? NearestFoodIn(
        WorldState world, GameDatabase database, RoomInstance room, TilePos from)
    {
        ObjectState? best = null;
        long bestDistance = long.MaxValue;

        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            ObjectState thing = objects[i];
            if (thing.InHand) continue;
            if (thing.CarriedByCreatureId >= 0) continue;
            if (!IsFood(database, thing)) continue;
            if (!Contains(room, thing.TileIndex)) continue;

            long distance = DistanceSquared(world, from, thing.TileIndex);
            if (best is not null
                && (distance > bestDistance
                    || (distance == bestDistance && thing.Id >= best.Id)))
            {
                continue;
            }

            best = thing;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// Whether this room holds <b>any</b> object at all — the original's own
    /// gate on setting out for a hatchery, and a weaker test than "has a bird".
    ///
    /// <para><c>FUN_004ded00</c> walks the keeper's hatcheries and keeps only
    /// those whose <c>ObjectList</c> head is non-zero. <b>The room dump names
    /// that field</b>, and it is every object in the room rather than the food
    /// in it — so a <b>Chicken Coop</b> counts. Any hatchery of six tiles or
    /// more earns a coop and therefore passes this test for ever, which makes
    /// the gate nearly always true and exactly one thing false: a hatchery too
    /// small to earn a coop can never lay, so its object list stays empty and
    /// nobody walks to it. That is the case the test is for, and reproducing it
    /// costs one predicate.</para>
    ///
    /// <para>When it excludes every hatchery the original says so in its own
    /// words — three separate interface messages for "no hatchery", "hatchery
    /// unreachable" and "hatchery empty".</para>
    /// </summary>
    public static bool HoldsAnyObject(WorldState world, RoomInstance room)
    {
        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            if (Contains(room, objects[i].TileIndex)) return true;
        }

        return false;
    }

    /// <summary>Whether a tile index belongs to this room.</summary>
    public static bool Contains(RoomInstance room, int tileIndex)
    {
        IReadOnlyList<int> tiles = room.TileIndices;
        for (int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i] == tileIndex) return true;
        }

        return false;
    }

    /// <summary>Squared straight-line distance in whole tiles.</summary>
    public static long DistanceSquared(WorldState world, TilePos from, int tileIndex)
    {
        long dx = (tileIndex % world.Width) - from.X;
        long dy = (tileIndex / world.Width) - from.Y;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// Eat one chicken in a hatchery: health up by
    /// <c>min(HpFromChicken, missing)</c>, <c>HungerNeed</c> down by <b>one</b>,
    /// and the hunger clock restarted.
    ///
    /// <para>Returns the health actually gained, which is <b>0 for the
    /// Firefly</b> — its <c>HpFromChicken</c> is zero and it still eats two, so
    /// a chicken is not a healing item and a test says so.</para>
    ///
    /// <para>The clock restart is the original's: eating stamps
    /// <c>LastAteTime</c>, exactly as the periodic increment does, so the next
    /// helping of hunger is a full <c>HungerRate</c> after the last bird rather
    /// than after the last <i>full</i> meal.</para>
    /// </summary>
    public static int Eat(GameDatabase database, CreatureDef creature, CreatureState state) =>
        Swallow(database, creature, state, 1);

    /// <summary>
    /// Eat one chicken in a prison, which pays off <b>the whole of
    /// <c>HungerFill</c></b> rather than one.
    ///
    /// <para>Not a tidier version of <see cref="Eat"/> — the original's prison
    /// state passes the level-scaled <c>HungerFill</c> where its hatchery state
    /// passes a literal 1, and the consequence is that one bird dropped into a
    /// cell fills whatever is in there. Nothing calls this yet - and the reason has
    /// changed, because the old one has expired. It used to be that a prisoner
    /// <i>could not move</i>; since ADR-0073 it does. What is missing now is
    /// only the drop: a keeper putting a bird into a cell, and the prisoners
    /// racing it. That is the remaining half of Q172 and nothing blocks
    /// it.</para>
    /// </summary>
    public static int EatInPrison(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        Swallow(database, creature, state, HungerFill(database, creature, state.Level));

    /// <summary>
    /// The hunger clock: one helping every <c>HungerRate</c> seconds, added
    /// rather than assigned, with the clock restarted whether or not anything
    /// was eaten. Returns true when a helping landed.
    /// </summary>
    public static bool Tick(GameDatabase database, CreatureDef creature, CreatureState state)
    {
        int fill = HungerFill(database, creature, state.Level);
        if (fill <= 0) return false;

        int rate = HungerRateSeconds(creature);
        if (rate <= 0 || rate >= CreatureHealthRules.Never) return false;
        if (state.HungerTicks < SimulationTiming.TicksFromSeconds(rate)) return false;

        state.HungerTicks = 0;
        state.HungerNeed += fill;
        return true;
    }

    private static int Swallow(
        GameDatabase database, CreatureDef creature, CreatureState state, int pays)
    {
        state.HungerNeed = Math.Max(0, state.HungerNeed - pays);
        state.HungerTicks = 0;
        state.TargetObjectId = NoChicken;

        return CreatureHealthRules.Apply(database, creature, state, creature.HpFromChicken);
    }
}
