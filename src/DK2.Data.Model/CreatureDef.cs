// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Data.Model;

/// <summary>Room attraction: which room (and minimum size) lures this creature through the portal.</summary>
public sealed record CreatureAttraction(uint Present, ushort RoomId, ushort RoomSize);

/// <summary>A creature's innate spell slot.</summary>
public sealed record CreatureSpellSlot(byte CreatureSpellId, byte LevelAvailable, Fp32 ShotDelay, byte PlayAnimation);

/// <summary>Resistance against one attack type (percentage).</summary>
public sealed record CreatureResistance(AttackType AttackType, byte Value);

/// <summary>Weighted job choice used by the mood-driven job tables.</summary>
public sealed record CreatureJobPreference(JobType Job, ushort MoodChange, ushort ManaChange, byte ChancePercent);

/// <summary>Secondary job option with mood/mana effects.</summary>
public sealed record CreatureJobAlternative(JobType Job, ushort MoodChange, ushort ManaChange);

/// <summary>
/// Gameplay-relevant creature definition parsed from CREATURES(170) records.
/// Field layout CONFIRMED from ufdada/dk2-tools creatures_struct.bt, offsets
/// self-validated against the template's own anchors and the 5449-byte record
/// size of the reference Creatures.kwd (docs/research/kwd-creatures.md).
/// Art/animation resources are intentionally reduced to their names — asset
/// resolution goes through DK2.Assets later.
/// </summary>
public sealed record CreatureDef
{
    public required string Name { get; init; }
    public required byte CreatureId { get; init; }
    public required CreatureFlags Flags { get; init; }

    /// <summary>
    /// The second flag field (record offset 0x1545). Not <c>required</c>: it
    /// carries four bits, three of them unidentified, and the only one that
    /// decides behaviour today —
    /// <see cref="CreatureFlags2.AvailableViaHeroPortal"/> — is correct to
    /// default off, since a test fixture's invented creature is not something a
    /// Hero Portal generates.
    /// </summary>
    public CreatureFlags2 Flags2 { get; init; }

    // Movement / perception
    public required Fp32 Speed { get; init; }
    public required Fp32 RunSpeed { get; init; }
    public required Fp32 ShuffleSpeed { get; init; }
    public required Fp32 Height { get; init; }
    public required Fp32 EyeHeight { get; init; }
    public required Fp32 DistanceCanSee { get; init; }
    public required Fp32 DistanceCanHear { get; init; }
    /// <summary>
    /// Radius used to reveal fog, in fixed-point tile units. All 46 shipped
    /// creatures carry <c>0x6000</c>, exactly six tiles; DKII.EXE reads the
    /// field and compares squared distance to tile centres.
    /// </summary>
    public required Fp32 PerceptionRange { get; init; }

    /// <summary>The steering footprint in 1/4096 tile (KWD <c>0xEA0</c>): half of it, in 16.16,
    /// is the body's separation radius and the weight it adds to a crowd
    /// (docs/research/steering-behaviours.md §5.4).</summary>
    public required Fp32 FootprintRadius { get; init; }

    /// <summary>Size class 1..5 for the separation share table (KWD <c>0xEA8</c> shifted down twelve;
    /// docs/research/navigation-and-steering.md §3.2).</summary>
    public required int SizeClass { get; init; }

    /// <summary>Turn rate in 1/2048 of a turn per game second (KWD <c>0xF20</c>); the loader divides it
    /// by the tick rate, floored at one (docs/research/steering-behaviours.md §5.3).</summary>
    public required ushort TurnRate { get; init; }

    // Vitals / needs
    public required ushort Hp { get; init; }
    public required ushort HpFromChicken { get; init; }
    public required Fp32 HungerRate { get; init; }
    public required byte HungerFill { get; init; }
    public required int TimeAwake { get; init; }
    public required int TimeSleep { get; init; }
    public required short OwnLandHealthIncreasePerSecond { get; init; }
    public required ushort DecomposeValue { get; init; }

    // Combat
    public required ushort Fear { get; init; }
    public required ushort Threat { get; init; }
    public required ushort MeleeDamage { get; init; }
    public required ushort SlapDamage { get; init; }
    public required Fp32 MeleeRange { get; init; }
    public required Fp32 MeleeRecharge { get; init; }

    /// <summary>File <c>0xCF9</c>, seconds: how long a melee swing winds up before the blow lands,
    /// the interval the swing's own gate waits on (combat-cadence.md §1, <c>def+0x5CA</c>). Our
    /// name; the shipped one is Q336. Zero on the Imp and every unique record.</summary>
    public required Fp32 MeleeHitDelay { get; init; }
    public required AttackType MeleeAttackType { get; init; }
    public required Fp32 StunDuration { get; init; }
    public required FightStyle FightStyle { get; init; }
    public required IReadOnlyList<CreatureResistance> Resistances { get; init; }
    public required IReadOnlyList<CreatureSpellSlot> Spells { get; init; }

    // Economy / work
    public required ushort Pay { get; init; }
    public required ushort MaxGoldHeld { get; init; }
    public required ushort InitialGoldHeld { get; init; }
    public required ushort ExpForNextLevel { get; init; }
    public required ushort ExpPerSecond { get; init; }
    public required ushort ExpPerSecondTraining { get; init; }
    public required ushort ResearchPerSecond { get; init; }
    public required ushort ManufacturePerSecond { get; init; }
    public required ushort ManaGeneratedByPrayerPerSecond { get; init; }
    public required JobClass JobClass { get; init; }

    // Mood / anger thresholds
    public required byte UnhappyThreshold { get; init; }
    public required short AngerNoLair { get; init; }
    public required short AngerNoFood { get; init; }
    public required short AngerNoPay { get; init; }
    public required short AngerNoWork { get; init; }
    public required short AngerSlap { get; init; }
    public required short AngerInHand { get; init; }
    public required short TortureHpChange { get; init; }
    public required short TortureMoodChange { get; init; }
    public required Fp32 TortureTimeToConvert { get; init; }

    // Job selection tables
    public required IReadOnlyList<CreatureJobPreference> HappyJobs { get; init; }
    public required IReadOnlyList<CreatureJobPreference> UnhappyJobs { get; init; }
    public required IReadOnlyList<CreatureJobPreference> AngryJobs { get; init; }
    public required IReadOnlyList<JobType> HateJobs { get; init; }
    public required IReadOnlyList<CreatureJobAlternative> AlternativeJobs { get; init; }
    public required IReadOnlyList<CreatureAttraction> Attractions { get; init; }

    // Misc gameplay
    public required Fp32 GuardDuration { get; init; }
    public required Fp32 IdleDuration { get; init; }
    public required Fp32 SlapFearlessDuration { get; init; }
    public required short PossessionManaCost { get; init; }
    public required byte LairObjectId { get; init; }
    public required byte CloneCreatureId { get; init; }

    // String table references (localised text)
    public required ushort NameStringId { get; init; }
    public required ushort TooltipStringId { get; init; }
    public required ushort IntroductionStringId { get; init; }
}
