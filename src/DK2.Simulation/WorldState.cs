// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The mutable per-tile simulation state, distinct from <see cref="MapTile"/>,
/// which is the immutable file view. Terrain, owner and flags come straight from
/// the MAP record (CONFIRMED); health is seeded from
/// <see cref="TerrainDef.StartingHealth"/> and is what digging, claiming and
/// reinforcing modify (CONFIRMED, docs/research/kwd-terrain-rooms.md); gold held
/// is where a keeper's gold actually is, because every limit DK2 sets on gold is
/// per tile (<see cref="GoldStore"/>).
/// </summary>
public struct TileState
{
    public byte TerrainId;
    public byte OwnerPlayerId;

    /// <summary>
    /// The MAP record's third byte per tile, preserved verbatim — and, for a
    /// bridge, <b>the hazard the bridge spans</b>: 2 for lava, 1 for water. The
    /// byte is 1 on every tile of all 66 shipped maps except bridge tiles over
    /// lava, where it is 2, and the two sets never cross; a sold bridge reverts to
    /// the water or lava under it rather than to floor (owner, 2026-09-03). Q16,
    /// Q64; <see cref="BridgeRules"/> carries the evidence and the confidence.
    /// <para>Written by <see cref="RoomRules.Place"/> when a player builds a
    /// bridge, since the level file only speaks for bridges it shipped with.
    /// Whether the byte carries other meanings on other terrain is UNKNOWN;
    /// nothing reads it except the bridge rules.</para>
    /// </summary>
    public byte Flags;

    /// <summary>Current tile health. Seeded from the terrain type's starting health.</summary>
    public int Health;

    /// <summary>
    /// Gold physically stored on this tile. Non-zero only where
    /// <see cref="GoldStore.Capacity"/> is — a treasury or dungeon-heart tile. A
    /// player's spendable gold is the sum of this over the tiles they own; there
    /// is no player-level gold counter, on purpose (ADR-0015,
    /// <see cref="GoldStore"/>).
    /// </summary>
    public int GoldHeld;

    /// <summary>
    /// Which players have this tile tagged for digging: bit <c>n</c> is set for
    /// player id <c>n</c>. STRONGLY INFERRED that tagging is per player rather
    /// than global — DK2 is multiplayer and each keeper marks and sees their own
    /// dig orders, and the terrain table carries a per-type <c>Taggable</c> flag
    /// rather than a shared tag list. The bitmask is our representation, not a
    /// claim about the original's storage.
    /// </summary>
    public ushort TaggedBy;

    /// <summary>
    /// Keepers that have discovered this tile: bit <c>n</c> belongs to player
    /// id <c>n</c>. This is our per-keeper representation, not a claim about
    /// DK2's packing. DK2 persists its live tile fog bit in saved games, so the
    /// information is simulation state and must be hashed and snapshotted.
    /// </summary>
    public ushort DiscoveredBy;

    /// <summary>
    /// Whether this tile is floor that a worker <b>dug out</b> and nobody has
    /// claimed since — the tile an imp goes back for before anything else.
    /// <para>A dig leaving plain unowned path beside a keeper's floor raises a
    /// task that sits <b>first</b> in the imp's whole-map order, ahead of digging,
    /// and is taken as soon as none of the tile's eight neighbours still carries a
    /// dig task; so a corridor is dug through and claimed behind the digging
    /// front. STRONGLY INFERRED (Q154, docs/research/imp-task-priority.md §6 —
    /// rank and the eight-neighbour rule read, the trigger inferred).</para>
    /// <para>Set by <see cref="Systems.TileWorkSystem"/> when excavation destroys
    /// a tile; cleared by any later change of terrain, which is what a claim is.
    /// Our representation: the original keeps the task in the keeper's list.</para></summary>
    public bool FreshlyDug;

    /// <summary>
    /// Health owed to this tile that was too small to apply, carried between
    /// applications; always smaller in magnitude than
    /// <summary>
    /// Ticks left before this tile burns away, or 0 when nothing is burning.
    /// <para>A wooden bridge over lava does not last
    /// (<c>WoodBridgeLifeOnLavaSeconds</c> = 30) and <b>the clock starts when the
    /// bridge is built</b> — not when a creature steps on it, and not as damage to
    /// the tile's health. CONFIRMED (owner, 2026-09-03; Q67).</para>
    /// <para>A separate countdown rather than health decay, because health is what
    /// a rival attacking the bridge reduces and a bridge over water has the same
    /// 2000 health and never burns. Set by <see cref="RoomRules.Place"/>, spent by
    /// <see cref="Systems.BridgeBurnSystem"/>, and reset to zero the moment the
    /// tile stops being that bridge, however it stops.</para>
    /// </summary>
    public int BurnTicksRemaining;

    /// <summary>
    /// Ticks this portal has waited since it last produced a creature, or 0 on every
    /// tile that is not a portal's centre. Managed by <see cref="Systems.PortalSystem"/>.
    /// <para><b>Per portal</b>, CONFIRMED (owner, 2026-09-07): a second portal produced
    /// 8 seconds after the first where the interval was about 60, and one clock cannot
    /// emit twice in 8 s while waiting 60. It sits on the portal's centre tile, which
    /// <see cref="PortalRules.CanProduce"/> guarantees the portal owns, because
    /// <see cref="RoomInstance"/> is derived fresh from the tiles and remembers nothing.</para>
    /// <para><b>Counted up</b>, as DK2 does (<c>room+0x49</c> holds the tick of the last
    /// generation), so a portal blocked for want of a lair banks its wait and produces
    /// the moment the lair is built. It runs only while owned, so claiming an idle
    /// portal costs a full interval — UNKNOWN, Q86.</para></summary>
    public int EntranceIdleTicks;

    /// <summary>
    /// How many creatures this portal has generated, on the same tile as
    /// <see cref="EntranceIdleTicks"/> and zero elsewhere: the <c>n</c> in the arrival
    /// interval, climbing while the owner holds it. CONFIRMED (Q86, docs/research/portals.md).
    /// <para>DK2 keeps it at <c>room+0x4D</c>, increments it once per creature generated,
    /// and computes <c>(n² + EntranceGenerationSpeedSeconds)</c> game seconds at
    /// <c>0x004DBBE0</c>. Per portal, not per keeper: a second portal claimed after five
    /// arrivals started again at 25 s, not 50 (owner, 2026-09-07). It never decrements —
    /// of 129 references to a <c>+0x4D</c> field, none is a <c>DEC</c> or <c>SUB</c>, so
    /// a portal does not speed up when creatures die. An ownership change is the one
    /// thing that resets it: the room type's handler stamps the clock and zeros the tally
    /// (<c>ARivalTakingAWornPortalGetsItAtTheBaseInterval</c>).</para></summary>
    public int EntranceCreaturesGenerated;

    /// <summary>
    /// A hatchery's clock towards its next egg, on the room's lowest tile index
    /// and zero everywhere else. DK2 keeps it on the room — its room debug dump
    /// prints <c>GameTickLastGenerated</c>, <c>GenerationSpeed</c> and
    /// <c>ChickensGenerated</c> for the Hatchery case — but rooms here are derived
    /// rather than stored (<see cref="RoomInstance"/>), and the lowest tile index
    /// is stable because <c>RoomInstance.TileIndices</c> is kept ascending.
    /// <para>Deviation: a hatchery that grows or is cut so that its lowest tile
    /// changes starts its clock again, where the original's clock belongs to the
    /// room object and survives. Costs at most one laying interval, and only while
    /// a keeper is building.</para>
    /// </summary>
    public int HatchTicks;

    /// <summary>Highest player id that fits in <see cref="TaggedBy"/>.</summary>
    public const int MaxTaggingPlayerId = 15;

    public readonly bool IsTaggedBy(byte playerId) =>
        playerId <= MaxTaggingPlayerId && (TaggedBy & (ushort)(1 << playerId)) != 0;

    public readonly bool IsTaggedByAnyone => TaggedBy != 0;

    public readonly bool IsDiscoveredBy(byte playerId) =>
        playerId <= MaxTaggingPlayerId && (DiscoveredBy & (ushort)(1 << playerId)) != 0;

    public void SetDiscoveredBy(byte playerId)
    {
        if (playerId > MaxTaggingPlayerId)
            throw new ArgumentOutOfRangeException(
                nameof(playerId), playerId,
                $"Tile discovery only tracks player ids 0..{MaxTaggingPlayerId}.");

        DiscoveredBy |= (ushort)(1 << playerId);
    }

    public void SetTaggedBy(byte playerId, bool tagged)
    {
        if (playerId > MaxTaggingPlayerId)
            throw new ArgumentOutOfRangeException(
                nameof(playerId), playerId,
                $"Tile tags only track player ids 0..{MaxTaggingPlayerId}.");

        ushort bit = (ushort)(1 << playerId);
        TaggedBy = tagged ? (ushort)(TaggedBy | bit) : (ushort)(TaggedBy & ~bit);
    }

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(TerrainId);
        hash.Add(OwnerPlayerId);
        hash.Add(Flags);
        hash.Add(Health);
        hash.Add(GoldHeld);
        hash.Add(TaggedBy);
        hash.Add(DiscoveredBy);
        hash.Add(FreshlyDug ? (byte)1 : (byte)0);
        hash.Add(BurnTicksRemaining);
        hash.Add(EntranceIdleTicks);
        hash.Add(EntranceCreaturesGenerated);
        hash.Add(HatchTicks);
    }
}

/// <summary>
/// Per-player simulation state. <b>Mana is here and gold is not</b>, and the
/// asymmetry is the data's: mana is generated by owned land and spent from a
/// single pool (<c>MaximumManaThreshold</c>,
/// <c>ExcessManaDecreaseRatePerSecond</c>) — a player-level number with no
/// location — while gold is always in a place and every limit the balance table
/// sets on it is per tile, so it lives on tiles (<see cref="GoldStore"/>,
/// ADR-0015).
/// </summary>
public struct PlayerState
{
    public byte PlayerId;
    public int Mana;

    /// <summary>Gold this keeper's creatures have mined, DK2's <c>player+0x15A</c> —
    /// <c>GoldMined</c> in the level-statistics struct embedded at <c>+0x142</c>
    /// (`player-object.md`). Trigger condition type 31 compares it.</summary>
    /// <remarks>Counts gold <b>released</b> from the seam, not gold banked: DK2 credits it
    /// at <c>0x004973CF</c> before anything asks whether the imp could carry it, so a full
    /// imp still adds to the total. Only ever added to; nothing spends it.</remarks>
    public int GoldMined;

    /// <summary>The keeper that killed this one, DK2's <c>player+0x132</c> —
    /// <c>KilledByPlayerId</c> in its debug dumper. Zero means alive: condition type 47
    /// returns false outright on zero and otherwise compares it (ADR-0116).</summary>
    /// <remarks><b>Nothing sets it.</b> No keeper can be defeated here yet — a fallen heart
    /// is not even claimable (Q207) — so every keeper reads as alive, which is the answer
    /// DK2 gives for a living keeper too. DK2 stores the killer's <i>object</i> id and
    /// converts it through that object's <c>Number</c>; we store the player id, which is
    /// the same value only while <c>TagId</c> and <c>Number</c> agree (Q271).</remarks>
    public byte KilledByPlayerId;

    /// <summary>Slaps this keeper has been counted for, DK2's <c>player+0xEE6</c> —
    /// <c>NumberOfSlaps</c> in its debug dumper. Condition type 71 compares it
    /// (<c>0x004C8491</c>); both shipped records ask for "at least", 11 and 50.</summary>
    /// <remarks><b>Nothing sets it.</b> Slapping is a keeper's own act and no such act
    /// exists here yet, so zero is this simulation's true count rather than a stand-in —
    /// which is why the condition reads the field instead of answering a constant. Whether
    /// the tally counts slaps <i>given</i> or <i>received</i> is UNKNOWN (Q278); the
    /// dumper's name does not say and no writer has been read.</remarks>
    public int SlapCount;

    /// <summary>The creature this keeper is possessing, DK2's <c>player+0x134</c> —
    /// <c>CreaturePossessed</c>. Zero means not possessing, and condition type 73 returns
    /// <b>false outright</b> on zero without reading its comparison
    /// (<c>0x004C8500</c>).</summary>
    /// <remarks><b>Nothing sets it.</b> Possession is an interface act this project does
    /// not have, so every keeper reads as not possessing — the answer DK2 gives for a
    /// keeper who is not. What the arm compares when a keeper <i>is</i> possessing, the
    /// possessed creature's <c>+0x488</c>, is UNKNOWN (Q279), so type 73 declines rather
    /// than guesses the moment this stops being zero.</remarks>
    public int PossessedCreatureId;

    /// <summary>
    /// Research points banked by this keeper, DK2's <c>player+0x20F</c>. Only ever
    /// added to: the original keeps a <i>current target</i> beside it at
    /// <c>player+0x213</c> and completes the target when the points reach its cost,
    /// but this project has the accrual and not the tree
    /// (<see cref="ResearchRules"/>), so points bank indefinitely. Faithful for the
    /// case that matters, since the original credits them whether or not a target
    /// is selected — the field is written before the target is looked at.
    /// <para>Not implemented: spending. The original's adder has a subtract path
    /// (a negative credit is clamp-subtracted rather than allowed below zero) but
    /// nothing has been found that passes it a negative amount, so what that path
    /// is for is UNKNOWN (Q218).</para></summary>
    public int ResearchPoints;

    /// <summary>
    /// The earliest tick at which this keeper may be handed a free imp.
    /// <para><b>A deadline, not a countdown</b> — the whole of Q56. DK2 keeps one
    /// field on the keeper and writes it in exactly one place, immediately after a
    /// free imp is made, as <c>now + TimeBeforeFreeImpGeneratedSeconds</c>. Nothing
    /// resets it, pauses it, or touches it while the keeper is at full strength, so
    /// a keeper who has been at four imps for a minute has a deadline a minute in
    /// the past: the first tick they drop below <c>MinimumImpThreshold</c> the
    /// replacement arrives <b>at once</b>, and only the ones after it are five
    /// seconds apart. See <see cref="ImpSupplyRules"/> and
    /// docs/research/imps-and-mana.md.</para>
    /// </summary>
    public GameTick NextFreeImpTick;

    /// <summary>
    /// The tick this keeper's next payday falls due. A deadline, like
    /// <see cref="NextFreeImpTick"/>, and the original keeps it the same way: one
    /// field on the player, compared against now, rewritten as
    /// <c>now + PayDayFrequencySeconds × ticksPerSecond</c> the moment it fires.
    /// The first is seeded from <c>TimeBeforeFirstPayDaySeconds</c> rather than
    /// zero, so a level does not open with the whole dungeon owed money.
    /// <para>Deviation (ADR-0078): zero means "not scheduled yet" and
    /// <see cref="Systems.PaydaySystem"/> seeds it the first tick it sees one,
    /// where the original reads zero as <i>never</i> and seeds in an initialiser.
    /// <see cref="FromMap"/> has no <c>GameDatabase</c> to seed from, and a player
    /// slot with no creatures bills nobody.</para></summary>
    public GameTick NextPaydayTick;

    /// <summary>
    /// The Hand of Evil: the <b>things</b> this keeper is holding, in pick-up order.
    /// Things, not creatures (ADR-0075) — DK2 keeps one array of thing ids on the
    /// player record and branches on the thing's kind, so a creature and a chicken
    /// share the slots (the owner has watched four chickens held at once); our ids
    /// are separate spaces, so each slot carries which it is.
    /// <para><b>A stack</b>: the original appends at <c>NumberOfThingsInHand</c> and
    /// its drop handler reads back from the same index before decrementing, so A
    /// then B puts down B first. Capacity is <c>MaxNumberOfThingsInHand</c>, shipped
    /// at 64 (<see cref="HandRules"/>); the creature's half of the relation is
    /// <see cref="CreatureState.InHand"/> (ADR-0059). Never null —
    /// <see cref="WorldState"/> gives every keeper a list on creation.</para></summary>
    public List<HandItem> Hand;

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(PlayerId);
        hash.Add(Mana);
        hash.Add(GoldMined);
        hash.Add(KilledByPlayerId);
        hash.Add(SlapCount);
        hash.Add(PossessedCreatureId);
        hash.Add(ResearchPoints);
        hash.Add(NextFreeImpTick);
        hash.Add(NextPaydayTick);
        hash.Add(Hand.Count);
        foreach (HandItem held in Hand)
        {
            hash.Add((byte)held.Kind);
            hash.Add(held.Id);
        }
    }
}

/// <summary>What kind of thing a hand slot holds.</summary>
public enum HandItemKind : byte
{
    /// <summary>A creature — <see cref="CreatureState.InHand"/>.</summary>
    Creature = 0,

    /// <summary>A loose object — <see cref="ObjectState.InHand"/>.</summary>
    Object = 1,
}

/// <summary>
/// One slot of the Hand of Evil. DK2 needs no discriminator — creatures and
/// objects share one thing table and one id space, and its handlers branch on a
/// kind field inside the thing — but ours are separate lists with independent
/// ids, so the kind rides on the slot.
/// </summary>
public readonly record struct HandItem(HandItemKind Kind, int Id)
{
    public static HandItem Of(CreatureState creature) =>
        new(HandItemKind.Creature, creature.Id);

    public static HandItem Of(ObjectState thing) => new(HandItemKind.Object, thing.Id);

    public bool IsObject => Kind == HandItemKind.Object;
}

/// <summary>
/// One action point as DK2 constructs it from a placed THING. The rectangle, ids
/// and flags are level-authored data; the passage count and wait timer are
/// runtime fields and stay mutable even though their consumers are not yet
/// identified. There is deliberately no <c>Available</c> field — the four trigger
/// actions once thought to write one use their byte as input to separate
/// operations and never write the action-point object (ADR-0103).
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/action-points.md")]
public sealed class ActionPointState
{
    /// <summary>The five source flag bits DK2's loader copies: 0, 2, 3, 4 and 5.</summary>
    public const byte LoadedFlagMask = 0x3D;

    public byte Id { get; init; }
    public ushort StartX { get; init; }
    public ushort StartY { get; init; }
    public ushort EndX { get; init; }
    public ushort EndY { get; init; }
    public ushort TriggerId { get; init; }
    public byte NextWaypointId { get; init; }
    public byte Flags { get; init; }

    /// <summary>Runtime counter initialised to zero. Its modifying path remains Q236.</summary>
    public ushort PassageCount { get; set; }

    /// <summary>
    /// The authored wait delay multiplied by DK2's four-Hz tick rate at load.
    /// <para>Scales by the <b>original</b> rate while the trigger threshold beside
    /// it scales by ours; the two are equal today. Both reproduce the same loader
    /// idiom — one multiply by variable 19 at load — but this is a stored field
    /// mirroring the original's saved block, so it keeps the original's number
    /// whatever our step rate becomes, while
    /// <see cref="TriggerState.HasElapsed"/> builds a deadline against <i>our</i>
    /// clock and must scale by <see cref="SimulationTiming.TicksPerSecond"/>. If
    /// <c>TicksPerSecond</c> ever stops being 4, do not make one match the other:
    /// whatever decrements this counter has to say which clock it counts in.</para>
    /// </summary>
    public ushort WaitTimerTicks { get; set; }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(StartX);
        hash.Add(StartY);
        hash.Add(EndX);
        hash.Add(EndY);
        hash.Add(TriggerId);
        hash.Add(NextWaypointId);
        hash.Add(Flags);
        hash.Add(PassageCount);
        hash.Add(WaitTimerTicks);
    }
}

/// <summary>
/// One door projected from a placed THING. Authored bytes remain verbatim; the
/// construction predicate is the one runtime value currently derived from them.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/doors-and-traps.md")]
public sealed class DoorState
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Unknown_0008 { get; init; }
    public ushort TriggerId { get; init; }
    public byte DoorId { get; init; }
    public byte PlayerId { get; init; }
    public byte Flags { get; init; }
    public bool UnderConstruction { get; init; }
    /// <summary>How long <see cref="Unknown_0011"/> is. The snapshot writes those bytes
    /// without a length and reads back exactly this many, so a door carrying any other
    /// number would desync the stream and silently corrupt everything after it.</summary>
    public const int Unknown0011Length = 3;

    /// <summary>Bytes 17..19 of the record, kept together because nothing yet says they
    /// are separate fields (Q252).</summary>
    public required ReadOnlyMemory<byte> Unknown_0011
    {
        get => _unknown0011;
        init => _unknown0011 = value.Length == Unknown0011Length
            ? value
            : throw new ArgumentException(
                $"A door's trailing unknown is {Unknown0011Length} bytes, not {value.Length}.",
                nameof(Unknown_0011));
    }

    private readonly ReadOnlyMemory<byte> _unknown0011;

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(X);
        hash.Add(Y);
        hash.Add(Unknown_0008);
        hash.Add(TriggerId);
        hash.Add(DoorId);
        hash.Add(PlayerId);
        hash.Add(Flags);
        hash.Add(UnderConstruction);
        foreach (byte value in Unknown_0011.Span) hash.Add(value);
    }
}

/// <summary>
/// One trap projected from a placed THING. Authored bytes remain verbatim; the
/// construction predicate is the one runtime value currently derived from them.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/doors-and-traps.md")]
public sealed class TrapState
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Unknown_0008 { get; init; }
    public byte NumberOfShots { get; init; }
    public byte TrapId { get; init; }
    public byte PlayerId { get; init; }
    public byte Unknown_000F { get; init; }
    public bool UnderConstruction { get; init; }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(X);
        hash.Add(Y);
        hash.Add(Unknown_0008);
        hash.Add(NumberOfShots);
        hash.Add(TrapId);
        hash.Add(PlayerId);
        hash.Add(Unknown_000F);
        hash.Add(UnderConstruction);
    }
}

/// <summary>
/// One authored member of a hero party. Party members are templates for a
/// future spawn, not creatures currently placed in the level (ADR-0108).
/// Every parsed field is retained verbatim and no spawning behaviour exists.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-parties-and-effect-generators.md")]
public sealed class HeroPartyMemberState
{
    public const int Unknown001DLength = 2;

    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public ushort GoldHeld { get; init; }
    public byte Level { get; init; }
    public byte Flags { get; init; }
    public int ObjectiveTargetActionPointId { get; init; }
    public int InitialHealth { get; init; }
    public ushort TriggerId { get; init; }
    public byte ObjectiveTargetPlayerId { get; init; }
    public byte Objective { get; init; }
    public byte CreatureId { get; init; }
    public required ReadOnlyMemory<byte> Unknown_001D
    {
        get => _unknown001D;
        init => _unknown001D = value.Length == Unknown001DLength
            ? value
            : throw new ArgumentException(
                $"A hero-party member's trailing unknown is {Unknown001DLength} bytes, not {value.Length}.",
                nameof(Unknown_001D));
    }
    public byte Flags2 { get; init; }

    private readonly ReadOnlyMemory<byte> _unknown001D;

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(X); hash.Add(Y); hash.Add(Z); hash.Add(GoldHeld);
        hash.Add(Level); hash.Add(Flags); hash.Add(ObjectiveTargetActionPointId);
        hash.Add(InitialHealth); hash.Add(TriggerId); hash.Add(ObjectiveTargetPlayerId);
        hash.Add(Objective); hash.Add(CreatureId);
        foreach (byte value in Unknown_001D.Span) hash.Add(value);
        hash.Add(Flags2);
    }
}

/// <summary>
/// One hero party projected verbatim in THINGS-file order. It is authored
/// state only: parties do not spawn, and their members are not placed creatures.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-parties-and-effect-generators.md")]
public sealed class HeroPartyState
{
    public required string Name { get; init; }
    public ushort TriggerId { get; init; }
    public byte PartyId { get; init; }
    public int Unknown_0023 { get; init; }
    public int Unknown_0027 { get; init; }
    public required IReadOnlyList<HeroPartyMemberState> Members { get; init; }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Name); hash.Add(TriggerId); hash.Add(PartyId);
        hash.Add(Unknown_0023); hash.Add(Unknown_0027);
        hash.Add(Members.Count);
        foreach (HeroPartyMemberState member in Members) member.HashInto(ref hash);
    }
}

/// <summary>
/// One effect generator projected verbatim in THINGS-file order. Generation
/// behaviour is UNKNOWN and deliberately absent (ADR-0108).
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-parties-and-effect-generators.md")]
public sealed class EffectGeneratorState
{
    public const int Unknown001ELength = 6;

    public int X { get; init; }
    public int Y { get; init; }
    public int Unknown_0008 { get; init; }
    public int Unknown_000C { get; init; }
    public ushort Unknown_0010 { get; init; }
    public ushort Unknown_0012 { get; init; }
    public required IReadOnlyList<ushort> EffectIds { get; init; }
    public byte Frequency { get; init; }
    public byte GeneratorId { get; init; }
    public required ReadOnlyMemory<byte> Unknown_001E
    {
        get => _unknown001E;
        init => _unknown001E = value.Length == Unknown001ELength
            ? value
            : throw new ArgumentException(
                $"An effect generator's trailing unknown is {Unknown001ELength} bytes, not {value.Length}.",
                nameof(Unknown_001E));
    }

    private readonly ReadOnlyMemory<byte> _unknown001E;

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(X); hash.Add(Y); hash.Add(Unknown_0008); hash.Add(Unknown_000C);
        hash.Add(Unknown_0010); hash.Add(Unknown_0012);
        hash.Add(EffectIds.Count);
        foreach (ushort effectId in EffectIds) hash.Add(effectId);
        hash.Add(Frequency); hash.Add(GeneratorId);
        foreach (byte value in Unknown_001E.Span) hash.Add(value);
    }
}

/// <summary>
/// All mutable simulation state for one level. Owns no behaviour — systems act
/// on it. Everything reachable from here is hashed and snapshotted; anything
/// not reachable from here does not exist as far as determinism is concerned,
/// which is the invariant that keeps the gate meaningful.
/// </summary>
public sealed class WorldState
{
    private readonly TileState[] _tiles;
    private readonly PlayerState[] _players; // kept sorted by PlayerId, for stable hashing
    private readonly List<TileWorkOrder> _pendingWork = [];
    private readonly List<CreatureState> _creatures = [];
    private readonly List<ObjectState> _objects = [];
    private readonly List<ActionPointState> _actionPoints = [];
    private readonly List<DoorState> _doors = [];
    private readonly List<TrapState> _traps = [];
    private readonly List<HeroPartyState> _heroParties = [];
    private readonly List<EffectGeneratorState> _effectGenerators = [];
    private readonly List<TempleSacrificeState> _templeSacrifices = [];
    private int _nextCreatureId = 1;
    private int _nextObjectId = 1;

    private WorldState(
        int width, int height, TileState[] tiles, PlayerState[] players, bool fogOfWarEnabled)
    {
        Width = width;
        Height = height;
        _tiles = tiles;
        _players = players;
        FogOfWarEnabled = fogOfWarEnabled;

        // One place, so no construction path can produce a keeper without a hand:
        // PlayerState is a struct in an array, and a caller using an object
        // initialiser would have had to know to supply the list.
        for (int i = 0; i < _players.Length; i++)
            _players[i].Hand ??= [];

        CurrentTick = GameTick.Zero;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Whether player-specific discovery gates observation of this world.
    /// DKII.EXE stores this as a boolean game configuration value. The other
    /// setup-screen fog labels have not yet been tied to proven engine state.
    /// </summary>
    public bool FogOfWarEnabled { get; }

    /// <summary>The tick the world is currently *at*; the next Tick() executes this one.</summary>
    public GameTick CurrentTick { get; internal set; }

    public int TileCount => _tiles.Length;

    public ref TileState Tile(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return ref _tiles[(y * Width) + x];
    }

    public ref TileState Tile(TilePos p) => ref Tile(p.X, p.Y);

    public ref TileState TileAtIndex(int index) => ref _tiles[index];

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
    public bool Contains(TilePos p) => Contains(p.X, p.Y);

    public IReadOnlyList<byte> PlayerIds => _players.Select(p => p.PlayerId).ToArray();

    public int PlayerCount => _players.Length;

    public ref PlayerState PlayerAtIndex(int index) => ref _players[index];

    public ref PlayerState Player(byte playerId)
    {
        for (int i = 0; i < _players.Length; i++)
            if (_players[i].PlayerId == playerId)
                return ref _players[i];
        throw new KeyNotFoundException($"No player with id {playerId} in this world.");
    }

    /// <summary>
    /// The level's trigger flags and timers. One bank per level, never per
    /// player — see <see cref="TriggerState"/> for the four readings behind
    /// that.
    /// </summary>
    public TriggerState Triggers { get; } = new();

    /// <summary>
    /// Work applications waiting to be resolved by
    /// <see cref="Systems.TileWorkSystem"/>, in the order they were queued. A
    /// list, not a set or a dictionary: order is behaviour (ADR-0010). Two imps
    /// working the same seam in a different order can end up carrying different
    /// amounts of gold, so the queue is FIFO and never sorted or deduplicated.
    /// Produced by <see cref="Systems.ImpTaskSystem"/>; the scenario harness and
    /// tests may also queue directly, and say so in their reports.
    /// </summary>
    public IReadOnlyList<TileWorkOrder> PendingWork => _pendingWork;

    public void QueueWork(TileWorkOrder order)
    {
        if ((uint)order.TileIndex >= (uint)_tiles.Length)
            throw new ArgumentOutOfRangeException(
                nameof(order), order.TileIndex, "Work order references a tile outside the map.");
        _pendingWork.Add(order);
    }

    public void QueueWork(int x, int y, TileWorkKind kind, byte playerId)
    {
        if (!Contains(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the map.");
        _pendingWork.Add(new TileWorkOrder((y * Width) + x, kind, playerId));
    }

    internal void ClearPendingWork() => _pendingWork.Clear();

    /// <summary>
    /// Every creature in the level, in spawn order. The order is behaviour, not
    /// bookkeeping (ADR-0010): creatures are updated in it, so two imps reaching
    /// for the same job get it in this order. Never sorted, never reordered —
    /// removing a creature must preserve the order of the rest.
    /// </summary>
    public IReadOnlyList<CreatureState> Creatures => _creatures;

    /// <summary>
    /// Puts a creature on the map, at the centre of the given tile. Every
    /// in-simulation producer goes through here —
    /// <see cref="Systems.PortalSystem"/>, <see cref="Systems.ImpSupplySystem"/>,
    /// <see cref="Systems.PrisonSystem"/> and <see cref="ThingProjection"/> — and
    /// so do tests and scenario files, which place creatures directly and say so
    /// in their reports.
    /// </summary>
    public CreatureState SpawnCreature(
        GameDatabase database, byte creatureTypeId, byte ownerPlayerId, TilePos at)
    {
        if (!Contains(at))
            throw new ArgumentOutOfRangeException(nameof(at), at, "Spawn tile is outside the map.");

        var creature = new CreatureState
        {
            Id = _nextCreatureId++,
            CreatureTypeId = creatureTypeId,
            OwnerPlayerId = ownerPlayerId,
            PosX = CreatureState.CentreOf(at.X),
            PosY = CreatureState.CentreOf(at.Y),
            Activity = CreatureActivity.Idle,

            // The database is a parameter for this one line: a creature spawned at
            // zero health dies on the tick it arrives, so there is deliberately no
            // overload that leaves health out.
            Health = database.CreatureById(creatureTypeId).Hp,
        };

        _creatures.Add(creature);
        return creature;
    }

    /// <summary>
    /// Takes a creature out of the world, and says whether it was there.
    /// <para>Not death but the end of it: dying in DK2 is a process — a creature
    /// collapses at zero health, lies rescuable for
    /// <c>CreatureDyingStateDurationSeconds</c>, and only then dies (Q99,
    /// CONFIRMED, ADR-0051). <see cref="Systems.CreatureHealthSystem"/> runs that
    /// process and calls this at the end of it. Not implemented: the corpse, which
    /// is a creature in a state waiting on an imp that can carry one and not an
    /// object (docs/research/objects.md §4) — so this removal stands in for the
    /// body rotting away too.</para>
    /// <para>Ids are never reused: <c>_nextCreatureId</c> only climbs and a
    /// snapshot restores the counter rather than deriving it.</para></summary>
    public bool RemoveCreature(int id)
    {
        for (int i = 0; i < _creatures.Count; i++)
        {
            if (_creatures[i].Id != id) continue;
            _creatures.RemoveAt(i);
            return true;
        }
        return false;
    }

    public CreatureState? CreatureById(int id)
    {
        foreach (CreatureState c in _creatures)
            if (c.Id == id) return c;
        return null;
    }

    /// <summary>
    /// Every object standing in the level, in the order it was placed. One list
    /// for every kind, because DK2's object is one type with a tagged union rather
    /// than a family of types (<see cref="ObjectState"/>, ADR-0054,
    /// docs/research/objects.md §7): a mechanic often has to ask what is on a tile
    /// without knowing what it is looking for, as an imp choosing by
    /// <c>PickUpPriority</c> does.
    /// <para>Order is behaviour, as on <see cref="Creatures"/> — two beds freed on
    /// one tick are claimed in this order. Never sorted, never reordered.</para>
    /// </summary>
    public IReadOnlyList<ObjectState> Objects => _objects;

    /// <summary>
    /// Puts an object on a tile. Takes a definition id rather than a definition so
    /// the lookup is the validation: it throws rather than placing a nameless
    /// object every later rule would have to tolerate.
    /// </summary>
    public ObjectState SpawnObject(
        GameDatabase database, byte objectTypeId, byte ownerPlayerId, int tileIndex)
    {
        if ((uint)tileIndex >= (uint)_tiles.Length)
            throw new ArgumentOutOfRangeException(
                nameof(tileIndex), tileIndex, "Object tile is outside the map.");

        // Refused rather than placed: an unknown object would answer "not a bed,
        // not gold, not food" to every rule that asks a type flag, which is a
        // silent wrong answer rather than a loud one.
        if (database.ObjectById(objectTypeId) is null)
            throw new KeyNotFoundException(
                $"No object definition with id {objectTypeId}.");

        var instance = new ObjectState
        {
            Id = _nextObjectId++,
            ObjectTypeId = objectTypeId,
            OwnerPlayerId = ownerPlayerId,
            TileIndex = tileIndex,
        };

        _objects.Add(instance);
        return instance;
    }

    /// <summary>
    /// Takes an object out of the world, and says whether it was there. Ids are
    /// never reused, exactly as for creatures.
    /// </summary>
    public bool RemoveObject(int id)
    {
        for (int i = 0; i < _objects.Count; i++)
        {
            if (_objects[i].Id != id) continue;
            _objects.RemoveAt(i);
            return true;
        }
        return false;
    }

    public ObjectState? ObjectById(int id)
    {
        foreach (ObjectState o in _objects)
            if (o.Id == id) return o;
        return null;
    }

    /// <summary>
    /// The level's action points in THINGS-file order. The original walks its
    /// collection linearly for several queries, so this is never sorted by id.
    /// </summary>
    public IReadOnlyList<ActionPointState> ActionPoints => _actionPoints;

    internal void AddActionPoint(ActionPointState actionPoint) => _actionPoints.Add(actionPoint);

    /// <summary>The level's doors in THINGS-file order. No door behaviour exists yet.</summary>
    public IReadOnlyList<DoorState> Doors => _doors;

    internal void AddDoor(DoorState door) => _doors.Add(door);

    /// <summary>The level's traps in THINGS-file order. No trap behaviour exists yet.</summary>
    public IReadOnlyList<TrapState> Traps => _traps;

    internal void AddTrap(TrapState trap) => _traps.Add(trap);

    /// <summary>The level's unspawned hero-party records in THINGS-file order.</summary>
    public IReadOnlyList<HeroPartyState> HeroParties => _heroParties;

    internal void AddHeroParty(HeroPartyState party) => _heroParties.Add(party);

    /// <summary>The level's effect generators in THINGS-file order. No behaviour exists yet.</summary>
    public IReadOnlyList<EffectGeneratorState> EffectGenerators => _effectGenerators;

    internal void AddEffectGenerator(EffectGeneratorState generator) => _effectGenerators.Add(generator);

    /// <summary>Persistent offering histories, ordered by their Temple room anchor.</summary>
    public IReadOnlyList<TempleSacrificeState> TempleSacrifices => _templeSacrifices;

    internal TempleSacrificeState TempleSacrificeAt(int anchorTileIndex, byte ownerPlayerId)
    {
        int index = 0;
        while (index < _templeSacrifices.Count
               && _templeSacrifices[index].AnchorTileIndex < anchorTileIndex)
            index++;

        if (index < _templeSacrifices.Count
            && _templeSacrifices[index].AnchorTileIndex == anchorTileIndex)
        {
            TempleSacrificeState existing = _templeSacrifices[index];
            if (existing.OwnerPlayerId != ownerPlayerId)
                throw new InvalidOperationException("A Temple anchor cannot belong to two players.");
            return existing;
        }

        var created = new TempleSacrificeState(anchorTileIndex, ownerPlayerId);
        _templeSacrifices.Insert(index, created);
        return created;
    }

    public bool HasPlayer(byte playerId)
    {
        foreach (PlayerState p in _players)
            if (p.PlayerId == playerId) return true;
        return false;
    }

    /// <summary>
    /// Builds the initial world for a level: every tile takes its terrain, owner
    /// and flags from the MAP record and its health from that terrain type's
    /// <see cref="TerrainDef.StartingHealth"/>.
    /// <para>Not implemented: starting gold and mana. The level's PLAYERS record
    /// carries them and the parser reads them, but nothing wires them into a world
    /// yet, so callers that need them set them explicitly and scenarios say so.
    /// Gold has to go somewhere real — a caller deposits it with
    /// <see cref="GoldStore.Deposit"/>, which refuses outright if the player owns
    /// no treasury.</para>
    /// </summary>
    public static WorldState FromMap(
        GameDatabase database,
        LevelMap map,
        IEnumerable<byte> playerIds,
        bool fogOfWarEnabled = false)
    {
        var tiles = new TileState[map.Width * map.Height];
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                MapTile source = map[x, y];
                TerrainDef terrain = database.TerrainById(source.TerrainId);
                tiles[(y * map.Width) + x] = new TileState
                {
                    TerrainId = source.TerrainId,
                    OwnerPlayerId = source.PlayerId,
                    Flags = source.Flags,
                    Health = terrain.StartingHealth,
                };
            }
        }

        PlayerState[] players = playerIds
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new PlayerState { PlayerId = id })
            .ToArray();

        return new WorldState(map.Width, map.Height, tiles, players, fogOfWarEnabled);
    }

    /// <summary>Reconstructs a world from a snapshot. Used only by <see cref="WorldSnapshot"/>.</summary>
    internal static WorldState FromParts(
        int width, int height, TileState[] tiles, PlayerState[] players, bool fogOfWarEnabled)
        => new(width, height, tiles, players, fogOfWarEnabled);

    internal TileState[] RawTiles => _tiles;
    internal PlayerState[] RawPlayers => _players;
    internal List<TileWorkOrder> RawPendingWork => _pendingWork;
    internal List<CreatureState> RawCreatures => _creatures;
    internal List<ObjectState> RawObjects => _objects;
    internal List<ActionPointState> RawActionPoints => _actionPoints;
    internal List<DoorState> RawDoors => _doors;
    internal List<TrapState> RawTraps => _traps;
    internal List<HeroPartyState> RawHeroParties => _heroParties;
    internal List<EffectGeneratorState> RawEffectGenerators => _effectGenerators;
    internal List<TempleSacrificeState> RawTempleSacrifices => _templeSacrifices;

    /// <summary>Restores the id counter after a snapshot, so ids stay unique across a save.</summary>
    internal int NextCreatureId
    {
        get => _nextCreatureId;
        set => _nextCreatureId = value;
    }

    /// <summary>Restores the object id counter after a snapshot.</summary>
    internal int NextObjectId
    {
        get => _nextObjectId;
        set => _nextObjectId = value;
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(CurrentTick);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(FogOfWarEnabled ? (byte)1 : (byte)0);
        hash.Add(_tiles.Length);
        foreach (TileState t in _tiles) t.HashInto(ref hash);
        hash.Add(_players.Length);
        foreach (PlayerState p in _players) p.HashInto(ref hash);
        hash.Add(_pendingWork.Count);
        foreach (TileWorkOrder o in _pendingWork) o.HashInto(ref hash);

        // The id counter is hashed too: two worlds holding identical creatures but
        // disagreeing about the next id diverge the moment either one spawns.
        hash.Add(_creatures.Count);
        hash.Add(_nextCreatureId);
        foreach (CreatureState c in _creatures) c.HashInto(ref hash);

        hash.Add(_objects.Count);
        hash.Add(_nextObjectId);
        foreach (ObjectState o in _objects) o.HashInto(ref hash);

        hash.Add(_actionPoints.Count);
        foreach (ActionPointState actionPoint in _actionPoints) actionPoint.HashInto(ref hash);

        hash.Add(_doors.Count);
        foreach (DoorState door in _doors) door.HashInto(ref hash);

        hash.Add(_traps.Count);
        foreach (TrapState trap in _traps) trap.HashInto(ref hash);

        hash.Add(_heroParties.Count);
        foreach (HeroPartyState party in _heroParties) party.HashInto(ref hash);

        hash.Add(_effectGenerators.Count);
        foreach (EffectGeneratorState generator in _effectGenerators) generator.HashInto(ref hash);

        hash.Add(_templeSacrifices.Count);
        foreach (TempleSacrificeState temple in _templeSacrifices) temple.HashInto(ref hash);

        // The trigger banks are level state, not level data: an action record
        // writes a flag and a later condition reads it, so two worlds that
        // disagree about a flag diverge as soon as that condition is evaluated.
        Triggers.HashInto(ref hash);
    }
}
