// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>Which of a creature's six anger counters a change lands in.</summary>
/// <remarks>
/// <para>The indices are DK2's own, read off the sixty call sites of its
/// anger-add routine (2026-09-06), each of which passes one of these six to
/// subscript an <c>int[6]</c>. Four are certain because one creature-record
/// field feeds each and names it: <c>angerNoFood</c> → 1, <c>angerNoLair</c> →
/// 2, <c>angerNoWork</c> → 3, <c>angerNoPay</c> → 4.</para>
/// <para>The names of 0 and 5 are ours and the taxonomy is UNKNOWN (Q202); what
/// reaches each is in docs/research/creature-anger.md §"Anger is six counters".
/// Praying is the only source reaching all six, draining
/// <c>ModifyCreatureAngerWhilePrayingPerSecond</c> (−275) from each per second.</para>
/// </remarks>
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

/// <summary>The anger wrapper's third argument (<c>0x00478B80</c>), sent as a 17/18 message's
/// Data1 and indexing the creature's complaint speech; it is not the bucket. Values are read off
/// the wrapper's sixty call sites; the names are ours, after the sites that pass them
/// (computer-keeper-messages.md, the census). 6 is passed by sites not built here.</summary>
public enum AngerComplaint : byte
{
    /// <summary>The need-anger routine's Lair charge and the in-lair calm.</summary>
    NoLair = 0,

    /// <summary>The need-anger routine's food charge and the prisoner's failed search.</summary>
    NoFood = 1,

    /// <summary>The need-anger routine's pay charge, the Casino and a delivered drop.</summary>
    Money = 2,

    /// <summary>The happy chooser's failed pass, and a waiting pit fighter's work charge.</summary>
    NoWork = 3,

    /// <summary>The hand's slap.</summary>
    Slapped = 4,

    /// <summary>Being held in the hand (<c>0x00488DB2</c>).</summary>
    Held = 5,

    /// <summary>A room shared with the other alignment (<c>0x004E8C60</c>, ADR-0479).</summary>
    HatedCompany = 7,

    /// <summary>The Torture Chamber's act.</summary>
    Tortured = 8,

    /// <summary>Company in a cell, the Temple, spectating and a waiting pit fighter's company charge.</summary>
    General = 9,
}

/// <summary>
/// A creature's anger: <b>six independent counters, not one number</b>, banded
/// by the <b>largest</b> of them and never by their sum. The original's anger-add
/// routine adds to one bucket, clamps it to <c>[0, 65533]</c>, then walks all
/// six for the maximum; nothing sums them (ADR-0081).
/// <para>The two models disagree in both directions. A creature at 20 000 hungry
/// and 20 000 unpaid is unhappy here and angry under one counter; and a bed
/// draining <see cref="AngerBucket.Treatment"/> at 550 a second does not touch
/// an unpaid wage filling <see cref="AngerBucket.Pay"/> at 60, where one counter
/// would have the bed win nine to one. Six named fields rather than an array, so
/// "add anger" is not a sentence this simulation can express.</para>
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
    /// only on a strict <c>&gt;</c>, so ties resolve to the lower index, as
    /// <see cref="Worst"/> keeps.</para>
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
    /// Ties go to the lower index, as the original's scan does: <c>0x004871B0</c>,
    /// which the keeper's make-happy remedy dispatches on.
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
