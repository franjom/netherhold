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
    Attackable = 1u << 9,
    Unk10 = 1u << 10,
    Unk11 = 1u << 11,
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

    /// <summary>Object ids auto-placed on the room's tiles.</summary>
    public required IReadOnlyList<byte> ObjectIds { get; init; }

    public required ushort HealthGain { get; init; }
    public required Fp32 CeilingHeight { get; init; }
    public required byte RecommendedSizeX { get; init; }
    public required byte RecommendedSizeY { get; init; }
    public required byte OrderInEditor { get; init; }

    public required ushort NameStringId { get; init; }
    public required ushort TooltipStringId { get; init; }

    public bool IsBuildable => Flags.HasFlag(RoomFlags.Buildable);
    public bool IsGood => Flags.HasFlag(RoomFlags.IsGood);
    public bool HasWalls => Flags.HasFlag(RoomFlags.HasWalls);

    /// <summary>The strongly-typed room type, when it is one we have named.</summary>
    public RoomTypeId? TypeId =>
        Enum.IsDefined((RoomTypeId)RoomId) ? (RoomTypeId)RoomId : null;
}
