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
        byte ownerPlayerId,
        bool tagged,
        bool solid,
        int goldHeld,
        int healthPermille)
    {
        Position = position;
        Ground = ground;
        Furniture = furniture;
        OwnerPlayerId = ownerPlayerId;
        Tagged = tagged;
        Solid = solid;
        GoldHeld = goldHeld;
        HealthPermille = healthPermille;
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

    /// <summary>Who holds it. 0 is unowned.</summary>
    public byte OwnerPlayerId { get; }

    /// <summary>Marked for digging by somebody — the player's own marks are visible in DK2.</summary>
    public bool Tagged { get; }

    /// <summary>Whether the terrain fills its tile. A renderer needs this before it has a mesh.</summary>
    public bool Solid { get; }

    /// <summary>Gold sitting in this tile (a treasury tile, a heart tile).</summary>
    public int GoldHeld { get; }

    /// <summary>
    /// Health as thousandths of the terrain's maximum, so a renderer can show
    /// damage without knowing the terrain table. An integer, per ADR-0001;
    /// 1000 for a terrain with no meaningful maximum.
    ///
    /// <para><b>Corroborated after the fact.</b> This was added on the reasoning
    /// that a renderer ought not need the terrain table to draw damage. DK2
    /// really does show wall damage, and shows it by swapping the wall's
    /// <i>texture</i> for a damage-state one — CONFIRMED from Simon Schreibt's
    /// captures (<c>docs/research/wall-rendering.md</c>). So a fraction is the
    /// right thing to hand across, and the assets it will select are textures
    /// rather than meshes.</para>
    /// </summary>
    public int HealthPermille { get; }

    public bool IsRoom => !Furniture.IsEmpty;
}

/// <summary>
/// One creature, as a renderer needs it.
/// </summary>
public readonly struct CreatureView
{
    public CreatureView(
        int id,
        byte ownerPlayerId,
        int posX,
        int posY,
        AssetRef mesh,
        AssetRef animation,
        int goldHeld)
    {
        Id = id;
        OwnerPlayerId = ownerPlayerId;
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
/// Turns simulation state into asset-keyed descriptions a renderer can draw —
/// the whole of the presentation boundary the plan draws in §4.7, and the only
/// place where "what the game is doing" becomes "what to put on screen".
///
/// <para><b>Why this exists rather than each renderer reading
/// <see cref="WorldState"/> itself.</b> Every renderer would otherwise repeat the
/// same three decisions — which asset key a terrain implies, which animation an
/// activity implies, how a room's floor differs from its furniture — and they
/// would drift apart. Getting those wrong in two places is worse than getting
/// them wrong in one, because the disagreement then looks like a simulation bug.
/// The Unity view and the harness's text view consume this same type, which is
/// what makes the text view evidence about the Unity view.</para>
///
/// <para><b>Observation only.</b> Nothing here writes to the world; the view
/// types are readonly structs holding copies. Note honestly that this is a
/// convention rather than a compiler guarantee: <see cref="WorldState.Tile(int,int)"/>
/// hands out a <c>ref</c>, so a determined renderer could still mutate. The
/// guarantee the plan wants — "deleting every GameObject must not change the
/// simulation" — holds in the direction that matters, because the simulation has
/// no reference to any of this.</para>
///
/// <para><b>No caching.</b> A view is built on demand from current state, so it
/// cannot go stale and there is no invalidation to get wrong. If that ever costs
/// too much, the fix is to build views for a region rather than to hold one
/// between ticks — a cached view is a second copy of the world, and this project
/// has one authoritative copy on purpose.</para>
/// </summary>
public sealed class WorldView
{
    private readonly GameDatabase _database;

    /// <summary>
    /// Asset keys per terrain and room id, worked out once. A dictionary rather
    /// than a rebuild per tile because a 61×61 level is 3,721 tiles a frame and
    /// the answer only depends on the id.
    /// </summary>
    private readonly Dictionary<byte, AssetRef> _terrainAssets = [];
    private readonly Dictionary<byte, bool> _terrainSolid = [];
    private readonly Dictionary<byte, AssetRef> _roomFurniture = [];
    private readonly Dictionary<byte, AssetRef> _creatureMeshes = [];
    public WorldView(GameDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));

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
    public TileView TileAt(WorldState world, int x, int y)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!world.Contains(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the world.");

        // Copied by value on purpose: a renderer gets no route back into state.
        TileState tile = world.Tile(x, y);
        TerrainDef terrain = _database.TerrainById(tile.TerrainId);

        _terrainAssets.TryGetValue(tile.TerrainId, out AssetRef ground);
        _roomFurniture.TryGetValue(tile.TerrainId, out AssetRef furniture);
        _terrainSolid.TryGetValue(tile.TerrainId, out bool solid);

        return new TileView(
            new TilePos(x, y),
            ground,
            furniture,
            tile.OwnerPlayerId,
            tile.IsTaggedByAnyone,
            solid,
            tile.GoldHeld,
            HealthPermille(tile.Health, terrain.MaxHealth));
    }

    /// <summary>Every creature, in the simulation's own order (which is spawn order).</summary>
    public IEnumerable<CreatureView> Creatures(WorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        foreach (CreatureState creature in world.Creatures)
        {
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
        _ => throw new ArgumentOutOfRangeException(
            nameof(activity), activity, "No animation key for this activity."),
    };

    private static int HealthPermille(int health, int maxHealth)
    {
        if (maxHealth <= 0) return 1000;
        long permille = (long)health * 1000 / maxHealth;
        return permille < 0 ? 0 : permille > 1000 ? 1000 : (int)permille;
    }
}
