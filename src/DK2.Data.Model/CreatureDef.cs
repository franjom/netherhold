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
/// <remarks>Mood and mana are signed 16-bit (<c>0x00492320</c>, Q11); no reader of either is known.</remarks>
public sealed record CreatureJobPreference(JobType Job, short MoodChange, short ManaChange, byte ChancePercent);

/// <summary>Secondary job option with mood/mana effects.</summary>
public sealed record CreatureJobAlternative(JobType Job, short MoodChange, short ManaChange);

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
    private byte _baseCreatureId;
    public required string Name { get; init; }
    public required byte CreatureId { get; init; }

    /// <summary>
    /// The ordinary creature archetype represented by a unique definition
    /// (CREATURES record offset <c>0xD3B</c>); zero for non-unique definitions.
    /// </summary>
    public byte UniqueBaseCreatureId { get => _baseCreatureId; init => _baseCreatureId = value; }

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

    /// <summary>Where a creature steps to as it finishes coming out of a portal, relative to the
    /// point it was made at, in 1/4096 tiles: record offset <c>0xE94</c> (x) and <c>0xE98</c> (y),
    /// the first of the eight runtime vectors at <c>+0x6EC</c> (loader <c>0x00493B37</c>) that
    /// state 193 adds (<c>0x00459C20</c>). Not <c>required</c>: zero leaves it where it was made.</summary>
    public int EntranceOffsetX { get; init; }

    /// <inheritdoc cref="EntranceOffsetX"/>
    public int EntranceOffsetY { get; init; }

    /// <summary>The Entrance animation's frame count and rate (animation block 30, the one
    /// action 49 plays while state 193 waits). Zero when the creature has none, as the Imp.</summary>
    public int EntranceFrames { get; init; }

    /// <inheritdoc cref="EntranceFrames"/>
    public int EntranceFramesPerSecond { get; init; }

    /// <summary>The Getup animation's frame count and rate (animation block 27, the one action 53
    /// plays while state 257 waits for it). Zero when the creature has none, as the Horny.</summary>
    public int GetUpFrames { get; init; }

    /// <inheritdoc cref="GetUpFrames"/>
    public int GetUpFramesPerSecond { get; init; }

    /// <summary>The Angry animation (block 9, the one action 41 plays): its file flags and its
    /// frame count and rate. Zero when the creature has none, as the Imp; the loader copies a block
    /// only when its resource is present (<c>0x00492320</c>, <c>0x00504D80</c>). Bit 0 of the flags
    /// is what <c>CreatureDef_HasArt(9)</c> tests in prison state 116, together with the type (Q188).</summary>
    public uint AngryArtFlags { get; init; }

    /// <summary>The Angry block's resource type (block +0x50, 1..8 when the loader keeps it, 0 for
    /// none). <c>0x00504D80</c> ORs <c>type != 0</c> into the runtime bit 0 that state 116 tests.</summary>
    public byte AngryArtType { get; init; }

    /// <inheritdoc cref="AngryArtFlags"/>
    public int AngryFrames { get; init; }

    /// <inheritdoc cref="AngryArtFlags"/>
    public int AngryFramesPerSecond { get; init; }

    public CreatureAnimationTiming KnockdownAnimation { get; init; }
    public CreatureAnimationTiming GetUpAnimation { get; init; }
    public CreatureAnimationTiming KnockdownFallbackAnimation { get; init; }
    public CreatureAnimationTiming GetUpFallbackAnimation { get; init; }
    public CreatureAnimationTiming ReaperSummonAnimation { get; init; }
    public CreatureAnimationTiming ReaperSummonFallbackAnimation { get; init; }

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

    /// <summary>File byte <c>0x0F4C</c>, runtime <c>+0x787</c>: summed over a player's creatures to
    /// pick the strongest player for a hero job with no target (<c>0x004BCB20</c>). Imp 10, Dwarf 15,
    /// King Reginald 125, Dark Angel 232. Its name is UNKNOWN; this is its only gameplay reader.</summary>
    public byte Unknown_0F4C { get; init; }
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

    /// <summary>CREATURES record offset <c>0x0FCB</c>, unnamed in the published field list
    /// (<c>unk_28</c>). Its one reader subtracts it from the treatment bucket when a dropped
    /// chicken or gold reaches the creature it was dropped on (hand-drop-earmark.md §8). Zero on
    /// all 46 shipped creatures; what it was meant to be called is Q433.</summary>
    public short DropDeliveryCalm { get; init; }

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
    /// <summary>The same source byte as UniqueBaseCreatureId (Q528); editing either view updates both.</summary>
    public required byte CloneCreatureId { get => _baseCreatureId; init => _baseCreatureId = value; }

    // String table references (localised text)
    public required ushort NameStringId { get; init; }
    public required ushort TooltipStringId { get; init; }
    public required ushort IntroductionStringId { get; init; }
}
