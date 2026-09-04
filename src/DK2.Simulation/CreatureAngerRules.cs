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
    /// Anger at which a creature becomes unhappy — <c>UnhappyThreshold</c>
    /// percent of <see cref="Ceiling"/>, which is <b>16383</b> for every
    /// creature in the game, all 46 of which carry 50.
    /// </summary>
    public static int UnhappyAt(CreatureDef creature) =>
        Ceiling * creature.UnhappyThreshold / 100;

    /// <summary>
    /// Anger at which a creature becomes angry: <b>the ceiling itself</b>.
    ///
    /// <para>There is no variable for this. It is the owner's 2:1 ratio: angry
    /// arrived at exactly twice the time unhappy did, and unhappy is at 50%.</para>
    /// </summary>
    public static int AngryAt(CreatureDef creature) => Ceiling;

    /// <summary>Where this creature sits on the ladder right now.</summary>
    public static CreatureMood MoodOf(CreatureDef creature, CreatureState state)
    {
        if (state.Anger >= AngryAt(creature)) return CreatureMood.Angry;
        if (state.Anger >= UnhappyAt(creature)) return CreatureMood.Unhappy;
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
    public static int Apply(CreatureState state, int delta)
    {
        int before = state.Anger;
        state.Anger = Math.Clamp(before + delta, 0, Ceiling);
        return state.Anger - before;
    }

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
    /// <item><term><c>AngerNoFood</c> +100</term><description><b>NOT applied.</b>
    /// Chickens are objects and OBJECTS is unparsed (M6), so <i>no</i> dungeon
    /// can feed anybody. Charging it would anger every creature in every level
    /// regardless of play.</description></item>
    /// <item><term><c>AngerNoWork</c> +50</term><description><b>NOT applied.</b>
    /// Non-workers have no job system yet, so "no work available" is a fact
    /// about this simulation rather than about the dungeon.</description></item>
    /// <item><term><c>AngerNoPay</c> +60</term><description><b>NOT applied.</b>
    /// There is no payday: <c>PayDayFrequencySeconds</c> (600) is parsed and
    /// unread, and nothing has ever paid a wage.</description></item>
    /// <item><term><c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c> +150</term>
    /// <description><b>NOT applied.</b> Needs the hated-creature relation, which
    /// is parsed but unused.</description></item>
    /// </list>
    ///
    /// <para>Each omission is a line in the scenario reports rather than a
    /// silence. When a hatchery can feed a creature, <c>AngerNoFood</c> goes in
    /// the same day.</para>
    /// </summary>
    public static int PerSecond(
        WorldState world,
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        // A creature asleep in its own bed calms, and calming wins: it cannot
        // be simultaneously bedless and in its bed, so these never both fire.
        if (LairRules.AsleepInOwnBed(world, state))
            return database[VariableTypeId.ModifyAngerOfCreatureInLairPerSecond];

        // No bed, and it wanted one. A creature whose record gives it no lair
        // object at all — the Imp, the Skeleton, Horny — is not owed one and is
        // not angered by its absence.
        if (LairRules.NeedsBed(creature) && state.LairTileIndex < 0)
            return creature.AngerNoLair;

        return 0;
    }

    /// <summary>
    /// Whether this creature can be angered at all. False for the
    /// <b>Skeleton</b>, the only creature in the game whose four
    /// <c>angerNo*</c> fields are all zero.
    ///
    /// <para>The Imp, the Dwarf and Horny carry <b>1</b> — not zero, but a
    /// hundredth of a Goblin's 100, so at 32767 a lairless Imp would take
    /// <b>nine hours</b> to become angry. Present so that something can happen;
    /// small enough that nothing does. The DK2 wiki states flatly that
    /// <i>"Imps never get angry"</i>, and 1 a second is how the shipped data
    /// says the same thing without a special case.</para>
    /// </summary>
    public static bool CanBeAngered(CreatureDef creature) =>
        creature.AngerNoLair != 0 || creature.AngerNoFood != 0
        || creature.AngerNoPay != 0 || creature.AngerNoWork != 0;

    /// <summary>
    /// A slap: <c>AngerSlap</c>, +3000 for a Goblin and <b>−5000 for Jack</b>,
    /// whose whole record runs backwards.
    ///
    /// <para><b>Nothing calls this</b> — the hand does not exist — but it is
    /// nonetheless the best-measured value in the mechanic, because <i>counting
    /// slaps is how the ceiling was pinned</i>. The owner's Salamander took
    /// exactly 6 to unhappy and 11 to angry, which brackets the ceiling into
    /// (30000, 33000] and rules out the 65535 reading that the timing alone
    /// could not (Q114, closed).</para>
    ///
    /// <para>So when the hand arrives, this is already correct rather than
    /// pending: 6 slaps make a content creature sulk and 11 make it leave, for
    /// any of the many creatures whose <c>AngerSlap</c> is 3000.</para>
    /// </summary>
    public static int Slap(CreatureDef creature, CreatureState state) =>
        Apply(state, creature.AngerSlap);

    /// <summary>
    /// Being held in the Hand of Evil: <c>AngerInHand</c>, which <b>calms</b>
    /// most creatures (−250 for a Goblin, −500 for a Warlock) and annoys Jack
    /// (+500). Also uncalled, for the same reason.
    /// </summary>
    public static int Held(CreatureDef creature, CreatureState state) =>
        Apply(state, creature.AngerInHand);

    /// <summary>
    /// What an angry creature decides to do, drawn from its own
    /// <c>AngryJobs</c> table — for a Goblin, <c>Leave(50%)</c> and
    /// <c>Rebel(50%)</c>.
    ///
    /// <para>Drawn from the simulation's own RNG so the choice is part of the
    /// deterministic stream. The table's percentages are used as weights in
    /// record order, which is the same convention <see cref="PortalRules"/>
    /// uses for the creature pool.</para>
    ///
    /// <para><b>The wiki gives a different model, and it is recorded rather
    /// than followed</b> (Q116): its DK2 section says an angry creature heads
    /// for the exit and rebels *"if they can't do that"* — a fallback, where the
    /// record plainly holds a weighted table. Both cannot be right; the record
    /// is the stronger source, and reachability is not something this
    /// simulation can test yet anyway.</para>
    /// </summary>
    public static JobType AngryChoice(
        CreatureDef creature, SimRandom rng)
    {
        IReadOnlyList<CreatureJobPreference> table = creature.AngryJobs;
        if (table.Count == 0) return JobType.None;

        int total = 0;
        for (int i = 0; i < table.Count; i++) total += table[i].ChancePercent;
        if (total <= 0) return table[0].Job;

        int roll = rng.NextInt(total);
        for (int i = 0; i < table.Count; i++)
        {
            roll -= table[i].ChancePercent;
            if (roll < 0) return table[i].Job;
        }
        return table[^1].Job;
    }
}
