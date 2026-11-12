// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Health and hunger; every number below is the shipped data's. Own-land regeneration is
/// a no-op for 44 of the 46 creatures: only the Imp (67 a second) and King Reginald (400) carry a
/// rate. Going unfed costs no health — the price is anger (<c>angerNoFood</c>, 100 for a Goblin)
/// and no balance variable turns hunger into damage.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-needs.md")]
public static class CreatureHealthRules
{
    /// <summary>The 32767 that means "never" in the creature record — a saturated 16-bit value in a
    /// wider field, set <b>per need</b> not per creature: eight never get hungry, six never sleep,
    /// and neither set contains the other (the Vampire sleeps but never eats, the Dwarf eats but
    /// never sleeps), which rules out one "undead" flag. <c>CreatureNeedsOnRealDataTests</c> names both sets.</summary>
    public const int Never = short.MaxValue;

    /// <summary>Maximum health: the record's <c>Hp</c> scaled by the creature's level
    /// (<see cref="CreatureLevelRules"/>), which is why it takes the state and the database. There
    /// is deliberately no level-free overload — one would cap a level-10 Goblin at 1500, not 9000.</summary>
    public static int MaxHealth(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        CreatureLevelRules.MaxHealth(database, creature, state.Level);

    /// <summary>A health change as <c>Thing_ModifyHealth</c> (<c>0x004B2CE0</c>) makes it: the delta is added with
    /// no lower clamp (<c>0x004B2EEC..0x004B2F03</c>), so a blow's overkill stays and a heal lifts it from there.
    /// The cut at the maximum is ours (DK2's callers cut the heal). Damage that leaves health at or below zero
    /// books the collapse (<see cref="CreatureState.CollapseDue"/>; DK2 collapses inside the call, <c>0x004B2FB5</c>,
    /// for a heal too). A dying or dead creature keeps the zero floor and books nothing. Returns what landed, counted from zero. The <paramref name="source"/> is stamped first,
    /// for any delta (ADR-0764, ADR-0765).</summary>
    public static int Apply(
        GameDatabase database, CreatureDef creature, CreatureState state, int delta, byte source)
    {
        state.HealthSourcePlayerId = source;
        int before = state.Health;
        int max = MaxHealth(database, creature, state);
        bool floor = CreatureDeathRules.IsDying(state) || CreatureDeathRules.IsCorpse(state);
        state.Health = Math.Min(before + delta, max);
        if (floor && state.Health < 0) state.Health = 0;
        if (!floor && delta < 0 && state.Health <= 0) state.CollapseDue = true;
        return Math.Max(state.Health, 0) - Math.Max(before, 0);
    }

    /// <summary>Whether this creature is at or below <c>CreatureCriticalHealthPercentageOfMax</c>
    /// (20 %) of its maximum. No simulation reader, and no reader found in DK2 either: a raw scan of
    /// every initialised block for the variable's cached global finds one reference, the loader
    /// writing it (ADR-0085). Kept because the variable is shipped and named.</summary>
    public static bool IsCriticallyHurt(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        state.Health * 100
            <= MaxHealth(database, creature, state)
               * database[VariableTypeId.CreatureCriticalHealthPercentageOfMax];

    /// <summary>Whether this creature is hurt enough that DK2 sends it to bed —
    /// <c>CreatureSleepsWhenBelowPercentHealth</c>, 50. Puts a bedless creature to sleep untired (state 80, <c>0x0046F785</c>);
    /// with a bed it also needs tiredness (ADR-0543). Read by <see cref="Systems.CreatureSleepSystem"/>.</summary>
    public static bool ShouldSleepFromWounds(
        GameDatabase database, CreatureDef creature, CreatureState state) =>
        state.Health * 100
            < MaxHealth(database, creature, state)
              * database[VariableTypeId.CreatureSleepsWhenBelowPercentHealth];

    /// <summary>The creature update's own-land regeneration (<c>0x00488A73..0x00488C32</c>), every turn:
    /// on a tile its owner holds (<c>[tile+8] &amp; 0xFFF</c> == <c>+0x28</c>), the record's per-second value
    /// over the turn rate (the loader's <c>def+0x6A6</c>, Imp 67 to 16), level-scaled, negatives to 0 and
    /// capped at <c>0xFFFE</c>, then cut so health stops at the level's maximum. Skipped only for a dying
    /// creature (<c>+0x3E6</c> bit 21, <c>0x0048B839</c>; ADR-0764): ours reads the bit as the dying state or a
    /// pending collapse DK2's damage call would have made (<see cref="CreatureDeathRules.HasDyingState"/>). An
    /// Imp or a possessed creature below 1 health is healed until the removal walk. Not built: a Good
    /// creature on Hero Lair terrain (35) takes the terrain's word instead (<c>0x00488A8F..0x00488AB3</c>),
    /// which <see cref="TerrainHealthPerSecond"/> stands in for; and the record's heal effect (def <c>+0x774</c>, <c>0x00488BF2</c>).</summary>
    public static int OwnLandRegenPerTurn(
        WorldState world, GameDatabase database, CreatureDef creature, CreatureState state)
    {
        int perTurn = creature.OwnLandHealthIncreasePerSecond / SimulationTiming.OriginalTicksPerGameSecond;
        if (perTurn == 0 || CreatureDeathRules.IsDying(state) || CreatureDeathRules.IsCorpse(state)) return 0;
        if (CreatureDeathRules.HasCollapsed(state) && CreatureDeathRules.HasDyingState(world, database, creature, state)) return 0;
        TilePos at = state.Tile;
        if (!world.Contains(at) || world.Tile(at.X, at.Y).OwnerPlayerId != state.OwnerPlayerId) return 0;

        int amount = Math.Min(Math.Max(CreatureLevelRules.Scale(perTurn,
            CreatureLevelRules.Percent(database, CreatureStatId.OwnLandHealthIncreasePerSecond, state.Level)), 0), 0xFFFE);
        if (amount == 0) return 0;
        int max = CreatureLevelRules.MaxHealth(database, creature, state.Level);
        return state.Health + amount > max ? max - state.Health : amount;
    }

    /// <summary>Health per second from the terrain a creature is standing on —
    /// <c>TerrainDef.Damage</c>, a <b>signed delta and not damage</b>, which a creature able to
    /// cross the hazard does not pay, standing or down. Three of 41 terrains carry one and the
    /// third is why the field's name is wrong: Water +100, Lava +100, Hero Lair −67, negated here
    /// so positive heals. Crossing a hazard you can cross is free (CONFIRMED, owner), so
    /// <see cref="CreatureFlags.CanWalkOnWater"/> (44 of 46, not either Vampire) and
    /// <see cref="CreatureFlags.CanWalkOnLava"/> (seven) decide the cost. Cadence is ours: UNKNOWN, per second or per tick (Q118).</summary>
    public static int TerrainHealthPerSecond(
        WorldState world, GameDatabase database, CreatureDef creature, CreatureState state)
    {
        TilePos at = state.Tile;
        if (!world.Contains(at)) return 0;

        TerrainDef terrain = database.TerrainById(world.Tile(at.X, at.Y).TerrainId);
        if (terrain.Damage == 0) return 0;

        if (terrain.IsWater && ((creature.Flags & CreatureFlags.CanWalkOnWater) == CreatureFlags.CanWalkOnWater)) return 0;
        if (terrain.IsLava && ((creature.Flags & CreatureFlags.CanWalkOnLava) == CreatureFlags.CanWalkOnLava)) return 0;

        return -terrain.Damage;
    }

    /// <summary>Health per second for a creature <b>asleep in its own bed</b> —
    /// <c>ModifyHealthOfCreatureInLairPerSecond</c>, <b>100</b>, and the main way anything heals in
    /// DK2. CONFIRMED (owner, 2026-09-04), and both halves are load-bearing: <b>asleep</b>, so a
    /// wounded creature standing awake in its own lair heals nothing; and in <b>its own bed</b>,
    /// one per tile and one per creature (<see cref="LairRules"/>), so a bedless creature never
    /// heals here. A Goblin's <c>TimeSleep</c> of 9 seconds is worth 900 health of its 1500 (Q103).</summary>
    public static int LairHealPerSecond(
        WorldState world, GameDatabase database, CreatureState state) =>
        LairRules.AsleepInOwnBed(world, database, state)
            ? database[VariableTypeId.ModifyHealthOfCreatureInLairPerSecond]
            : 0;

    /// <summary>Whether this creature ever gets hungry — false for the eight at
    /// <see cref="Never"/>. Tested on <c>HungerFill</c> rather than the 32767 sentinel: a creature
    /// needing zero chickens can never be hungry, and the two encodings agree on all 46 (<c>CreatureNeedsOnRealDataTests</c> checks that).</summary>
    public static bool CanGetHungry(CreatureDef creature) => creature.HungerFill > 0;

    /// <summary>One chicken eaten in a hatchery: pays off one of the <c>HungerNeed</c>, restarts
    /// the hunger clock and restores <c>HpFromChicken</c>, capped at the level-scaled maximum. The
    /// clamp is load-bearing, not defensive: <c>HpFromChicken</c> is 0 for the Firefly even though
    /// it eats two, and 250 for Jack, whose whole maximum is 240.</summary>
    public static void Eat(GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (!EatingRules.StillOwesFood(state)) return;

        EatingRules.Eat(database, creature, state);
    }
}
