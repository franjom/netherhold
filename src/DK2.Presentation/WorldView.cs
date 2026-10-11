// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>
/// One tile, as a renderer needs it: where it is, what to draw there, and the
/// few state bits a player can see.
/// </summary>
public readonly struct TileView
{
    public TileView(
        TilePos position,
        AssetRef ground,
        AssetRef furniture,
        PlayerId ownerPlayerId,
        bool tagged,
        bool solid,
        int goldHeld,
        int healthPermille,
        RoomWalls walls = default,
        TerrainPieces pieces = default,
        TerrainPieceKeys? pieceKeys = null,
        bool explored = true,
        bool hasDoor = false,
        PieceSet roomFloor = default,
        RoomPieceKeys? roomKeys = null,
        PieceSet bed = default,
        TerrainPieceKeys? bedKeys = null,
        LiquidSurface surface = LiquidSurface.None)
    {
        Position = position;
        Ground = ground;
        Furniture = furniture;
        OwnerPlayerId = ownerPlayerId.Value;
        Tagged = tagged;
        Solid = solid;
        GoldHeld = goldHeld;
        HealthPermille = healthPermille;
        Walls = walls;
        Pieces = pieces;
        PieceKeys = pieceKeys;
        Explored = explored;
        HasDoor = hasDoor;
        RoomFloor = roomFloor;
        RoomKeys = roomKeys;
        Bed = bed;
        BedKeys = bedKeys;
        Surface = surface;
    }

    public TilePos Position { get; }

    /// <summary>The terrain's own asset: a mesh for a wall, a texture for a floor.</summary>
    public AssetRef Ground { get; }

    /// <summary>
    /// The room's furniture, when this tile is part of a room; empty otherwise.
    /// Separate from <see cref="Ground"/> because a room is paved ground plus the
    /// things standing on it, and selling takes the second and leaves the first.
    /// </summary>
    public AssetRef Furniture { get; }

    /// <summary>Who holds it. 0 is unowned: ground that cannot be owned shows 0, though the
    /// simulation gives it to the neutral player (ADR-0362).</summary>
    public byte OwnerPlayerId { get; }

    /// <summary>Marked for digging by the viewing player. DK2 draws every piece of such a tile, and a
    /// room wall facing it, tinted (128, 128, 200) under an overlay, whatever the terrain or its
    /// owner (tag-highlight.md).</summary>
    public bool Tagged { get; }

    /// <summary>Whether the terrain fills its tile. A renderer needs this before it has a mesh.</summary>
    public bool Solid { get; }

    /// <summary>Gold sitting in this tile (a treasury tile, a heart tile).</summary>
    public int GoldHeld { get; }

    /// <summary>Health as thousandths of the terrain's maximum, so a renderer can show damage
    /// without knowing the terrain table. An integer, per ADR-0001; 1000 for a terrain with no
    /// meaningful maximum.
    /// <para>DK2 does show wall damage, and shows it by swapping the wall's <i>texture</i> for a
    /// damage-state one — CONFIRMED from Simon Schreibt's captures
    /// (docs/research/wall-rendering.md), so what this selects are textures, not meshes.</para></summary>
    public int HealthPermille { get; }

    /// <summary>The room wall pieces drawn against this tile's sides; empty off a room
    /// (<see cref="RoomWalls"/>).</summary>
    public RoomWalls Walls { get; }

    /// <summary>The plain terrain pieces this tile draws (<see cref="TerrainPieces"/>); empty on a
    /// room, water, lava or a tile under fog.</summary>
    public TerrainPieces Pieces { get; }

    /// <summary>The keys <see cref="Pieces"/> draws with; null when it is empty.</summary>
    public TerrainPieceKeys? PieceKeys { get; }

    /// <summary>Explored by the viewer (always, with fog off). With <see cref="HasDoor"/>, what the
    /// terrain lattice reads of a tile beyond its terrain, so a change rebuilds the bend.</summary>
    public bool Explored { get; }

    /// <summary>A door stands here, open or not (the lattice's door test).</summary>
    public bool HasDoor { get; }

    /// <summary>The room floor pieces this tile draws (<see cref="PieceSet"/>); empty off a room.</summary>
    public PieceSet RoomFloor { get; }

    /// <summary>The keys <see cref="RoomFloor"/> and <see cref="Walls"/> draw with; null off a room
    /// and on the constructions not built (the Final, FrontEnd and Tile hero gates).</summary>
    public RoomPieceKeys? RoomKeys { get; }

    /// <summary>The water or lava bed under this tile (<c>0x0043E440</c>): a water or lava tile's
    /// own, or a bridge's; empty where none is drawn, as under open water.</summary>
    public PieceSet Bed { get; }

    /// <summary>The liquid's keys, for its bed pieces; null where no liquid lies.</summary>
    public TerrainPieceKeys? BedKeys { get; }

    /// <summary>The liquid surface drawn over this tile: on every water and lava tile, open or
    /// shore, and under a bridge over water but not over lava (room-floors-and-liquids.md §6).</summary>
    public LiquidSurface Surface { get; }

    public bool IsRoom => !Furniture.IsEmpty;
}

/// <summary>
/// One creature, as a renderer needs it.
/// </summary>
public readonly struct CreatureView
{
    public CreatureView(
        int id,
        PlayerId ownerPlayerId,
        int posX,
        int posY,
        AssetRef mesh,
        AssetRef animation,
        int goldHeld,
        int height = Fp32.One,
        int meshYaw = 0,
        int pitch = 0,
        int roll = 0,
        int? previousPosX = null,
        int? previousPosY = null,
        bool playsAnimation = false,
        bool animationLoops = true,
        GameTick? animationStartedAt = null,
        Fp32? animationMovementSpeed = null)
    {
        Id = id;
        OwnerPlayerId = ownerPlayerId.Value;
        PosX = posX;
        PosY = posY;
        Mesh = mesh;
        Animation = animation;
        GoldHeld = goldHeld;
        Height = height;
        MeshYaw = meshYaw;
        Pitch = pitch;
        Roll = roll;
        PreviousPosX = previousPosX ?? posX;
        PreviousPosY = previousPosY ?? posY;
        PlaysAnimation = playsAnimation;
        AnimationLoops = animationLoops;
        AnimationStartedAt = animationStartedAt;
        AnimationMovementSpeed = animationMovementSpeed;
    }

    /// <summary>The simulation's creature id. A renderer's object is a view of this and nothing more.</summary>
    public int Id { get; }

    public byte OwnerPlayerId { get; }

    /// <summary>
    /// Drawing point in <see cref="Fp32"/> tile units: usually the simulation position,
    /// with the device offset added for torture. A renderer converts (ADR-0001) —
    /// divide by <see cref="Fp32.One"/> for a tile index, or convert to float for
    /// a world position.
    /// </summary>
    public int PosX { get; }
    public int PosY { get; }

    /// <summary>Draw height and model turns; torture attachment is presentation only.</summary>
    public int Height { get; }
    public int MeshYaw { get; }
    public int Pitch { get; }
    public int Roll { get; }

    public AssetRef Mesh { get; }

    /// <summary>Which animation to play, chosen from the creature's activity.</summary>
    public AssetRef Animation { get; }

    public int PreviousPosX { get; }
    public int PreviousPosY { get; }
    public bool PlaysAnimation { get; }
    public bool AnimationLoops { get; }
    public GameTick? AnimationStartedAt { get; }
    public Fp32? AnimationMovementSpeed { get; }

    /// <summary>Gold in its hands. Visible in DK2 — an imp carrying gold looks like it.</summary>
    public int GoldHeld { get; }

    /// <summary>The tile it is standing in.</summary>
    public TilePos Tile => new(PosX / Fp32.One, PosY / Fp32.One);
}

/// <summary>
/// One object standing on a tile, as a renderer needs it: a bed, a chicken coop,
/// an egg, a pile of gold, a room's pillar.
///
/// <para>Placed as DK2 places it (furniture-placement.md, object-drawing.md): a point
/// in the tile, a height, a heading and a scale. The simulation holds a tile only, so the
/// point is what the placing pass would have chosen; a pile dropped at a random point
/// (gold-on-the-floor.md §7) is drawn at its tile's centre.</para>
/// </summary>
public readonly struct ObjectView
{
    public ObjectView(int id, byte objectTypeId, PlayerId ownerPlayerId, TilePos tile, AssetRef mesh, int goldHeld,
        int posX, int posY, int height, int heading, int scale, int pitch = 0, int roll = 0,
        Fp32? previousPosX = null, Fp32? previousPosY = null, int? previousHeading = null,
        bool playsAnimation = false, GameTick? animationStartedAt = null)
    {
        Pitch = pitch;
        Roll = roll;
        Id = id;
        ObjectTypeId = objectTypeId;
        OwnerPlayerId = ownerPlayerId.Value;
        Tile = tile;
        Mesh = mesh;
        GoldHeld = goldHeld;
        PosX = posX;
        PosY = posY;
        Height = height;
        Heading = heading;
        Scale = scale;
        PreviousPosX = previousPosX?.Raw ?? posX;
        PreviousPosY = previousPosY?.Raw ?? posY;
        PreviousHeading = previousHeading ?? heading;
        PlaysAnimation = playsAnimation;
        AnimationStartedAt = animationStartedAt;
    }

    /// <summary>The simulation's object id, stable for the object's whole life; negative for a
    /// decoration the view adds (<see cref="WorldView.Decorations"/>), stable while its room stands.</summary>
    public int Id { get; }

    /// <summary>Position in <see cref="Fp32"/> tile units (<c>+0x1A</c>, <c>+0x1E</c>).</summary>
    public int PosX { get; }

    public int PosY { get; }

    /// <summary>Height in <see cref="Fp32"/> units, ordinary floor at one tile (<c>+0x22</c>).</summary>
    public int Height { get; }

    /// <summary>Heading, 2048 to a full turn, 0 north and <c>0x200</c> east (<c>+0xF4</c>).</summary>
    public int Heading { get; }

    /// <summary>Yaw in the renderer's frame: DK2's row-vector rotation and (x, −z, −y)
    /// conversion give +(heading + half a turn); object-drawing.md §4.</summary>
    public int MeshYaw => (Heading + 0x400) & 0x7FF;

    /// <summary>Uniform scale, <see cref="Fp32.One"/> for 1 (RenderInfo <c>+4</c>, <c>+8</c>).</summary>
    public int Scale { get; }

    public int PreviousPosX { get; }
    public int PreviousPosY { get; }
    public int PreviousHeading { get; }
    public bool PlaysAnimation { get; }
    public GameTick? AnimationStartedAt { get; }

    /// <summary>A turn about the model's own X axis, 2048 to a turn (<c>+0x64</c>, RenderInfo
    /// <c>+0x20</c>). DK2 turns a model by this, then <see cref="Roll"/>, then the heading
    /// (<c>0x00580480</c>, object-drawing.md §4).</summary>
    public int Pitch { get; }

    /// <summary>A turn about the model's own Y axis, 2048 to a turn (<c>+0x68</c>, RenderInfo
    /// <c>+0x24</c>).</summary>
    public int Roll { get; }

    /// <summary>Which of the 145 definitions this is.</summary>
    public byte ObjectTypeId { get; }

    /// <summary>Whose it is. A bed belongs to its sleeper's keeper; a floor pile to nobody.</summary>
    public byte OwnerPlayerId { get; }

    /// <summary>The tile it stands on.</summary>
    public TilePos Tile { get; }

    public AssetRef Mesh { get; }

    /// <summary>
    /// How much gold, when this object is a pile — the object union's
    /// <c>Gold</c> member. Zero for everything else, and a renderer that wants
    /// to know whether it means anything should ask whether the definition is
    /// gold rather than whether this is non-zero: an emptied pile is destroyed,
    /// but a bed's zero is not an empty pile.
    /// </summary>
    public int GoldHeld { get; }
}

/// <summary>Turns simulation state into asset-keyed descriptions a renderer can draw — the whole
/// of the presentation boundary the plan draws in §4.7.
/// <para>It exists rather than each renderer reading <see cref="WorldState"/> itself because every
/// renderer would otherwise repeat the same three decisions — which asset key a terrain implies,
/// which animation an activity implies, how a room's floor differs from its furniture — and a
/// disagreement between two of them looks like a simulation bug. The Unity view and the harness's
/// text view consume this same type, which is what makes the text view evidence about the Unity
/// view.</para>
/// <para><b>Observation only</b>, by convention rather than compiler guarantee: the view types are
/// readonly structs holding copies, but <see cref="WorldState.Tile(int,int)"/> hands out a
/// <c>ref</c>. What holds absolutely is the other direction — the simulation holds no reference to
/// any of this. <b>No caching</b>: a view is built on demand, so it cannot go stale.</para></summary>
public sealed partial class WorldView
{
    private readonly GameDatabase _database;
    private readonly byte _viewingPlayerId;
    private readonly TerrainDef? _fogTerrain;

    /// <summary>
    /// Asset keys per terrain and room id, worked out once. A dictionary rather
    /// than a rebuild per tile because a 61×61 level is 3,721 tiles a frame and
    /// the answer only depends on the id.
    /// </summary>
    private readonly Dictionary<byte, AssetRef> _terrainAssets = [];
    private readonly Dictionary<byte, bool> _terrainSolid = [];
    private readonly Dictionary<byte, AssetRef> _roomFurniture = [];
    private readonly Dictionary<byte, AssetRef> _creatureMeshes = [];
    private readonly Dictionary<byte, AssetRef> _objectMeshes = [];
    private readonly Dictionary<byte, TerrainPieceKeys> _pieceKeys = [];
    private readonly bool[] _solidById = new bool[256];
    private readonly bool[] _ownWallsById = new bool[256];
    private readonly Dictionary<byte, RoomPieceKeys> _roomKeys = [];
    private readonly Dictionary<byte, RoomPieceKeys> _destroyedHeartKeys = [];

    // What the floors read of whole rooms, found once a tick (EnsureRooms).
    private readonly HashSet<int> _checkerSlots = [];
    private readonly List<RoomFacts> _roomFacts = [];
    private int[] _roomOf = [];
    private WorldState? _roomsWorld;
    private GameTick _roomsTick;

    /// <summary>
    /// Creates one keeper's view of the world. Player 1 remains the default for
    /// existing single-player front ends; multiplayer callers must select the
    /// keeper whose screen they are producing.
    /// </summary>
    public WorldView(GameDatabase database)
        : this(database, PlayerIds.Good)
    {
    }

    public WorldView(GameDatabase database, PlayerId viewingPlayerId)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        if (viewingPlayerId > TileState.MaxTaggingPlayerId)
            throw new ArgumentOutOfRangeException(nameof(viewingPlayerId));
        _viewingPlayerId = viewingPlayerId.Value;
        _fogTerrain = database.Terrain.Values.FirstOrDefault(t =>
            string.Equals(t.Name, "Fog Of War", StringComparison.OrdinalIgnoreCase));

        foreach (KeyValuePair<byte, TerrainDef> entry in database.Terrain)
        {
            TerrainDef terrain = entry.Value;

            // The same choice AssetCatalogue makes, and it has to be the same:
            // a key the catalogue never lists is a key no converter produces.
            _terrainAssets[entry.Key] = terrain.IsSolid
                ? AssetRef.ForTerrain(terrain.Name, AssetKind.Mesh)
                : AssetRef.ForTerrain(terrain.Name, AssetKind.Texture);
            _terrainSolid[entry.Key] = terrain.IsSolid;
            _pieceKeys[entry.Key] = new TerrainPieceKeys(terrain.Name);
            _solidById[entry.Key] = terrain.IsSolid;
        }

        foreach (KeyValuePair<byte, RoomDef> entry in database.RoomsByTerrain)
        {
            _roomFurniture[entry.Key] = AssetRef.ForRoom(entry.Value.Name, AssetKind.Mesh);
            if (AssetCatalogue.RoomPieceCount(entry.Value) > 0) _roomKeys[entry.Key] = new RoomPieceKeys(entry.Value);
            if (entry.Value.TypeId == RoomTypeId.DungeonHeart)
                _destroyedHeartKeys[entry.Key] = new RoomPieceKeys(entry.Value, destroyedHeart: true);
            // Room definition +0x43D bit 3, which the loader sets for these constructions
            // (wall-pieces.md §1): a terrain face against such a room is the room's to draw.
            _ownWallsById[entry.Key] = entry.Value.Construction is TileConstruction.Complete
                or TileConstruction.Normal or TileConstruction.DoubleQuad or TileConstruction.HeroGate
                or TileConstruction.HeroGateFrontEndTile or TileConstruction.HeroGate3By1;
        }

        foreach (KeyValuePair<byte, CreatureDef> entry in database.Creatures)
            _creatureMeshes[entry.Key] = AssetRef.ForCreature(entry.Value.Name, AssetKind.Mesh);

        foreach (KeyValuePair<byte, ObjectDef> entry in database.Objects)
            _objectMeshes[entry.Key] = AssetRef.ForObject(entry.Value.Name, AssetKind.Mesh);
    }

    private bool _previewing;
    private int _previewX1, _previewY1, _previewX2, _previewY2;

    /// <summary>
    /// The tag drag in progress, corner to corner, or nulls for none: every Taggable tile inside it
    /// shows as tagged (<see cref="TileView.Tagged"/>), as DK2 shows those inside the bridge
    /// rectangle <c>+0x10..+0x1C</c> (<c>0x0043E860</c>; tag-highlight.md). HYPOTHESIS: what fills
    /// that rectangle is unread, so that only the tag tool fills it, and with both corners
    /// inclusive, is this renderer's guess.
    /// </summary>
    public void PreviewTags(TilePos? from, TilePos? to)
    {
        _previewing = from is not null && to is not null;
        if (!_previewing) return;
        TilePos a = from!.Value, b = to!.Value;
        (_previewX1, _previewX2) = a.X <= b.X ? (a.X, b.X) : (b.X, a.X);
        (_previewY1, _previewY2) = a.Y <= b.Y ? (a.Y, b.Y) : (b.Y, a.Y);
    }

    private bool ShowsTag(in TileState tile, TerrainDef terrain, int x, int y) =>
        tile.IsTaggedBy(_viewingPlayerId)
        || (_previewing && terrain.IsTaggable
            && x >= _previewX1 && x <= _previewX2 && y >= _previewY1 && y <= _previewY2);

    /// <summary>Every tile, in row order.</summary>
    public IEnumerable<TileView> Tiles(WorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                yield return TileAt(world, x, y);
    }

    /// <summary>One tile.</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/fog-of-war.md")]
    public TileView TileAt(WorldState world, int x, int y)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!world.Contains(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the world.");

        // Copied by value on purpose: a renderer gets no route back into state.
        TileState tile = world.Tile(x, y);
        TerrainDef terrain = _database.TerrainById(tile.TerrainId);

        if (world.FogOfWarEnabled &&
            !tile.IsDiscoveredBy(_viewingPlayerId) &&
            !terrain.IsRevealedThroughFog)
        {
            TerrainDef fog = _fogTerrain ?? throw new InvalidOperationException(
                "Fog of war is enabled but the database has no 'Fog Of War' terrain definition.");
            _terrainAssets.TryGetValue(fog.TerrainId, out AssetRef fogGround);
            _terrainSolid.TryGetValue(fog.TerrainId, out bool fogSolid);

            return new TileView(
                new TilePos(x, y),
                fogGround,
                default,
                ownerPlayerId: 0,
                ShowsTag(tile, fog, x, y),
                fogSolid,
                goldHeld: 0,
                HealthPermille(fog.StartingHealth, fog.MaxHealth),
                explored: false);
        }

        _terrainAssets.TryGetValue(tile.TerrainId, out AssetRef ground);
        _roomFurniture.TryGetValue(tile.TerrainId, out AssetRef furniture);
        _terrainSolid.TryGetValue(tile.TerrainId, out bool solid);

        return new TileView(
            new TilePos(x, y),
            ground,
            furniture,
            terrain.IsOwnable ? tile.OwnerPlayerId : (byte)0,
            ShowsTag(tile, terrain, x, y),
            solid,
            tile.GoldHeld,
            HealthPermille(tile.Health, terrain.MaxHealth),
            WallsOf(world, x, y, tile.TerrainId),
            PiecesOf(world, x, y, tile, terrain),
            _pieceKeys[tile.TerrainId],
            !world.FogOfWarEnabled || tile.IsDiscoveredBy(_viewingPlayerId),
            world.DoorAt(x, y) is not null,
            RoomFloorOf(world, x, y, tile),
            RoomKeysOf(world, tile),
            BedOf(world, x, y, tile, terrain, out TerrainPieceKeys? bedKeys),
            bedKeys,
            SurfaceOf(tile, terrain));
    }

    /// <summary>The per-tile pass's liquid arm (<c>0x0043D1C5..0x0043D394</c>): water and lava
    /// terrain draw their surface; a bridge draws water's over water and none over lava.</summary>
    private RoomPieceKeys? RoomKeysOf(WorldState world, in TileState tile) =>
        IsRuinedHeart(world, tile) ? _destroyedHeartKeys[tile.TerrainId]
        : _roomKeys.TryGetValue(tile.TerrainId, out RoomPieceKeys? keys) ? keys : null;

    private bool IsRuinedHeart(WorldState world, in TileState tile) =>
        _destroyedHeartKeys.ContainsKey(tile.TerrainId) && world.HasPlayer(tile.OwnerPlayerId)
        && world.Player(tile.OwnerPlayerId).HeartRuined;

    private LiquidSurface SurfaceOf(in TileState tile, TerrainDef terrain)
    {
        if (terrain.IsLava) return LiquidSurface.Lava;
        if (terrain.IsWater) return LiquidSurface.Water;
        return BridgeRules.SpannedTerrain(_database, tile) is { IsWater: true } ? LiquidSurface.Water : LiquidSurface.None;
    }

    // The quarter table 0x0066DDA0 of the Normal floor and the Quad bridges, (piece, rotation) by a
    // quadrant's (first, diagonal, last) same-room bits: 0 an edge, 1 an outer corner, 2 an inner
    // corner, 3 interior. The Stone Bridge's 0x0066DE60 differs in entries 4 and 6.
    private static readonly (byte Piece, byte Rotation)[] NormalQuarter =
        [(1, 3), (0, 3), (1, 3), (0, 3), (0, 0), (2, 1), (0, 0), (3, 0)];

    private static readonly (byte Piece, byte Rotation)[] StoneBridgeQuarter =
        [(1, 3), (0, 3), (1, 3), (0, 3), (0, 2), (2, 1), (0, 2), (3, 0)];

    // DoubleQuad's 0x0066DDE0: 0..7 by the inner mask, 8..15 by the inverted room mask.
    private static readonly (byte Piece, byte Rotation)[] DoubleQuadQuarter =
    [
        (13, 2), (10, 2), (11, 1), (10, 2), (10, 1), (12, 3), (10, 1), (12, 3),
        (3, 2), (0, 2), (2, 1), (0, 2), (0, 1), (3, 0), (0, 1), (1, 1),
    ];

    // The Heart's 0x0066DBC8, piece << 4 | rotation by dx + 5·dy from the first slab.
    private static readonly byte[] HeartCode =
    [
        0x33, 0x23, 0x23, 0x23, 0x30,
        0x22, 0x00, 0x10, 0x01, 0x20,
        0x22, 0x13, 0x40, 0x11, 0x20,
        0x22, 0x03, 0x12, 0x02, 0x20,
        0x32, 0x21, 0x21, 0x21, 0x31,
    ];

    // The 3×3 rotated 0x0066DB98 by cx + 3·cy: corners piece 0, edges piece 1, the centre piece 2.
    private static readonly byte[] RotatedThreeCode = [0x00, 0x10, 0x01, 0x13, 0x20, 0x11, 0x03, 0x12, 0x02];

    // The bed's whole-tile patterns 0x0066CC30, (want, care) over the liquid mask, the first match
    // winning, with their pieces 0x0066DEC8; then the quarters 0x0066DEA0.
    private static readonly (byte Want, byte Care)[] BedPattern =
    [
        (0x07, 0x57), (0x1C, 0x5D), (0x70, 0x75), (0xC1, 0xD5),
        (0xFD, 0xFF), (0xF7, 0xFF), (0xDF, 0xFF), (0x7F, 0xFF),
        (0xC7, 0xD7), (0x1F, 0x5F), (0xF1, 0xF5), (0x7C, 0x7D), (0xFF, 0xFF),
    ];

    private static readonly (byte Piece, byte Rotation)[] BedWhole =
        [(1, 0), (1, 1), (1, 2), (1, 3), (2, 0), (2, 1), (2, 2), (2, 3), (0, 0), (0, 1), (0, 3), (0, 2), (3, 0)];

    private static readonly (byte Piece, byte Rotation)[] BedQuarter =
        [(5, 1), (4, 1), (5, 1), (4, 1), (4, 2), (6, 3), (4, 2), (7, 2)];

    private const int NormalCentrePiece = 9, NormalInteriorPiece = 3;
    private const int DoubleQuadInteriorPiece = 13, DoubleQuadDoorPiece = 14;
    private const byte WorkshopEmptySlotObjectId = 46;

    /// <summary>
    /// A room tile's floor pieces by its construction (<c>0x00443610</c>'s arms,
    /// floor-and-terrain-pieces.md §4, room-floors-and-liquids.md), from the tile codes <c>0x004EDC10</c> writes:
    /// Normal <c>0x00444970</c>, Quad (bridges) <c>0x00443910</c>, 3×3 <c>0x00446140</c>, 3×3
    /// rotated <c>0x00446210</c>, the Heart <c>0x004462E0</c>, DoubleQuad <c>0x00443F20</c>, and
    /// the 2×2 and 3×1 hero gates <c>0x00446800</c> (room-floors-and-liquids.md §7).
    /// <para>Deviates: the Final, FrontEnd and Tile hero gates and destroyed gates are not built.
    /// The destroyed Heart uses room +0x63's zero constructor default for piece 5.</para>
    /// </summary>
    private PieceSet RoomFloorOf(WorldState world, int x, int y, in TileState tile)
    {
        if (!_database.RoomsByTerrain.TryGetValue(tile.TerrainId, out RoomDef? room)) return default;
        return room.Construction switch
        {
            TileConstruction.Normal => NormalFloor(world, room, x, y, tile),
            TileConstruction.QuadConstruction => QuarterFloor(SameRoomMask(world, x, y, tile),
                room.TypeId == RoomTypeId.StoneBridge ? StoneBridgeQuarter : NormalQuarter),
            TileConstruction.ThreeByThree => ThreeByThreeCell(world, x, y, tile) is int code
                ? default(PieceSet).WithWhole(code, 0) : default,
            TileConstruction.ThreeByThreeRotated => ThreeByThreeCell(world, x, y, tile) is int cell
                ? Coded(RotatedThreeCode[cell]) : default,
            TileConstruction.FiveByFiveRotated => HeartFloor(world, x, y),
            TileConstruction.DoubleQuad => DoubleQuadFloor(world, room, x, y, tile),
            TileConstruction.HeroGate2By2 => HeroGate2By2Floor(world, x, y),
            TileConstruction.HeroGate3By1 => HeroGate3By1Floor(world, x, y, tile),
            _ => default,
        };
    }

    /// <summary>Normal (<c>0x00444970</c>): an edge tile four quarters by its same-room mask; an
    /// interior floor slot the centre piece 9, but for a Workshop slot whose furniture is the empty
    /// object 46; any other interior tile four interior quarters.</summary>
    private PieceSet NormalFloor(WorldState world, RoomDef room, int x, int y, in TileState tile)
    {
        int mask = SameRoomMask(world, x, y, tile);
        if (mask != 0xFF) return QuarterFloor(mask, NormalQuarter);

        PieceSet pieces = default;
        if (IsFloorSlot(world, room, x, y)
            && !(room.TypeId == RoomTypeId.Workshop && room.ObjectIds.Count >= 3
                && room.ObjectIds[AutoFurnitureRules.Choice(x, y, 3)] == WorkshopEmptySlotObjectId))
            return pieces.WithWhole(NormalCentrePiece, 0);

        for (int q = 0; q < 4; q++) pieces = pieces.WithQuarter(q, NormalInteriorPiece, q);
        return pieces;
    }

    /// <summary>Four quarters from a (piece, rotation) table by the mask, each turned by its quadrant.</summary>
    private static PieceSet QuarterFloor(int mask, (byte Piece, byte Rotation)[] table)
    {
        PieceSet pieces = default;
        for (int q = 0; q < 4; q++)
        {
            (byte piece, byte rotation) = table[QuadrantIndex(mask, q)];
            pieces = pieces.WithQuarter(q, piece, rotation + q);
        }
        return pieces;
    }

    private static PieceSet Coded(byte code) => default(PieceSet).WithWhole(code >> 4, code & 3);

    /// <summary>The 3×3 cell <c>0x004EF730</c>: cx + 3·cy, cx 0 where the west tile is not this
    /// room, else 1 where the east one is, else 2 (cy from north and south); null, writing nothing,
    /// with an orthogonal neighbour off the map. Deviates: DK2 then leaves the byte as it was and
    /// draws that (no shipped Portal touches the edge); and its "same room" is the room instance
    /// (<c>0x004559B0</c>), here this type and owner, which <see cref="RoomIndex"/> joins.</summary>
    private static int? ThreeByThreeCell(WorldState world, int x, int y, in TileState tile)
    {
        if (!world.Contains(x - 1, y) || !world.Contains(x + 1, y)
            || !world.Contains(x, y - 1) || !world.Contains(x, y + 1))
            return null;
        int mask = SameRoomMask(world, x, y, tile);
        int cx = (mask & (1 << 6)) == 0 ? 0 : (mask & (1 << 2)) != 0 ? 1 : 2;
        int cy = (mask & (1 << 0)) == 0 ? 0 : (mask & (1 << 4)) != 0 ? 1 : 2;
        return cx + (3 * cy);
    }

    /// <summary>The Heart (<c>0x004EFBE0</c>, <c>0x004462E0</c>): the 5×5 table by the tile's offset
    /// from the room's first slab; the centre's piece 4 draws nothing, nor does a tile beyond 5×5.</summary>
    private PieceSet HeartFloor(WorldState world, int x, int y)
    {
        if (FactsAt(world, x, y) is not { } room) return default;
        int dx = x - room.FirstX, dy = y - room.FirstY;
        if (dx is < 0 or > 4 || dy is < 0 or > 4) return default;
        byte code = HeartCode[dx + (5 * dy)];
        if (IsRuinedHeart(world, world.Tile(x, y)))
            return dx == 1 && dy == 1 ? default(PieceSet).WithWhole(5, 0) : Coded(code);
        return code >> 4 == 4 ? default : Coded(code);
    }

    /// <summary>The 2×2 hero gate (<c>0x004F0070</c>, <c>0x00446800</c>): piece dx + 2·dy whole at
    /// rotation 0 by the tile's offset from the room's first slab (table <c>0x0066DD88</c>); a tile
    /// beyond the 2×2 takes code 0x10, piece 1.</summary>
    private PieceSet HeroGate2By2Floor(WorldState world, int x, int y)
    {
        if (FactsAt(world, x, y) is not { } room) return default;
        int dx = x - room.FirstX, dy = y - room.FirstY;
        return dx is >= 0 and <= 1 && dy is >= 0 and <= 1
            ? default(PieceSet).WithWhole(dx + (2 * dy), 0)
            : default(PieceSet).WithWhole(1, 0);
    }

    /// <summary>The 3×1 hero gate (<c>0x004F00F0</c>, <c>0x00446800</c>): piece k whole at rotation
    /// (o + 1) &amp; 3, o the gate's orientation. Along x for o 0 and 2, along y for 1 and 3: for o 0
    /// or 1, k = 3 where the tile two back (−x or −y) is this gate, else 4 where the one back is,
    /// else 5; for o 2 or 3 the reverse (5, 4, 3).
    /// <para>Deviates: DK2 takes o from the level's room record (direction / 2, the THINGS loader at
    /// <c>0x00521420</c>), which the world does not keep; this infers it as the side opposite the
    /// one solid orthogonal neighbour of the gate's middle tile, which matches all 41 shipped 3×1
    /// records. With no such side there is no o, and DK2 leaves the code unwritten: nothing is
    /// drawn.</para></summary>
    private PieceSet HeroGate3By1Floor(WorldState world, int x, int y, in TileState tile)
    {
        if (HeroGateOrientation(world, x, y, tile) is not int o) return default;
        int sx = (o & 1) == 0 ? 1 : 0, sy = 1 - sx;
        bool twoBack = SameRoomTile(world, x - (2 * sx), y - (2 * sy), tile);
        bool oneBack = SameRoomTile(world, x - sx, y - sy, tile);
        int k = o <= 1
            ? twoBack ? 3 : oneBack ? 4 : 5
            : twoBack ? 5 : oneBack ? 4 : 3;
        return default(PieceSet).WithWhole(k, o + 1);
    }

    /// <summary>The 3×1 gate's orientation o (room <c>+0x9B</c>), inferred from its middle tile: the
    /// side opposite its only solid orthogonal neighbour; null without exactly one.</summary>
    private int? HeroGateOrientation(WorldState world, int x, int y, in TileState tile)
    {
        // The middle tile: this one when the gate runs on both ways along an axis, else its neighbour.
        int mx = x, my = y;
        bool w = SameRoomTile(world, x - 1, y, tile), e = SameRoomTile(world, x + 1, y, tile);
        bool n = SameRoomTile(world, x, y - 1, tile), s = SameRoomTile(world, x, y + 1, tile);
        if (w != e) mx += e ? 1 : -1;
        else if (n != s) my += s ? 1 : -1;

        int face = -1;
        for (int side = 0; side < 4; side++)
        {
            int nx = mx + Dx8[side * 2], ny = my + Dy8[side * 2];
            if (!world.Contains(nx, ny) || !_solidById[world.Tile(nx, ny).TerrainId]) continue;
            if (face >= 0) return null;
            face = side;
        }
        return face < 0 ? null : (face + 2) & 3;
    }

    /// <summary>The tile is on the map with this tile's room terrain and owner
    /// (<c>Room_TileIsOwnedType</c>'s test as the gate writer uses it).</summary>
    private static bool SameRoomTile(WorldState world, int x, int y, in TileState tile)
    {
        if (!world.Contains(x, y)) return false;
        TileState other = world.Tile(x, y);
        return other.TerrainId == tile.TerrainId && other.OwnerPlayerId == tile.OwnerPlayerId;
    }

    /// <summary>DoubleQuad (<c>0x00443F20</c>, room-floors-and-liquids.md §4): the Prison's door tile and the Combat Pit's
    /// entry tile one piece 14 in the north-west quarter; an interior tile piece 13 four times;
    /// otherwise each quarter an inner-ring piece by its interior neighbours, or else an outer-ring
    /// piece by the room mask inverted, turned half a turn further.</summary>
    private PieceSet DoubleQuadFloor(WorldState world, RoomDef room, int x, int y, in TileState tile)
    {
        PieceSet pieces = default;
        if (room.TypeId is RoomTypeId.Prison or RoomTypeId.CombatPit
            && FactsAt(world, x, y) is { } facts && facts.DoorTile == (y * world.Width) + x)
            return pieces.WithQuarter(0, DoubleQuadDoorPiece, 0);

        int mask = SameRoomMask(world, x, y, tile);
        if (mask == 0xFF)
        {
            for (int q = 0; q < 4; q++) pieces = pieces.WithQuarter(q, DoubleQuadInteriorPiece, q);
            return pieces;
        }

        // A neighbour of this room counts as interior when its own mask is full.
        int inner = 0;
        for (int k = 0; k < 8; k++)
        {
            int nx = x + Dx8[k], ny = y + Dy8[k];
            if ((mask & (1 << k)) != 0 && SameRoomMask(world, nx, ny, world.Tile(nx, ny)) == 0xFF) inner |= 1 << k;
        }

        for (int q = 0; q < 4; q++)
        {
            int i = QuadrantIndex(inner, q);
            (byte piece, byte rotation) = i != 0
                ? DoubleQuadQuarter[i]
                : DoubleQuadQuarter[8 + QuadrantIndex(~mask & 0xFF, q)];
            pieces = pieces.WithQuarter(q, piece, rotation + q + (i != 0 ? 0 : 2));
        }
        return pieces;
    }

    /// <summary>Tile byte <c>+0xC</c> for a room tile (<c>0x004EFF00</c>): bit k set where neighbour
    /// k is on the map with this tile's terrain and owner.</summary>
    private static int SameRoomMask(WorldState world, int x, int y, in TileState tile)
    {
        int mask = 0;
        for (int k = 0; k < 8; k++)
        {
            int nx = x + Dx8[k], ny = y + Dy8[k];
            if (!world.Contains(nx, ny)) continue;
            TileState other = world.Tile(nx, ny);
            if (other.TerrainId == tile.TerrainId && other.OwnerPlayerId == tile.OwnerPlayerId) mask |= 1 << k;
        }
        return mask;
    }

    /// <summary>A quadrant's (first, diagonal, last) bits of directions 2q − 2, 2q − 1, 2q.</summary>
    private static int QuadrantIndex(int mask, int q) =>
        (((mask >> ((2 * q + 6) & 7)) & 1) << 2)
        | (((mask >> ((2 * q + 7) & 7)) & 1) << 1)
        | ((mask >> (2 * q)) & 1);

    /// <summary>
    /// The liquid bed <c>0x0043E440</c> (room-floors-and-liquids.md §3) under a water or lava
    /// tile, or under a bridge over one: the first whole-tile pattern the liquid mask matches, else
    /// four quarters; open water (every neighbour liquid) draws none. The liquid's surface (art 2,
    /// an alpha texture) is drawn by code not yet found.
    /// </summary>
    private PieceSet BedOf(WorldState world, int x, int y, in TileState tile, TerrainDef terrain,
        out TerrainPieceKeys? keys)
    {
        bool bridge = (terrain.Flags & TerrainFlags.ConstructionTypeWater) == 0;
        TerrainDef? liquid = bridge ? BridgeRules.SpannedTerrain(_database, tile) : terrain;
        keys = liquid is null ? null : _pieceKeys[liquid.TerrainId];
        if (liquid is null) return default;

        // 0x00450470 for a liquid tile: off the map, the same terrain or a bridge over it. The
        // bridge's own mask (0x00443910): the liquid's flag or a bridge over the same liquid.
        TerrainFlags flag = liquid.IsLava ? TerrainFlags.Lava : TerrainFlags.Water;
        int mask = 0;
        for (int k = 0; k < 8; k++)
        {
            int nx = x + Dx8[k], ny = y + Dy8[k];
            if (!world.Contains(nx, ny))
            {
                if (!bridge) mask |= 1 << k;
                continue;
            }
            TileState other = world.Tile(nx, ny);
            bool same = bridge
                ? (_database.TerrainById(other.TerrainId).Flags & flag) != 0
                : other.TerrainId == liquid.TerrainId;
            if (same || (BridgeRules.SpannedTerrain(_database, other) is { } under && under.TerrainId == liquid.TerrainId))
                mask |= 1 << k;
        }

        if (!liquid.IsLava && mask == 0xFF) return default;
        for (int i = 0; i < BedPattern.Length; i++)
            if ((mask & BedPattern[i].Care) == BedPattern[i].Want)
                return default(PieceSet).WithWhole(BedWhole[i].Piece, BedWhole[i].Rotation);
        return QuarterFloor(mask, BedQuarter);
    }

    /// <summary>What a floor reads of its whole room: the first slab (<c>room+0x2D</c>), the
    /// Prison's or Combat Pit's door tile, and the wall phases.</summary>
    private readonly record struct RoomFacts(int FirstX, int FirstY, int DoorTile, int PhaseNorthSouth, int PhaseEastWest);

    private RoomFacts? FactsAt(WorldState world, int x, int y)
    {
        EnsureRooms(world);
        int index = _roomOf[(y * world.Width) + x];
        return index == 0 ? null : _roomFacts[index - 1];
    }

    /// <summary>Finds the rooms, their facts and the checkerboard rooms' floor slots, once a tick:
    /// a whole-map pass that allocates. Keyed by the tick, so a tile changed between ticks (by a
    /// tool, not the simulation) is seen only at the next one.</summary>
    private void EnsureRooms(WorldState world)
    {
        if (ReferenceEquals(_roomsWorld, world) && _roomsTick == world.CurrentTick) return;
        _roomsWorld = world;
        _roomsTick = world.CurrentTick;
        _checkerSlots.Clear();
        _roomFacts.Clear();
        _rooms.Clear();
        if (_roomOf.Length != world.TileCount) _roomOf = new int[world.TileCount];
        else Array.Clear(_roomOf, 0, _roomOf.Length);

        foreach (RoomInstance room in RoomIndex.Build(world, _database))
        {
            RoomTypeId? type = room.Definition.TypeId;
            int door = type is RoomTypeId.Prison or RoomTypeId.CombatPit ? JailBreakRules.DoorTile(world, room) : -1;
            (int northSouth, int eastWest) = WallPhases(world, room);
            int first = SlabChain.FirstSlab(world, room);
            _roomFacts.Add(new RoomFacts(first % world.Width, first / world.Width, door, northSouth, eastWest));
            _rooms.Add(room);
            foreach (int tile in room.TileIndices) _roomOf[tile] = _roomFacts.Count;
            if (AutoFurnitureRules.UsesCheckerboard(type))
                _checkerSlots.UnionWith(AutoFurnitureRules.FloorSlotSet(world, room));
        }
    }

    /// <summary>
    /// The room's wall phases <c>0x004ECEB0</c> (room-floors-and-liquids.md §5): walking the slab chain, the first straight
    /// wall on a north or south side sets the north-south phase to x &amp; 1, on an east or west
    /// side the east-west phase to 1 − (y &amp; 1); −1 while none is found. Bridges take 0 and 0.
    /// <para>Deviates: DK2 keeps a phase once set while the room grows, and the level start turns a
    /// level room's −1 into 0; this finds it afresh from the room as it stands.</para>
    /// </summary>
    private (int NorthSouth, int EastWest) WallPhases(WorldState world, RoomInstance room)
    {
        if (BridgeRules.IsBridge(room.Definition)) return (0, 0);
        int northSouth = -1, eastWest = -1, width = world.Width;
        foreach (int tile in SlabChain.Order(world, room))
        {
            int x = tile % width, y = tile / width;
            for (int side = 0; side < 4; side++)
            {
                if (!StraightWall(world, room, x, y, side)) continue;
                if ((side & 1) == 0) { if (northSouth < 0) northSouth = x & 1; }
                else if (eastWest < 0) eastWest = 1 - (y & 1);
            }
            if (northSouth >= 0 && eastWest >= 0) break;
        }
        return (northSouth, eastWest);
    }

    /// <summary>A straight wall on a side: the tile outside AllowRoomWalls, the two tiles along the
    /// wall in this room, and both diagonals outside solid and AllowRoomWalls.</summary>
    private bool StraightWall(WorldState world, RoomInstance room, int x, int y, int side)
    {
        int d = side * 2;
        return WallTile(world, x + Dx8[d], y + Dy8[d], solid: false)
            && InRoom(world, room, x + Dx8[(d + 6) & 7], y + Dy8[(d + 6) & 7])
            && InRoom(world, room, x + Dx8[(d + 2) & 7], y + Dy8[(d + 2) & 7])
            && WallTile(world, x + Dx8[(d + 7) & 7], y + Dy8[(d + 7) & 7], solid: true)
            && WallTile(world, x + Dx8[(d + 1) & 7], y + Dy8[(d + 1) & 7], solid: true);
    }

    private bool WallTile(WorldState world, int x, int y, bool solid)
    {
        if (!world.Contains(x, y)) return false;
        TerrainDef terrain = _database.TerrainById(world.Tile(x, y).TerrainId);
        return terrain.Flags.HasFlag(TerrainFlags.AllowRoomWalls) && (!solid || terrain.IsSolid);
    }

    private static bool InRoom(WorldState world, RoomInstance room, int x, int y) =>
        world.Contains(x, y) && room.Contains((y * world.Width) + x);

    /// <summary>Whether the whole wall on <paramref name="side"/> is the decorated piece 8: on north
    /// and south faces where x plus the north-south phase is even, on east and west faces where y
    /// plus the east-west phase is odd (<c>0x004452F1..0x0044535F</c>).</summary>
    private bool Decorated(WorldState world, int x, int y, int side)
    {
        if (FactsAt(world, x, y) is not { } room) return false;
        return (side & 1) == 0
            ? room.PhaseNorthSouth >= 0 && ((x + room.PhaseNorthSouth) & 1) == 0
            : room.PhaseEastWest >= 0 && ((y + room.PhaseEastWest) & 1) == 1;
    }

    /// <summary><c>Room_IsFloorSlot</c> <c>0x00454110</c> for an interior tile: always, but for
    /// the checkerboard rooms only the room's own parity. Deviates: DK2 also refuses the Graveyard
    /// cell holding object 75, which then draws interior quarters, not piece 9.</summary>
    private bool IsFloorSlot(WorldState world, RoomDef room, int x, int y)
    {
        if (!AutoFurnitureRules.UsesCheckerboard(room.TypeId)) return true;
        EnsureRooms(world);
        return _checkerSlots.Contains((y * world.Width) + x);
    }

    // The claimed-quarter table 0x0066DA08: the piece for a quadrant's (side, diagonal, side) edge
    // bits. 3 interior, 0 and 4 the two straight edges, 1 an outer corner, 2 an inner corner.
    private static readonly byte[] ClaimedQuarterPiece = [3, 0, 2, 0, 4, 1, 4, 1];

    // Terrains 0x1F (Mana Vault: MANA_VAULT or MANA_VAULT_CLAIMED by owner) and 0x23 (another
    // quarter source, vtable +0x2AC) have arms of their own in 0x0043E860 that are not built.
    private const byte ManaVaultTerrainId = 0x1F, OtherQuarterTerrainId = 0x23;

    /// <summary>
    /// The plain terrain builder <c>0x0043E860</c> (floor-and-terrain-pieces.md §5): a solid tile
    /// draws a face on each side whose neighbour is off the map, or explored and open, except
    /// against a room that draws its own walls when this terrain is AllowRoomWalls; then a top, or
    /// for a PlayerColouredWall the owner's top quarters. An open tile draws the owner's floor
    /// quarters when PlayerColouredPath, else its whole floor.
    /// <para>Deviates: solidity stands in for the art test and for nav-solidity; the face test's
    /// fog exceptions, hero walls, the Mana Vault, terrain 0x23 and the top variant are not built
    /// (floor-and-terrain-pieces.md §5).</para>
    /// </summary>
    private TerrainPieces PiecesOf(WorldState world, int x, int y, in TileState tile, TerrainDef terrain)
    {
        TerrainFlags flags = terrain.Flags;
        if ((flags & (TerrainFlags.Room | TerrainFlags.ConstructionTypeWater)) != 0
            || tile.TerrainId is ManaVaultTerrainId or OtherQuarterTerrainId)
            return default;

        // The owner's set: player id − 1, so 0 the heroes and 1 neutral (AssetCatalogue.ClaimedSets).
        int set = Math.Clamp((tile.OwnerPlayerId == 0 ? PlayerIds.Neutral : tile.OwnerPlayerId) - 1,
            0, AssetCatalogue.ClaimedSets - 1);
        TerrainPieces pieces = default;
        if (terrain.IsSolid)
        {
            bool allowRoomWalls = (flags & TerrainFlags.AllowRoomWalls) != 0;
            for (int side = 0; side < 4; side++)
            {
                int nx = x + Dx8[side * 2], ny = y + Dy8[side * 2];
                // Off the map the face is drawn (0x0043F1F6..0x0043F224).
                if (world.Contains(nx, ny))
                {
                    if (!IsVisible(world, new TilePos(nx, ny)) || IsSolid(world, nx, ny)) continue;
                    if (allowRoomWalls && _ownWallsById[world.Tile(nx, ny).TerrainId]) continue;
                }
                pieces = pieces.WithFace(side);
            }

            if ((flags & TerrainFlags.PlayerColouredWall) != 0)
                return pieces.WithQuarters(QuarterKind.Top, set, QuarterPieces(TopEdges(world, x, y)));
            return pieces.WithTop();
        }

        if ((flags & TerrainFlags.PlayerColouredPath) != 0)
            return pieces.WithQuarters(QuarterKind.Floor, set, QuarterPieces(FloorEdges(world, x, y, tile.TerrainId)));
        return pieces.WithFloor();
    }

    /// <summary>A wall top's live scan: bit k set where neighbour k is on the map and open.
    /// LIKELY: DK2's scan (<c>0x00440001..0x00440068</c>) also needs the neighbour clear of
    /// <c>0x004434F0</c>'s grid bit 15, and explored or a flag set; those are not built, and
    /// terrain solidity stands in for vtable <c>+0x208</c> (nav-solid).</summary>
    private int TopEdges(WorldState world, int x, int y)
    {
        int mask = 0;
        for (int k = 0; k < 8; k++)
        {
            int nx = x + Dx8[k], ny = y + Dy8[k];
            if (world.Contains(nx, ny) && !IsSolid(world, nx, ny)) mask |= 1 << k;
        }
        return mask;
    }

    /// <summary>An open tile's stored mask (<c>0x00450470</c>): bit k set where neighbour k is off
    /// the map or another terrain.</summary>
    private static int FloorEdges(WorldState world, int x, int y, byte terrainId)
    {
        int mask = 0;
        for (int k = 0; k < 8; k++)
        {
            int nx = x + Dx8[k], ny = y + Dy8[k];
            if (!world.Contains(nx, ny) || world.Tile(nx, ny).TerrainId != terrainId) mask |= 1 << k;
        }
        return mask;
    }

    /// <summary>The four quadrants' pieces, packed 3 bits each: quadrant q reads directions
    /// 2q − 2, 2q − 1 and 2q (NW reads W, NW, N), as (first, diagonal, last) bits 2, 1, 0.</summary>
    private static int QuarterPieces(int edges)
    {
        int packed = 0;
        for (int q = 0; q < 4; q++) packed |= ClaimedQuarterPiece[QuadrantIndex(edges, q)] << (3 * q);
        return packed;
    }

    private bool IsSolid(WorldState world, int x, int y) => _solidById[world.Tile(x, y).TerrainId];

    // DK2's eight directions N, NE, E, SE, S, SW, W, NW (0x006BC370).
    private static readonly int[] Dx8 = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] Dy8 = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>
    /// The room wall chooser <c>0x00444F10</c> for a room that draws its own walls (Normal and
    /// DoubleQuad construction): a side with a visible AllowRoomWalls tile outside it gets a wall,
    /// whole where the room runs on both ways along a wall, else two halves.
    /// <para>Deviates as ADR-0392 lists: the same room is the same room terrain and secret doors are
    /// not walls. A whole side is the decorated piece 8 on the room's wall phase
    /// (<see cref="Decorated"/>).</para>
    /// </summary>
    private RoomWalls WallsOf(WorldState world, int x, int y, byte terrainId)
    {
        if (!_database.RoomsByTerrain.TryGetValue(terrainId, out RoomDef? room)
            || room.Construction is not (TileConstruction.Normal or TileConstruction.DoubleQuad or TileConstruction.HeroGate3By1))
            return default;

        if (room.Construction == TileConstruction.HeroGate3By1)
        {
            ref TileState tile = ref world.Tile(x, y);
            if (HeroGateOrientation(world, x, y, tile) is not int orientation) return default;
            PieceSet floor = HeroGate3By1Floor(world, x, y, tile);
            RoomWalls gate = default;
            int face = (orientation + 2) & 3;
            for (int side = 0; side < 4; side++)
            {
                int nx = x + Dx8[side * 2], ny = y + Dy8[side * 2];
                if (!world.Contains(nx, ny) || SameRoomTile(world, nx, ny, tile)
                    || (world.FogOfWarEnabled && !world.Tile(nx, ny).IsDiscoveredBy(_viewingPlayerId))) continue;
                if (side == face) gate = gate.WithWholePiece(side, floor.WholePiece - 3, side - 1);
                else if (TakesRoomWalls(world, nx, ny)) gate = gate.WithWholePiece(side, 6, side - 1);
            }
            return gate;
        }

        RoomWalls walls = default;
        for (int side = 0; side < 4; side++)
        {
            int d = side * 2;
            int nx = x + Dx8[d], ny = y + Dy8[d];
            if (!world.Contains(nx, ny) || !IsVisible(world, new TilePos(nx, ny))
                || !TakesRoomWalls(world, nx, ny))
                continue;
            bool runsOnA = RunsOn(world, x, y, terrainId, (d + 6) & 7, (d + 7) & 7);
            bool runsOnB = RunsOn(world, x, y, terrainId, (d + 2) & 7, (d + 1) & 7);
            walls = runsOnA && runsOnB
                ? walls.WithWhole(side, Decorated(world, x, y, side))
                : walls.WithHalves(side, runsOnA, runsOnB);
        }

        return walls;
    }

    /// <summary>The wall runs on past this tile: the tile along it is the same room and the
    /// diagonal behind it is a solid AllowRoomWalls tile.</summary>
    private bool RunsOn(WorldState world, int x, int y, byte terrainId, int along, int diagonal)
    {
        int sx = x + Dx8[along], sy = y + Dy8[along];
        int dx = x + Dx8[diagonal], dy = y + Dy8[diagonal];
        if (!world.Contains(sx, sy) || !world.Contains(dx, dy)) return false;
        if (world.Tile(sx, sy).TerrainId != terrainId) return false;
        TerrainDef behind = _database.TerrainById(world.Tile(dx, dy).TerrainId);
        return behind.IsSolid && behind.Flags.HasFlag(TerrainFlags.AllowRoomWalls);
    }

    private bool TakesRoomWalls(WorldState world, int x, int y) =>
        _database.TerrainById(world.Tile(x, y).TerrainId).Flags.HasFlag(TerrainFlags.AllowRoomWalls);

    /// <summary>Every creature, in the simulation's own order (which is spawn order).</summary>
    public IEnumerable<CreatureView> Creatures(WorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.InHand) continue;
            TilePos position = creature.Tile;
            if (!IsVisible(world, position)) continue;

            _creatureMeshes.TryGetValue(creature.CreatureTypeId, out AssetRef mesh);

            int posX = creature.PosX, posY = creature.PosY, height = FloorZ;
            TerrainDef terrain = _database.TerrainById(world.Tile(position).TerrainId);
            if ((terrain.IsWater || terrain.IsLava
                    || (_database.RoomsByTerrain.TryGetValue(terrain.TerrainId, out RoomDef? room) && BridgeRules.IsBridge(room)))
                && !WaterSpeedRules.IsFlying(_database.CreatureById(creature.CreatureTypeId), creature))
                height = FloorHeight(world, position.X, position.Y);
            int yaw = (creature.Heading + 1024) & 2047, pitch = 0, roll = 0;
            int previousX = creature.LastPosX, previousY = creature.LastPosY;
            bool meleeAnimation = IsMeleeAnimation(creature, world.CurrentTick);
            bool castAnimation = IsCastAnimation(creature, world.CurrentTick);
            bool playsAnimation = PlaysMappedAnimation(creature, world.CurrentTick);
            AssetRef animation = AnimationFor(creature, world.CurrentTick);
            if (AttachedWheel(world, creature) is { } wheel)
            {
                mesh = animation;
                posX = wheel.PosX;
                posY = wheel.PosY;
                height = wheel.Height;
                yaw = wheel.MeshYaw;
                pitch = wheel.Pitch;
                roll = wheel.Roll;
                if (pitch == 0)
                {
                    int side = (wheel.Heading >> 9) & 3;
                    posX += Dx8[side * 2] * 0x1C2;
                    posY += Dy8[side * 2] * 0x1C2;
                }
                else height += 0x200;
                previousX = posX;
                previousY = posY;
            }

            yield return new CreatureView(
                creature.Id,
                creature.OwnerPlayerId,
                posX,
                posY,
                mesh,
                animation,
                creature.GoldHeld, height, yaw, pitch, roll, previousX, previousY,
                playsAnimation, creature.Activity != CreatureActivity.Dying && !meleeAnimation && !castAnimation,
                meleeAnimation || castAnimation ? new GameTick(creature.SwingStartTick)
                    : creature.Dk2StateId == EatingRules.AfterMealState && creature.StateEnteredTick >= 0
                    ? new GameTick(creature.StateEnteredTick) : null,
                !meleeAnimation && !castAnimation && IsTravelAnimation(creature) ? MovementAnimationSpeed(creature) : null);
        }
    }

    /// <summary>
    /// Every object, in the simulation's own order (which is spawn order).
    ///
    /// <para>Spawn order rather than tile order, deliberately and for the same
    /// reason creatures are: it is the order the simulation holds them in, so a
    /// renderer that diffs two frames sees a stable sequence, and an object that
    /// moves does not jump position in the list.</para>
    /// </summary>
    public IEnumerable<ObjectView> Objects(WorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        foreach (ObjectState instance in world.Objects)
        {
            if (instance.InHand) continue;
            var position = new TilePos(
                instance.TileIndex % world.Width, instance.TileIndex / world.Width);
            if (!IsVisible(world, position) || BedHidden(world, instance)) continue;

            yield return Placed(world, instance, position);
        }
    }

    /// <summary>
    /// The animation key for a creature's current activity.
    ///
    /// <para>The mapping is <c>CreatureActivity</c> → the snake_case name in
    /// <see cref="AssetCatalogue.CreatureAnimations"/>, and the two lists are
    /// asserted equal by <c>AssetCatalogueTests</c> — so a new activity fails a
    /// test rather than silently rendering as nothing.</para>
    /// </summary>
    public AssetRef AnimationFor(CreatureState creature, GameTick tick)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));

        CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
        string clip = ActivityKeySegment(creature.Activity);
        if (IsMeleeAnimation(creature, tick)) clip = "fighting";
        else if (IsCastAnimation(creature, tick)) clip = "casting";
        else if (creature.Dk2StateId == EatingRules.AfterMealState) clip = "eating";
        else if (creature.Activity is CreatureActivity.Imprisoned or CreatureActivity.AtWork or CreatureActivity.PitFighting or CreatureActivity.Idle or CreatureActivity.Fighting
            && IsTravelAnimation(creature))
            clip = creature.Running ? "running" : "moving_to_job";
        else if (creature.Activity == CreatureActivity.Working
            && definition.Name.Equals("Imp", StringComparison.OrdinalIgnoreCase))
            clip = creature.LastTaskKind switch
            {
                ImpTaskKind.Claim => "claiming",
                ImpTaskKind.Reinforce or ImpTaskKind.Fix => "reinforcing",
                _ => clip,
            };
        else if (creature.Activity == CreatureActivity.Fighting) clip = "idle";
        else if (creature.Running && IsTravelAnimation(creature)) clip = "running";
        return AssetRef.ForCreature(
            definition.Name,
            AssetKind.Animation,
            clip);
    }

    // Enable only the existing source mappings whose action is represented by this view.
    // Other job actions still need their own art slots and clocks.
    public bool PlaysMappedAnimation(CreatureState creature, GameTick tick) =>
        IsMeleeAnimation(creature, tick) || IsCastAnimation(creature, tick)
        || creature.Dk2StateId == EatingRules.AfterMealState || creature.Activity switch
    {
        CreatureActivity.Idle or CreatureActivity.Sleeping or CreatureActivity.Dying or CreatureActivity.Fighting
            or CreatureActivity.BeingTortured => true,
        CreatureActivity.Working => creature.LastTaskKind is ImpTaskKind.Excavate or ImpTaskKind.Mine
            or ImpTaskKind.Claim or ImpTaskKind.Reinforce or ImpTaskKind.Fix,
        _ => IsTravelAnimation(creature),
    };

    private readonly Dictionary<byte, int> _meleeHoldTicks = [];
    private readonly Dictionary<byte, int> _castHoldTicks = [];

    private bool IsCastAnimation(CreatureState creature, GameTick tick)
    {
        if (creature.SwingInProgress)
            return creature.SwingKind is DK2.Simulation.Combat.SpellRules.SpellCast
                or DK2.Simulation.Combat.SpellRules.FreeAimCast;
        if (creature.Possessed
            || creature.Activity is not (CreatureActivity.Fighting or CreatureActivity.PitFighting)
            || creature.SwingTargetKind == DK2.Simulation.Combat.FightTargetKind.None
            || creature.SwingSpellSlot >= creature.SpellStamps.Length
            || creature.SpellStamps[creature.SwingSpellSlot] != creature.SwingStartTick
            || creature.AttackType is not (DK2.Simulation.Combat.CombatRules.AttackMode.SpellAtCreature
                or DK2.Simulation.Combat.CombatRules.AttackMode.SpellAtThing
                or DK2.Simulation.Combat.CombatRules.AttackMode.SpellAtAlly)
            || creature.StateEnteredTick > creature.SwingStartTick) return false;
        if (!_castHoldTicks.TryGetValue(creature.CreatureTypeId, out int hold))
        {
            CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
            int slot = AnimationTimingRules.ResolveArtSlot(definition, 6);
            _castHoldTicks[creature.CreatureTypeId] = hold = AnimationTimingRules.ActionHoldTicks(definition.ArtSlot(slot));
        }
        long elapsed = tick.Value - creature.SwingStartTick;
        return elapsed >= 0 && elapsed < hold;
    }

    private bool IsMeleeAnimation(CreatureState creature, GameTick tick)
    {
        if (creature.SwingKind != DK2.Simulation.Combat.SpellRules.MeleeSwing) return false;
        if (creature.SwingInProgress) return true;
        if (creature.Activity is not (CreatureActivity.Fighting or CreatureActivity.PitFighting)
            || creature.SwingTargetKind == DK2.Simulation.Combat.FightTargetKind.None
            || creature.TimeLastUsedMelee != creature.SwingStartTick
            || creature.AttackType is not (DK2.Simulation.Combat.CombatRules.AttackMode.MeleeAtCreature
                or DK2.Simulation.Combat.CombatRules.AttackMode.MeleeAtThing)
            || creature.StateEnteredTick > creature.SwingStartTick) return false;
        if (!_meleeHoldTicks.TryGetValue(creature.CreatureTypeId, out int hold))
            _meleeHoldTicks[creature.CreatureTypeId] = hold = AnimationTimingRules.ActionHoldTicks(
                _database.CreatureById(creature.CreatureTypeId).ArtSlot(5));
        long elapsed = tick.Value - creature.SwingStartTick;
        return elapsed >= 0 && elapsed < hold;
    }

    private static bool IsTravelAnimation(CreatureState creature) => creature.Activity switch
    {
        CreatureActivity.Fighting => creature.HasRoute,
        CreatureActivity.AtWork or CreatureActivity.PitFighting or CreatureActivity.Idle => creature.HasRoute
            && creature.Dk2StateId is 6 or 7,
        CreatureActivity.MovingToJob or CreatureActivity.DeliveringGold or CreatureActivity.GoingToSleep
            or CreatureActivity.Leaving or CreatureActivity.FetchingGold or CreatureActivity.SeekingLair
            or CreatureActivity.FetchingBody or CreatureActivity.CarryingBody or CreatureActivity.FetchingObject
            or CreatureActivity.CarryingObject or CreatureActivity.SeekingFood or CreatureActivity.FetchingChicken
            or CreatureActivity.SeekingWages or CreatureActivity.GoingToJob or CreatureActivity.Fleeing
            or CreatureActivity.Withdrawing or CreatureActivity.GoingToBody or CreatureActivity.Rescuing
            or CreatureActivity.HeedingCallToArms or CreatureActivity.Imprisoned => creature.HasRoute,
        _ => false,
    };

    private static Fp32 MovementAnimationSpeed(CreatureState creature)
    {
        int x = Math.Abs(creature.Body.Vel.X >> 4);
        int y = Math.Abs(creature.Body.Vel.Y >> 4);
        return new Fp32(x + y - (Math.Min(x, y) >> 1));
    }

    /// <summary>
    /// <c>MovingToJob</c> → <c>moving_to_job</c>. A <c>switch</c> rather than
    /// reflection over the enum name, so adding an activity without deciding its
    /// animation does not compile.
    /// </summary>
    public static string ActivityKeySegment(CreatureActivity activity) => activity switch
    {
        CreatureActivity.Idle => "idle",
        CreatureActivity.MovingToJob => "moving_to_job",
        CreatureActivity.Working => "working",
        CreatureActivity.DeliveringGold => "delivering_gold",
        CreatureActivity.GoingToSleep => "going_to_sleep",
        CreatureActivity.Sleeping => "sleeping",
        CreatureActivity.Dying => "dying",
        CreatureActivity.Leaving => "leaving",
        CreatureActivity.FetchingGold => "fetching_gold",
        CreatureActivity.SeekingLair => "seeking_lair",
        CreatureActivity.Dead => "dead",
        CreatureActivity.FetchingBody => "fetching_body",
        CreatureActivity.CarryingBody => "carrying_body",
        CreatureActivity.Imprisoned => "imprisoned",
        CreatureActivity.FetchingObject => "fetching_object",
        CreatureActivity.CarryingObject => "carrying_object",
        CreatureActivity.SeekingFood => "seeking_food",
        CreatureActivity.FetchingChicken => "fetching_chicken",
        CreatureActivity.SeekingWages => "seeking_wages",
        CreatureActivity.GoingToJob => "going_to_job",
        CreatureActivity.AtWork => "at_work",
        CreatureActivity.Fighting => "fighting",
        CreatureActivity.Fleeing => "fleeing",
        CreatureActivity.Withdrawing => "withdrawing",
        CreatureActivity.RaisingAlarm => "raising_alarm",
        CreatureActivity.Reporting => "reporting",
        CreatureActivity.Frozen => "frozen",
        CreatureActivity.Stunned => "stunned",
        CreatureActivity.Celebrating => "celebrating",
        CreatureActivity.CollectingWinnings => "collecting_winnings",
        CreatureActivity.GoingToBody => "going_to_body",
        CreatureActivity.BeingTortured => "being_tortured",
        CreatureActivity.Rescuing => "rescuing",
        CreatureActivity.PitFighting => "pit_fighting",
        CreatureActivity.HeedingCallToArms => "heeding_call_to_arms",
        CreatureActivity.BeingLifted => "being_lifted",
        CreatureActivity.Chicken => "chicken",
        CreatureActivity.JackInTheBox => "jack_in_the_box",
        CreatureActivity.UnbuiltHandler => "unbuilt_handler",
        CreatureActivity.ReaperArriving => "reaper_arriving",
        CreatureActivity.Slapped => "slapped",
        _ => throw new ArgumentOutOfRangeException(
            nameof(activity), activity, "No animation key for this activity."),
    };

    private static int HealthPermille(int health, int maxHealth)
    {
        if (maxHealth <= 0) return 1000;
        long permille = (long)health * 1000 / maxHealth;
        return permille < 0 ? 0 : permille > 1000 ? 1000 : (int)permille;
    }

    private bool IsVisible(WorldState world, TilePos position)
    {
        if (!world.FogOfWarEnabled) return true;
        return world.Tile(position).IsDiscoveredBy(_viewingPlayerId);
    }
}
