// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// Which creature stat a <see cref="CreatureStatEntry"/> row scales.
///
/// <para><b>These are the ids in `GlobalVariables.kwd`'s `CreatureStats` rows</b>,
/// which carry a <b>percentage of the level-1 value</b> for levels 2 to 10. The
/// table has 32 stats x 9 levels and has been parsed into
/// <see cref="GlobalVariables.CreatureStats"/> since variables were first read —
/// and was looked at by nothing until 2026-09-04, when the owner remarked that
/// <i>"imps gain speed as they gain more levels"</i> (Q119).</para>
///
/// <para><b>Names are OpenKeeper's</b> <c>Variable.CreatureStats.StatType</c>,
/// used under ADR-0003's second amendment with attribution in
/// <c>THIRD-PARTY-NOTICES.md</c>. They are corroborated by the shape of the
/// shipped column in every case listed here: Health, MeleeDamage, Fear and
/// Threat rise to 500-600%, the two speeds rise to 250%, and
/// ExperienceForNextLevel to 1000%, which is what a levelling curve looks
/// like.</para>
///
/// <para>Only the stats this project can currently apply are named. The table
/// has 32 rows and ten of them are 100% at every level — a stat that never
/// scales — so a missing name here is not a gap in the data.</para>
/// </summary>
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

    /// <summary>
    /// How many chickens fill this creature up — and, in the original, both the
    /// amount one hunger period adds to <c>HungerNeed</c> and the level at which
    /// a creature goes looking for a bird. <b>100% at every level in the shipped
    /// table</b>, so it scales nothing; named because <c>EatingRules</c> applies
    /// it, and applying it is what the original's hunger routine does
    /// (<c>docs/research/eating.md</c>).
    /// </summary>
    HungerFill = 9,

    /// <summary>
    /// Experience needed for the next level: 130% at level 2 rising to
    /// <b>1000%</b> at 10, against the creature record's own
    /// <c>ExpForNextLevel</c> (3000 for all but the uniques' 2800).
    /// </summary>
    ExperienceForNextLevel = 13,

    ResearchPerSecond = 17,
    ManufacturePerSecond = 18,
    DecomposeValue = 19,

    /// <summary>Walking speed: 105% per level to level 5, then 150/175/200/<b>250%</b>.</summary>
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
