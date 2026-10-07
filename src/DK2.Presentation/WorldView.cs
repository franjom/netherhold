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
        RoomWalls walls = default)
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

    /// <summary>Marked for digging by somebody — the player's own marks are visible in DK2.</summary>
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
        int goldHeld)
    {
        Id = id;
        OwnerPlayerId = ownerPlayerId.Value;
        PosX = posX;
        PosY = posY;
        Mesh = mesh;
        Animation = animation;
        GoldHeld = goldHeld;
    }

    /// <summary>The simulation's creature id. A renderer's object is a view of this and nothing more.</summary>
    public int Id { get; }

    public byte OwnerPlayerId { get; }

    /// <summary>
    /// Position in <see cref="Fp32"/> tile units, exactly as the simulation holds
    /// it. <b>A renderer converts; the simulation never does</b> (ADR-0001) —
    /// divide by <see cref="Fp32.One"/> for a tile index, or convert to float for
    /// a world position.
    /// </summary>
    public int PosX { get; }
    public int PosY { get; }

    public AssetRef Mesh { get; }

    /// <summary>Which animation to play, chosen from the creature's activity.</summary>
    public AssetRef Animation { get; }

    /// <summary>Gold in its hands. Visible in DK2 — an imp carrying gold looks like it.</summary>
    public int GoldHeld { get; }

    /// <summary>The tile it is standing in.</summary>
    public TilePos Tile => new(PosX / Fp32.One, PosY / Fp32.One);
}

/// <summary>
/// One object standing on a tile, as a renderer needs it: a bed, a chicken coop,
/// an egg, a pile of gold.
///
/// <para><b>A tile and not a position</b>, because that is all the simulation
/// has. DK2 gives an object a sub-tile position and every one of the three
/// gold-dropping paths picks a different point within the tile
/// (gold-on-the-floor.md §7), so a faithful renderer will want more than this —
/// but inventing a position here would be inventing behaviour, and the field
/// arrives with the first mechanic that moves an object.</para>
/// </summary>
public readonly struct ObjectView
{
    public ObjectView(int id, byte objectTypeId, PlayerId ownerPlayerId, TilePos tile, AssetRef mesh, int goldHeld)
    {
        Id = id;
        ObjectTypeId = objectTypeId;
        OwnerPlayerId = ownerPlayerId.Value;
        Tile = tile;
        Mesh = mesh;
        GoldHeld = goldHeld;
    }

    /// <summary>The simulation's object id, stable for the object's whole life.</summary>
    public int Id { get; }

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
public sealed class WorldView
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
        }

        foreach (KeyValuePair<byte, RoomDef> entry in database.RoomsByTerrain)
            _roomFurniture[entry.Key] = AssetRef.ForRoom(entry.Value.Name, AssetKind.Mesh);

        foreach (KeyValuePair<byte, CreatureDef> entry in database.Creatures)
            _creatureMeshes[entry.Key] = AssetRef.ForCreature(entry.Value.Name, AssetKind.Mesh);

        foreach (KeyValuePair<byte, ObjectDef> entry in database.Objects)
            _objectMeshes[entry.Key] = AssetRef.ForObject(entry.Value.Name, AssetKind.Mesh);
    }

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
                tile.IsTaggedBy(_viewingPlayerId),
                fogSolid,
                goldHeld: 0,
                HealthPermille(fog.StartingHealth, fog.MaxHealth));
        }

        _terrainAssets.TryGetValue(tile.TerrainId, out AssetRef ground);
        _roomFurniture.TryGetValue(tile.TerrainId, out AssetRef furniture);
        _terrainSolid.TryGetValue(tile.TerrainId, out bool solid);

        return new TileView(
            new TilePos(x, y),
            ground,
            furniture,
            terrain.IsOwnable ? tile.OwnerPlayerId : (byte)0,
            tile.IsTaggedBy(_viewingPlayerId),
            solid,
            tile.GoldHeld,
            HealthPermille(tile.Health, terrain.MaxHealth),
            WallsOf(world, x, y, tile.TerrainId));
    }

    // DK2's eight directions N, NE, E, SE, S, SW, W, NW (0x006BC370).
    private static readonly int[] Dx8 = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] Dy8 = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>
    /// The room wall chooser <c>0x00444F10</c> for a room that draws its own walls (Normal and
    /// DoubleQuad construction): a side with a visible AllowRoomWalls tile outside it gets a wall,
    /// whole where the room runs on both ways along a wall, else two halves.
    /// <para>Deviates as ADR-0392 lists: the same room is the same room terrain, secret doors are
    /// not walls, and no furniture phase is kept, so a whole side is always piece 7.</para>
    /// </summary>
    private RoomWalls WallsOf(WorldState world, int x, int y, byte terrainId)
    {
        if (!_database.RoomsByTerrain.TryGetValue(terrainId, out RoomDef? room)
            || room.Construction is not (TileConstruction.Normal or TileConstruction.DoubleQuad))
            return default;

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
                ? walls.WithWhole(side)
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

            yield return new CreatureView(
                creature.Id,
                creature.OwnerPlayerId,
                creature.PosX,
                creature.PosY,
                mesh,
                AnimationFor(creature),
                creature.GoldHeld);
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
            if (!IsVisible(world, position)) continue;

            _objectMeshes.TryGetValue(instance.ObjectTypeId, out AssetRef mesh);

            yield return new ObjectView(
                instance.Id,
                instance.ObjectTypeId,
                instance.OwnerPlayerId,
                position,
                mesh,
                instance.GoldHeld);
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
    public AssetRef AnimationFor(CreatureState creature)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));

        CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
        return AssetRef.ForCreature(
            definition.Name,
            AssetKind.Animation,
            ActivityKeySegment(creature.Activity));
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
