// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Health and hunger.</summary>
/// <remarks>
/// <para>Every number is the shipped data's: maximum health is the record's
/// <c>Hp</c>, own-land regeneration its <c>OwnLandHealthIncreasePerSecond</c>,
/// lair healing <c>ModifyHealthOfCreatureInLairPerSecond</c>, hunger its
/// <c>HungerRate</c> and <c>HungerFill</c>. The readings that are ours are
/// called out at the methods making them (docs/research/creature-needs.md).</para>
/// <para>Own-land regeneration is a no-op for 44 of the 46 creatures: only the
/// Imp (67 a second) and King Reginald (400) carry a rate. Going unfed costs no
/// health — the price is anger (the record's <c>angerNoFood</c>, 100 for a
/// Goblin) and no balance variable turns hunger into damage.</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-needs.md")]
public static class CreatureHealthRules
{
    /// <summary>
    /// The 32767 that means "never" in the creature record — a general
    /// sentinel, per need rather than per creature. <c>short.MaxValue</c> in a
    /// wider field, so a saturated 16-bit value rather than a meaningful one —
    /// and 32767 seconds is nine hours of play either way.
    /// <para>Eight creatures never get hungry (Imp, Vampire, Skeleton, Horny,
    /// Stone Knight and the unique Firefly, Skeleton and Vampire); six never
    /// sleep (Imp, Skeleton, Dwarf, Horny, Stone Knight, Unique Skeleton).
    /// Neither set contains the other — the Vampire sleeps but never eats, the
    /// Dwarf eats but never sleeps — which rules out a single "undead" flag, so
    /// <c>CreatureNeedsOnRealDataTests</c> asserts both sets by name.</para>
    /// </summary>
    public const int Never = short.MaxValue;

    /// <summary>
    /// Maximum health: the record's <c>Hp</c> scaled by the creature's level
    /// (<see cref="CreatureLevelRules"/>), which is why it takes the state and
    /// the database. There is deliberately no level-free overload — a call that
    /// forgot the level would cap a level-10 Goblin at 1500 instead of 9000.
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
    /// Whether this creature is at or below
    /// <c>CreatureCriticalHealthPercentageOfMax</c> (20%) of its maximum.
    ///
    /// <para>No simulation reader, and DK2 has none either: a raw scan of every
    /// initialised block for the variable's cached global finds one reference,
    /// the loader writing it (re-checked 2026-09-07; ADR-0085). Kept because the
    /// variable is shipped and named. The number a knocked-out creature actually
    /// gets is <see cref="KnockoutHealth"/>, a sixth, read from the damage
    /// path.</para>
    /// </summary>
    public static bool IsCriticallyHurt(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        state.Health * 100
            <= MaxHealth(database, creature, state)
               * database[VariableTypeId.CreatureCriticalHealthPercentageOfMax];

    /// <summary>
    /// The sixth in <see cref="KnockoutHealth"/> — and the number of petals on
    /// the flower above a creature's head, which is not a coincidence.
    /// </summary>
    public const int KnockoutFraction = 6;

    /// <summary>
    /// What a creature has left the moment it is knocked out: exactly one sixth
    /// of its maximum, and a flower has exactly six petals (Q171).
    /// <para>DK2's <c>ModifyHealth</c> applies the delta and then, if the result
    /// went below zero and the creature is not already dying, sets health to
    /// <c>maxHealth / 6</c> and raises the <c>CreatureDying</c> flag — computed
    /// as <c>100 * max / 6 / 100</c>, a sixth expressed as a percentage. Damage
    /// taken <i>while</i> dying misses that branch, goes negative and kills.
    /// STRONGLY INFERRED (binary). Open: one sixth is one petal but the owner
    /// counts two in a cell (Q167) — display, not health. Trap: it is not
    /// <c>CreatureCriticalHealthPercentageOfMax</c>.</para>
    /// </summary>
    public static int KnockoutHealth(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        // DK2: 100 * max / 6 / 100. At least one, because a creature brought
        // round at nothing is a creature that dies on the tick it wakes.
        Math.Max(1, MaxHealth(database, creature, state) / KnockoutFraction);

    /// <summary>
    /// Whether this creature is hurt enough that DK2 sends it to bed —
    /// <c>CreatureSleepsWhenBelowPercentHealth</c>, which is 50. The condition
    /// that makes a wounded creature lie down without being tired; read by
    /// <see cref="LairRules"/> and <see cref="Systems.CreatureSleepSystem"/>.
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
    /// <para>The tile test is ours: HYPOTHESIS (Q100). It compares the tile's
    /// owner against the creature's, as OpenKeeper does. UNKNOWN whether DK2
    /// also requires the creature to be idle, or excludes it while fighting or
    /// working.</para>
    /// </summary>
    public static int OwnLandRegenPerSecond(
        WorldState world, CreatureDef creature, CreatureState state)
    {
        int rate = creature.OwnLandHealthIncreasePerSecond;
        if (rate == 0) return 0;

        // Standing on your own land does not pick you up off the floor: Q99
        // gives exactly one route back from a collapse, an imp carrying the
        // creature to its bed. Without this an Imp, which regenerates 67 a
        // second at home, would be unkillable on its own territory. Lair healing
        // is deliberately not gated this way - healing a creature at zero health
        // in its own bed is what a rescue is.
        if (CreatureDeathRules.IsDown(state)) return 0;

        TilePos at = state.Tile;
        if (!world.Contains(at)) return 0;

        return world.Tile(at.X, at.Y).OwnerPlayerId == state.OwnerPlayerId ? rate : 0;
    }

    /// <summary>
    /// Health per second from the terrain a creature is standing on —
    /// <c>TerrainDef.Damage</c>, a <b>signed delta and not damage</b>, which a
    /// creature able to cross the hazard does not pay, standing or down.
    /// <para>Three of 41 terrains carry one, and the third is why the field's
    /// name is wrong: Water +100, Lava +100, Hero Lair −67. Negated here so
    /// positive heals. Crossing a hazard you can cross is free (CONFIRMED,
    /// owner), so <see cref="CreatureFlags.CanWalkOnWater"/> (44 of 46, not
    /// either Vampire) and <see cref="CreatureFlags.CanWalkOnLava"/> (seven)
    /// decide the cost as they decide <see cref="WorldPathGrid"/>. Cadence is
    /// ours: UNKNOWN, per second or per tick (Q118).</para>
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
    /// <para>CONFIRMED by the owner (2026-09-04), and both halves are
    /// load-bearing: <b>asleep</b>, so a wounded creature standing awake in its
    /// own lair heals nothing; and in <b>its own bed</b>, one per tile and one
    /// per creature (<see cref="LairRules"/>), so a bedless creature never heals
    /// here whatever it stands on. The healing is therefore a consequence of the
    /// sleep cycle and not of position: a Goblin's <c>TimeSleep</c> of 9 seconds
    /// is worth 900 health of its 1500 (Q103).</para>
    /// </summary>
    public static int LairHealPerSecond(
        WorldState world, GameDatabase database, CreatureState state) =>
        LairRules.AsleepInOwnBed(world, database, state)
            ? database[VariableTypeId.ModifyHealthOfCreatureInLairPerSecond]
            : 0;

    /// <summary>
    /// Whether this creature ever gets hungry. False for the eight listed at
    /// <see cref="Never"/>.
    ///
    /// <para>Tested on <c>HungerFill</c> rather than on the 32767 sentinel: a
    /// creature needing zero chickens can never be hungry, and the two encodings
    /// agree on all 46 creatures — which
    /// <c>CreatureNeedsOnRealDataTests</c> checks rather than assumes.</para>
    /// </summary>
    public static bool CanGetHungry(CreatureDef creature) => creature.HungerFill > 0;

    /// <summary>
    /// One chicken eaten in a hatchery: it pays off one of the
    /// <c>HungerNeed</c>, restarts the hunger clock and restores
    /// <c>HpFromChicken</c> — capped, as always, at the level-scaled maximum.
    ///
    /// <para>The clamp is load-bearing, not defensive: <c>HpFromChicken</c> is 0
    /// for the Firefly even though it eats two, and 250 for Jack, whose whole
    /// maximum is 240.</para>
    /// </summary>
    public static void Eat(GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (!EatingRules.StillOwesFood(state)) return;

        EatingRules.Eat(database, creature, state);
    }
}
