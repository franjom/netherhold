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
    /// <c>MaxGoldHeld</c> scaled by the MaxGoldHeld row: 105, 110 and 115% at levels 2 to 4, 120% from level 5.</summary>
    public static int MaxGoldHeld(GameDatabase database, CreatureDef creature, int level) =>
        Scale(creature.MaxGoldHeld, Percent(database, CreatureStatId.MaxGoldHeld, level));

    /// <summary>What one work hit earns (ADR-0386): the raw, undivided <c>ImpExperienceGainPerSecond</c>
    /// of 20. Its nine readers are the work actions' callbacks (dig <c>0x00496E00</c>, mining
    /// <c>0x004971D0</c>, claim <c>0x004974C0</c>, reinforce <c>0x00497960</c>, and the floor attack,
    /// conversion, room attack and repair), each paying once per hit whose tile test passes.</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
    public static int WorkExperiencePerHit(GameDatabase database) =>
        database[VariableTypeId.ImpExperienceGainPerSecond];

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
        in StateHookContext hooks, CreatureDef creature, CreatureState state, int amount)
    {
        GameDatabase database = hooks.Database;
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
        return LevelUp(hooks, creature, state);
    }

    /// <summary>The level increment's pit clause: a fighter that reaches
    /// <c>CombatPitMaxExperienceLevel</c> is set to 1, and idle sends it back to 138.</summary>
    private static void LeaveThePitAtTheCap(in StateHookContext hooks, CreatureState state)
    {
        if (CombatPitRules.AtTheCap(hooks.Database, state)) CreatureStateMachine.TrySetState(hooks, state, CreatureStateMachine.Idle);
    }

    /// <summary>DK2's level increment <c>0x0048B120</c>, without its sound: nothing at
    /// level 10 or on a finished (dead) creature; otherwise the level rises by one and health is
    /// carried across as its share of the maximum — <c>ratio = (health &lt;&lt; 12) / oldMax</c>
    /// in 12-bit fixed point, <c>new = (ratio × newMax) &gt;&gt; 12</c> — applied as a delta through
    /// the health routine, which clamps at the new maximum. A Combat Pit fighter's move to 1 at the
    /// cap is <see cref="LeaveThePitAtTheCap"/>; the <c>+0x3AF</c> adjustment is not modelled
    /// (Q427). Its twenty call sites include the experience adder, the party spawn and the trigger
    /// actions, so every promotion goes through it. Returns whether it rose.</summary>
    public static bool LevelUp(in StateHookContext hooks, CreatureDef creature, CreatureState state)
    {
        GameDatabase database = hooks.Database;
        if (state.Level >= MaxLevel || state.Health <= 0) return false;
        int oldMax = CreatureHealthRules.MaxHealth(database, creature, state);
        int current = state.Health;
        long ratio = ((long)current << 12) / oldMax;
        state.Level++;
        int newMax = CreatureHealthRules.MaxHealth(database, creature, state);
        int target = (int)((ratio * newMax) >> 12);
        // Level up and down pass the creature's own +0x36 back (0x0048B28F, 0x0048B0EE): the source is kept.
        CreatureHealthRules.Apply(database, creature, state, target - current, state.HealthSourcePlayerId);
        hooks.RequestEffect(nameof(CreatureLevelRules), new ViewEffectRequest(new EffectId(339), state.OwnerPlayerId,
            new FpPosition3(new Fp32(state.PosX), new Fp32(state.PosY), new Fp32(state.PosZ)),
            new Dk2Angle((ushort)state.Heading)));
        LeaveThePitAtTheCap(hooks, state);
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
