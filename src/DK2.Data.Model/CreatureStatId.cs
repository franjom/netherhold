// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>Which creature stat a <see cref="CreatureStatEntry"/> row scales: the ids in
/// <c>GlobalVariables.kwd</c>'s <c>CreatureStats</c> rows, each a <b>percentage of the level-1
/// value</b> for levels 2 to 10 — 32 stats x 9 levels, parsed into
/// <see cref="GlobalVariables.CreatureStats"/> and read by nothing until Q119.
/// <para><b>Names are OpenKeeper's</b> <c>Variable.CreatureStats.StatType</c>, under ADR-0003's
/// second amendment with attribution in <c>THIRD-PARTY-NOTICES.md</c>, corroborated by the shape
/// of the shipped column: Health, MeleeDamage, Fear and Threat to 500-600 %, the two speeds to
/// 250 %, ExperienceForNextLevel to 1000 %. Only the stats this project can apply are named; ten
/// of the 32 are 100 % at every level, so a missing name is not a gap in the data.</para></summary>
public enum CreatureStatId
{
    /// <summary>Physical height. 100% at every level.</summary>
    HeightTiles = 0,

    /// <summary>Maximum health: 125% at level 2 rising to <b>600%</b> at 10.</summary>
    Health = 1,

    Fear = 2,
    Threat = 3,

    /// <summary>Melee damage: to <b>600%</b> at level 10.</summary>
    MeleeDamage = 4,

    /// <summary>Wages: to 250%. A level-10 creature costs two and a half times as much.</summary>
    Pay = 5,

    MaxGoldHeld = 6,

    /// <summary>How many chickens fill this creature up — in the original, both the amount one
    /// hunger period adds to <c>HungerNeed</c> and the level at which a creature goes looking for a
    /// bird. <b>100 % at every level in the shipped table</b>, so it scales nothing; named because
    /// <c>EatingRules</c> applies it, as the original's hunger routine does (docs/research/eating.md).</summary>
    HungerFill = 9,

    /// <summary>Experience needed for the next level: 130 % at level 2 rising to <b>1000 %</b> at
    /// 10, against the record's own <c>ExpForNextLevel</c> (3000 for all but the uniques' 2800).</summary>
    ExperienceForNextLevel = 13,

    /// <summary>Scales the record's <c>ExpPerSecond</c> — <b>combat</b> experience, the rate a
    /// creature earns while fighting. Unused: nothing in this simulation fights (Q205).
    /// <para><b>The name is ours, read off the binary by position rather than guessed.</b> DK2's
    /// per-tick hook for state category <b>22</b> (battle, fleeing, responding to a threat) reads
    /// the record's <c>ExpPerSecond</c> and scales it by the stat column at <c>+0x1dd</c>; the
    /// columns <c>0x1dd</c>, <c>0x1ef</c>, <c>0x201</c>, <c>0x213</c> are stats <b>15, 16, 17,
    /// 18</b> against a nine-level stride of 18 bytes. <c>0x201</c> and <c>0x213</c> are
    /// <see cref="ResearchPerSecond"/> and <see cref="ManufacturePerSecond"/>, whose category 4
    /// and 5 hooks read exactly the record fields those names predict, so the two unnamed columns
    /// are pinned from both sides — and the shipped table agrees: 15 and 16 are 100 % at every
    /// level while 17 and 18 share one curve (125 % at L2 to 700 % at L10).</para></summary>
    ExpPerSecond = 15,

    /// <summary>Scales the record's <c>ExpPerSecondTraining</c> — the training room's rate; see
    /// <see cref="ExpPerSecond"/> for how the two names were pinned. <b>100 % at every level in the
    /// shipped table</b>, so it scales nothing today; applied anyway, because the original applies
    /// it and a column of hundreds is data rather than an absence.</summary>
    ExpPerSecondTraining = 16,

    ResearchPerSecond = 17,
    ManufacturePerSecond = 18,
    DecomposeValue = 19,

    /// <summary>Walking speed: 105 % at level 2 rising in fives to 125 % at 6, then
    /// 150/175/200/<b>250 %</b> at 10.</summary>
    Speed = 21,

    /// <summary>Running speed, and it scales identically to <see cref="Speed"/>.</summary>
    RunSpeed = 22,

    TortureTimeToConvert = 24,
    PossessionManaCost = 27,

    /// <summary>Own-land regeneration. 100% at every level, so an Imp heals at 67 for ever.</summary>
    OwnLandHealthIncreasePerSecond = 28,

    DistanceCanHear = 30,
    MeleeRecharge = 31,
}
