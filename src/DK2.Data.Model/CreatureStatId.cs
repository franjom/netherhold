// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>Which creature stat a <see cref="CreatureStatEntry"/> row scales: the ids in
/// <c>GlobalVariables.kwd</c>'s <c>CreatureStats</c> rows, each a <b>percentage of the level-1
/// value</b> for levels 2 to 10 — 32 stats x 9 levels (Q119).
/// <para>Names are OpenKeeper's <c>Variable.CreatureStats.StatType</c> (ADR-0003 amendment 2,
/// attribution in <c>THIRD-PARTY-NOTICES.md</c>), corroborated by the shipped column's shape. Only
/// the stats this project can apply are named; ten of the 32 are 100 % at every level, so a missing
/// name is not a gap in the data.</para></summary>
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
    /// hunger period adds to <c>HungerNeed</c> and the level at which a creature looks for a bird.
    /// 100 % at every level shipped, so it scales nothing; applied anyway (eating.md).</summary>
    HungerFill = 9,

    /// <summary>Experience needed for the next level: 130 % at level 2 rising to <b>1000 %</b> at
    /// 10, against the record's own <c>ExpForNextLevel</c> (3000 for all but the uniques' 2800).</summary>
    ExperienceForNextLevel = 13,

    /// <summary>Scales the record's <c>ExpPerSecond</c> — <b>combat</b> experience, above level 1
    /// (<c>0x0047A870</c>, ADR-0366). This name and
    /// <see cref="ExpPerSecondTraining"/>'s are ours, read off the binary by position and pinned
    /// from both sides against <see cref="ResearchPerSecond"/> and
    /// <see cref="ManufacturePerSecond"/> (docs/research/creature-levels.md).</summary>
    ExpPerSecond = 15,

    /// <summary>Scales the record's <c>ExpPerSecondTraining</c> — the training room's rate.
    /// <b>100 % at every level in the shipped table</b>, so it scales nothing today; applied
    /// anyway, because the original applies it and a column of hundreds is data, not an
    /// absence.</summary>
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
