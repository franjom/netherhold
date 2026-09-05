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
/// satisfied: eating needs a chicken, the Chicken is object 9, and nothing lays
/// one — a Hatchery is a room this simulation does not have. That gap costs no
/// health on purpose: in DK2
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

    /// <summary>
    /// Maximum health: the record's <c>Hp</c> <b>scaled by the creature's
    /// level</b>.
    ///
    /// <para>This method used to return <c>creature.Hp</c> flat, with a comment
    /// saying "until levels exist". Levels exist now — as a stat multiplier, at
    /// least (<see cref="CreatureLevelRules"/>) — so it takes the state and the
    /// database. The old signature was deliberately removed rather than kept as
    /// an overload: a call that forgot the level would silently cap a level-10
    /// Goblin at 1500 health instead of 9000, and that is precisely the class of
    /// bug this project keeps finding in its own six-month-old assumptions.</para>
    /// </summary>
    public static int MaxHealth(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        CreatureLevelRules.MaxHealth(database, creature, state.Level);

    /// <summary>
    /// Applies a health change and clamps it. Positive heals, negative hurts.
    /// Returns the amount that actually landed, which is less than asked for at
    /// either end.
    /// </summary>
    public static int Apply(
        GameDatabase database, CreatureDef creature, CreatureState state, int delta)
    {
        int before = state.Health;
        int max = MaxHealth(database, creature, state);
        state.Health = Math.Clamp(before + delta, 0, max);
        return state.Health - before;
    }

    /// <summary>
    /// Whether this creature is at or below <c>CreatureCriticalHealthPercentageOfMax</c>
    /// (20%) of its maximum — DK2's own threshold for "in trouble", and the one
    /// a fleeing or recuperating creature will be keyed off.
    ///
    /// <para><see cref="PrisonRules.Imprison"/> reads the number this compares
    /// against (<see cref="CriticalHealth"/>); nothing reads the comparison
    /// itself yet. It is here because the variable exists and the percentage is
    /// not obvious from anywhere else.</para>
    /// </summary>
    public static bool IsCriticallyHurt(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        state.Health * 100
            <= MaxHealth(database, creature, state)
               * database[VariableTypeId.CreatureCriticalHealthPercentageOfMax];

    /// <summary>
    /// <c>CreatureCriticalHealthPercentageOfMax</c> of this creature's maximum,
    /// as health — <b>two petals of the flower above its head</b>.
    ///
    /// <para><b>What a petal is worth was settled by the owner</b> (2026-09-05,
    /// Q167): a creature dragged into a prison arrives with <i>"two petals"</i>,
    /// and 20 is the one shipped percentage that reads as two of ten. See
    /// <see cref="PrisonRules.Imprison"/>, which is the only caller.</para>
    ///
    /// <para>At least one, because a creature brought round at nothing is a
    /// creature that dies on the tick it wakes.</para>
    /// </summary>
    public static int CriticalHealth(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        Math.Max(1,
            MaxHealth(database, creature, state)
            * database[VariableTypeId.CreatureCriticalHealthPercentageOfMax] / 100);

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
            < MaxHealth(database, creature, state)
              * database[VariableTypeId.CreatureSleepsWhenBelowPercentHealth];

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
    /// <c>TerrainDef.Damage</c>, which is a <b>signed delta and not damage</b>,
    /// and which a creature that can cross the hazard does not pay at all.
    ///
    /// <para><b>Three of 41 terrains carry one</b>, and the third is why the
    /// field's name is wrong: <b>Water +100</b>, <b>Lava +100</b>, and
    /// <b>Hero Lair −67</b> — the hero side's lair, healing whatever stands on
    /// it. Returned negated here so the sign convention matches everything else
    /// in this file: positive heals, negative hurts. Hero Lair is neither water
    /// nor lava, so the immunity below never reaches it and it heals everybody.</para>
    ///
    /// <para><b>Crossing a hazard you are able to cross is free</b> — CONFIRMED
    /// (owner, 2026-09-04): <i>"no creature that can traverse through water
    /// looses health"</i>. The first version of this method charged whatever was
    /// standing in water, which would have drowned an imp in five seconds doing
    /// the exact job the owner had just watched it do. The flag that decides
    /// passability decides the cost too, one hazard at a time — so
    /// <see cref="WorldPathGrid"/> and this method now read the same bit for the
    /// same reason.</para>
    ///
    /// <para><b>Then who ever pays it?</b> A creature that cannot cross is never
    /// routed into water by the path grid — but it can be <i>put</i> there: from
    /// the keeper's hand, or in the original by a patrol route drawn across it.
    /// And the roster says water is charged to almost nobody, because
    /// <see cref="CreatureFlags.CanWalkOnWater"/> is on <b>44 of the 46</b>
    /// creatures. The two without it are the <b>Vampire</b> and the <b>Unique
    /// Vampire</b>.</para>
    ///
    /// <para><b>Three sources that never met agree on those two records.</b> The
    /// owner named it from play before the column was read (<i>"I think vampires
    /// can't cross through water, they need bridges"</i>); the shipped
    /// <c>Creatures.kwd</c> has the flag missing on exactly those two; and the
    /// DK2 wiki's warning about water damage sits on the <b>Vampire page</b>,
    /// where it says a patrol routed through water may leave a vampire damaged,
    /// resurrected or dead. A rule confirmed by observation, by data and by
    /// documentation, on the one creature it applies to.</para>
    ///
    /// <para><b>Lava is the wide case.</b> Only <b>seven</b> creatures carry
    /// <see cref="CreatureFlags.CanWalkOnLava"/> — Salamander, Firefly, Giant,
    /// Fairy, Horny and the unique Firefly and Salamander — so lava is what the
    /// other 39 pay for, and lava is where this rule does most of its work. An
    /// Imp crosses water for nothing and burns in lava, which is the pairing
    /// <c>CreatureNeedsTests</c> now uses.</para>
    ///
    /// <para><b>The cadence is still ours</b> (Q118), and the owner's answer
    /// took away the cheap way to measure it: nothing they can walk into water
    /// takes damage, so the per-second reading cannot be checked with a
    /// stopwatch until a Vampire is dropped in a lake. At 100 a second a
    /// Vampire lasts 25 seconds; per tick it would last one and a quarter.</para>
    ///
    /// <para>Applied to a creature that is <see cref="CreatureDeathRules.IsDown"/>
    /// as well as a standing one, deliberately: a creature that collapses in
    /// lava is in lava, and lying in it is no safer than walking through it.</para>
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
    public static int TerrainHealthPerSecond(
        WorldState world, GameDatabase database, CreatureDef creature, CreatureState state)
    {
        TilePos at = state.Tile;
        if (!world.Contains(at)) return 0;

        TerrainDef terrain = database.TerrainById(world.Tile(at.X, at.Y).TerrainId);
        if (terrain.Damage == 0) return 0;

        if (terrain.IsWater && creature.Flags.HasFlag(CreatureFlags.CanWalkOnWater)) return 0;
        if (terrain.IsLava && creature.Flags.HasFlag(CreatureFlags.CanWalkOnLava)) return 0;

        return -terrain.Damage;
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
        LairRules.AsleepInOwnBed(world, database, state)
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
    public static void Eat(GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (state.ChickensNeeded <= 0) return;

        state.ChickensNeeded--;
        if (state.ChickensNeeded == 0) state.HungerTicks = 0;

        Apply(database, creature, state, creature.HpFromChicken);
    }
}
