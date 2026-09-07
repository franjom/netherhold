// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Where a creature sits on the ladder. CONFIRMED (owner, Q9/Q105/Q106).</summary>
public enum CreatureMood
{
    /// <summary>Below <c>UnhappyThreshold</c>. Does its accustomed job.</summary>
    Content = 0,

    /// <summary>At or above <c>UnhappyThreshold</c> — 50% of the ceiling.</summary>
    Unhappy = 1,

    /// <summary>At the ceiling. Picks from its angry job table: Leave, or Rebel.</summary>
    Angry = 2,
}

/// <summary>
/// Anger — and the number that blocked it for four sittings is
/// <b>32767</b>, measured by the owner with a stopwatch (Q106, 2026-09-04).
///
/// <para><b>The measurement.</b> A Goblin was put in a dungeon with no lair, so
/// its only anger source was <c>AngerNoLair</c> = 100 a second, and everything
/// else was provided. It became <i>unhappy</i> at about <b>2:45</b> and
/// <i>angry</i> at about <b>5:30</b>.</para>
///
/// <list type="table">
/// <item><term>165 s × 100/s = 16 500</term><description>vs 16 383, which is 32767/2</description></item>
/// <item><term>330 s × 100/s = 33 000</term><description>vs <b>32 767</b>, which is <c>short.MaxValue</c></description></item>
/// </list>
///
/// <para>Both within ~1%, which is inside the resolution of the observation.</para>
///
/// <para><b>The ratio is the stronger half of the evidence.</b> 330/165 is
/// <b>exactly 2</b>, and that pins three structural facts without needing the
/// ceiling at all: anger accrues <i>linearly</i>, the angry threshold is
/// <b>100%</b> of the ceiling — a number that appears nowhere in the 214 balance
/// variables — and <c>UnhappyThreshold</c> = 50 really is a <i>percentage</i>
/// rather than an absolute. A ceiling guessed wrong would still have to satisfy
/// that 2:1, and 10000 (OpenKeeper's guess) does not fit the absolute times at
/// all: it predicts 50 s and 100 s.</para>
///
/// <para><b>Why 32767 is the right reading of that measurement.</b> It is
/// <c>short.MaxValue</c>, and three things line up behind it:</para>
///
/// <list type="bullet">
/// <item>The six per-creature anger fields are <b>signed 16-bit</b>
/// (ADR-0050) — an s16 accumulator saturates at exactly this.</item>
/// <item>32767 is <i>already</i> DK2's sentinel in the same record, where it
/// means "never" for <c>HungerRate</c> and <c>TimeAwake</c>. The same saturated
/// s16 turning up twice is a house style, not a coincidence.</item>
/// <item>It needs <b>no invented cadence</b>: <c>AngerNoLair</c> is applied once
/// a second, exactly as the field name says.</item>
/// </list>
///
/// <para><b>Timing alone left one ambiguity, and a second measurement closed
/// it.</b> Duration cannot separate <i>(100 a second into 32767)</i> from
/// <i>(200 a second into 65535)</i> — doubling both preserves every time, and
/// 65535 is just as round a number. That is the shape of <b>Q47</b>, where "half
/// the tabled delta twice a second" fitted every observation and was wrong, so
/// it was named rather than buried.</para>
///
/// <para><b>The discriminator was a slap</b>, because a discrete delta scales
/// with the ceiling where a rate does not. The owner slapped a <b>Salamander</b>
/// (whose <c>AngerSlap</c> is also 3000): <b>exactly 6 slaps to unhappy, then 5
/// more to angry</b>. Eleven in total, and the two counts <i>bracket</i> the
/// ceiling rather than merely agreeing with it:</para>
///
/// <list type="table">
/// <item><term>5 slaps not enough, 6 enough</term>
/// <description>unhappy ∈ (15000, 18000] ⇒ ceiling ∈ (30000, 36000]</description></item>
/// <item><term>10 not enough, 11 enough</term>
/// <description>angry ∈ (30000, 33000] ⇒ ceiling ∈ <b>(30000, 33000]</b></description></item>
/// </list>
///
/// <para>32767 is the only meaningful number in that window. <b>65535 and
/// 10000 are excluded outright</b> — 65535 would have taken 11 slaps to
/// <i>unhappy</i> and 22 to angry.</para>
///
/// <para><b>And that fixes the cadence, which is the part worth having.</b> With
/// the ceiling pinned at 32767, the owner's 165 seconds gives the rate as
/// 16383/165 = <b>99.3, i.e. 100 once a second</b>, exactly as the field name
/// says. Two independent methods — a stopwatch on a rate and a count of discrete
/// events — landing on one number, which is what closes Q106 and Q114 together
/// rather than trading one unknown for another.</para>
///
/// <para><b>OpenKeeper is no help and says so itself.</b>
/// <c>CreatureMoodSystem</c> is an empty stub, and <c>CreaturesController</c>
/// carries the comment <c>// I guess the initial mood is 10000</c>. A guess, in
/// the source, never implemented — and refuted by the measurement above.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-anger.md")]
public static class CreatureAngerRules
{
    /// <summary>
    /// The ceiling: <b>32767</b>, <c>short.MaxValue</c>. CONFIRMED by owner
    /// measurement — see the type summary for the arithmetic and for the one
    /// alternative it does not exclude (Q114).
    ///
    /// <para>Deliberately <i>not</i> a balance variable, because it is not in
    /// the balance table. All 214 were read: there is no <c>MaxAnger</c>, no
    /// <c>AngerCeiling</c>, no <c>AnnoyanceMax</c>. It is a property of the
    /// field width.</para>
    /// </summary>
    public const int Ceiling = short.MaxValue;

    /// <summary>
    /// Where a counter actually stops: <b>65533</b>, and it is <i>not</i>
    /// <see cref="Ceiling"/>.
    ///
    /// <para>Read from the original's anger-add routine on 2026-09-06
    /// (ADR-0080), which clamps each bucket to <c>[0, 0xFFFD]</c> and only then
    /// compares the largest of them against the bands. So a grievance keeps
    /// climbing for a second full ceiling's worth <i>after</i> the creature has
    /// already turned angry — which is why a long-neglected creature does not
    /// calm down the moment a keeper does one thing right.</para>
    ///
    /// <para>Nothing about this touches the owner's stopwatch. That measured
    /// the <i>band</i> a creature crosses, which is still
    /// <see cref="Ceiling"/>; this is the saturation above it, which no
    /// observation could have separated from a clamp at the band.</para>
    /// </summary>
    public const int Saturation = 0xFFFD;

    /// <summary>
    /// Anger at which a creature becomes unhappy — <c>UnhappyThreshold</c>
    /// percent of <see cref="Ceiling"/>, which is <b>16383</b> for every
    /// creature in the game, all 46 of which carry 50.
    /// </summary>
    public static int UnhappyAt(CreatureDef creature) =>
        Ceiling * creature.UnhappyThreshold / 100;

    /// <summary>
    /// Applies a change to one bucket and clamps it to
    /// <c>[0, <see cref="Saturation"/>]</c>. Returns what actually landed.
    ///
    /// <para><b>Per bucket, because DK2's is.</b> Its routine subscripts an
    /// <c>int[6]</c> with the caller's first argument and clamps that one slot;
    /// nothing pools them. See <see cref="CreatureAnger"/> for what the six are
    /// and what the split changes.</para>
    ///
    /// <para>Clamped at zero rather than allowed negative: a creature cannot be
    /// <i>better</i> than content, and the original clamps the same way — the
    /// negative branch masks the value to zero rather than keeping it.</para>
    /// </summary>
    public static int Apply(
        CreatureDef creature, CreatureState state, AngerBucket bucket, int delta)
    {
        if (!CanBeAngered(creature)) return 0;

        int before = state.Anger[bucket];
        state.Anger[bucket] = Math.Clamp(before + delta, 0, Saturation);
        return state.Anger[bucket] - before;
    }

    /// <summary>
    /// Anger at which a creature becomes angry: <b>the ceiling itself</b>.
    ///
    /// <para>There is no variable for this. It is the owner's 2:1 ratio: angry
    /// arrived at exactly twice the time unhappy did, and unhappy is at 50%.</para>
    /// </summary>
    public static int AngryAt(CreatureDef creature) => Ceiling;

    /// <summary>Where this creature sits on the ladder right now.</summary>
    /// <para><b>From the largest bucket, never from their sum</b> (ADR-0080).
    /// The original finds the maximum of its six counters and bands that one
    /// value, so two half-grievances leave a creature unhappy where a single
    /// pooled counter would have made it angry.</para>
    public static CreatureMood MoodOf(CreatureDef creature, CreatureState state)
    {
        int worst = state.Anger.Max;
        if (worst > AngryAt(creature)) return CreatureMood.Angry;
        if (worst >= UnhappyAt(creature)) return CreatureMood.Unhappy;
        return CreatureMood.Content;
    }

    /// <summary>
    /// Applies an anger change and clamps it to <c>[0, Ceiling]</c>. Returns
    /// what actually landed.
    ///
    /// <para>Clamped at zero rather than allowed negative: a creature cannot be
    /// <i>better</i> than content, and letting anger go to −20000 would give a
    /// creature that had won a combat pit an hour of immunity to everything.
    /// The floor is ours; nothing in the data states it (Q115).</para>
    /// </summary>


    /// <summary>
    /// The per-second anger change from unmet needs and from a bed.
    ///
    /// <para><b>Only two of DK2's sources are applied, and the reason is the
    /// same for both omissions:</b> a source is applied when the player's own
    /// choices can turn it off. Otherwise anger stops being feedback and becomes
    /// a countdown that no dungeon can survive, which would say nothing about
    /// DK2 and would make every long scenario end in mass desertion.</para>
    ///
    /// <list type="table">
    /// <item><term><c>AngerNoLair</c> +100</term><description><b>APPLIED.</b>
    /// Lairs are built, sold and filled, so the player decides. This is the one
    /// the owner measured.</description></item>
    /// <item><term><c>ModifyAngerOfCreatureInLairPerSecond</c> −550</term>
    /// <description><b>APPLIED.</b> Sleeping in one's own bed is implemented, so
    /// recovery is real — and 5.5× faster than the accrual it undoes.</description></item>
    /// <item><term><c>AngerNoFood</c> +100</term><description><b>Applied, but
    /// not here.</b> The prison charges it — one quarter of it, per failed
    /// search for food in the cell — because a keeper can stock a cell and so
    /// the source is one the player can turn off
    /// (<see cref="PrisonRules.AngerPerFailedFoodSearch"/>, ADR-0074). It is
    /// still <b>not</b> in this per-second sum, which is the general case: a
    /// hungry creature that is free walks to a hatchery, and charging every
    /// creature on a map that has no hatchery yet would be the countdown this
    /// list exists to avoid. The old reason - <i>no dungeon can feed
    /// anybody</i> - expired at ADR-0066.</description></item>
    /// <item><term><c>AngerNoWork</c> +50</term><description><b>NOT applied.</b>
    /// Non-workers have no job system yet, so "no work available" is a fact
    /// about this simulation rather than about the dungeon.</description></item>
    /// <item><term><c>AngerNoPay</c> +60</term><description><b>APPLIED since
    /// ADR-0079.</b> Payday exists (ADR-0078) and a keeper decides whether it
    /// can meet the bill, so this is exactly the kind of source this list is
    /// for. <b>Being owed is the whole trigger</b> — the original tests the
    /// outstanding wage against zero and nothing else, with no treasury test
    /// and no "did it fail to collect" test
    /// (<see cref="PaydayRules.IsOwedWages"/>).</description></item>
    /// <item><term><c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c> +150</term>
    /// <description><b>NOT applied.</b> Needs the hated-creature relation, which
    /// is parsed but unused.</description></item>
    /// </list>
    ///
    /// <para>Each omission is a line in the scenario reports rather than a
    /// silence. When a hatchery can feed a creature, <c>AngerNoFood</c> goes in
    /// the same day.</para>
    /// </summary>
    public static CreatureAnger PerSecond(
        WorldState world,
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        // A CAPTIVE TAKES NO NEED-ANGER AT ALL, and this is the original's
        // rule read from its state table rather than a kindness of ours: the
        // need-anger routine refuses outright when the current state carries
        // bit 1 of the state record's flag byte, and all ELEVEN "being held
        // captive" states carry it. A captive's anger is the prison's business
        // alone - it charges AngerNoFood per failed search for a bird in the
        // cell, and nothing else (ADR-0064, ADR-0074).
        //
        // Corrected 2026-09-06 (ADR-0079). Until then a prisoner took
        // AngerNoLair every second like anybody else, on the reasoning that it
        // had no bed - which is true, and is not what DK2 does with it.
        CreatureAnger delta = default;
        if (PrisonRules.IsImprisoned(state)) return delta;

        // ADDITIVE since ADR-0079, and PER BUCKET since ADR-0080. The
        // original's need-anger routine (FUN_00478d00) tests lair, food and pay
        // one after another and charges each into its own counter, so a bedless
        // unpaid creature takes both and neither can pay off the other.
        if (LairRules.NeedsBed(creature) && !LairRules.HasBed(world, database, state))
        {
            // No bed, and it wanted one. A creature whose record gives it no
            // lair object at all — the Imp, the Skeleton, Horny — is not owed
            // one and is not angered by its absence.
            delta.Lair += creature.AngerNoLair;
        }

        // Owed wages it has not collected.
        if (PaydayRules.IsOwedWages(database, creature, state))
        {
            delta.Pay += creature.AngerNoPay;
        }

        // AND A BED DOES NOT PAY OFF EITHER OF THEM, which is the correction
        // ADR-0080 is for. `ModifyAngerOfCreatureInLairPerSecond` reaches
        // bucket 5 and nothing else; the two charges above are buckets 2 and 4.
        // Sleeping it off used to cancel them here at 550 a second against 160,
        // and in DK2 it does not touch them at all.
        if (LairRules.AsleepInOwnBed(world, database, state))
        {
            delta.Treatment += database[VariableTypeId.ModifyAngerOfCreatureInLairPerSecond];
        }

        return delta;
    }

    /// <summary>Applies a whole tick's worth of per-bucket change.</summary>
    public static void Apply(CreatureDef creature, CreatureState state, CreatureAnger delta)
    {
        for (int i = 0; i < CreatureAnger.Count; i++)
        {
            var bucket = (AngerBucket)i;
            if (delta[bucket] != 0) Apply(creature, state, bucket, delta[bucket]);
        }
    }

    /// <summary>
    /// Whether this creature can be angered at all — <b>one flag, read from the
    /// record</b>. False for the <b>Skeleton</b> and for <b>Jack</b>, the two
    /// creatures carrying <see cref="CreatureFlags2.NeverAngered"/>.
    ///
    /// <para><b>This used to be inferred from the anger values, and the
    /// inference was wrong about Jack</b> (Q210). It read: <i>"false for the
    /// Skeleton, the only creature whose four <c>angerNo*</c> fields are all
    /// zero"</i> — true of the Skeleton, and true only by accident of its
    /// record. Jack's four fields are <b>120, 110, 60 and 50</b>, his
    /// <c>AngerInHand</c> is +500 and his <c>AngerSlap</c> is <b>−5000</b>; DK2
    /// reads none of them, because the wrapper every anger site goes through
    /// tests the flag and returns first. So our Jack accumulated anger on two
    /// unguarded paths and was <i>calmed</i> by slapping. Populated, plausible
    /// fields are not evidence of a live mechanism.</para>
    ///
    /// <para><b>The gate now sits in <see cref="Apply(CreatureDef,
    /// CreatureState, AngerBucket, int)"/></b>, the one adder, which is where
    /// DK2 puts it: a single wrapper guards a single adder, so no source can
    /// bypass it. The remaining callers keep their own checks only to skip work
    /// they would otherwise do for nothing.</para>
    ///
    /// <para>The Imp, the Dwarf and Horny are <i>not</i> flagged and can be
    /// angered in principle. They carry <b>1</b> — not zero, but a hundredth of
    /// a Goblin's 100, so at 32767 a lairless Imp would take <b>nine hours</b>
    /// to become angry. Present so that something can happen; small enough that
    /// nothing does. The DK2 wiki states flatly that <i>"Imps never get
    /// angry"</i>, and 1 a second is how the shipped data says the same thing
    /// without a special case — which is a different mechanism from the
    /// Skeleton's and Jack's, and worth keeping distinct.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-anger.md")]
    public static bool CanBeAngered(CreatureDef creature) =>
        !creature.Flags2.HasFlag(CreatureFlags2.NeverAngered);

    /// <summary>
    /// A slap: <c>AngerSlap</c>, +3000 for a Goblin and <b>−5000 for Jack</b>,
    /// whose whole record runs backwards.
    ///
    /// <para><b>Called by <see cref="Systems.HandSystem"/> since ADR-0062</b>,
    /// and it was the best-measured value in the mechanic long before
    /// anything called it, because <i>counting slaps is how the ceiling was
    /// pinned</i>. The owner's Salamander took
    /// exactly 6 to unhappy and 11 to angry, which brackets the ceiling into
    /// (30000, 33000] and rules out the 65535 reading that the timing alone
    /// could not (Q114, closed).</para>
    ///
    /// <para>So when the hand arrived, this was already correct rather than
    /// pending: 6 slaps make a content creature sulk and 11 make it leave, for
    /// any of the many creatures whose <c>AngerSlap</c> is 3000 - which
    /// <c>HandTests.SixSlapsMakeAGoblinSulkAndElevenMakeItLeave</c> now
    /// asserts.</para>
    ///
    /// <para><b>There is no cooldown on it.</b> The original's
    /// <c>LastSlapTick</c> window guards its knock-back impulse and nothing
    /// else, so a keeper who clicks fast angers a creature fast; the owner's six
    /// slaps were six because a hand is slow (docs/research/hand-of-evil.md
    /// section 4).</para>
    /// </summary>
    public static int Slap(CreatureDef creature, CreatureState state) =>
        Apply(creature, state, AngerBucket.Treatment, creature.AngerSlap);

    /// <summary>
    /// Being held in the Hand of Evil: <c>AngerInHand</c>, which <b>calms</b>
    /// most creatures (−250 for a Goblin, −500 for a Warlock) and annoys Jack
    /// (+500).
    ///
    /// <para><b>A rate, not a one-shot</b> (ADR-0062): the original applies it
    /// <i>once a game second</i>, and not until twenty seconds after the
    /// pick-up. So carrying a creature across the dungeon costs a keeper
    /// nothing, and parking one in the hand is a way to calm it down — see
    /// <see cref="HandRules.AngerDueThisTick"/>.</para>
    /// </summary>
    public static int Held(CreatureDef creature, CreatureState state) =>
        Apply(creature, state, AngerBucket.Treatment, creature.AngerInHand);

    /// <summary>
    /// How long an angry creature must wait between selections that came to
    /// something: <b>five game seconds</b>, the original's <c>5 x</c> variable
    /// 19 (Q122, ADR-0061).
    ///
    /// <para>Variable 19 is DK2's ticks-a-game-second and is <b>4</b> in every
    /// shipped level (Q1). Ours is <see cref="SimulationTiming.TicksPerSecond"/>
    /// = 20 and the two disagree — a conflict recorded in Q1 and deliberately
    /// not resolved by fiat. What the original expresses is <i>five game
    /// seconds</i>, so that is what this is; if the tick rate is ever settled,
    /// this constant does not change.</para>
    /// </summary>
    public const int ReselectionGateTicks = 5 * SimulationTiming.TicksPerSecond;

    /// <summary>
    /// The roll an angry creature makes: <c>rand(100)</c>, and it is
    /// <b>0..99</b> rather than 1..100.
    ///
    /// <para>Read from the original's own random helper, which ends in
    /// <c>% param</c> — so the top of the last cumulative range is unreachable
    /// when a table's chances sum to 100. See <see cref="CumulativeRanges"/>
    /// for the other half of that arithmetic, which is where it shows.</para>
    /// </summary>
    public static int RollPercent(SimRandom rng) => rng.NextInt(100);

    /// <summary>
    /// A job table as the original's choosers build it: a running total per
    /// entry, in <b>record order</b>, with <b>inclusive</b> bounds at both ends.
    ///
    /// <para><b>The inclusive bounds are a quirk and are reproduced.</b> A
    /// 50/50 table becomes <c>[0,50]</c> and <c>[50,100]</c> — the ranges
    /// <i>overlap</i> at 50, and the caller takes the first entry whose range
    /// holds the roll, so entry 0 gets 51 of the 100 rolls and entry 1 gets 49.
    /// A Goblin's coin toss is not quite a coin toss; and with
    /// <see cref="RollPercent"/> stopping at 99, the last entry's upper bound
    /// is never reached at all.</para>
    ///
    /// <para><b>Record order is the order.</b> The original sorts this array by
    /// upper bound before rolling, with a comparator that returns 0 for "less"
    /// and 1 for everything else — never negative — applied to an array that is
    /// already ascending by construction. It is a sort that cannot reorder
    /// anything, so it is not one; what it leaves behind is the record's own
    /// order, which is what this builds and what the fall-through then
    /// walks.</para>
    ///
    /// <para>Entries are kept whatever their chance, zero-chance ones included
    /// (their <c>Low == High</c> makes them reachable by exactly one roll).
    /// Filtering here would move the boundaries and change every other entry's
    /// odds; the original skips an entry at the moment it tests it, not when it
    /// builds the ranges, and so does the caller.</para>
    /// </summary>
    public static void CumulativeRanges(
        IReadOnlyList<CreatureJobPreference> table,
        List<(JobType Job, int Low, int High)> into)
    {
        into.Clear();
        int running = 0;
        for (int i = 0; i < table.Count; i++)
        {
            int high = running + table[i].ChancePercent;
            into.Add((table[i].Job, running, high));
            running = high;
        }
    }
}
