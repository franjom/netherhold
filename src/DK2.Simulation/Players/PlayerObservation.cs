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

/// <summary>An owned room as the HUD shows it.</summary>
public readonly record struct PlayerRoomSummary(RoomTypeId Type, int TileCount);

/// <summary>An owned creature from the creature overview. <paramref name="Tile"/> is null while it is
/// held; <paramref name="Handle"/> is set only when it is also a visible target.</summary>
public readonly record struct PlayerCreatureSummary(
    CreatureTypeId Type, TilePos? Tile, bool InHand, PlayerThingHandle? Handle = null);

public enum PlayerThingKind : byte { Creature, Object, Special, StoreGold, Door }

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
}

/// <summary>A room the bottom panel offers, at its displayed per-tile price.</summary>
public readonly record struct PlayerRoomOffer(RoomTypeId Room, GoldAmount Cost, bool Affordable);

/// <summary>What the player may do now, as the HUD offers it.</summary>
public sealed class PlayerOffers
{
    public static readonly PlayerOffers None = new();

    public PlayerActionKinds Actions { get; init; }

    public IReadOnlyList<PlayerRoomOffer> Rooms { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerRoomOffer>();

    public IReadOnlyList<GuiButtonTarget> Panels { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<GuiButtonTarget>();

    /// <summary>Create Imp's displayed price, when offered.</summary>
    public ManaAmount? CreateImpCost { get; init; }
}

/// <summary>
/// Everything one player may know at a completed tick, detached from the world: no engine record,
/// delegate or writable collection crosses here, and every list is a copy (campaign player plan,
/// task 1). Version <see cref="SchemaVersion"/> changes when a field is added or reinterpreted.
/// </summary>
public sealed class PlayerObservation
{
    public const int SchemaVersion = 1;

    public required PlayerStamp Stamp { get; init; }
    public PlayerPhase Phase { get; init; } = PlayerPhase.Playing;
    public PlayerResult Result { get; init; }
    public GoldAmount Gold { get; init; }
    public ManaAmount Mana { get; init; }
    /// <summary>Items in the hand; the held creatures are flagged in <see cref="Creatures"/>.</summary>
    public int HandCount { get; init; }
    public PlayerTileGrid Tiles { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = PlayerTileGrid.Empty;
    public IReadOnlyList<PlayerRoomSummary> Rooms { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerRoomSummary>();
    public IReadOnlyList<PlayerCreatureSummary> Creatures { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerCreatureSummary>();
    public IReadOnlyList<PlayerVisibleThing> Things { get; init => field = ReadOnly.Copy(value); } = ReadOnly.Empty<PlayerVisibleThing>();
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
