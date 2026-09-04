// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Health and hunger: the first two things a creature has that are not about
/// where it is standing.
///
/// <para><b>Why health first.</b> Nearly every unbuilt mechanic reads it. A lair
/// heals, a prison drains, torture damages, praying heals, fire burns, combat
/// subtracts, a chicken restores, and <c>CreatureSleepsWhenBelowPercentHealth</c>
/// makes sleep itself a function of it. None of those can be written against a
/// creature that has no health, and all of them are one line once it does.</para>
///
/// <para><b>Every number here is the shipped data's.</b> Maximum health is the
/// creature record's <c>Hp</c>; regeneration on a keeper's own land is the
/// record's <c>OwnLandHealthIncreasePerSecond</c>; healing in a lair is
/// <c>ModifyHealthOfCreatureInLairPerSecond</c>; hunger is the record's
/// <c>HungerRate</c> and <c>HungerFill</c>. Nothing is invented, and the two
/// readings that are ours are called out at the methods that make them.</para>
///
/// <para><b>The zeroes are the finding, again.</b> Exactly <b>two</b> of the 46
/// creatures carry a non-zero <c>OwnLandHealthIncreasePerSecond</c> — the Imp at
/// 67 a second and King Reginald at 400 — so standing-on-your-own-land
/// regeneration is real, data-driven, and a no-op for the other 44. This is the
/// same shape as mana, where three terrain types out of 41 generate any
/// (ADR-0017), and it is the reason to read the whole column before believing a
/// rule is general.</para>
///
/// <para><b>What is deliberately absent.</b> Hunger can arrive and cannot be
/// satisfied, because eating needs chickens, chickens are objects, and the
/// OBJECTS record is unparsed (M6). That gap costs no health on purpose: in DK2
/// the price of going unfed is <i>anger</i> — the creature record's own
/// <c>angerNoFood</c>, 100 for a Goblin — and <b>there is no variable anywhere
/// in the balance table that turns hunger into damage</b>. Checked: 21 health
/// variables and nine anger ones, and no starvation among them.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-needs.md")]
public static class CreatureHealthRules
{
    /// <summary>
    /// The 32767 that means "never" in the creature record.
    ///
    /// <para><b>A general sentinel, and per need rather than per creature</b> —
    /// which had been an open question in <c>kwd-creatures.md</c> since
    /// creatures were parsed ("is it an Imp quirk?"). <b>Eight</b> creatures
    /// never get hungry (Imp, Vampire, Skeleton, Horny, Stone Knight, and the
    /// unique Firefly, Skeleton and Vampire) and <b>six</b> never sleep (Imp,
    /// Skeleton, Dwarf, Horny, Stone Knight, Unique Skeleton).</para>
    ///
    /// <para><b>Neither set contains the other</b>, which is what rules out a
    /// single "undead" flag: the Vampire sleeps but never eats, and <b>the Dwarf
    /// eats but never sleeps</b>. Both counts were guessed wrong before the
    /// install was asked — the first guess missed the Stone Knight and the
    /// uniques, the second missed the Dwarf — so
    /// <c>CreatureNeedsOnRealDataTests</c> asserts both sets by name.</para>
    ///
    /// <para>It is <c>short.MaxValue</c> in a field wide enough to hold more, so
    /// it is a saturated 16-bit value someone typed rather than a magic number
    /// with meaning — but it means never either way, because 32767 seconds is
    /// nine hours of play.</para>
    /// </summary>
    public const int Never = short.MaxValue;

    /// <summary>Maximum health, which is the record's <c>Hp</c> until levels exist.</summary>
    public static int MaxHealth(CreatureDef creature) => creature.Hp;

    /// <summary>
    /// Applies a health change and clamps it. Positive heals, negative hurts.
    /// Returns the amount that actually landed, which is less than asked for at
    /// either end.
    /// </summary>
    public static int Apply(CreatureDef creature, CreatureState state, int delta)
    {
        int before = state.Health;
        int max = MaxHealth(creature);
        state.Health = Math.Clamp(before + delta, 0, max);
        return state.Health - before;
    }

    /// <summary>
    /// Whether this creature is at or below <c>CreatureCriticalHealthPercentageOfMax</c>
    /// (20%) of its maximum — DK2's own threshold for "in trouble", and the one
    /// a fleeing or recuperating creature will be keyed off.
    ///
    /// <para>Nothing reads it yet. It is here because the variable exists and
    /// the percentage is not obvious from anywhere else.</para>
    /// </summary>
    public static bool IsCriticallyHurt(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        state.Health * 100
            <= MaxHealth(creature) * database[VariableTypeId.CreatureCriticalHealthPercentageOfMax];

    /// <summary>
    /// Whether this creature is hurt enough that DK2 sends it to bed —
    /// <c>CreatureSleepsWhenBelowPercentHealth</c>, which is 50.
    ///
    /// <para>Also unread today, and the reason is worth stating: sleep needs a
    /// lair a creature <i>owns</i>, and lair ownership is the next mechanic
    /// rather than this one. When it lands, this is the condition that makes a
    /// wounded creature go and lie down without being tired.</para>
    /// </summary>
    public static bool ShouldSleepFromWounds(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        state.Health * 100
            < MaxHealth(creature) * database[VariableTypeId.CreatureSleepsWhenBelowPercentHealth];

    /// <summary>
    /// Health per second for a creature standing on land its own keeper owns —
    /// the record's <c>OwnLandHealthIncreasePerSecond</c>, or 0 off its own
    /// land.
    ///
    /// <para><b>The tile test is ours</b> (Q100). OpenKeeper compares the tile's
    /// owner against the creature's owner, which is the obvious reading and the
    /// one taken here. What neither the files nor OpenKeeper settle is whether
    /// DK2 also requires the creature to be idle, or excludes it while fighting
    /// or working — a healing rate that applies mid-combat is a different game
    /// from one that does not.</para>
    ///
    /// <para>Returns 0 at full health for nothing but tidiness; the caller
    /// clamps anyway.</para>
    /// </summary>
    public static int OwnLandRegenPerSecond(
        WorldState world, CreatureDef creature, CreatureState state)
    {
        int rate = creature.OwnLandHealthIncreasePerSecond;
        if (rate == 0) return 0;

        // Standing on your own land does not pick you up off the floor. The
        // owner's answer to Q99 gives exactly one route back from a collapse —
        // an imp carrying the creature to its bed — so passive regeneration
        // must not be a second one. Without this an Imp, which regenerates 67 a
        // second at home, would be unkillable on its own territory: see
        // CreatureDeathRules.IsDown. Lair healing is deliberately *not* gated
        // this way, because healing a creature at zero health in its own bed is
        // precisely what a rescue is.
        if (CreatureDeathRules.IsDown(state)) return 0;

        TilePos at = state.Tile;
        if (!world.Contains(at)) return 0;

        return world.Tile(at.X, at.Y).OwnerPlayerId == state.OwnerPlayerId ? rate : 0;
    }

    /// <summary>
    /// Health per second from the terrain a creature is standing on —
    /// <c>TerrainDef.Damage</c>, which is a <b>signed delta and not damage</b>.
    ///
    /// <para><b>Three of 41 terrains carry one</b>, and the third is why the
    /// field's name is wrong: <b>Water +100</b>, <b>Lava +100</b>, and
    /// <b>Hero Lair −67</b> — the hero side's lair, healing whatever stands on
    /// it. Returned negated here so the sign convention matches everything else
    /// in this file: positive heals, negative hurts.</para>
    ///
    /// <para><b>CONFIRMED that hazards are crossed at all</b> (owner, 2026-09-04):
    /// a gem tile on an island cut off by water, no bridge, and imps waded over,
    /// mined it and carried the gold home. The original states the rule in its
    /// own tooltip — <i>"Water. Impassable to some, others are merely hindered
    /// by it."</i> — and the DK2 wiki corroborates the cost, warning that
    /// patrols routed through water "may get damaged, resurrect, and even
    /// die".</para>
    ///
    /// <para><b>The cadence is ours</b> (Q117). The field is applied once a
    /// game second, which is what every other rate in the data does and what
    /// this system already runs on — but nothing states it, and at 100 a second
    /// a Goblin drowns in fifteen seconds while an Imp lasts five. If DK2
    /// charges per tick instead, hazards are twenty times deadlier than this.
    /// The owner is the check: does an imp visibly lose health wading across?</para>
    ///
    /// <para>Applied to a creature that is <see cref="CreatureDeathRules.IsDown"/>
    /// as well as a standing one, deliberately: a creature that collapses in
    /// water is in water, and lying in it is no safer than walking through
    /// it.</para>
    ///
    /// <para><b>Though it has no observable effect there</b>, and the first
    /// version of this comment claimed otherwise — that terrain was the one
    /// thing which could still kill something inside its rescue window. It
    /// cannot: a creature at zero health cannot be taken lower, so the clamp
    /// makes every second after the collapse a no-op. A test written to
    /// demonstrate the claim refuted it. The rule is kept because "terrain acts
    /// on whatever is standing in it" is the true one and starts mattering the
    /// moment anything can heal a down creature in place; what was wrong was
    /// the boast, not the behaviour.</para>
    /// </summary>
    public static int TerrainHealthPerSecond(WorldState world, GameDatabase database, CreatureState state)
    {
        TilePos at = state.Tile;
        if (!world.Contains(at)) return 0;

        return -database.TerrainById(world.Tile(at.X, at.Y).TerrainId).Damage;
    }

    /// <summary>
    /// Health per second for a creature <b>asleep in its own bed</b> —
    /// <c>ModifyHealthOfCreatureInLairPerSecond</c>, which is <b>100</b>, and is
    /// the main way anything heals in DK2.
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-04): <i>"when a creature is
    /// injured it heals while it sleeps in its own bed."</i> Both halves are
    /// load-bearing and both were wrong in the first implementation, which
    /// healed anything standing on any of its keeper's lair tiles:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Asleep, not present.</b> A wounded creature standing awake in
    /// its own lair heals nothing.</item>
    /// <item><b>Its own bed, not its keeper's lair.</b> One bed per tile, one
    /// tile per creature — see <see cref="LairRules"/>. A creature with no bed
    /// never heals here at all, whatever it is standing on.</item>
    /// </list>
    ///
    /// <para>Which makes the healing rate a <i>consequence</i> of the sleep
    /// cycle rather than of position: a Goblin sleeps for its record's
    /// <c>TimeSleep</c> of 9 seconds, so one sleep is worth 900 health of its
    /// 1500 (Q103).</para>
    /// </summary>
    public static int LairHealPerSecond(
        WorldState world, GameDatabase database, CreatureState state) =>
        LairRules.AsleepInOwnBed(world, state)
            ? database[VariableTypeId.ModifyHealthOfCreatureInLairPerSecond]
            : 0;

    /// <summary>
    /// Whether this creature ever gets hungry. False for the Imp, Vampire,
    /// Skeleton and Horny.
    ///
    /// <para>Tested on <c>HungerFill</c> rather than on the 32767 sentinel,
    /// because a creature needing zero chickens to fill up can never be hungry
    /// in any useful sense and the two encodings agree on all 46 creatures — a
    /// fact <c>CreatureNeedsOnRealDataTests</c> checks rather than assumes.
    /// OpenKeeper gates the same way, and only creates its hunger component when
    /// <c>hungerFill > 0</c>.</para>
    /// </summary>
    public static bool CanGetHungry(CreatureDef creature) => creature.HungerFill > 0;

    /// <summary>
    /// Whether enough time has passed since this creature last ate.
    ///
    /// <para><c>HungerRate</c> is in <b>seconds</b> — 180 for a Goblin, 220 for
    /// a Bile Demon — and is compared against time since the last full meal, not
    /// since the last chicken. A creature part-way through its chickens is
    /// already hungry and stays so; the clock only resets when it is
    /// <i>full</i>.</para>
    /// </summary>
    public static bool HungerHasArrived(CreatureDef creature, CreatureState state)
    {
        if (!CanGetHungry(creature)) return false;
        if (state.ChickensNeeded > 0) return true;

        int rate = creature.HungerRate.Raw / Fp32.One;
        return rate > 0 && rate < Never
            && state.HungerTicks >= SimulationTiming.TicksFromSeconds(rate);
    }

    /// <summary>
    /// Marks a creature hungry: it now needs <c>HungerFill</c> chickens. Returns
    /// false if it was already hungry or never gets hungry.
    /// </summary>
    public static bool MakeHungry(CreatureDef creature, CreatureState state)
    {
        if (!CanGetHungry(creature) || state.ChickensNeeded > 0) return false;

        state.ChickensNeeded = creature.HungerFill;
        return true;
    }

    /// <summary>
    /// One chicken eaten: it counts down the hunger and restores
    /// <c>HpFromChicken</c>. When the last one goes down the creature is full
    /// and its clock restarts.
    ///
    /// <para><b>Nothing calls this yet</b> — there are no chickens to eat (M6) —
    /// and it exists for the same reason <see cref="WorldState.RemoveCreature"/>
    /// does: the counting rule is the part worth getting right and it can be
    /// tested now, whereas waiting for the hatchery would mean writing it
    /// blind. Note <c>HpFromChicken</c> is <b>0 for the Firefly</b> even though
    /// it eats two, so a chicken is not guaranteed to heal.</para>
    /// </summary>
    public static void Eat(CreatureDef creature, CreatureState state)
    {
        if (state.ChickensNeeded <= 0) return;

        state.ChickensNeeded--;
        if (state.ChickensNeeded == 0) state.HungerTicks = 0;

        Apply(creature, state, creature.HpFromChicken);
    }
}
