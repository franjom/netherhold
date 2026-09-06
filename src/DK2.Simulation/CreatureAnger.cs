// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>
/// Which of a creature's six anger counters a change lands in.
///
/// <para>The indices are DK2's own, read off the call sites of its anger-add
/// routine (<c>FUN_00478b80</c> → <c>FUN_00487030</c>, 2026-09-06). Every one
/// of the sixty sites in the executable passes one of these six as its first
/// argument, and the routine uses it to subscript an <c>int[6]</c>.</para>
///
/// <para><b>Four of the six are certain</b>, because exactly one creature-record
/// field feeds each and the field names them: <c>angerNoFood</c> → 1,
/// <c>angerNoLair</c> → 2, <c>angerNoWork</c> → 3, <c>angerNoPay</c> → 4.
/// Those four are the needs, and they are the four the need-anger routine
/// charges.</para>
///
/// <para><b>The other two names are ours and the taxonomy is UNKNOWN (Q202).</b>
/// What is read, and all that is read, is which sources reach which index:</para>
///
/// <list type="table">
/// <item><term><see cref="Treatment"/> (5)</term><description><c>angerSlap</c>,
/// <c>angerInHand</c>, <c>ModifyAngerOfCreatureInLairPerSecond</c>,
/// <c>ModifyAngerOfCombatPitVictor</c>, <c>GamblingModifyCreatureAngerMoney</c>
/// and <c>...Smiles</c>, and the torture sites. Handling, resting and
/// entertainment.</description></item>
/// <item><term><see cref="Company"/> (0)</term><description>
/// <c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c>, a share of the casino
/// and of combat-pit spectating, and — oddly — <c>angerNoWork</c> at one site
/// inside the combat pit. That last does not fit the name and is why the name
/// is marked as ours.</description></item>
/// </list>
///
/// <para>Praying is the only source that reaches all six: the Temple drains
/// <c>ModifyCreatureAngerWhilePrayingPerSecond</c> (−275) from every counter
/// once a second, in six consecutive calls.</para>
/// </summary>
public enum AngerBucket
{
    /// <summary>Index 0. Name ours; see the enum summary (Q202).</summary>
    Company = 0,

    /// <summary>Index 1. <c>angerNoFood</c>, and nothing else feeds it.</summary>
    Hunger = 1,

    /// <summary>Index 2. <c>angerNoLair</c>, and nothing else feeds it.</summary>
    Lair = 2,

    /// <summary>Index 3. <c>angerNoWork</c>.</summary>
    Work = 3,

    /// <summary>Index 4. <c>angerNoPay</c>, and nothing else feeds it.</summary>
    Pay = 4,

    /// <summary>Index 5. Name ours; see the enum summary (Q202).</summary>
    Treatment = 5,
}

/// <summary>
/// A creature's anger: <b>six independent counters, not one number</b>, and the
/// mood is decided by the <b>largest</b> of them rather than by their sum.
///
/// <para><b>Read from the original on 2026-09-06 (ADR-0080).</b> Its anger-add
/// routine takes a bucket index and an amount, adds, clamps the bucket to
/// <c>[0, 65533]</c>, then walks all six to find the maximum and sets the mood
/// band from that one value. Nothing anywhere sums them.</para>
///
/// <para><b>Why that is worth a struct rather than a comment.</b> The two
/// models disagree wherever a creature has more than one grievance, and they
/// disagree in both directions:</para>
///
/// <list type="bullet">
/// <item>Two half-grievances do <b>not</b> add up to desertion. A creature at
/// 20 000 hungry and 20 000 unpaid is <i>unhappy</i> here and would have been
/// <i>angry</i> under a single counter.</item>
/// <item>A kindness in one bucket does <b>not</b> pay off a grievance in
/// another. A bed drains <see cref="AngerBucket.Treatment"/> at 550 a second
/// and an unpaid wage fills <see cref="AngerBucket.Pay"/> at 60; under one
/// counter the bed wins nine to one and the creature never leaves, and under
/// six the bed does not touch the wage at all and the creature leaves in nine
/// minutes. <b>That correction retires the arithmetic ADR-0079 recorded</b>,
/// which was right about the two numbers and wrong about what they do to each
/// other.</item>
/// </list>
///
/// <para>A struct with six named fields rather than an array, so that every
/// call site has to say <i>which</i> grievance it is talking about: "add anger"
/// is no longer a sentence this simulation can express, which is the point.</para>
/// </summary>
public struct CreatureAnger : IEquatable<CreatureAnger>
{
    /// <summary>How many buckets there are. Six, and the original walks 0..5.</summary>
    public const int Count = 6;

    /// <summary>Index 0 — <see cref="AngerBucket.Company"/>.</summary>
    public int Company;

    /// <summary>Index 1 — <see cref="AngerBucket.Hunger"/>.</summary>
    public int Hunger;

    /// <summary>Index 2 — <see cref="AngerBucket.Lair"/>.</summary>
    public int Lair;

    /// <summary>Index 3 — <see cref="AngerBucket.Work"/>.</summary>
    public int Work;

    /// <summary>Index 4 — <see cref="AngerBucket.Pay"/>.</summary>
    public int Pay;

    /// <summary>Index 5 — <see cref="AngerBucket.Treatment"/>.</summary>
    public int Treatment;

    /// <summary>One bucket by index, in the original's own order.</summary>
    public int this[AngerBucket bucket]
    {
        get => bucket switch
        {
            AngerBucket.Company => Company,
            AngerBucket.Hunger => Hunger,
            AngerBucket.Lair => Lair,
            AngerBucket.Work => Work,
            AngerBucket.Pay => Pay,
            AngerBucket.Treatment => Treatment,
            _ => throw new ArgumentOutOfRangeException(nameof(bucket)),
        };
        set
        {
            switch (bucket)
            {
                case AngerBucket.Company: Company = value; break;
                case AngerBucket.Hunger: Hunger = value; break;
                case AngerBucket.Lair: Lair = value; break;
                case AngerBucket.Work: Work = value; break;
                case AngerBucket.Pay: Pay = value; break;
                case AngerBucket.Treatment: Treatment = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(bucket));
            }
        }
    }

    /// <summary>
    /// The largest of the six — <b>the only number the mood is read from</b>.
    ///
    /// <para>The original's loop starts at bucket 0 and takes a later bucket
    /// only on a strict <c>&gt;</c>, so ties resolve to the lower index. That
    /// matters for nothing here, because the value is what is used and not the
    /// index, but it is the original's order and this keeps it.</para>
    /// </summary>
    public int Max
    {
        get
        {
            int most = Company;
            if (Hunger > most) most = Hunger;
            if (Lair > most) most = Lair;
            if (Work > most) most = Work;
            if (Pay > most) most = Pay;
            if (Treatment > most) most = Treatment;
            return most;
        }
    }

    /// <summary>Every bucket at zero — a creature with nothing to complain about.</summary>
    public bool IsCalm =>
        (Company | Hunger | Lair | Work | Pay | Treatment) == 0;

    /// <summary>
    /// The bucket the mood is currently coming from, which is what a report
    /// wants to name: <i>why</i> this creature is cross, not merely how much.
    /// Ties go to the lower index, as the original's scan does.
    /// </summary>
    public AngerBucket Worst
    {
        get
        {
            AngerBucket worst = AngerBucket.Company;
            int most = Company;
            if (Hunger > most) { most = Hunger; worst = AngerBucket.Hunger; }
            if (Lair > most) { most = Lair; worst = AngerBucket.Lair; }
            if (Work > most) { most = Work; worst = AngerBucket.Work; }
            if (Pay > most) { most = Pay; worst = AngerBucket.Pay; }
            if (Treatment > most) worst = AngerBucket.Treatment;
            return worst;
        }
    }

    public void Hash(ref Hash64 hash)
    {
        hash.Add(Company);
        hash.Add(Hunger);
        hash.Add(Lair);
        hash.Add(Work);
        hash.Add(Pay);
        hash.Add(Treatment);
    }

    public bool Equals(CreatureAnger other) =>
        Company == other.Company && Hunger == other.Hunger && Lair == other.Lair
        && Work == other.Work && Pay == other.Pay && Treatment == other.Treatment;

    public override bool Equals(object? obj) => obj is CreatureAnger other && Equals(other);

    public override int GetHashCode() =>
        (((((Company * 397) ^ Hunger) * 397 ^ Lair) * 397 ^ Work) * 397 ^ Pay) * 397 ^ Treatment;

    public override string ToString() =>
        $"company={Company} hunger={Hunger} lair={Lair} " +
        $"work={Work} pay={Pay} treatment={Treatment}";
}
