// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// A decoded KWD record that retains its exact source bytes. The retained image
/// is deliberate: editor padding and still-unknown fields must round-trip
/// without being normalised or silently dropped.
/// </summary>
public abstract record KwdRoundTripRecord
{
    private readonly byte[] _sourceBytes;

    protected KwdRoundTripRecord(byte[] sourceBytes) =>
        _sourceBytes = sourceBytes?.ToArray() ?? throw new ArgumentNullException(nameof(sourceBytes));

    public int ConsumedLength => _sourceBytes.Length;
    public byte[] ToBytes() => _sourceBytes.ToArray();
}

/// <summary>An 84-byte art-resource descriptor embedded in definition records.</summary>
public sealed record KwdArtResource(
    string Name,
    uint Flags,
    ReadOnlyMemory<byte> Unknown_0044,
    byte Type,
    byte Unknown_0051,
    byte Unknown_0052,
    byte Unknown_0053)
{
    public ProceduralArtKind ProceduralKind => Type == 7 && Unknown_0044.Length >= 4
        ? (ProceduralArtKind)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(Unknown_0044.Span)
        : ProceduralArtKind.None;
}

public enum ProceduralArtKind : uint
{
    None = 0, ScrollingGrid = 1, Lightning = 2, SelectionCage = 3,
    IceRing = 4, DoubleIceRing = 6, TripleLightning = 7, FixedLightning = 8,
}

/// <summary>A 24-byte light descriptor embedded in SHOTS and EFFECTS.</summary>
public sealed record KwdLight(
    int X, int Y, int Z, int Radius, uint Flags,
    byte Red, byte Green, byte Blue, byte Alpha);

public sealed record CreatureSpellDef : KwdRoundTripRecord
{
    public CreatureSpellDef(byte[] sourceBytes) : base(sourceBytes) { }
    public required string Name { get; init; }
    public required KwdArtResource EditorIcon { get; init; }
    public required KwdArtResource GuiIcon { get; init; }
    public uint ShotData1 { get; init; }
    public uint ShotData2 { get; init; }
    public int Range { get; init; }
    public uint Flags { get; init; }
    public ushort CombatPoints { get; init; }
    public ushort SoundEvent { get; init; }
    public ushort NameStringId { get; init; }
    public ushort TooltipStringId { get; init; }
    public ushort GeneralDescriptionStringId { get; init; }
    public ushort StrengthStringId { get; init; }
    public ushort WeaknessStringId { get; init; }
    public byte CreatureSpellId { get; init; }
    public byte ShotTypeId { get; init; }
    public byte AlternativeShotId { get; init; }
    public byte AlternativeRoomId { get; init; }
    public int RechargeTime { get; init; }
    public byte AlternativeShot { get; init; }
    public required ReadOnlyMemory<byte> Unknown_00EF { get; init; }
}

public sealed record ShotDef : KwdRoundTripRecord
{
    public ShotDef(byte[] sourceBytes) : base(sourceBytes) { }
    public required string Name { get; init; }
    public required KwdArtResource MeshResource { get; init; }
    public required KwdLight Light { get; init; }
    public int AirFriction { get; init; }
    public int Mass { get; init; }
    public int Speed { get; init; }
    public uint Unknown_0098 { get; init; }
    public uint Unknown_009C { get; init; }
    public uint ShotProcessFlags { get; init; }
    public int Radius { get; init; }
    public uint Flags { get; init; }
    public ushort GeneralEffectId { get; init; } public ushort CreationEffectId { get; init; }
    public ushort DeathEffectId { get; init; } public ushort TimedEffectId { get; init; }
    public ushort HitSolidEffectId { get; init; } public ushort HitLavaEffectId { get; init; }
    public ushort HitWaterEffectId { get; init; } public ushort HitThingEffectId { get; init; }
    public ushort Health { get; init; }
    public byte ShotId { get; init; }
    public byte DeathShotId { get; init; }
    public byte TimedDelay { get; init; }
    public byte HitSolidShotId { get; init; }
    public byte HitLavaShotId { get; init; }
    public byte HitWaterShotId { get; init; }
    public byte HitThingShotId { get; init; }
    public byte DamageType { get; init; }
    public byte CollideType { get; init; }
    public byte ProcessType { get; init; }
    public byte AttackCategory { get; init; }
    public required string SoundCategory { get; init; }
    public ushort Threat { get; init; }
    public int BurnDuration { get; init; }
}

public sealed record EffectDef : KwdRoundTripRecord
{
    public EffectDef(byte[] sourceBytes) : base(sourceBytes) { }
    public required string Name { get; init; }
    public required KwdArtResource ArtResource { get; init; }
    public required KwdLight Light { get; init; }
    public int Mass { get; init; } public int AirFriction { get; init; } public int Elasticity { get; init; }
    public int Radius { get; init; } public int MinSpeedXy { get; init; } public int MaxSpeedXy { get; init; }
    public int MinSpeedYz { get; init; } public int MaxSpeedYz { get; init; }
    public int MinScale { get; init; } public int MaxScale { get; init; }
    public uint Flags { get; init; }
    public ushort EffectId { get; init; }
    public ushort MinHp { get; init; } public ushort MaxHp { get; init; }
    public ushort FadeDuration { get; init; } public ushort NextEffectId { get; init; }
    public ushort DeathEffectId { get; init; } public ushort HitSolidEffectId { get; init; }
    public ushort HitWaterEffectId { get; init; } public ushort HitLavaEffectId { get; init; }
    public required IReadOnlyList<ushort> GenerateIds { get; init; }
    public ushort OuterOriginRange { get; init; } public ushort LowerHeightLimit { get; init; }
    public ushort UpperHeightLimit { get; init; } public ushort OrientationRange { get; init; }
    public ushort SpriteSpinRateRange { get; init; } public ushort WhirlpoolRate { get; init; }
    public ushort DirectionalSpread { get; init; } public ushort CircularPathRate { get; init; }
    public ushort InnerOriginRange { get; init; } public ushort GenerateRandomness { get; init; }
    public ushort Unknown_00EE { get; init; } public ushort Unknown_00F0 { get; init; }
    public byte GenerationType { get; init; }
    public byte ElementsPerTurn { get; init; }
    public ushort Unknown_00F4 { get; init; }
}

public sealed record EffectElementDef : KwdRoundTripRecord
{
    public EffectElementDef(byte[] sourceBytes) : base(sourceBytes) { }
    public required string Name { get; init; }
    public required KwdArtResource ArtResource { get; init; }
    public int Mass { get; init; } public int AirFriction { get; init; } public int Elasticity { get; init; }
    public int MinSpeedXy { get; init; } public int MaxSpeedXy { get; init; }
    public int MinSpeedYz { get; init; } public int MaxSpeedYz { get; init; }
    public int MinScale { get; init; } public int MaxScale { get; init; } public int ScaleRatio { get; init; }
    public uint Flags { get; init; }
    public ushort EffectElementId { get; init; }
    public ushort MinHp { get; init; } public ushort MaxHp { get; init; }
    public ushort DeathElementId { get; init; } public ushort HitSolidElementId { get; init; }
    public ushort HitWaterElementId { get; init; } public ushort HitLavaElementId { get; init; }
    public byte Red { get; init; }
    public byte Green { get; init; }
    public byte Blue { get; init; }
    public byte RandomColorIndex { get; init; }
    public byte TableColorIndex { get; init; }
    public byte FadePercentage { get; init; }
    public ushort NextEffectId { get; init; }
}

public abstract record ThingRecord : KwdRoundTripRecord
{
    protected ThingRecord(int tag, byte[] sourceBytes) : base(sourceBytes) => Tag = tag;
    public int Tag { get; }
}

public sealed record ObjectThing : ThingRecord
{
    public ObjectThing(byte[] bytes) : base(194, bytes) { }
    public int X { get; init; } public int Y { get; init; }
    public required ReadOnlyMemory<byte> Unknown_0008 { get; init; }
    public int KeeperSpellId { get; init; } public int MoneyAmount { get; init; }
    public ushort TriggerId { get; init; } public byte ObjectId { get; init; } public byte PlayerId { get; init; }
}

public sealed record TrapThing : ThingRecord
{
    public TrapThing(byte[] bytes) : base(195, bytes) { }
    public int X { get; init; } public int Y { get; init; } public int Unknown_0008 { get; init; }
    public byte NumberOfShots { get; init; } public byte TrapId { get; init; }
    public byte PlayerId { get; init; } public byte Unknown_000F { get; init; }
}

public sealed record DoorThing : ThingRecord
{
    public DoorThing(byte[] bytes) : base(196, bytes) { }
    public int X { get; init; } public int Y { get; init; } public int Unknown_0008 { get; init; }
    public ushort TriggerId { get; init; } public byte DoorId { get; init; } public byte PlayerId { get; init; }
    public byte Flags { get; init; } public required ReadOnlyMemory<byte> Unknown_0011 { get; init; }
}

public sealed record ActionPointThing : ThingRecord
{
    public ActionPointThing(byte[] bytes) : base(197, bytes) { }
    public int StartX { get; init; } public int StartY { get; init; }
    public int EndX { get; init; } public int EndY { get; init; }
    public ushort WaitDelay { get; init; } public ushort Flags { get; init; }
    public ushort TriggerId { get; init; } public byte ActionPointId { get; init; }
    public byte NextWaypointId { get; init; } public required string Name { get; init; }
}

public abstract record CreatureThing : ThingRecord
{
    protected CreatureThing(int tag, byte[] bytes) : base(tag, bytes) { }
    public int X { get; init; } public int Y { get; init; } public int Z { get; init; }
    public ushort GoldHeld { get; init; } public byte Level { get; init; } public byte Flags { get; init; }
}

public sealed record NeutralCreatureThing : CreatureThing
{
    public NeutralCreatureThing(byte[] bytes) : base(198, bytes) { }
    public int InitialHealth { get; init; } public ushort TriggerId { get; init; }
    public byte CreatureId { get; init; } public byte Unknown_0017 { get; init; }
}

public sealed record GoodCreatureThing : CreatureThing
{
    public GoodCreatureThing(byte[] bytes) : base(199, bytes) { }
    public int ObjectiveTargetActionPointId { get; init; } public int InitialHealth { get; init; }
    public ushort TriggerId { get; init; } public byte ObjectiveTargetPlayerId { get; init; }
    public byte Objective { get; init; } public byte CreatureId { get; init; }
    public byte WanderRadius { get; init; } public byte PartyId { get; init; } public byte Flags2 { get; init; }
}

public sealed record KeeperCreatureThing : CreatureThing
{
    public KeeperCreatureThing(byte[] bytes) : base(200, bytes) { }
    public int InitialHealth { get; init; } public int ObjectiveTargetActionPointId { get; init; }
    public ushort TriggerId { get; init; } public byte CreatureId { get; init; } public byte PlayerId { get; init; }
}

public sealed record HeroPartyThing : ThingRecord
{
    public HeroPartyThing(byte[] bytes) : base(201, bytes) { }
    public required string Name { get; init; } public ushort TriggerId { get; init; }
    public byte PartyId { get; init; } public int Unknown_0023 { get; init; } public int Unknown_0027 { get; init; }
    public required IReadOnlyList<GoodCreatureThing> Members { get; init; }
}

public sealed record DeadBodyThing : ThingRecord
{
    public DeadBodyThing(byte[] bytes) : base(202, bytes) { }
    public int X { get; init; } public int Y { get; init; } public int Z { get; init; }
    public ushort GoldHeld { get; init; } public byte CreatureId { get; init; } public byte PlayerId { get; init; }
}

public sealed record EffectGeneratorThing : ThingRecord
{
    public EffectGeneratorThing(byte[] bytes) : base(203, bytes) { }
    public int X { get; init; } public int Y { get; init; }
    public int Unknown_0008 { get; init; } public int Unknown_000C { get; init; }
    public ushort Unknown_0010 { get; init; } public ushort Unknown_0012 { get; init; }
    public required IReadOnlyList<ushort> EffectIds { get; init; }
    public byte Frequency { get; init; } public byte GeneratorId { get; init; }
    public required ReadOnlyMemory<byte> Unknown_001E { get; init; }
}

public sealed record RoomThing : ThingRecord
{
    public RoomThing(byte[] bytes) : base(204, bytes) { }
    public int X { get; init; } public int Y { get; init; } public int Unknown_0008 { get; init; }
    public ushort Unknown_000C { get; init; } public byte Direction { get; init; } public byte Unknown_000F { get; init; }
    public ushort InitialHealth { get; init; } public byte RoomType { get; init; } public byte PlayerId { get; init; }
}

public sealed record CameraThing : ThingRecord
{
    public CameraThing(byte[] bytes) : base(205, bytes) { }
    public int X { get; init; } public int Y { get; init; } public int Z { get; init; }
    public int MinClipX { get; init; } public int MinClipY { get; init; } public int MinClipZ { get; init; }
    public int MaxClipX { get; init; } public int MaxClipY { get; init; } public int MaxClipZ { get; init; }
    public int ViewDistance { get; init; } public int MinViewDistance { get; init; } public int MaxViewDistance { get; init; }
    public int Zoom { get; init; } public int MinZoom { get; init; } public int MaxZoom { get; init; }
    public int Lens { get; init; } public int MinLens { get; init; } public int MaxLens { get; init; }
    public uint Flags { get; init; }
    public ushort AngleYaw { get; init; } public ushort AngleRoll { get; init; } public ushort AnglePitch { get; init; }
    public ushort CameraId { get; init; }
}

public abstract record TriggerRecord : KwdRoundTripRecord
{
    protected TriggerRecord(int tag, byte[] bytes) : base(bytes) => Tag = tag;
    public int Tag { get; }
    /// <summary>The type-dependent eight-byte union, retained byte-for-byte.</summary>
    public required ReadOnlyMemory<byte> Body { get; init; }
    public ushort TriggerId { get; init; }
    public ushort NextTriggerId { get; init; }
    public ushort ChildTriggerId { get; init; }
    public byte Type { get; init; }
    public byte RepeatCount { get; init; }
    /// <summary>Type-aware views over <see cref="Body"/>; see trigger-semantics.md for each name's provenance.</summary>
    public required IReadOnlyDictionary<string, long> Parameters { get; init; }
}

public sealed record GenericTriggerRecord : TriggerRecord
{
    public GenericTriggerRecord(byte[] bytes) : base(213, bytes) { }
    public byte BodyByte0 => Body.Span[0]; public byte BodyByte1 => Body.Span[1];
    public byte BodyByte2 => Body.Span[2]; public byte BodyByte3 => Body.Span[3];
    public uint BodyValue { get; init; }
}

public sealed record ActionTriggerRecord : TriggerRecord
{
    public ActionTriggerRecord(byte[] bytes) : base(214, bytes) { }
    public byte BodyByte0 => Body.Span[0]; public byte BodyByte1 => Body.Span[1];
    public byte BodyByte2 => Body.Span[2]; public byte BodyByte3 => Body.Span[3];
    public ushort BodyWord4 { get; init; } public ushort BodyWord6 { get; init; }
    public uint BodyDword0 { get; init; } public uint BodyDword4 { get; init; }
}

public sealed record LevelDef : KwdRoundTripRecord
{
    public LevelDef(byte[] bytes) : base(bytes) { }
    public required string Name { get; init; } public required string Description { get; init; }
    public required string Author { get; init; } public required string Email { get; init; }
    public required string Information { get; init; }
    public ushort TriggerId { get; init; } public ushort TicksPerSecond { get; init; }
    public required ReadOnlyMemory<byte> Unknown_1184 { get; init; }
    public required IReadOnlyList<string> Messages { get; init; }
    public ushort Flags { get; init; } public required string SoundCategory { get; init; }
    public byte TalismanPieces { get; init; }
    public required ReadOnlyMemory<byte> PreviousRewards { get; init; }
    public required ReadOnlyMemory<byte> NextRewards { get; init; }
    public byte SoundTrack { get; init; } public byte TextTableId { get; init; }
    public ushort TitleTextId { get; init; } public ushort PlotTextId { get; init; }
    public ushort DebriefTextId { get; init; } public ushort ObjectiveTextId { get; init; }
    public ushort Unknown_63B9 { get; init; }
    public ushort SubObjective1TextId { get; init; } public ushort SubObjective2TextId { get; init; }
    public ushort SubObjective3TextId { get; init; } public ushort SpecialLevelIndex { get; init; }
    public required ReadOnlyMemory<byte> IntroductionOverrideObjects { get; init; }
    public required IReadOnlyList<ushort> IntroductionOverrideTextIds { get; init; }
    public required string TerrainPath { get; init; }
    public required ReadOnlyMemory<byte> Unknown_6403 { get; init; }
    /// <summary>The chosen OneShotReaper level, or null when extended LEVEL metadata is absent.</summary>
    public byte? OneShotReaperLevel => Unknown_6403.IsEmpty ? null : Unknown_6403.Span[0];
    public required IReadOnlyList<KwdLevelFilePath> Paths { get; init; }
    public required IReadOnlyList<uint> UnknownPathTail { get; init; }
}

public sealed record KwdLevelFilePath(KwdRecordType RecordType, int Unknown_0004, string Path);
