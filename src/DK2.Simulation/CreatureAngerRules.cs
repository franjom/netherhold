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

    /// <summary>Above the ceiling — the original's comparison is <c>&gt; 32767</c>, not
    /// <c>&gt;=</c>. Picks from its angry job table: Leave, or Rebel.</summary>
    Angry = 2,
}

/// <summary>Anger: six counters, a ceiling of 32767, and the bands on it, accruing linearly and
/// applied once a game second. CONFIRMED by two owner measurements (Q106, Q114,
/// docs/research/creature-anger.md).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-anger.md")]
public static class CreatureAngerRules
{
    /// <summary>The band a creature crosses: <b>32767</b>, <c>short.MaxValue</c>. CONFIRMED by
    /// owner measurement (Q106, Q114). Not a balance variable — all 214 were read and there is no
    /// <c>MaxAnger</c> — but a property of the field width: the six per-creature anger fields are
    /// signed 16-bit (ADR-0050).</summary>
    public const int Ceiling = short.MaxValue;

    /// <summary>Where a counter actually stops: <b>65533</b>, which is not <see cref="Ceiling"/>.
    /// The original's anger-add clamps each bucket to <c>[0, 0xFFFD]</c> and only then bands the
    /// largest (ADR-0081), so a grievance climbs for a second full ceiling's worth after the creature turns angry — invisible to the owner's stopwatch, which measured the band.</summary>
    public const int Saturation = 0xFFFD;

    /// <summary>Anger at which a creature becomes unhappy — <c>UnhappyThreshold</c> percent of
    /// <see cref="Ceiling"/>, which is <b>16383</b> for all 46 creatures, every one carrying 50.</summary>
    public static int UnhappyAt(CreatureDef creature) =>
        Ceiling * creature.UnhappyThreshold / 100;

    /// <summary>Applies a change to one bucket and clamps it to
    /// <c>[0, <see cref="Saturation"/>]</c>. Returns what actually landed. Per bucket because DK2's
    /// routine subscripts an <c>int[6]</c> and clamps that slot, pooling nothing
    /// (<see cref="CreatureAnger"/>). The single <see cref="CanBeAngered"/> gate lives here.</summary>
    public static int Apply(
        CreatureDef creature, CreatureState state, AngerBucket bucket, int delta)
    {
        if (!CanBeAngered(creature)) return 0;

        int before = state.Anger[bucket];
        state.Anger[bucket] = Math.Clamp(before + delta, 0, Saturation);
        return state.Anger[bucket] - before;
    }

    /// <summary>The anger wrapper <c>0x00478B80(bucket, amount, complaint)</c>: the add behind the
    /// world switch, then, when the amount is positive and the band changed, message 17 at
    /// unhappy or 18 at angry to the creature's owner, Data1 the complaint and Data2 the creature
    /// (<see cref="PostMoodChange"/>). A never-angry creature takes nothing and posts nothing.</summary>
    public static int Apply(
        WorldState world, CreatureDef creature, CreatureState state,
        AngerBucket bucket, int delta, AngerComplaint complaint)
    {
        if (!world.Triggers.CreatureAngerEnabled || !CanBeAngered(creature)) return 0;
        CreatureMood before = MoodOf(creature, state);
        int applied = Apply(creature, state, bucket, delta);
        if (delta > 0) PostMoodChange(world, creature, state, before, complaint);
        return applied;
    }

    /// <summary>The wrapper's tail: with the band moved off <paramref name="before"/>, band 1
    /// posts 17 and band 2 posts 18. The caller has already tested the amount. Not built:
    /// the creature trigger event 11 DK2 fires after 18 (<c>0x00478C17</c>); no anger site holds
    /// the trigger executor.</summary>
    private static void PostMoodChange(
        WorldState world, CreatureDef creature, CreatureState state, CreatureMood before, AngerComplaint complaint)
    {
        CreatureMood after = MoodOf(creature, state);
        if (after == before) return;
        PlayerMessageKind? kind = after switch
        {
            CreatureMood.Unhappy => PlayerMessageKind.CreatureUnhappy,
            CreatureMood.Angry => PlayerMessageKind.CreatureAngry,
            _ => null,
        };
        if (kind is PlayerMessageKind posted && world.HasPlayer(state.OwnerPlayerId))
            world.Messages.Post(world, state.OwnerPlayerId, posted, (int)complaint, state.Id);
    }

    /// <summary>Wipes one grievance to zero: DK2's <c>0x00486F80(bucket)</c>, which adds the
    /// counter's own negation through the same clamp, behind the same world switch. Called
    /// with the lair bucket as a bed goes down (state 80) and the pay bucket once a wage is
    /// taken in full (state 189), so both are forgotten at once rather than cooled
    /// (<c>payday.md</c> §4.4, <c>lair-acquisition.md</c> §9.1). A negative amount posts nothing.</summary>
    public static int Forget(
        WorldState world, CreatureDef creature, CreatureState state, AngerBucket bucket) =>
        world.Triggers.CreatureAngerEnabled ? Apply(creature, state, bucket, -state.Anger[bucket]) : 0;

    /// <summary>The anger a creature must <b>exceed</b> to be angry: the ceiling itself. No variable
    /// states it — it is the owner's 2:1 ratio, angry at twice the time unhappy took.</summary>
    public static int AngryAt(CreatureDef creature) => Ceiling;

    /// <summary>Where this creature sits on the ladder, from the largest bucket and never from their
    /// sum (ADR-0081): two half-grievances leave it unhappy where a pool would say angry.</summary>
    public static CreatureMood MoodOf(CreatureDef creature, CreatureState state) => MoodOf(creature, state.Anger);

    /// <summary>The band of a set of counters, for an observation that holds a copy.</summary>
    public static CreatureMood MoodOf(CreatureDef creature, in CreatureAnger anger)
    {
        int worst = anger.Max;
        if (worst > AngryAt(creature)) return CreatureMood.Angry;
        if (worst >= UnhappyAt(creature)) return CreatureMood.Unhappy;
        return CreatureMood.Content;
    }

    /// <summary>The per-second anger change from unmet needs and from a bed: three of DK2's six
    /// sources — <c>AngerNoLair</c> +100, <c>AngerNoPay</c> +60 (ADR-0079; being owed is the whole
    /// trigger, <see cref="PaydayRules.IsOwedWages"/>) and
    /// <c>ModifyAngerOfCreatureInLairPerSecond</c> −550. Elsewhere: <c>AngerNoFood</c> +100 in the
    /// prison only (ADR-0074), <c>AngerNoWork</c>, the loaded 12, on each failed pass in <see cref="Systems.CreatureJobSystem"/>,
    /// and <c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c> +150 every four seconds in a
    /// cell (<see cref="PrisonRules.CellAngerPerApplication"/>, ADR-0171). That variable's
    /// <i>other</i> reader, the mixed-alignment room rule, is still unbuilt.</summary>
    public static CreatureAnger PerSecond(
        WorldState world,
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        // A captive takes no need-anger at all: the original's need-anger routine refuses when the
        // current state carries bit 1 of the state record's flag byte, and all eleven "being held
        // captive" states carry it. A captive's anger is the prison's (ADR-0064, ADR-0074).
        CreatureAnger delta = default;
        if (PrisonRules.IsImprisoned(state)) return delta;
        // The need-anger routine 0x00478D00 returns at once for a follower (0x00478D1D): no lair,
        // food or pay charge. The in-lair calm below is another routine's and still applies.
        bool follower = state.FollowLeaderId != 0;

        // Additive (ADR-0079) and per bucket (ADR-0081): the original's need-anger routine
        // (FUN_00478d00) tests lair, food and pay in turn and charges each into its own counter, so
        // a bedless unpaid creature takes both and neither pays off the other.
        if (!follower && LairRules.NeedsBed(creature) && !LairRules.HasBed(world, database, state))
        {
            // A creature whose record gives it no lair object at all — the Imp, the Skeleton,
            // Horny — is not owed a bed and is not angered by its absence.
            delta.Lair += creature.AngerNoLair;
        }

        // Owed wages it has not collected.
        if (!follower && PaydayRules.IsOwedWages(database, creature, state))
        {
            delta.Pay += creature.AngerNoPay;
        }

        // And cooling pays off neither of them (ADR-0081): ModifyAngerOfCreatureInLairPerSecond
        // reaches bucket 5 and nothing else, where the charges above are buckets 2 and 4.
        // What empties those two is an event, not a rate: Forget, on a bed or a full wage.
        // Cooling is category-gated, not bed-gated — seven states including awake ones
        // (Q123, ADR-0171).
        if (LairRules.CoolsAngerInLair(state))
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

    /// <summary>A whole delta as one wrapper call per non-zero bucket in index order, each with the
    /// same complaint: the Casino (2), spectating (9).</summary>
    public static void Apply(
        WorldState world, CreatureDef creature, CreatureState state, CreatureAnger delta, AngerComplaint complaint)
    {
        for (int i = 0; i < CreatureAnger.Count; i++)
        {
            var bucket = (AngerBucket)i;
            if (delta[bucket] != 0) Apply(world, creature, state, bucket, delta[bucket], complaint);
        }
    }

    /// <summary>The same with a complaint per bucket, indexed by bucket: the Temple's six calls
    /// (<see cref="PrayingComplaints"/>) and the per-second needs (<see cref="NeedComplaints"/>).</summary>
    public static void Apply(
        WorldState world, CreatureDef creature, CreatureState state, CreatureAnger delta,
        IReadOnlyList<AngerComplaint> complaints)
    {
        for (int i = 0; i < CreatureAnger.Count; i++)
        {
            var bucket = (AngerBucket)i;
            if (delta[bucket] != 0) Apply(world, creature, state, bucket, delta[bucket], complaints[i]);
        }
    }

    /// <summary>The Temple hook <c>0x0047B970</c>: buckets 0 to 5 with complaints 9, 1, 0, 3, 2, 9.</summary>
    public static readonly IReadOnlyList<AngerComplaint> PrayingComplaints =
    [
        AngerComplaint.General, AngerComplaint.NoFood, AngerComplaint.NoLair,
        AngerComplaint.NoWork, AngerComplaint.Money, AngerComplaint.General,
    ];

    /// <summary><see cref="PerSecond"/>'s sources: the need-anger routine <c>0x00478D00</c> charges
    /// Lair with 0, food with 1 and pay with 2, and the in-lair calm <c>0x004DFF00</c> passes 0.
    /// Buckets 0 and 3 take no per-second charge; their entries are never read.</summary>
    public static readonly IReadOnlyList<AngerComplaint> NeedComplaints =
    [
        AngerComplaint.General, AngerComplaint.NoFood, AngerComplaint.NoLair,
        AngerComplaint.NoWork, AngerComplaint.Money, AngerComplaint.NoLair,
    ];

    /// <summary>Whether this creature can be angered at all — one flag read from the record. False
    /// for the Skeleton and Jack, the two carrying <see cref="CreatureFlags2.NeverAngered"/>, whose
    /// doc has the trap: Jack's anger values are populated and DK2 reads none of them, so deriving
    /// the exempt set from the values made him calmable by slapping (Q210, ADR-0087). The Imp, the
    /// Dwarf and Horny are unflagged and carry 1 against a Goblin's 100 — a lairless Imp takes nine hours.</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-anger.md")]
    public static bool CanBeAngered(CreatureDef creature) =>
        !creature.Flags2.HasFlag(CreatureFlags2.NeverAngered);

    /// <summary>A slap: <c>AngerSlap</c>, +3000 for a Goblin and <b>−5000 for Jack</b>, whose whole
    /// record runs backwards. Called by <see cref="Systems.HandSystem"/> (ADR-0062). Six slaps make
    /// a content creature sulk and 11 make it leave at 3000 — CONFIRMED by the owner's Salamander
    /// (Q114). No cooldown: <see cref="HandRules.KnockBackWindowSeconds"/>.</summary>
    public static int Slap(CreatureDef creature, CreatureState state) =>
        Apply(creature, state, AngerBucket.Treatment, creature.AngerSlap);

    public static int Slap(WorldState world, CreatureDef creature, CreatureState state) =>
        Apply(world, creature, state, AngerBucket.Treatment, creature.AngerSlap, AngerComplaint.Slapped);

    /// <summary>Being held in the Hand of Evil: <c>AngerInHand</c>, which <b>calms</b> most
    /// creatures (−250 for a Goblin, −500 for a Warlock) and annoys Jack (+500). A rate, not a
    /// one-shot (ADR-0062): once a game second, from twenty seconds after the pick-up
    /// (<see cref="HandRules.AngerDueThisTick"/>), so parking a creature in the hand calms it.</summary>
    public static int Held(CreatureDef creature, CreatureState state) =>
        Apply(creature, state, AngerBucket.Treatment, creature.AngerInHand);

    public static int Held(WorldState world, CreatureDef creature, CreatureState state) =>
        Apply(world, creature, state, AngerBucket.Treatment, creature.AngerInHand, AngerComplaint.Held);

    /// <summary>How long an angry creature must wait between selections that came to something:
    /// <b>five game seconds</b>, the original's <c>5 x</c> variable 19 (Q122, ADR-0061). What the
    /// original expresses is five game <i>seconds</i>, so that is what this is and the tick rate
    /// does not change it.</summary>
    public const int ReselectionGateTicks = 5 * SimulationTiming.TicksPerSecond;

    /// <summary>The roll an angry creature makes: <c>rand(100)</c>, and it is <b>0..99</b> rather
    /// than 1..100 — the original's random helper ends in <c>% param</c>, so the top of the last
    /// cumulative range is unreachable when a table's chances sum to 100
    /// (<see cref="CumulativeRanges"/>).</summary>
    public static int RollPercent(SimRandom rng) => rng.NextInt(100);

    /// <summary>A job table as the original's choosers build it: a running total per entry, in
    /// <b>record order</b>, with <b>inclusive</b> bounds at both ends. The inclusive bounds are a
    /// reproduced quirk — a 50/50 table becomes <c>[0,50]</c> and <c>[50,100]</c> and the caller
    /// takes the first range holding the roll, so entry 0 gets 51 rolls of 100 and entry 1 gets 49,
    /// while <see cref="RollPercent"/> stopping at 99 makes the last upper bound unreachable. Record
    /// order survives because the original's pre-roll sort never returns negative on an ascending array; zero-chance entries are kept because filtering would move every other boundary.</summary>
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
