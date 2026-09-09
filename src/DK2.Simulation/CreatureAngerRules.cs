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

    /// <summary>
    /// Above the ceiling — the original's comparison is <c>&gt; 32767</c>, not
    /// <c>&gt;=</c>. Picks from its angry job table: Leave, or Rebel.
    /// </summary>
    Angry = 2,
}

/// <summary>Anger: six counters, a ceiling of 32767, and the bands on it.</summary>
/// <remarks>
/// <para>Rule: anger accrues linearly; a creature is unhappy at
/// <c>UnhappyThreshold</c> (50) percent of the ceiling, 16383, and angry above
/// the ceiling itself, 32767 (<c>short.MaxValue</c>). Applied once a game
/// second.</para>
/// <para>CONFIRMED by two owner measurements — a stopwatch on a lairless Goblin
/// at <c>AngerNoLair</c> 100/s (unhappy 2:45, angry 5:30) and a slap count on a
/// Salamander at <c>AngerSlap</c> 3000 (6 to unhappy, 11 to angry, bracketing
/// the ceiling into (30000, 33000]). Q106, Q114;
/// docs/research/creature-anger.md §"The two measurements".</para>
/// </remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-anger.md")]
public static class CreatureAngerRules
{
    /// <summary>
    /// The band a creature crosses: <b>32767</b>, <c>short.MaxValue</c>.
    /// CONFIRMED by owner measurement (Q106, Q114).
    ///
    /// <para>Not a balance variable: all 214 were read and there is no
    /// <c>MaxAnger</c>, <c>AngerCeiling</c> or <c>AnnoyanceMax</c>. It is a
    /// property of the field width — the six per-creature anger fields are
    /// signed 16-bit (ADR-0050).</para>
    /// </summary>
    public const int Ceiling = short.MaxValue;

    /// <summary>
    /// Where a counter actually stops: <b>65533</b>, which is not
    /// <see cref="Ceiling"/>.
    ///
    /// <para>The original's anger-add routine clamps each bucket to
    /// <c>[0, 0xFFFD]</c> and only then bands the largest of them (ADR-0080), so
    /// a grievance climbs for a second full ceiling's worth after the creature
    /// has turned angry and a long-neglected creature does not calm the moment a
    /// keeper does one thing right. Invisible to the owner's stopwatch, which
    /// measured the band.</para>
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
    /// <para>Per bucket, because DK2's is: its routine subscripts an
    /// <c>int[6]</c> with the caller's first argument and clamps that one slot,
    /// pooling nothing (<see cref="CreatureAnger"/>). Clamped at zero rather
    /// than allowed negative, as the original clamps — its negative branch masks
    /// the value to zero. The single gate on
    /// <see cref="CanBeAngered"/> lives here, as DK2 puts it, so no source can
    /// bypass it.</para>
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
    /// The anger a creature must <b>exceed</b> to be angry: the ceiling itself.
    /// No variable states it — it is the owner's 2:1 ratio, angry at exactly
    /// twice the time unhappy took, with unhappy at 50%.
    /// </summary>
    public static int AngryAt(CreatureDef creature) => Ceiling;

    /// <summary>
    /// Where this creature sits on the ladder right now, from the largest bucket
    /// and never from their sum (ADR-0080) — the original bands the maximum of
    /// its six counters, so two half-grievances leave a creature unhappy where a
    /// pooled counter would have made it angry.
    /// </summary>
    public static CreatureMood MoodOf(CreatureDef creature, CreatureState state)
    {
        int worst = state.Anger.Max;
        if (worst > AngryAt(creature)) return CreatureMood.Angry;
        if (worst >= UnhappyAt(creature)) return CreatureMood.Unhappy;
        return CreatureMood.Content;
    }

    /// <summary>
    /// The per-second anger change from unmet needs and from a bed. Charges
    /// three of DK2's six sources here: <c>AngerNoLair</c> +100 (the one the
    /// owner measured), <c>AngerNoPay</c> +60 (ADR-0079; being owed is the whole
    /// trigger, <see cref="PaydayRules.IsOwedWages"/>), and
    /// <c>ModifyAngerOfCreatureInLairPerSecond</c> −550.
    /// <para>Elsewhere: <c>AngerNoFood</c> +100 in the prison only, a quarter
    /// per failed search in the cell (ADR-0074,
    /// <see cref="PrisonRules.AngerPerFailedFoodSearch"/>); <c>AngerNoWork</c>
    /// +50 in <see cref="Systems.CreatureJobSystem"/>. Not implemented:
    /// <c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c> +150.</para>
    /// </summary>
    public static CreatureAnger PerSecond(
        WorldState world,
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        // A CAPTIVE TAKES NO NEED-ANGER AT ALL: the original's need-anger
        // routine refuses outright when the current state carries bit 1 of the
        // state record's flag byte, and all eleven "being held captive" states
        // carry it. A captive's anger is the prison's business alone
        // (ADR-0064, ADR-0074).
        CreatureAnger delta = default;
        if (PrisonRules.IsImprisoned(state)) return delta;

        // ADDITIVE (ADR-0079) and PER BUCKET (ADR-0080): the original's
        // need-anger routine (FUN_00478d00) tests lair, food and pay one after
        // another and charges each into its own counter, so a bedless unpaid
        // creature takes both and neither pays off the other.
        if (LairRules.NeedsBed(creature) && !LairRules.HasBed(world, database, state))
        {
            // A creature whose record gives it no lair object at all - the Imp,
            // the Skeleton, Horny - is not owed a bed and is not angered by its
            // absence.
            delta.Lair += creature.AngerNoLair;
        }

        // Owed wages it has not collected.
        if (PaydayRules.IsOwedWages(database, creature, state))
        {
            delta.Pay += creature.AngerNoPay;
        }

        // AND A BED DOES NOT PAY OFF EITHER OF THEM (ADR-0080):
        // ModifyAngerOfCreatureInLairPerSecond reaches bucket 5 and nothing
        // else, where the two charges above are buckets 2 and 4.
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
    /// Whether this creature can be angered at all — one flag read from the
    /// record. False for the Skeleton and for Jack, the two creatures carrying
    /// <see cref="CreatureFlags2.NeverAngered"/> (Q210, STRONGLY INFERRED).
    /// <para>Trap: Jack's four <c>angerNo*</c> fields are populated (120, 110,
    /// 60, 50), his <c>AngerInHand</c> +500 and his <c>AngerSlap</c>
    /// <b>−5000</b>, and DK2 reads none of them because the wrapper every anger
    /// site goes through tests the flag first — deriving the exempt set from the
    /// values made Jack calmable by slapping. The Imp, the Dwarf and Horny are
    /// not flagged and carry 1, a hundredth of a Goblin's 100, so a lairless Imp
    /// takes nine hours: the data saying <i>"Imps never get angry"</i>.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-anger.md")]
    public static bool CanBeAngered(CreatureDef creature) =>
        !creature.Flags2.HasFlag(CreatureFlags2.NeverAngered);

    /// <summary>
    /// A slap: <c>AngerSlap</c>, +3000 for a Goblin and <b>−5000 for Jack</b>,
    /// whose whole record runs backwards. Called by
    /// <see cref="Systems.HandSystem"/> (ADR-0062).
    /// <para>Six slaps make a content creature sulk and 11 make it leave, for
    /// any creature whose <c>AngerSlap</c> is 3000 — CONFIRMED by the owner's
    /// Salamander (Q114), asserted by
    /// <c>HandTests.SixSlapsMakeAGoblinSulkAndElevenMakeItLeave</c>. No
    /// cooldown: the original's <c>LastSlapTick</c> window guards its knock-back
    /// impulse and nothing else, so the owner's six were six because a hand is
    /// slow (docs/research/hand-of-evil.md §4).</para>
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
    /// <para>Variable 19 is DK2's ticks-a-game-second and is 4 in every shipped
    /// level (Q1); ours is <see cref="SimulationTiming.TicksPerSecond"/>. What
    /// the original expresses is five game <i>seconds</i>, so that is what this
    /// is, and the tick rate does not change it.</para>
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
    /// <para>The inclusive bounds are a quirk and are reproduced: a 50/50 table
    /// becomes <c>[0,50]</c> and <c>[50,100]</c>, overlapping at 50, and the
    /// caller takes the first entry whose range holds the roll — so entry 0 gets
    /// 51 rolls of 100 and entry 1 gets 49. With <see cref="RollPercent"/>
    /// stopping at 99, the last upper bound is never reached. Record order
    /// survives because the original's pre-roll sort uses a comparator that never
    /// returns negative on an already-ascending array. Zero-chance entries are
    /// kept, because filtering would move every other entry's boundaries.</para>
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
