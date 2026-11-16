// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.ObjectModel;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Players;

/// <summary>What one tile shows its player. An unknown tile is all defaults, whatever lies there.</summary>
[Flags]
public enum PlayerTileFlags : byte
{
    None = 0,
    Known = 1,
    Solid = 2,
    Diggable = 4,
    Tagged = 8,
    GoldSeam = 16,
    Water = 32,
    Lava = 64,
    /// <summary>One of the player's own traps, or a trap blueprint it placed, stands here (schema 6).</summary>
    OwnTrap = 128,
}

/// <summary>One tile as its player sees it: flags, owner and room type (default when not a room).</summary>
public readonly record struct PlayerTile(PlayerTileFlags Flags, PlayerId Owner, RoomTypeId Room)
{
    public bool IsKnown => (Flags & PlayerTileFlags.Known) != 0;
}

/// <summary>An immutable copy of the player's tile view, row-major.</summary>
public sealed class PlayerTileGrid
{
    public static readonly PlayerTileGrid Empty = new(0, 0, []);

    private readonly PlayerTile[] _tiles;

    /// <summary>Copies <paramref name="tiles"/>, so the caller may reuse its buffer.</summary>
    public PlayerTileGrid(int width, int height, ReadOnlySpan<PlayerTile> tiles)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        long count = (long)width * height;
        if (tiles.Length != count)
            throw new ArgumentException($"Expected {count} tiles, got {tiles.Length}.", nameof(tiles));
        Width = width;
        Height = height;
        _tiles = tiles.ToArray();
    }

    public int Width { get; }
    public int Height { get; }

    public bool Contains(TilePos tile) => (uint)tile.X < (uint)Width && (uint)tile.Y < (uint)Height;

    public PlayerTile this[TilePos tile] => Contains(tile)
        ? _tiles[tile.Y * Width + tile.X]
        : throw new ArgumentOutOfRangeException(nameof(tile), tile, "Outside the grid.");
}

/// <summary>An owned room type as the HUD shows it: its tiles, and for the rooms whose hover text gives a fill (schema 7)
/// what they hold summed over every room of the type: the Lair's beds (<see cref="Capacity"/>) and beds taken
/// (<see cref="Used"/>), as its status-bar hover reads (string 2435); the Treasury's gold capacity and gold held (string
/// 2581). Zero for the other types.</summary>
public readonly record struct PlayerRoomSummary(RoomTypeId Type, int TileCount, int Capacity = 0, int Used = 0);

/// <summary>A creature's mood as its status flower shows it (SM-Happy, SM-Sad, SM-Angry).</summary>
public enum PlayerCreatureMood : byte { Happy = 0, Unhappy = 1, Angry = 2 }

/// <summary>An owned creature from the creature overview. <paramref name="Tile"/> is null while it is
/// held; <paramref name="Handle"/> is set only when it is also a visible target. <paramref name="Level"/>
/// (1-10), <paramref name="Petals"/> and <paramref name="Mood"/> are what its status flower shows when
/// hovered (status-flowers.md: SL-01..SL-10, H-00..H-05, SM-*; campaign bot stage 3 plan, Task 4): six
/// petals at full health, one fewer per quarter lost, so two in the last quarter and one only once
/// collapsed or dying. A creature whose flower never shows (held, joining, an own Horny,
/// <c>StatusFlowerRules.NeverShows</c>) has level 0, 0 petals and no mood: unknown. <paramref name="Hungry"/>
/// (schema 7) is whether the flower's centre shows the chicken (SJ-Hungry): unhappy or angry with hunger its largest
/// grievance, or content and eating (status-flowers.md section 2). A status (stunned, afraid, leaving) that takes the
/// centre over is not modelled.</summary>
public readonly record struct PlayerCreatureSummary(
    CreatureTypeId Type, TilePos? Tile, bool InHand, PlayerThingHandle? Handle = null,
    int Level = 0, int Petals = 0, PlayerCreatureMood? Mood = null, bool Hungry = false)
{
    /// <summary>Whether the flower's level, petals and mood are known.</summary>
    public bool FlowerKnown => Level > 0;
}

public enum PlayerThingKind : byte { Creature, Object, Special, StoreGold, Door }

/// <summary>Another player's trap as the viewer sees it on screen (schema 7, economy and assault plan A1): its tile,
/// trap id, owner, and <paramref name="ReachTiles"/>, how far its target finder looks: 0 for a trap that fires at its own
/// tile, else its definition's detection range, plus two for one that turns toward a target (traps.md sections 5 and 6;
/// public data, as a creature's fighting style is). Shown as the world view draws it: on a visible tile, built, and not hidden (a hidden trap shows once it
/// fires or a revealing creature stands beside it, traps.md section 11).</summary>
public readonly record struct PlayerVisibleTrap(TilePos Tile, byte Type, PlayerId Owner, int ReachTiles);

/// <summary>What the hand may currently do to one visible thing.</summary>
[Flags]
public enum PlayerThingActions : byte
{
    None = 0,
    PickUp = 1,
    Slap = 2,
    Activate = 4,
}

/// <summary>A thing the player's view admits as a target. A creature carries its
/// <see cref="CreatureType"/>; an object or special its <see cref="ObjectType"/>; a store pile or
/// door neither.</summary>
public readonly record struct PlayerVisibleThing
{
    public PlayerVisibleThing(PlayerThingHandle handle, PlayerThingKind kind, TilePos tile, PlayerId owner,
        PlayerThingActions actions, CreatureTypeId? creatureType = null, ObjectTypeId? objectType = null)
    {
        if (!handle.IsValid) throw new ArgumentException("A visible thing needs a handle.", nameof(handle));
        if ((kind == PlayerThingKind.Creature) != creatureType.HasValue)
            throw new ArgumentException("Exactly the creatures carry a creature type.", nameof(creatureType));
        if ((kind is PlayerThingKind.Object or PlayerThingKind.Special) != objectType.HasValue)
            throw new ArgumentException("Exactly the objects and specials carry an object type.", nameof(objectType));
        Handle = handle;
        Kind = kind;
        Tile = tile;
        Owner = owner;
        Actions = actions;
        CreatureType = creatureType;
        ObjectType = objectType;
    }

    public PlayerThingHandle Handle { get; }
    public PlayerThingKind Kind { get; }
    public TilePos Tile { get; }
    public PlayerId Owner { get; }
    public PlayerThingActions Actions { get; }
    public CreatureTypeId? CreatureType { get; }
    public ObjectTypeId? ObjectType { get; }
}

/// <summary>A message the player received: the tick it was first observed, its ordinal among this
/// player's messages (1, 2, ...; never the queue id, which other players' messages also advance),
/// its kind, and its resolved text when known. No payload naming another object crosses here.</summary>
public readonly record struct PlayerNotice(GameTick Tick, int Ordinal, PlayerMessageKind Kind, string? Text = null);

/// <summary>Action families the player may request now.</summary>
[Flags]
public enum PlayerActionKinds : ushort
{
    None = 0,
    TagArea = 1 << 0,
    BuildRoom = 1 << 1,
    SelectPanel = 1 << 2,
    PickUp = 1 << 3,
    DropAt = 1 << 4,
    Slap = 1 << 5,
    ActivateSpecial = 1 << 6,
    CreateImpAt = 1 << 7,
    SkipIntro = 1 << 8,
    CastSpell = 1 << 9,
    PlaceDoor = 1 << 10,
    PlaceTrap = 1 << 11,
}

/// <summary>A Workshop panel item: a door or a trap.</summary>
public enum PlayerWorkshopKind : byte { Door, Trap }

/// <summary>A door or trap the Workshop panel offers (campaign bot stage 3 plan, Task 4): its id (the
/// placement packets' door or trap id), its name, its displayed gold price and whether the keeper can pay
/// it, and <see cref="NeedsWorkshop"/>: the slot is greyed because the keeper owns no Workshop, and a
/// click places nothing (<c>0x0040849F</c>, <c>0x00408586</c>). The panel model (BottomPanelView) carries no stock count.</summary>
public readonly record struct PlayerWorkshopOffer(PlayerWorkshopKind Kind, byte Id, string Name, GoldAmount Cost, bool Affordable, bool NeedsWorkshop);

/// <summary>A room the bottom panel offers, at its displayed per-tile price.</summary>
public readonly record struct PlayerRoomOffer(RoomTypeId Room, GoldAmount Cost, bool Affordable);

/// <summary>A keeper spell the Spell panel offers (campaign bot stage 3 plan, Task 4): its id (the
/// panel's and the cast packets' spell id), its name, its displayed mana price and whether the keeper
/// can pay it, whether its slot's recharge pie is done (<see cref="Ready"/>), and whether it is cast on a
/// creature (<see cref="OnCreature"/>, the spell definition's target selector) or at a place. Create Imp
/// is listed too; it is cast with <see cref="PlayerAction.CreateImpAt"/>. Possession is not offered: the
/// player has no action to end one. <see cref="Upgraded"/> is whether the Library has upgraded it, which the
/// slot shows as the spell's second icon (panel-indicators.md; schema 5).</summary>
public readonly record struct PlayerSpellOffer(byte Spell, string Name, ManaAmount Cost, bool Affordable, bool OnCreature, bool Ready = true,
    bool Upgraded = false);

/// <summary>What the player may do now, as the HUD offers it.</summary>
public sealed class PlayerOffers
{
    public static readonly PlayerOffers None = new();

    public PlayerActionKinds Actions { get; init; }

    public IReadOnlyList<PlayerRoomOffer> Rooms { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerRoomOffer>();

    public IReadOnlyList<GuiButtonTarget> Panels { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<GuiButtonTarget>();

    /// <summary>Create Imp's displayed price, when offered.</summary>
    public ManaAmount? CreateImpCost { get; init; }

    /// <summary>Every door, then every trap, the Workshop panel offers, by id (Task 4).</summary>
    public IReadOnlyList<PlayerWorkshopOffer> Workshop { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerWorkshopOffer>();

    /// <summary>Every spell the Spell panel offers, by spell id (Task 4).</summary>
    public IReadOnlyList<PlayerSpellOffer> Spells { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerSpellOffer>();
}

/// <summary>
/// Everything one player may know at a completed tick, detached from the world: no engine record,
/// delegate or writable collection crosses here, and every list is a copy (campaign player plan,
/// task 1). Version <see cref="SchemaVersion"/> changes when a field is added or reinterpreted.
/// </summary>
public sealed class PlayerObservation
{
    /// <summary>2 adds each own creature's level, petals and mood; 3 every offered spell; 4 doors and traps; 5 the
    /// mana orb's income and upkeep and each spell's upgrade (Task 4); 6 the own-trap tile flag (defence plan D1); 7 the
    /// payday clock and wage bill, the Lair's and Treasury's fill, each creature's hunger, and other players' traps in
    /// view (economy and assault plan E1, A1).</summary>
    public const int SchemaVersion = 7;

    public required PlayerStamp Stamp { get; init; }
    public PlayerPhase Phase { get; init; } = PlayerPhase.Playing;
    public PlayerResult Result { get; init; }
    public GoldAmount Gold { get; init; }
    public ManaAmount Mana { get; init; }
    /// <summary>The mana orb's income figure: mana gained per second (<c>ManaRules.GainPerSecondByPlayerIndex</c>, as
    /// the HUD's <c>KeeperResourceView</c> shows it; schema 5).</summary>
    public ManaPerSecond ManaIncome { get; init; }
    /// <summary>The mana orb's upkeep figure: mana spent per second on Imps, traps and spells held
    /// (<c>ManaRules.UpkeepPerSecondByPlayerIndex</c>, as the HUD shows it; schema 5).</summary>
    public ManaPerSecond ManaUpkeep { get; init; }
    /// <summary>Items in the hand; the held creatures are flagged in <see cref="Creatures"/>.</summary>
    public int HandCount { get; init; }
    /// <summary>Ticks until the next payday, as the payday gauge shows it filling (schema 7).</summary>
    public int TicksToPayday { get; init; }
    /// <summary>The coming payday's total wages, as the payday gauge's hover text and the gold counter's warning show
    /// it (<c>CasinoJackpotRules.WageBill</c>, hud-resources.md; schema 7).</summary>
    public GoldAmount WageBill { get; init; }
    public PlayerTileGrid Tiles { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = PlayerTileGrid.Empty;
    public IReadOnlyList<PlayerRoomSummary> Rooms { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerRoomSummary>();
    public IReadOnlyList<PlayerCreatureSummary> Creatures { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerCreatureSummary>();
    public IReadOnlyList<PlayerVisibleThing> Things { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerVisibleThing>();
    /// <summary>Other players' traps in view (schema 7).</summary>
    public IReadOnlyList<PlayerVisibleTrap> EnemyTraps { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerVisibleTrap>();
    public IReadOnlyList<PlayerNotice> Notices { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerNotice>();
    public IReadOnlyList<PlayerReceipt> Receipts { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerReceipt>();
    public PlayerOffers Offers { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = PlayerOffers.None;
}

/// <summary>Copies into read-only storage that a consumer cannot cast back to a writable list.</summary>
internal static class ReadOnly
{
    public static IReadOnlyList<T> Empty<T>() => EmptyOf<T>.Value;

    public static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (source.Count == 0) return EmptyOf<T>.Value;
        var copy = new T[source.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
        return new ReadOnlyCollection<T>(copy);
    }

    private static class EmptyOf<T>
    {
        public static readonly ReadOnlyCollection<T> Value = new([]);
    }
}
