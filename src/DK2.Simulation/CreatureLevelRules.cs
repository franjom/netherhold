// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a level does to a creature: it multiplies its stats.</summary>
/// <remarks>
/// <para>Rule: <c>GlobalVariables.kwd</c> holds a 32-stat by 9-level table of percentages against
/// the level-1 value (<c>GlobalVariables.CreatureStats</c>) — health 125 % at L2 to 600 % at L10,
/// speed to 250 %, experience for the next level 130 % to 1000 %, ten of the 32 never scaling.
/// CONFIRMED (owner, Q119, Q124).</para>
/// <para>Built: work and training, per original tick (ADR-0083). Not implemented: fighting, so
/// nothing reaches level 5.</para>
/// <para>Trap: the record's <c>ExpPerSecond</c> (1200 for an Imp) is combat experience; its being
/// 60 × the global's 20 is a coincidence, not a per-minute unit (Q205, Q208).</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-levels.md")]
public static class CreatureLevelRules
{
    /// <summary>The level a creature arrives at.</summary>
    public const int FirstLevel = 1;

    /// <summary>Ten, because the shipped table's rows stop there.</summary>
    public const int MaxLevel = GameDatabase.MaxCreatureLevel;

    /// <summary>Clamps a level into the range the table can describe: a snapshot from another
    /// build, or a scenario asking for level 12, still gets an answer.</summary>
    public static int Clamp(int level) => Math.Clamp(level, FirstLevel, MaxLevel);

    /// <summary>Applies one of the table's percentages to a record value. 64-bit intermediate: the
    /// experience threshold reaches 1000 %, overflowing an <c>int</c> mid-multiply though the result fits.</summary>
    public static int Scale(int recordValue, int percent) =>
        (int)Math.Clamp((long)recordValue * percent / 100, int.MinValue, int.MaxValue);

    /// <summary>The percentage this stat carries at this level. 100 at level 1.</summary>
    public static int Percent(GameDatabase database, CreatureStatId stat, int level) =>
        database.StatPercent(stat, Clamp(level));

    /// <summary>Maximum health at this level: the record's <c>Hp</c> scaled by the Health row.</summary>
    public static int MaxHealth(GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.Hp, Percent(database, CreatureStatId.Health, level));

    /// <summary>The most gold this creature can carry at this level: the record's
    /// <c>MaxGoldHeld</c> scaled by the MaxGoldHeld row, 120% from level 5.</summary>
    public static int MaxGoldHeld(GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.MaxGoldHeld, Percent(database, CreatureStatId.MaxGoldHeld, level));

    /// <summary>Walking speed in fixed-point raw units at this level — the record's <c>Speed</c>
    /// scaled by the Speed row. Scaled raw on purpose: <see cref="Fp32"/>'s denominator is 4096 and
    /// the percentage is integer, so raw arithmetic is exact where a decimal round trip is not.</summary>
    public static int SpeedRaw(GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.Speed.Raw, Percent(database, CreatureStatId.Speed, level));

    /// <summary>What a worker earns for one of the original's ticks of work (250 ms, ADR-0089): the
    /// raw <c>ImpExperienceGainPerSecond</c> of 20, undivided — 80 a game second, so level 2 costs
    /// 3000 / 80 = 37.5 s. Trap: the name is wrong by a factor of four, this being the one rate of
    /// its family the loader does not divide through
    /// <see cref="SimulationTiming.LoaderRatePerTick"/>. Per tick of work, not per job: DK2 reads it
    /// at nine sites in the imp's task unit, each guarded by a tile-property predicate, so an idle
    /// imp earns nothing. STRONGLY INFERRED (closed call chain, Q79, Q124).</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-levels.md")]
    public static int WorkExperiencePerOriginalTick(
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (state.Level >= MaxLevel) return 0;
        if (!creature.Flags.HasFlag(CreatureFlags.IsWorker)) return 0;
        if (state.Activity != CreatureActivity.Working) return 0;

        return database[VariableTypeId.ImpExperienceGainPerSecond];
    }

    /// <summary>Adds experience and promotes at most one level, reproducing DK2's adder
    /// (<c>FUN_00457880</c>, Q127). Returns true if a level was gained. A threshold of zero refuses
    /// everything, gains and losses alike; a loss bigger than the balance is a no-op, not a clamp;
    /// reaching the threshold zeroes the balance rather than subtracting, so the overshoot is
    /// discarded and an award's cadence is observable — hence training pays on
    /// <see cref="SimulationTiming.OnOriginalTick"/>; one level per call, no loop, through
    /// <see cref="LevelUp"/> (Q427). A Combat Pit
    /// fighter at <c>CombatPitMaxExperienceLevel</c> is refused everything (combat-pit.md §7).</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
    public static bool Grant(
        GameDatabase database, CreatureDef creature, CreatureState state, int amount)
    {
        if (CombatPitRules.AtTheCap(database, state)) return false;
        int needed = ExperienceForNextLevel(database, creature, state.Level);
        if (needed <= 0) return false;

        if (amount < 0)
        {
            // Refused, not clamped: the original only subtracts when it fits.
            if (-amount <= state.ExperiencePoints) state.ExperiencePoints += amount;
            return false;
        }

        if (amount == 0) return false;

        state.ExperiencePoints += amount;
        if (state.ExperiencePoints < needed) return false;

        // The remainder is thrown away, and only one level is bought, through the same level
        // increment every promotion uses (0x004579D4 calls 0x0048B120), health rescaled with it.
        state.ExperiencePoints = 0;
        return LevelUp(database, creature, state);
    }

    /// <summary>The level increment's pit clause: a fighter that reaches
    /// <c>CombatPitMaxExperienceLevel</c> is set to 1, and idle sends it back to 138.</summary>
    private static void LeaveThePitAtTheCap(GameDatabase database, CreatureState state)
    {
        if (CombatPitRules.AtTheCap(database, state)) CreatureStateMachine.TrySetState(state, CreatureStateMachine.Idle);
    }

    /// <summary>DK2's level increment <c>0x0048B120</c>, without its effect and sound: nothing at
    /// level 10 or on a finished (dead) creature; otherwise the level rises by one and health is
    /// carried across as its share of the maximum — <c>ratio = (health &lt;&lt; 12) / oldMax</c>
    /// in 12-bit fixed point, <c>new = (ratio × newMax) &gt;&gt; 12</c> — applied as a delta through
    /// the health routine, which clamps at the new maximum. A Combat Pit fighter's move to 1 at the
    /// cap is <see cref="LeaveThePitAtTheCap"/>; the <c>+0x3AF</c> adjustment is not modelled
    /// (Q427). Its twelve callers include the experience adder, the party spawn and the trigger
    /// actions, so every promotion goes through it. Returns whether it rose.</summary>
    public static bool LevelUp(GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (state.Level >= MaxLevel || state.Health <= 0) return false;
        int oldMax = CreatureHealthRules.MaxHealth(database, creature, state);
        int current = state.Health;
        long ratio = ((long)current << 12) / oldMax;
        state.Level++;
        int newMax = CreatureHealthRules.MaxHealth(database, creature, state);
        int target = (int)((ratio * newMax) >> 12);
        CreatureHealthRules.Apply(database, creature, state, target - current);
        LeaveThePitAtTheCap(database, state);
        return true;
    }

    /// <summary>The experience needed to reach the next level: the record's <c>ExpForNextLevel</c>
    /// scaled by the table, indexed by the creature's CURRENT level. CONFIRMED (Q124): DK2's adder
    /// indexes the <c>ExperienceForNextLevel</c> column with the current level byte and uses the raw
    /// value at level 1, so the first level costs the record's own 3000 where indexing by the target
    /// would make it 3900. Not reproduced: the clamp of the scaled threshold to <c>0xFFFE</c>, which
    /// cannot bite, the largest shipped product being 3000 at 1000 %.</summary>
    public static int ExperienceForNextLevel(
        GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.ExpForNextLevel,
              Percent(database, CreatureStatId.ExperienceForNextLevel, level));
}
