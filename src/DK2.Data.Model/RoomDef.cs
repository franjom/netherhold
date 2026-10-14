// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Data.Model;

/// <summary>
/// Room placement flags (ROOMS record, offset 0x370).
/// CONFIRMED: ufdada/dk2-tools rooms_struct.bt bit order.
/// </summary>
[Flags]
public enum RoomFlags : uint
{
    PlaceableOnWater = 1u << 0,
    PlaceableOnLava = 1u << 1,
    PlaceableOnLand = 1u << 2,
    HasWalls = 1u << 3,
    Centre = 1u << 4,
    SpecialTiles = 1u << 5,
    NormalTiles = 1u << 6,
    Buildable = 1u << 7,
    SpecialWalls = 1u << 8,

    /// <summary>Bits 9 to 11 add 255 to the torch light's red, green and blue (the room loader,
    /// <c>0x004EBCD5..0x004EBD2A</c>; wall-torches.md §4). ufdada names bit 9 Attackable.</summary>
    TorchRedHigh = 1u << 9,
    TorchGreenHigh = 1u << 10,
    TorchBlueHigh = 1u << 11,
    HasFlame = 1u << 12,
    IsGood = 1u << 13,
}

/// <summary>
/// How a room's tiles are laid out/meshed as it grows.
/// CONFIRMED: rooms_struct.bt.
/// </summary>
public enum TileConstruction : byte
{
    None = 0,
    Complete = 1,
    QuadConstruction = 2,
    ThreeByThree = 3,
    ThreeByThreeRotated = 4,
    Normal = 5,
    CenterPool = 6,
    DoubleQuad = 7,
    FiveByFiveRotated = 8,
    HeroGate = 9,
    HeroGateTile = 10,
    HeroGate2By2 = 11,
    HeroGateFrontEndTile = 12,
    HeroGate3By1 = 13,
}

/// <summary>
/// One room type from the ROOMS(120) record (1055 bytes each).
/// Layout CONFIRMED — see docs/research/kwd-terrain-rooms.md.
/// </summary>
public sealed record RoomDef
{
    public required string Name { get; init; }
    public required byte RoomId { get; init; }
    public required RoomFlags Flags { get; init; }

    /// <summary>Gold cost per tile to build.</summary>
    public required ushort Cost { get; init; }
    /// <summary>Percentage of Cost refunded when sold.</summary>
    public required byte ReturnPercentage { get; init; }

    /// <summary>Terrain type laid down when this room is built.</summary>
    public required byte TerrainId { get; init; }
    public required TileConstruction Construction { get; init; }

    /// <summary>Creature this room spawns, if any (e.g. Crypt → Vampire).</summary>
    public required byte CreatedCreatureId { get; init; }

    /// <summary>Effect after room object creation, ROOMS file ushort at 0x384.</summary>
    public ushort CreatedObjectEffectId { get; init; }

    /// <summary>Object ids auto-placed on the room's tiles.</summary>
    public required IReadOnlyList<byte> ObjectIds { get; init; }

    public required ushort HealthGain { get; init; }
    public required Fp32 CeilingHeight { get; init; }
    public required byte RecommendedSizeX { get; init; }
    public required byte RecommendedSizeY { get; init; }
    public required byte OrderInEditor { get; init; }

    public required ushort NameStringId { get; init; }
    public required ushort TooltipStringId { get; init; }

    /// <summary>The torch a wall facing this room's floor carries (wall-torches.md §4). Absent
    /// from a hand-built definition, which gets the balance defaults' torch.</summary>
    public RoomTorch? Torch { get; init; }

    public bool IsBuildable => Flags.HasFlag(RoomFlags.Buildable);
    public bool PlaceableOnLand => Flags.HasFlag(RoomFlags.PlaceableOnLand);
    public bool PlaceableOnWater => Flags.HasFlag(RoomFlags.PlaceableOnWater);
    public bool PlaceableOnLava => Flags.HasFlag(RoomFlags.PlaceableOnLava);
    public bool IsGood => Flags.HasFlag(RoomFlags.IsGood);
    public bool HasWalls => Flags.HasFlag(RoomFlags.HasWalls);

    /// <summary>The strongly-typed room type, when it is one we have named.</summary>
    ///
    /// <remarks>
    /// The non-generic <c>Enum.IsDefined(Type, object)</c> rather than the
    /// generic overload, which is .NET 5+ and therefore unavailable on
    /// netstandard2.1 — the framework Unity actually loads (ADR-0002). It boxes,
    /// which is why this is a property on a definition record read at load time
    /// and not something a per-tick rule calls.
    /// </remarks>
    public RoomTypeId? TypeId =>
        Enum.IsDefined(typeof(RoomTypeId), (RoomTypeId)RoomId) ? (RoomTypeId)RoomId : null;
}

/// <summary>
/// A room's wall torch, read from its ROOMS record (wall-torches.md §4, CONFIRMED against the
/// shipped file). Colours include the +255 of flags 9 to 11.
/// </summary>
/// <param name="Red">File byte <c>0x397</c>, plus 255 with <see cref="RoomFlags.TorchRedHigh"/>.</param>
/// <param name="Green">File byte <c>0x398</c>, plus 255 with <see cref="RoomFlags.TorchGreenHigh"/>.</param>
/// <param name="Blue">File byte <c>0x399</c>, plus 255 with <see cref="RoomFlags.TorchBlueHigh"/>.</param>
/// <param name="Intensity">File word <c>0x36E</c>, kept by DK2 as one byte.</param>
/// <param name="Height">File word <c>0x380</c>, in 1/4096 tile.</param>
/// <param name="Radius">File dword <c>0x3C3</c>, in 1/4096 tile, before the light setter's clamp.</param>
/// <param name="ArtName">The torch art resource at <c>0x3C7</c>.</param>
/// <param name="HasArt">The name is non-empty, the loader's test for the art flag (<c>0x004EBE58</c>); false is a light with no mesh.</param>
public sealed record RoomTorch(
    int Red, int Green, int Blue, byte Intensity, int Height, int Radius, string ArtName, bool HasArt);
