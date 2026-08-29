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

    // Movement / perception
    public required Fp32 Speed { get; init; }
    public required Fp32 RunSpeed { get; init; }
    public required Fp32 ShuffleSpeed { get; init; }
    public required Fp32 Height { get; init; }
    public required Fp32 EyeHeight { get; init; }
    public required Fp32 DistanceCanSee { get; init; }
    public required Fp32 DistanceCanHear { get; init; }
    public required Fp32 PerceptionRange { get; init; }

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
    public required ushort AngerNoLair { get; init; }
    public required ushort AngerNoFood { get; init; }
    public required ushort AngerNoPay { get; init; }
    public required ushort AngerNoWork { get; init; }
    public required ushort AngerSlap { get; init; }
    public required ushort AngerInHand { get; init; }
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
