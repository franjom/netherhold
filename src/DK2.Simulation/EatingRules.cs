// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Hunger and the chicken that answers it.</summary>
/// <remarks><para>Read from the original's own <c>HungerState.cpp</c>, every number with
/// it (docs/research/eating.md). Hunger is an accumulator DK2's dump calls
/// <c>HungerNeed</c>: the clock <b>adds</b> <c>HungerFill</c> every
/// <c>HungerRate</c> seconds and caps nothing, hungry means
/// <c>HungerNeed &gt;= HungerFill</c>, and one chicken pays off one in a hatchery
/// (<see cref="Eat"/>) against all of it in a prison (<see cref="EatInPrison"/>).</para>
/// <para>Trap: the hatchery search matches the object <i>id</i> 9 where the
/// prison search matches the <see cref="ObjectFlags.TypeFood"/> <i>flag</i>, and
/// two records carry it — the Chicken and the Torture Pendulum.</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/eating.md")]
public static class EatingRules
{
    /// <summary>No chicken chosen — <see cref="CreatureState.TargetObjectId"/>'s idle value.</summary>
    public const int NoChicken = -1;

    /// <summary>DK2 state 91 (<c>0x5B</c>): find a hatchery and walk there in state 7,
    /// resuming in <see cref="InHatcheryState"/> (<c>eating.md</c> §2, <c>travel-states.md</c>
    /// §8). Group 7, category Eating, like the rest of the chain.</summary>
    public const int FindHatcheryState = 91;

    /// <summary>DK2 state 92 (<c>0x5C</c>): standing in the hatchery, choosing the nearest
    /// chicken and walking to it in state 6, resuming in <see cref="EatState"/>.</summary>
    public const int InHatcheryState = 92;

    /// <summary>DK2 state 93 (<c>0x5D</c>): eating the chosen chicken.</summary>
    public const int EatState = 93;

    /// <summary>DK2 state 94 (<c>0x5E</c>): after the meal — back to
    /// <see cref="InHatcheryState"/> while a helping is owed, else idle.</summary>
    public const int AfterMealState = 94;

    /// <summary>How near a creature must be to eat: one tile, squared.
    /// <para>The original's test is <c>dx² + dy² &lt; 0x1001</c> on fixed-point
    /// positions with 4096 to the tile, so a bird exactly one tile away is in
    /// reach and one further is not. Ours is in whole tiles, and the two agree on
    /// every position ours can produce.</para></summary>
    public const int ReachTilesSquared = 1;

    /// <summary>How long between attempts to go and eat: <b>one game second</b>.
    /// <para>The original refuses another attempt until four game ticks have
    /// passed, which is one game second on every shipped level (Q1/Q132) — and
    /// the prison's copy of the test reads
    /// <c>TimeBetweenReattemptingHungerSeconds</c>, also 4, so the two paths
    /// retry at the same rate whichever way the unit is read.</para></summary>
    public static int RetryTicks => SimulationTiming.TicksPerSecond;

    /// <summary>How much hunger one period adds, and the level of it a creature will not
    /// tolerate: the record's <c>HungerFill</c> scaled by creature stat row 9.
    /// <para>Row 9 is 100% at every level in the shipped table, so this is
    /// <c>HungerFill</c> exactly and the scaling is invisible. Applied anyway
    /// because the original applies it, and a mod that edits row 9 would show
    /// the difference.</para></summary>
    public static int HungerFill(GameDatabase database, CreatureDef creature, int level) =>
        CreatureLevelRules.Scale(
            creature.HungerFill,
            CreatureLevelRules.Percent(database, CreatureStatId.HungerFill, level));

    /// <summary>Seconds between one helping of hunger and the next — the record's
    /// <c>HungerRate</c>, which is 180 for a Goblin and 220 for a Bile Demon.
    /// <para>The original converts it to ticks as it loads the record
    /// (<c>value × ticks-per-second &gt;&gt; 12</c>, the shift undoing the
    /// field's fixed point), the same arithmetic
    /// <see cref="SimulationTiming.TicksFromSeconds"/> does — and incidental
    /// corroboration that variable 19 is ticks per game second, since
    /// <c>TimeAwake</c> and <c>TimeSleep</c> get the same multiply without the
    /// shift.</para></summary>
    public static int HungerRateSeconds(CreatureDef creature) =>
        creature.HungerRate.Raw / Fp32.One;

    /// <summary>Whether this creature will <b>set out</b> to eat:
    /// <c>HungerNeed &gt;= HungerFill</c>, which is the original's own entry
    /// test in the routine that drives needs.
    /// <para>Not the same question as <see cref="StillOwesFood"/>: the pair is a
    /// hysteresis. Going to eat costs a walk, so it takes a full helping to
    /// start one, and once a creature is standing over the birds it eats until it
    /// owes nothing — which is how <c>HungerFill</c> means "chickens needed to be
    /// full" while each bird pays off one.</para></summary>
    public static bool IsHungry(
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        int fill = HungerFill(database, creature, state.Level);
        return fill > 0 && state.HungerNeed >= fill;
    }

    /// <summary>Whether this creature has anything left to pay off at all:
    /// <c>HungerNeed &gt; 0</c>.
    /// <para>The original stores <c>HungerNeed != 0</c> in a byte the moment a
    /// chicken goes down, and its eating states branch on that byte to go round
    /// again — so a Bile Demon eats five birds in a row rather than one and a
    /// long wait. UNKNOWN which state re-enters which (Q174); that the loop is
    /// keyed on this number is not.</para></summary>
    public static bool StillOwesFood(CreatureState state) => state.HungerNeed > 0;

    /// <summary>Whether this object is food by the <b>flag</b> — the test the original's
    /// <i>prison</i> search uses, and the one that lets a Torture Pendulum
    /// count. Not used by the hatchery path; see the type summary.</summary>
    public static bool IsFood(GameDatabase database, ObjectState thing) =>
        database.ObjectById(thing.ObjectTypeId)?.IsFood == true;

    /// <summary>Whether this object is a chicken a creature could walk up to and eat: the
    /// hatchery search's own test (object id 9) plus "not in somebody's hands",
    /// which the original applies at the moment of eating rather than of
    /// choosing.</summary>
    public static bool IsEdibleChicken(ObjectState thing) =>
        thing.ObjectTypeId == HatcheryRules.ChickenObjectId
        && thing.CarriedByCreatureId < 0
        && !thing.InHand;

    /// <summary>The hatchery this creature is standing in, or null. The original's test
    /// is the tile underfoot: its room must be type 4 <b>and</b> owned by the
    /// creature's own keeper — an allied hatchery does not feed you, because the
    /// "allow allied" argument is passed as false.</summary>
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

    /// <summary>The nearest chicken in this room by <b>straight-line squared distance</b>
    /// — the original's ranking exactly, and deliberately not a path length.
    /// <para>Ties go to the lowest object id, which is spawn order. The original
    /// takes the first it meets at equal distance, in its room's own tile order;
    /// ours settles by id so the answer is reproducible whatever order the room
    /// was built in (ADR-0001).</para></summary>
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

    /// <summary>The nearest <b>food-flagged</b> object in this room — the <i>prison</i>
    /// search, and a different question from <see cref="NearestChickenIn"/>.
    /// <para>DK2's state 117 walks the prison's own tile chain and each tile's
    /// object list, keeping every object of kind <c>2</c> whose definition
    /// carries <see cref="ObjectFlags.TypeFood"/> — two of the 145 records, the
    /// Chicken and the Torture Pendulum — ranked by <b>straight-line squared
    /// distance</b> with a strict comparison. Ties go to the lowest object id
    /// (ADR-0001). A carried bird is not a candidate: ours in form, the
    /// original's in effect, since DK2 takes a carried object out of its tile's
    /// list where ours keeps its tile following the carrier.</para></summary>
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

    /// <summary>Whether this room holds <b>any</b> object at all — the original's own
    /// gate on setting out for a hatchery, and a weaker test than "has a bird".
    /// <para><c>FUN_004ded00</c> keeps only hatcheries whose <c>ObjectList</c>
    /// head is non-zero — every object in the room, not the food in it, so a
    /// <b>Chicken Coop</b> counts. Any hatchery of six tiles or more earns a
    /// coop and passes for ever, which leaves exactly one case false: a hatchery
    /// too small to earn a coop can never lay, so nobody walks to it.</para></summary>
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

    /// <summary>Eat one chicken in a hatchery: health up by
    /// <c>min(HpFromChicken, missing)</c>, <c>HungerNeed</c> down by <b>one</b>,
    /// and the hunger clock restarted. Returns the health actually gained, which
    /// is 0 for the Firefly — its <c>HpFromChicken</c> is zero and it still eats
    /// two, so a chicken is not a healing item.
    /// <para>The clock restart is the original's: eating stamps
    /// <c>LastAteTime</c> as the periodic increment does, so the next helping is
    /// a full <c>HungerRate</c> after the last bird rather than after the last
    /// <i>full</i> meal.</para></summary>
    public static int Eat(GameDatabase database, CreatureDef creature, CreatureState state) =>
        Swallow(database, creature, state, 1);

    /// <summary>Eat one chicken in a prison, which pays off <b>the whole of
    /// <c>HungerFill</c></b> rather than one.
    /// <para>Not a tidier version of <see cref="Eat"/>: the original's prison
    /// state passes the level-scaled <c>HungerFill</c> where its hatchery state
    /// passes a literal 1, so one bird dropped into a cell fills whatever is in
    /// there. Called by <see cref="Systems.PrisonerEatSystem"/>.</para></summary>
    public static int EatInPrison(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        Swallow(database, creature, state, HungerFill(database, creature, state.Level));

    /// <summary>The hunger clock: one helping every <c>HungerRate</c> seconds, added
    /// rather than assigned, with the clock restarted whether or not anything
    /// was eaten. Returns true when a helping landed.</summary>
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

    /// <summary>Eat a chicken dropped onto this creature: the same routine as <see cref="Eat"/>
    /// (<c>0x00463A10</c>, reached from the drop's landing hook), which pays off one helping and
    /// heals only while food is still owed. Ours keeps the creature's own target: the bird came to
    /// it, and a chase for another is not this bird's business. Returns the health gained.</summary>
    public static int EatDropped(GameDatabase database, CreatureDef creature, CreatureState state) =>
        StillOwesFood(state) ? Swallow(database, creature, state, 1, keepTarget: true) : 0;

    private static int Swallow(
        GameDatabase database, CreatureDef creature, CreatureState state, int pays,
        bool keepTarget = false)
    {
        state.HungerNeed = Math.Max(0, state.HungerNeed - pays);
        state.HungerTicks = 0;
        if (!keepTarget) state.TargetObjectId = NoChicken;

        return CreatureHealthRules.Apply(database, creature, state, creature.HpFromChicken);
    }
}
