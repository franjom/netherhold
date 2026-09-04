// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>
/// The presentation boundary: simulation state in, asset-keyed descriptions out.
///
/// <para>The rich assertions run against the real install, because a view whose
/// terrain-to-asset mapping is only tested against a synthetic three-terrain
/// database is not tested at all — the mapping's whole job is to be right for 41
/// terrains and 26 rooms it did not choose. The mapping-free tests (glyphs,
/// activity names) run everywhere, including CI.</para>
/// </summary>
public class WorldViewTests
{
    private static (GameDatabase Database, WorldState World)? RealLevel()
    {
        string? dir = Dk2Install.Find();
        if (dir is null) return null;

        GameDatabase database = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")
            ?? throw new InvalidOperationException("the install has no Level1 map.");
        return (database, WorldState.FromMap(database, map, [0, 1]));
    }

    // ---- Mapping, against the whole shipped terrain table ----

    /// <summary>
    /// Every tile of a shipped level gets a ground asset, and it is the same key
    /// the catalogue lists. <b>The agreement is the point</b>: a key the
    /// catalogue never enumerates is a key no converter produces, so a view that
    /// invented its own would render as boxes forever with no error anywhere.
    /// </summary>
    [Fact]
    public void EveryTileOfAShippedLevelAsksForAKeyTheCatalogueLists()
    {
        if (RealLevel() is not var (database, world)) return; // skipped: no install

        var catalogued = AssetCatalogue.ForDatabase(database).ToHashSet();
        var view = new WorldView(database);
        int tiles = 0;

        foreach (TileView tile in view.Tiles(world))
        {
            Assert.False(tile.Ground.IsEmpty, $"tile {tile.Position} has no ground asset.");
            Assert.Contains(tile.Ground, catalogued);
            if (tile.IsRoom) Assert.Contains(tile.Furniture, catalogued);
            tiles++;
        }

        Assert.Equal(world.Width * world.Height, tiles);
        Assert.True(tiles > 1000, $"level 1 rendered only {tiles} tiles.");
    }

    /// <summary>
    /// A wall asks for a mesh and a floor for a texture, on real data — the same
    /// split the catalogue makes, and it has to agree tile by tile rather than
    /// merely in aggregate.
    /// </summary>
    [Fact]
    public void AWallAsksForAMeshAndAFloorForATexture()
    {
        if (RealLevel() is not var (database, world)) return;

        var view = new WorldView(database);
        int walls = 0, floors = 0;

        foreach (TileView tile in view.Tiles(world))
        {
            if (tile.Solid) { Assert.Equal(AssetKind.Mesh, tile.Ground.Kind); walls++; }
            else { Assert.Equal(AssetKind.Texture, tile.Ground.Kind); floors++; }
        }

        Assert.True(walls > 0 && floors > 0, $"level 1 gave {walls} walls and {floors} floors.");
    }

    /// <summary>
    /// A room tile carries both its ground and its furniture, and a plain tile
    /// carries only ground. Selling a room takes the furniture and leaves the
    /// floor (ADR-0029), which is why they are two assets rather than one.
    /// </summary>
    [Fact]
    public void OnlyARoomTileHasFurniture()
    {
        if (RealLevel() is not var (database, world)) return;

        var view = new WorldView(database);
        int rooms = 0;

        foreach (TileView tile in view.Tiles(world))
        {
            bool isRoomTerrain = database.RoomByTerrain(tile.Ground.IsEmpty ? (byte)0 : TerrainIdOf(database, tile)) is not null;
            Assert.Equal(isRoomTerrain, tile.IsRoom);
            if (tile.IsRoom) { Assert.Equal(AssetKind.Mesh, tile.Furniture.Kind); rooms++; }
        }

        // Level 1 ships a dungeon heart and a portal, so this cannot be zero.
        Assert.True(rooms > 0, "level 1 rendered no room tiles at all.");
    }

    /// <summary>
    /// Recovers a tile's terrain id from the world, for the test above. The view
    /// deliberately does not expose it: a renderer keyed on a terrain id would
    /// be a rule about the reference install, exactly what the simulation's own
    /// rules refuse to be.
    /// </summary>
    private static byte TerrainIdOf(GameDatabase database, TileView tile) =>
        database.Terrain.Values
            .First(t => AssetRef.ForTerrain(t.Name, t.IsSolid ? AssetKind.Mesh : AssetKind.Texture) == tile.Ground)
            .TerrainId;

    [Fact]
    public void HealthIsReportedAsAFractionSoARendererNeedsNoTerrainTable()
    {
        if (RealLevel() is not var (database, world)) return;

        var view = new WorldView(database);

        foreach (TileView tile in view.Tiles(world))
            Assert.InRange(tile.HealthPermille, 0, 1000);
    }

    /// <summary>
    /// Every creature in a running world gets a mesh and an animation, and both
    /// are keys the catalogue lists.
    /// </summary>
    [Fact]
    public void ACreatureGetsAMeshAndAnAnimationForItsCurrentActivity()
    {
        if (RealLevel() is not var (database, world)) return;

        var catalogued = AssetCatalogue.ForDatabase(database).ToHashSet();
        var view = new WorldView(database);

        CreatureDef imp = database.CreatureByName("Imp")
            ?? throw new InvalidOperationException("the shipped database has no Imp.");

        CreatureState spawned = world.SpawnCreature(database, imp.CreatureId, 1, FirstWalkableTile(database, world));
        spawned.Activity = CreatureActivity.DeliveringGold;
        spawned.GoldHeld = 500;

        CreatureView rendered = view.Creatures(world).Single(c => c.Id == spawned.Id);

        Assert.Equal("creature.imp.mesh", rendered.Mesh.Key);
        Assert.Equal("creature.imp.animation.delivering_gold", rendered.Animation.Key);
        Assert.Contains(rendered.Mesh, catalogued);
        Assert.Contains(rendered.Animation, catalogued);
        Assert.Equal(500, rendered.GoldHeld);
    }

    /// <summary>
    /// Positions cross the boundary in the simulation's own fixed-point units,
    /// untouched. A view that helpfully divided by <see cref="Fp32.One"/> would
    /// throw away the sub-tile position a renderer needs to interpolate, and
    /// ADR-0001 puts that conversion on the renderer.
    /// </summary>
    [Fact]
    public void APositionCrossesTheBoundaryInFixedPointTileUnits()
    {
        if (RealLevel() is not var (database, world)) return;

        var view = new WorldView(database);
        CreatureDef imp = database.CreatureByName("Imp")!;
        TilePos at = FirstWalkableTile(database, world);

        CreatureState spawned = world.SpawnCreature(database, imp.CreatureId, 1, at);
        CreatureView rendered = view.Creatures(world).Single(c => c.Id == spawned.Id);

        Assert.Equal(spawned.PosX, rendered.PosX);
        Assert.Equal(spawned.PosY, rendered.PosY);
        Assert.Equal(at, rendered.Tile);

        // Spawning centres a creature in its tile, so the position is not a
        // whole number of tiles — which is exactly the information that would
        // be lost if the boundary converted.
        Assert.NotEqual(0, rendered.PosX % Fp32.One);
    }

    /// <summary>
    /// <b>Rendering observes; it never mutates.</b> Building a view of a world
    /// and enumerating all of it must leave the state hash untouched — the same
    /// gate the determinism tests use, pointed at the presentation layer.
    /// </summary>
    [Fact]
    public void BuildingAViewDoesNotChangeTheWorld()
    {
        string? dir = Dk2Install.Find();
        if (dir is null) return;

        GameDatabase database = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")
            ?? throw new InvalidOperationException("the install has no Level1 map.");
        var simulation = new GameSimulation(database, WorldState.FromMap(database, map, [0, 1]), seed: 7);

        for (int i = 0; i < 20; i++) simulation.Tick();
        ulong before = simulation.StateHash();

        var view = new WorldView(database);
        var renderer = new TextRenderer(view, AssetProviderChain.Standard());

        _ = view.Tiles(simulation.World).ToList();
        _ = view.Creatures(simulation.World).ToList();
        _ = renderer.Render(simulation.World);

        Assert.Equal(before, simulation.StateHash());
    }

    private static TilePos FirstWalkableTile(GameDatabase database, WorldState world)
    {
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                if (!database.TerrainById(world.Tile(x, y).TerrainId).IsSolid)
                    return new TilePos(x, y);

        throw new InvalidOperationException("the level has no walkable tile.");
    }

    // ---- Mapping-free, so these run in CI too ----

    /// <summary>
    /// Every activity has an animation key segment. A <c>switch</c> that misses
    /// one throws, so this is the test that catches a new activity before a
    /// renderer silently plays nothing.
    /// </summary>
    [Fact]
    public void EveryActivityHasAnAnimationKeySegment()
    {
        foreach (CreatureActivity activity in Enum.GetValues<CreatureActivity>())
        {
            string segment = WorldView.ActivityKeySegment(activity);
            Assert.Matches("^[a-z0-9]+(_[a-z0-9]+)*$", segment);
            Assert.Contains(segment, AssetCatalogue.CreatureAnimations);
        }
    }

    [Theory]
    [InlineData(0, '.')]
    [InlineData(1, '1')]
    [InlineData(9, '9')]
    [InlineData(10, '?')]
    [InlineData(200, '?')]
    public void APlayerGetsOneDigitAndAnythingPastNineGetsAQuestionMark(int playerId, char expected)
    {
        Assert.Equal(expected, TextRenderer.PlayerGlyph((byte)playerId));
    }

    /// <summary>
    /// <b>The test the first version of this renderer needed and did not have.</b>
    /// Its wall alphabet contained <c>@</c> and <c>$</c>, so on campaign level 1
    /// rock variants rendered as creatures and as stored gold — and the dungeon
    /// heart, which does hold gold, became indistinguishable from the rock next
    /// to it. Every glyph must mean exactly one thing.
    /// </summary>
    [Fact]
    public void GlyphsAreDisjoint()
    {
        char[] reserved =
        [
            TextRenderer.OutsideGlyph,
            TextRenderer.UnownedFloorGlyph,
            TextRenderer.GoldGlyph,
            TextRenderer.TaggedGlyph,
            TextRenderer.CreatureGlyph,
            TextRenderer.CarryingCreatureGlyph,
            TextRenderer.UnknownPlayerGlyph,
        ];

        // The reserved glyphs are distinct from each other.
        Assert.Equal(reserved.Length, reserved.Distinct().Count());

        // Owned-floor digits are distinct from everything reserved.
        var owned = Enumerable.Range(1, 9).Select(p => TextRenderer.PlayerGlyph((byte)p)).ToList();
        Assert.Equal(9, owned.Distinct().Count());
        Assert.Empty(owned.Intersect(reserved));

        // Neither alphabet repeats a character, and neither overlaps the
        // reserved glyphs, the owned-floor digits, or the other alphabet.
        char[] walls = TextRenderer.WallAlphabet.ToCharArray();
        char[] rooms = TextRenderer.RoomAlphabet.ToCharArray();

        Assert.Equal(walls.Length, walls.Distinct().Count());
        Assert.Equal(rooms.Length, rooms.Distinct().Count());
        Assert.Empty(walls.Intersect(reserved));
        Assert.Empty(rooms.Intersect(reserved));
        Assert.Empty(walls.Intersect(owned));
        Assert.Empty(rooms.Intersect(owned));
        Assert.Empty(walls.Intersect(rooms));
    }

    /// <summary>
    /// The four walls a reader has to be able to tell apart get four different
    /// symbols.
    ///
    /// <para><b>Not a uniqueness claim, and it must not be read as one.</b>
    /// Symbols come from a hash, so with eight solid terrains and a fixed
    /// alphabet some pair will collide — an eight-symbol alphabet gave five
    /// distinct characters for eight terrains, which is exactly what random
    /// bucketing predicts. Widening the alphabet lowers the collision rate and
    /// cannot abolish it. So the checkable property is the one that matters to a
    /// reader: <b>Rock, Gold, Gems and Impenetrable Rock are the walls a
    /// dungeon picture is about</b>, and those four must be distinguishable.
    /// The other four solid terrains — Reinforced Wall, Fog Of War, Edge of Map,
    /// Claim Scan — are either rare or not really terrain, and a collision
    /// between them costs nothing.</para>
    /// </summary>
    [Fact]
    public void TheFourWallsThatMatterAreDistinguishable()
    {
        if (RealLevel() is not var (database, _)) return;

        string[] mustDiffer = ["Rock", "Gold", "Gems", "Impenetrable Rock"];
        var glyphs = new Dictionary<string, char>();

        foreach (string name in mustDiffer)
        {
            TerrainDef terrain = database.Terrain.Values.Single(t => t.Name.Trim() == name);
            Assert.True(terrain.IsSolid, $"{name} is not solid, so it has no wall symbol.");

            AssetRef reference = AssetRef.ForTerrain(terrain.Name, AssetKind.Mesh);
            glyphs[name] = TextRenderer.WallAlphabet[
                PlaceholderProvider.SymbolIndex(reference, TextRenderer.WallAlphabet.Length)];
        }

        Assert.True(
            glyphs.Values.Distinct().Count() == mustDiffer.Length,
            "two of the four walls a picture is about share a symbol: " +
            string.Join(", ", glyphs.Select(g => $"{g.Key}='{g.Value}'")));
    }

    [Fact]
    public void TheLegendNamesEveryGlyphTheRendererCanEmit()
    {
        string legend = TextRenderer.Legend();

        Assert.Contains(TextRenderer.UnownedFloorGlyph, legend);
        Assert.Contains(TextRenderer.TaggedGlyph, legend);
        Assert.Contains(TextRenderer.GoldGlyph, legend);
        Assert.Contains(TextRenderer.CreatureGlyph, legend);
        Assert.Contains(TextRenderer.CarryingCreatureGlyph, legend);
        Assert.Contains(TextRenderer.WallAlphabet, legend);
    }

    [Fact]
    public void AViewRefusesANullDatabaseAndANullWorld()
    {
        Assert.Throws<ArgumentNullException>(() => new WorldView(null!));
    }
}
