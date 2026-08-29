using DK2.Core;

namespace DK2.Data.Model;

/// <summary>
/// Terrain type flags (TERRAIN record, offset 0x190).
/// CONFIRMED: ufdada/dk2-tools terrains_struct.bt bit order.
/// </summary>
[Flags]
public enum TerrainFlags : uint
{
    Solid = 1u << 0,
    Impenetrable = 1u << 1,
    Ownable = 1u << 2,
    Taggable = 1u << 3,
    Room = 1u << 4,
    Attackable = 1u << 5,
    Torch = 1u << 6,
    Water = 1u << 7,
    Lava = 1u << 8,
    AlwaysExplored = 1u << 9,
    PlayerColouredPath = 1u << 10,
    PlayerColouredWall = 1u << 11,
    ConstructionTypeWater = 1u << 12,
    ConstructionTypeQuad = 1u << 13,
    UnexploreIfDugByAnotherPlayer = 1u << 14,
    FillInable = 1u << 15,
    AllowRoomWalls = 1u << 16,
    Decay = 1u << 17,
    RandomTexture = 1u << 18,
    TerrainColorRed = 1u << 19,
    TerrainColorGreen = 1u << 20,
    TerrainColorBlue = 1u << 21,
    DwarfCanDigThrough = 1u << 23,
    RevealThroughFogOfWar = 1u << 24,
    AmbientColorRed = 1u << 25,
    AmbientColorGreen = 1u << 26,
    AmbientColorBlue = 1u << 27,
    TerrainLight = 1u << 28,
    AmbientLight = 1u << 29,
}

/// <summary>
/// One terrain type from the TERRAIN(110) record (552 bytes each).
/// Gameplay fields only; art resources are omitted (see DK2.Assets).
/// Layout CONFIRMED — see docs/research/kwd-terrain-rooms.md.
/// </summary>
public sealed record TerrainDef
{
    public required string Name { get; init; }
    public required byte TerrainId { get; init; }
    public required TerrainFlags Flags { get; init; }

    /// <summary>Health a tile of this type starts with. Digging subtracts from it.</summary>
    public required ushort StartingHealth { get; init; }
    public required ushort MaxHealth { get; init; }

    /// <summary>Gold yielded when mined out (gold seams / gems).</summary>
    public required ushort GoldValue { get; init; }
    public required ushort ManaGain { get; init; }
    public required ushort MaxManaGain { get; init; }

    /// <summary>
    /// Health change per second for creatures on this tile. Negative values heal:
    /// Hero Lair stores -67 (CONFIRMED — reads as 65469 if treated as unsigned),
    /// which is why this field is signed.
    /// </summary>
    public required short Damage { get; init; }

    /// <summary>Terrain this becomes at full health (e.g. reinforced wall).</summary>
    public required byte MaxHealthTypeTerrainId { get; init; }
    /// <summary>Terrain this becomes when destroyed (e.g. wall to path).</summary>
    public required byte DestroyedTypeTerrainId { get; init; }

    public required Fp32 Depth { get; init; }
    public required ushort NameStringId { get; init; }
    public required ushort TooltipStringId { get; init; }

    public bool IsSolid => Flags.HasFlag(TerrainFlags.Solid);
    public bool IsImpenetrable => Flags.HasFlag(TerrainFlags.Impenetrable);
    public bool IsOwnable => Flags.HasFlag(TerrainFlags.Ownable);
    public bool IsTaggable => Flags.HasFlag(TerrainFlags.Taggable);
    public bool IsRoom => Flags.HasFlag(TerrainFlags.Room);
    public bool IsWater => Flags.HasFlag(TerrainFlags.Water);
    public bool IsLava => Flags.HasFlag(TerrainFlags.Lava);
}
