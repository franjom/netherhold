// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a level does to a creature: it multiplies its stats.</summary>
/// <remarks>
/// <para>Rule: <c>GlobalVariables.kwd</c> holds a 32-stat by 9-level table of
/// percentages against the level-1 value (<c>GlobalVariables.CreatureStats</c>)
/// — health 125% at level 2 to 600% at level 10, speed to 250%, experience for
/// the next level 130% to 1000%, ten of the 32 never scaling. CONFIRMED (owner,
/// Q119, Q124); curve and caps in docs/research/creature-levels.md.</para>
/// <para>Built: work and training, per original tick (ADR-0083). Not
/// implemented: fighting, so nothing reaches level 5. Trap: the record's
/// <c>ExpPerSecond</c> (1200 for an Imp) is <b>combat</b> experience, not
/// <c>ImpExperienceGainPerSecond</c> (20) per minute (Q205/Q208).</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-levels.md")]
public static class CreatureLevelRules
{
    /// <summary>The level a creature arrives at, and the only one it can be today.</summary>
    public const int FirstLevel = 1;

    /// <summary>Ten, because the shipped table's rows stop there.</summary>
    public const int MaxLevel = GameDatabase.MaxCreatureLevel;

    /// <summary>
    /// Clamps a level into the range the data can describe. A creature restored
    /// from a snapshot written by a different build, or placed by a scenario
    /// that asked for level 12, gets something the table can answer for.
    /// </summary>
    public static int Clamp(int level) => Math.Clamp(level, FirstLevel, MaxLevel);

    /// <summary>
    /// Applies one of the table's percentages to a value from the creature
    /// record.
    ///
    /// <para>64-bit intermediate because a stat at 1000% of a five-figure value
    /// overflows an <c>int</c> in the multiply while the result still fits, and
    /// the one field that reaches 1000% is the experience threshold.</para>
    /// </summary>
    public static int Scale(int recordValue, int percent) =>
        (int)Math.Clamp((long)recordValue * percent / 100, int.MinValue, int.MaxValue);

    /// <summary>The percentage this stat carries at this level. 100 at level 1.</summary>
    public static int Percent(GameDatabase database, CreatureStatId stat, int level) =>
        database.StatPercent(stat, Clamp(level));

    /// <summary>
    /// A creature's maximum health at its level: the record's <c>Hp</c> scaled
    /// by the Health row.
    /// </summary>
    public static int MaxHealth(GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.Hp, Percent(database, CreatureStatId.Health, level));

    /// <summary>
    /// Walking speed in fixed-point raw units at this level — the record's
    /// <c>Speed</c> scaled by the Speed row.
    ///
    /// <para>Scaled in raw units on purpose: <see cref="Fp32"/>'s denominator is
    /// 4096 and the percentage is integer, so scaling the raw value keeps the
    /// arithmetic exact where converting to and from a decimal would not.</para>
    /// </summary>
    public static int SpeedRaw(GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.Speed.Raw, Percent(database, CreatureStatId.Speed, level));

    /// <summary>
    /// What a worker earns for one of the original's ticks of work: the raw
    /// <c>ImpExperienceGainPerSecond</c> of <b>20</b>, undivided — <b>80 a game
    /// second</b>, so level 2 costs 3000 / 80 = <b>37.5 s</b>.
    /// <para>Trap: the name is wrong by a factor of four in the opposite
    /// direction to its four siblings on the creature record, which the loader
    /// divides through <see cref="SimulationTiming.LoaderRatePerTick"/>; this is
    /// a global, read raw, once per original tick (250 ms, ADR-0089). Per tick
    /// of work, not per job: DK2 reads it at nine sites in the imp's task unit,
    /// each guarded by a tile <i>property</i> predicate — STRONGLY INFERRED,
    /// closed call chain (Q79, Q124). An idle imp earns nothing.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-levels.md")]
    public static int WorkExperiencePerOriginalTick(
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (state.Level >= MaxLevel) return 0;
        if (!creature.Flags.HasFlag(CreatureFlags.IsWorker)) return 0;
        if (state.Activity != CreatureActivity.Working) return 0;

        return database[VariableTypeId.ImpExperienceGainPerSecond];
    }

    /// <summary>
    /// Adds experience and promotes at most one level, reproducing DK2's adder
    /// (<c>FUN_00457880</c>, Q127). Returns true if a level was gained. A
    /// threshold of zero refuses everything, gains and losses alike; a loss
    /// bigger than the balance is a no-op rather than a clamp; reaching the
    /// threshold <b>zeroes</b> the balance rather than subtracting, so the
    /// overshoot is discarded — which makes an award's <i>cadence</i> observable
    /// and is why training pays on
    /// <see cref="SimulationTiming.OnOriginalTick"/>; and one level per call, no
    /// loop. Not reproduced: its refusal when an unmapped creature flag
    /// (<c>0x8000</c>) is set at <c>CombatPitMaxExperienceLevel</c>.
    /// </summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
    public static bool Grant(
        GameDatabase database, CreatureDef creature, CreatureState state, int amount)
    {
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

        // The remainder is thrown away, and only one level is bought.
        state.ExperiencePoints = 0;
        if (state.Level >= MaxLevel) return false;

        state.Level++;
        return true;
    }

    /// <summary>
    /// The experience needed to reach the next level: the record's
    /// <c>ExpForNextLevel</c> scaled by the table.
    /// <para>Indexed by the creature's <b>current</b> level, which the binary
    /// confirms (Q124): DK2's adder indexes the <c>ExperienceForNextLevel</c>
    /// column with the current level byte and uses the record's raw value at
    /// level 1, so the first level costs exactly the record's own 3000; indexing
    /// by the target level would make it 3900. Not reproduced: the clamp of the
    /// scaled threshold to <c>0xFFFE</c>, which cannot bite, the largest shipped
    /// product being 3000 at 1000%.</para>
    /// </summary>
    public static int ExperienceForNextLevel(
        GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.ExpForNextLevel,
              Percent(database, CreatureStatId.ExperienceForNextLevel, level));
}
