// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Data.Model;

/// <summary>What kind of thing an object is, and what may be done to it.
/// <para><b>The seven <c>Type</c> bits are the discriminator</b>: the OBJECTS record carries no
/// type field. <see cref="TypeFood"/> is a chicken, <see cref="TypeLair"/> a bed,
/// <see cref="TypeGold"/> a pile, <see cref="TypeSpellBook"/> what a library makes. Only 52 of the
/// 145 carry any type flag, so a rule asks a flag rather than an id (docs/research/objects.md §3).</para>
/// <para>Names are OpenKeeper's <c>GameObject.ObjectFlag</c>, used under ADR-0003's second
/// amendment with attribution in <c>THIRD-PARTY-NOTICES.md</c>; every bit relied on here is checked
/// against the shipped file by <c>ObjectsOnRealDataTests</c>. The union of all flags across the 145
/// records is <b>0x07F7F7FF</b>: bits 11 and 19 are never set, nor is anything above bit 26, so a
/// bit nobody has seen set cannot be quietly named.</para>
/// </summary>
[Flags]
public enum ObjectFlags : uint
{
    None = 0,

    /// <summary>Decays on its own — what a corpse and a chicken carcass do.</summary>
    DieOverTime = 1u << 0,

    /// <summary>Decays unless it is in a room that keeps it.</summary>
    DieOverTimeIfNotInRoom = 1u << 1,

    /// <summary>A level special: the box a keeper picks up for a bonus.</summary>
    TypeSpecial = 1u << 2,

    /// <summary>A spell book, which is what research produces.</summary>
    TypeSpellBook = 1u << 3,

    /// <summary>A manufacture crate — a finished trap or door waiting to be placed.</summary>
    TypeCrate = 1u << 4,

    /// <summary>A creature's bed. The object a lair tile holds.</summary>
    TypeLair = 1u << 5,

    /// <summary>Gold: a pile on the floor or in a treasury.</summary>
    TypeGold = 1u << 6,

    /// <summary>Food: the Chicken (9) a hungry creature eats and, oddly, the Torture Pendulum (65).
    /// Not the Egg (47), which decays but is not food — Q133.</summary>
    TypeFood = 1u << 7,

    CanBePickedUp = 1u << 8,
    CanBeSlapped = 1u << 9,
    DieWhenSlapped = 1u << 10,

    /// <summary>The dungeon's level gem, which a level's objective may name.</summary>
    TypeLevelGem = 1u << 12,

    /// <summary><b>Dead.</b> Set on exactly one record — Call To Arms — and <b>tested nowhere in
    /// the executable</b> (raw scan for the mask against the definition's flag dword). The name is
    /// OpenKeeper's reading of the bit and a plausible one; what it is not is evidence about where
    /// a keeper may put a thing down. Q196.</summary>
    CanBeDroppedOnAnyLand = 1u << 13,
    Obstacle = 1u << 14,
    Bounce = 1u << 15,
    BoulderCanRollThrough = 1u << 16,
    BoulderDestroys = 1u << 17,
    Pillar = 1u << 18,
    DoorKey = 1u << 20,
    Damageable = 1u << 21,
    Highlightable = 1u << 22,
    Placeable = 1u << 23,
    FirstPersonObstacle = 1u << 24,
    SolidObstacle = 1u << 25,
    CastShadows = 1u << 26,
}

/// <summary>One OBJECTS(240) record: 145 of them, 894 bytes each (docs/research/objects.md).
/// <para>Includes animation timing, the authored light descriptor and the gameplay tail.</para>
/// <para><b>The layout is validated, not trusted.</b> OpenKeeper's field order gave the sequence;
/// the offsets come out of it arithmetically and then check themselves twice over — the fields from
/// <c>0x34C</c> to the end sum to <b>0x37E = 894</b>, the independently measured record stride, and
/// the <see cref="ObjectId"/> column reads 1 to 145 exactly once each.</para>
/// <para>Not the home of corpses: "a corpse is an object" is <b>refuted</b> (§4). A corpse is
/// <c>CreatureActivity.Dead</c> and an imp carries it to a Graveyard (ADR-0063); the simulation
/// counts its creature-definition value there rather than inventing a corpse object (ADR-0151).</para>
/// </summary>
public sealed record ObjectDef
{
    /// <summary>The editor's name, from the record's first 32 bytes.</summary>
    public required string Name { get; init; }

    /// <summary>The id rooms and creatures refer to. A Goblin's <c>LairObjectId</c> is 22, a
    /// Vampire's is 26, and the Graveyard's <c>ObjectIds</c> are 31 to 34 (Tomb Stone 1-4).</summary>
    public required byte ObjectId { get; init; }

    public required ObjectFlags Flags { get; init; }

    public KwdLight? Light { get; init; }

    /// <summary>Health, for the objects that can be destroyed.</summary>
    public required ushort Hp { get; init; }

    /// <summary>Mana this object is worth — non-zero on very few.</summary>
    public required ushort ManaValue { get; init; }

    /// <summary>How many of it a room tile holds — the unit a treasury tile's gold cap and a
    /// hatchery's chicken count would be expressed in.</summary>
    public required byte RoomCapacity { get; init; }

    /// <summary>Which state the object starts in — its own small state machine.</summary>
    public required byte StartState { get; init; }

    /// <summary>File word 0x358, copied to definition +0x117: the effect the state-50 process
    /// (<c>0x004A3C30</c>) ties to a loose object once a second. Only the Portal Gem (28) starts in
    /// state 50; its value is 297, Level Gem Idle Effect. 0 for none.</summary>
    public ushort IdleEffectId { get; init; }

    /// <summary>Ordering for what an imp picks up first when several things lie about.</summary>
    public required byte PickUpPriority { get; init; }

    public required Fp32 Width { get; init; }
    public required Fp32 Height { get; init; }
    public required Fp32 Mass { get; init; }
    public required Fp32 Speed { get; init; }

    /// <summary>File dword 0x33C; the physical step retains its low word as horizontal drag.</summary>
    public Fp32 AirFriction { get; init; }

    /// <summary>File word 0x34A; process 8 adds or subtracts it from a Chicken's heading.</summary>
    public ushort ReactionTurn { get; init; }

    /// <summary>The nine file art resources, retaining only integer animation timing.</summary>
    public IReadOnlyList<CreatureAnimationTiming> ArtSlots { get; init; } = Array.Empty<CreatureAnimationTiming>();

    public CreatureAnimationTiming ArtSlot(int slot) =>
        (uint)slot < (uint)ArtSlots.Count ? ArtSlots[slot] : default;

    /// <summary>String table ids, for a renderer rather than a rule.</summary>
    public required ushort NameStringId { get; init; }
    public required ushort TooltipStringId { get; init; }

    /// <summary>One of the twelve beds — eleven evil plus the Hero Creature Bed.</summary>
    public bool IsLair => Flags.HasFlag(ObjectFlags.TypeLair);

    /// <summary>Food: see <see cref="ObjectFlags.TypeFood"/> for what falls in the set.</summary>
    public bool IsFood => Flags.HasFlag(ObjectFlags.TypeFood);

    public bool IsGold => Flags.HasFlag(ObjectFlags.TypeGold);

    public bool IsSpellBook => Flags.HasFlag(ObjectFlags.TypeSpellBook);

    public bool IsSpecial => Flags.HasFlag(ObjectFlags.TypeSpecial);

    /// <summary>Whether the Hand of Evil may lift this — <b>26 of the 145 records</b>: the three
    /// golds, the Chicken, Call To Arms, all fifteen specials, and six odds and ends (the Casino
    /// Pay Out Adjuster, two Hero Gate tables, the Frontend 3D Arrow, the Prison Door Bar, the MPD
    /// Level Indicator). <b>Spell books, crates and creature beds are not in the set</b>: an imp
    /// carries a finished trap or a book to a library, not the keeper.
    /// <para><b>The flag has exactly one reader in the whole binary</b>, the cursor's target
    /// search, which also lets the <b>Dungeon Heart</b> through by id regardless of the flag — a
    /// targeting special case rather than a lifting one, so it is not reproduced here.</para></summary>
    public bool CanBePickedUp => Flags.HasFlag(ObjectFlags.CanBePickedUp);

    /// <summary>Decays on its own, with or without a room to keep it.</summary>
    public bool Decays =>
        Flags.HasFlag(ObjectFlags.DieOverTime)
        || Flags.HasFlag(ObjectFlags.DieOverTimeIfNotInRoom);
}
