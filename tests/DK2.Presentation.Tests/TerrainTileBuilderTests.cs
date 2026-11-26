// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The per-tile terrain build moved out of the Unity adapter (godot-step3-audit.md, 3a), pinned against the
/// old code (<see cref="LegacyTerrainChunkBuild"/>) on a wall, a room tile, a hero face, a liquid bed, a bridge and the
/// sight fog, vertex for vertex. Unity's quarter turn was a float quaternion, off the exact turn by up to a few 1e-8 of
/// a unit; the builder turns exactly, so positions and normals are compared to 1e-6 of their size (a few float steps
/// once the lattice bends them) and everything else exactly.</summary>
public class TerrainTileBuilderTests
{
    [Fact]
    public void RoomWallStateNamesTheNeighbourRatherThanTheOwningRoom()
    {
        TileView[] tiles = Grid();
        var builder = new TerrainTileBuilder(new FakePieces(), Width, Height, null, true);
        var geometry = new TerrainTileGeometry();
        builder.Prepare(1, 1, tiles, geometry);
        builder.Build(geometry);
        Vector2[] expected = [new(-1, -7), new(-1, -7), new(-1, 5), new(-1, 5),
            new(1, 1), new(6, -1), new(9, 9), new(4, 4), new(4, 4)];
        Assert.Equal(expected.Length, geometry.Pieces.Count);
        for (int p = 0; p < geometry.Pieces.Count; p++)
        {
            for (int v = geometry.PieceStarts[p]; v < geometry.PieceStarts[p] + geometry.Pieces[p].Piece.Positions.Length; v++)
            {
                Assert.Equal(expected[p], geometry.StateSources[v]);
            }
        }
    }

    [Fact]
    public void TextureStateSourcesReconstructEveryLegacyPieceLookAndKeepGeometry()
    {
        foreach (TileView[] tiles in new[] { Grid(), EdgeGrid() })
        {
            for (int i = 0; i < tiles.Length; i++)
        {
            var oldBuilder = new TerrainTileBuilder(new FakePieces(), Width, Height);
            var builder = new TerrainTileBuilder(new FakePieces(), Width, Height, null, true);
            var old = new TerrainTileGeometry();
            var current = new TerrainTileGeometry();
            oldBuilder.Prepare(i % Width, i / Width, tiles, old);
            builder.Prepare(i % Width, i / Width, tiles, current);
            oldBuilder.Build(old);
            builder.Build(current);
            Assert.Equal(old.VertexCount, current.VertexCount);
            for (int v = 0; v < old.VertexCount; v++)
            {
                Assert.Equal(old.Positions[v], current.Positions[v]);
                Assert.Equal(old.Normals[v], current.Normals[v]);
                Assert.Equal(old.Uvs[v], current.Uvs[v]);
            }
            for (int g = 0; g < old.Groups.Count; g++)
            {
                    TerrainTileBuilder.TriangleGroup group = old.Groups[g];
                TerrainMaterialKey key = old.Keys[group.Slot];
                int vertex = old.PieceStarts[group.Piece];
                Vector2 source = current.StateSources[vertex];
                Vector4 own = TerrainTileState.Encode(tiles[i]);
                Vector4 tag = source.X < 0 ? default : TerrainTileState.Encode(tiles[(int)source.X]);
                int tintIndex = source.Y < -1 ? -(int)source.Y - 2 : (int)source.Y;
                Vector4 tint = tintIndex < 0 ? default : TerrainTileState.Encode(tiles[tintIndex]);
                if (key.IsOverlay)
                    {
                        Assert.Equal(new Vector3(TerrainMaterialLook.For(key).Colour.X, TerrainMaterialLook.For(key).Colour.Y,
                        TerrainMaterialLook.For(key).Colour.Z), TerrainTileState.Overlay(own, tag));
                    }
                    else if (key.Variant != TerrainMaterialVariant.Fog)
                    {
                        Assert.Equal(TerrainMaterialLook.For(key).AddedLight,
                        TerrainTileState.AddedLight(key.Recipe, own, tag, tint, source.Y < -1));
                    }
                }
            Assert.DoesNotContain(current.Keys.Take(current.KeyCount), key => key.IsOverlay);
        }
        }
    }
    private const int Width = 4, Height = 3;
    private const float Tolerance = 1e-6f;

    private static readonly TerrainLightColour WallTint = new(300, 20, 40), RoomTint = new(12, 34, 56),
        QuarterTint = new(70, 0, 0), NeighbourTint = new(5, 6, 7);

    /// <summary>A wall: two faces at its decay stage's texture, a top with inverted normals, top quarters, tagged
    /// and tinted, so each group draws its tagged twin and the additive overlay.</summary>
    [Fact]
    public void AWallMatchesTheOldBuild() => AssertSameAsLegacy(Grid(), 1, 0);

    /// <summary>A room tile: floor quarters and whole piece with their own tints, the extra piece, whole walls 7 and 8
    /// taking the neighbour's tint, a custom whole gate piece under 7 (no tint), and halves facing a tagged neighbour.</summary>
    [Fact]
    public void ARoomTileMatchesTheOldBuild() => AssertSameAsLegacy(Grid(), 1, 1);

    /// <summary>A hero wall: a whole face and halves 4 and 5 swapped (0x0043E860).</summary>
    [Fact]
    public void AHeroFaceMatchesTheOldBuild() => AssertSameAsLegacy(Grid(), 2, 1);

    [Fact]
    public void ALiquidBedMatchesTheOldBuild() => AssertSameAsLegacy(Grid(), 0, 2);

    /// <summary>A bridge: the bed under it, then the room pieces.</summary>
    [Fact]
    public void ABridgeMatchesTheOldBuild() => AssertSameAsLegacy(Grid(), 1, 2);

    /// <summary>The sight fog's faded piece on a red tile: the faded twin, and still the red overlay.</summary>
    [Fact]
    public void TheSightFogOnARedTileMatchesTheOldBuild() => AssertSameAsLegacy(Grid(), 2, 2);

    [Fact]
    public void EveryTileOfTheGridMatchesTheOldBuild()
    {
        TileView[] tiles = Grid();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                AssertSameAsLegacy(tiles, x, y);
            }
        }
    }

    /// <summary>A room on each edge of the map with a wall facing off it: no neighbour lends its tag or tint, even where
    /// the row-major index would wrap onto a tagged, tinted tile. Also whole wall 6 beside 7 (the gate bound) and a tile
    /// with both a top and a floor (their order).</summary>
    [Fact]
    public void EdgeWallsGateBoundAndTopBeforeFloorMatchTheOldBuild()
    {
        TileView[] tiles = EdgeGrid();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                AssertSameAsLegacy(tiles, x, y);
            }
        }
    }

    /// <summary>A tile draws terrain when it has pieces and their keys, room keys, bed keys or a fog overlay.</summary>
    [Fact]
    public void ATileDrawsWhenItHasAnyTerrain()
    {
        var keys = new TerrainPieceKeys("Rock");
        TileView Tile(TerrainPieces pieces = default, TerrainPieceKeys? pieceKeys = null, RoomPieceKeys? roomKeys = null,
            TerrainPieceKeys? bedKeys = null, AssetRef fog = default) =>
            new(new TilePos(0, 0), default, default, default, false, false, 0, 1000, pieces: pieces, pieceKeys: pieceKeys,
                roomKeys: roomKeys, bedKeys: bedKeys, sightFogOverlay: fog);

        Assert.False(TerrainTileBuilder.Draws(Tile()));
        Assert.True(TerrainTileBuilder.Draws(Tile(default(TerrainPieces).WithFloor(), keys)));
        Assert.False(TerrainTileBuilder.Draws(Tile(default(TerrainPieces).WithFloor())));
        Assert.False(TerrainTileBuilder.Draws(Tile(pieceKeys: keys)));
        Assert.True(TerrainTileBuilder.Draws(Tile(roomKeys: new RoomPieceKeys(Room()))));
        Assert.True(TerrainTileBuilder.Draws(Tile(bedKeys: new TerrainPieceKeys("Water"))));
        Assert.True(TerrainTileBuilder.Draws(Tile(fog: AssetRef.ForTerrain("Fog", AssetKind.Mesh, "overlay"))));
    }

    /// <summary>Prepare builds the lattice cells around the tile, so Build, on a pool thread, only reads them (ADR-0696).</summary>
    [InstallFact]
    public void PrepareBuildsTheLatticeCellsBuildReads()
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(dir);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(dir, "Level1")!, [0, 1]);
        TileView[] tiles = new WorldView(db, PlayerIds.FirstKeeper).Tiles(world).ToArray();
        var lattice = new TerrainLattice(db, world, PlayerIds.FirstKeeper);
        const int x = 10, y = 12;
        Assert.False(lattice.IsBuilt(x, y));
        new TerrainTileBuilder(new FakePieces(), world.Width, world.Height, lattice).Prepare(x, y, tiles, new TerrainTileGeometry());
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Assert.True(lattice.IsBuilt(x + dx, y + dy), $"cell ({x + dx}, {y + dy})");
            }
        }
    }

    /// <summary>The hero halves swap 4 and 5 and leave the run-on piece 6.</summary>
    [Fact]
    public void HeroHalvesFourAndFiveSwap()
    {
        Assert.Equal(5, TerrainTileBuilder.SwapHeroHalf(4));
        Assert.Equal(4, TerrainTileBuilder.SwapHeroHalf(5));
        Assert.Equal(6, TerrainTileBuilder.SwapHeroHalf(6));
        TileView[] tiles = Grid();
        var pieces = new FakePieces();
        var geometry = new TerrainTileGeometry();
        new TerrainTileBuilder(pieces, Width, Height).Prepare(2, 1, tiles, geometry);
        TerrainPieceKeys keys = tiles[(1 * Width) + 2].PieceKeys!;
        // Side 1's halves: A (piece 4) in quadrant 1 draws hero face 5; B (piece 5) in quadrant 2 draws face 4.
        Assert.Contains(geometry.Pieces, p => p.Piece == pieces.Piece(keys.HeroFace(5)) && p.At == Quadrant(2, 1, 1) && p.Rotation == 1);
        Assert.Contains(geometry.Pieces, p => p.Piece == pieces.Piece(keys.HeroFace(4)) && p.At == Quadrant(2, 1, 2) && p.Rotation == 1);
    }

    /// <summary>Hand-worked values: a vertex at (0.5, 1.5, 0.25) turned a quarter (east to south) on tile (2, 1) of
    /// an unbent map lands at (2.75, 1.5, -2) with its normal (0, 0, 1) turned to (1, 0, 0).</summary>
    [Fact]
    public void AQuarterTurnPlacesAVertexClockwiseFromAbove()
    {
        var piece = new TerrainPiece([new Vector3(0.5f, 1.5f, 0.25f)], [Vector3.UnitZ], [new Vector2(0.25f, 0.75f)],
            [Array.Empty<int>()], [new ClassicMaterialRecipe("t", KmfBlend.Opaque, 0f)]);
        TerrainPieceKeys keys = new("Rock");
        var tiles = new TileView[Width * Height];
        tiles[(1 * Width) + 2] = new TileView(new TilePos(2, 1), default, default, default, false, true, 0, 1000,
            pieces: default(TerrainPieces).WithFace(1), pieceKeys: keys);
        var geometry = new TerrainTileGeometry();
        var builder = new TerrainTileBuilder(new FakePieces { [keys.Side] = piece }, Width, Height);
        builder.Prepare(2, 1, tiles, geometry);
        builder.Build(geometry);

        Assert.Equal(1, geometry.VertexCount);
        Assert.Equal(new Vector3(2.75f, 1.5f, -2f), geometry.Positions[0]);
        Assert.Equal(Vector3.UnitX, geometry.Normals[0]);
        Assert.Equal(new Vector2(0.25f, 0.75f), geometry.Uvs[0]);
        Assert.Equal(new Vector2(6, 1), geometry.LightAnchor);
    }

    [Theory]
    [InlineData(0, 1f, 2f, 3f)]
    [InlineData(1, 3f, 2f, -1f)]
    [InlineData(2, -1f, 2f, -3f)]
    [InlineData(3, -3f, 2f, 1f)]
    public void TheTurnsAreExactQuarters(int rotation, float x, float y, float z) =>
        Assert.Equal(new Vector3(x, y, z), TerrainTileBuilder.Turn(new Vector3(1, 2, 3), rotation));

    /// <summary>A rebuild of a tile whose buffers have grown allocates nothing (ADR-0695).</summary>
    [Fact]
    public void ARebuildAllocatesNothing()
    {
        TileView[] tiles = Grid();
        var builder = new TerrainTileBuilder(new FakePieces(), Width, Height);
        var geometry = new TerrainTileGeometry();
        for (int warm = 0; warm < 3; warm++)
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                builder.Prepare(i % Width, i / Width, tiles, geometry);
                builder.Build(geometry);
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < tiles.Length; i++)
        {
            builder.Prepare(i % Width, i / Width, tiles, geometry);
            builder.Build(geometry);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    /// <summary>The lattice bend in DK2's frame, on the shipped Level 1 with every tile its own view.</summary>
    [InstallFact]
    public void Level1BentMatchesTheOldBuild()
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")!;
        var world = WorldState.FromMap(db, map, [0, 1]);
        TileView[] tiles = new WorldView(db, PlayerIds.FirstKeeper).Tiles(world).ToArray();
        var pieces = new FakePieces();
        var builder = new TerrainTileBuilder(pieces, world.Width, world.Height,
            new TerrainLattice(db, world, PlayerIds.FirstKeeper));
        var legacy = new LegacyTerrainChunkBuild(pieces, tiles, world.Width,
            new TerrainLattice(db, world, PlayerIds.FirstKeeper));
        int compared = 0;
        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                if (TerrainTileBuilder.Draws(tiles[(y * world.Width) + x]))
                {
                    if (Compare(builder, legacy, tiles, world.Width, x, y) > 0)
                    {
                        compared++;
                    }
                }
            }
        }

        Assert.True(compared > 1000, compared.ToString());
    }

    private static void AssertSameAsLegacy(TileView[] tiles, int x, int y)
    {
        var pieces = new FakePieces();
        int vertices = Compare(new TerrainTileBuilder(pieces, Width, Height), new LegacyTerrainChunkBuild(pieces, tiles, Width, null),
            tiles, Width, x, y);
        Assert.True(vertices > 0, $"tile ({x}, {y}) draws nothing");
    }

    private static int Compare(TerrainTileBuilder builder, LegacyTerrainChunkBuild legacy, TileView[] tiles, int width, int x, int y)
    {
        var expected = new LegacyTerrainChunkBuild.TileGeometry();
        legacy.Prepare(x, y, tiles[(y * width) + x], expected);
        legacy.Build(expected);
        var actual = new TerrainTileGeometry();
        builder.Prepare(x, y, tiles, actual);
        builder.Build(actual);

        Assert.Equal(expected.VertexCount, actual.VertexCount);
        Assert.Equal(expected.LightAnchor, actual.LightAnchor);
        for (int i = 0; i < expected.VertexCount; i++)
        {
            AssertNear(expected.Positions[i], actual.Positions[i], $"position {i} of ({x}, {y})");
            AssertNear(expected.Normals[i], actual.Normals[i], $"normal {i} of ({x}, {y})");
            Assert.Equal(expected.Uvs[i], actual.Uvs[i]);
        }
        Assert.Equal(expected.MaterialCount, actual.KeyCount);
        for (int s = 0; s < expected.MaterialCount; s++)
        {
            Assert.Equal(expected.Materials[s].Key, actual.Keys[s]);
            Assert.Equal(expected.Triangles[s], actual.Triangles[s].AsSpan().ToArray());
        }
        return actual.VertexCount;
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, string what) =>
        Assert.True(Vector3.Distance(expected, actual) <= Tolerance * (1f + expected.Length()),
            $"{what}: expected {expected}, got {actual}");

    private static Vector3 Quadrant(int x, int y, int quadrant)
    {
        Vector2[] centres = [new(-0.25f, 0.25f), new(0.25f, 0.25f), new(0.25f, -0.25f), new(-0.25f, -0.25f)];
        return RenderWorld.Ground(x + .5f, y + .5f) + new Vector3(centres[quadrant].X, 0, centres[quadrant].Y);
    }

    private static RoomDef Room() => new()
    {
        Name = "Lair", RoomId = (byte)RoomTypeId.Lair, TerrainId = 12, Flags = RoomFlags.PlaceableOnLand,
        Construction = TileConstruction.Normal, Cost = 0, ReturnPercentage = 0,
        CreatedCreatureId = 0, ObjectIds = [], HealthGain = 0, CeilingHeight = new Fp32(0),
        RecommendedSizeX = 3, RecommendedSizeY = 3, OrderInEditor = 0, NameStringId = 0, TooltipStringId = 0,
    };

    /// <summary>Four by three tiles: row 0 a tagged plain floor, a wall and a tinted neighbour; row 1 a tagged
    /// neighbour, a room tile, a hero wall; row 2 a water bed, a bridge over lava and a fogged red tile.</summary>
    private static TileView[] Grid()
    {
        var rock = new TerrainPieceKeys("Rock");
        var claimed = new TerrainPieceKeys("Claimed Wall");
        var water = new TerrainPieceKeys("Water");
        var lava = new TerrainPieceKeys("Lava");
        var room = new RoomPieceKeys(Room());
        var tiles = new TileView[Width * Height];
        TileView At(int x, int y, bool tagged = false, TerrainPieces pieces = default, TerrainPieceKeys? pieceKeys = null,
            RoomWalls walls = default, PieceSet roomFloor = default, RoomPieceKeys? roomKeys = null, PieceSet bed = default,
            TerrainPieceKeys? bedKeys = null, TerrainLightColour ambientTint = default, TerrainLightColour quarterAmbientTint = default,
            AssetRef sightFogOverlay = default, byte sightFogOpacity = 0, int faceTexture = -1, bool red = false,
            AssetRef extraRoomPiece = default) =>
            new(new TilePos(x, y), default, default, default, tagged, false, 0, 1000, walls, pieces, pieceKeys,
                roomFloor: roomFloor, roomKeys: roomKeys, bed: bed, bedKeys: bedKeys, ambientTint: ambientTint,
                quarterAmbientTint: quarterAmbientTint, sightFogOverlay: sightFogOverlay, sightFogOpacity: sightFogOpacity,
                faceTexture: faceTexture, redHighlighted: red, extraRoomPiece: extraRoomPiece);

        tiles[0] = At(0, 0, tagged: true, pieces: default(TerrainPieces).WithFloor(), pieceKeys: rock);
        tiles[1] = At(1, 0, tagged: true, ambientTint: WallTint, faceTexture: 2, pieceKeys: claimed,
            pieces: default(TerrainPieces).WithFace(1).WithFace(2).WithTop().WithQuarters(QuarterKind.Top, 2, 0b100_011_010_001));
        tiles[2] = At(2, 0, pieces: default(TerrainPieces).WithFloor().WithQuarters(QuarterKind.Floor, 1, 0b000_001_010_011),
            pieceKeys: rock, ambientTint: NeighbourTint);
        tiles[3] = At(3, 0, pieces: default(TerrainPieces).WithFloor(), pieceKeys: rock, ambientTint: new TerrainLightColour(256, 512, 0));
        tiles[4] = At(0, 1, tagged: true, pieces: default(TerrainPieces).WithFloor(), pieceKeys: rock, ambientTint: NeighbourTint);
        tiles[5] = At(1, 1, roomKeys: room, ambientTint: RoomTint, quarterAmbientTint: QuarterTint,
            roomFloor: default(PieceSet).WithQuarter(0, 1, 2).WithQuarter(2, 3, 1).WithWhole(9, 3),
            extraRoomPiece: AssetRef.ForRoom("Lair", AssetKind.Mesh, "extra"),
            walls: default(RoomWalls).WithWhole(0, decorated: true).WithWholePiece(1, 3, 2).WithHalves(3, true, false)
                .WithWhole(2));
        tiles[6] = At(2, 1, pieces: default(TerrainPieces).WithTop(), pieceKeys: rock, ambientTint: WallTint,
            walls: default(RoomWalls).WithHalves(1, false, false).WithWhole(3).WithHalves(0, true, false));
        tiles[7] = At(3, 1, pieces: default(TerrainPieces).WithFloor(), pieceKeys: rock);
        tiles[8] = At(0, 2, bedKeys: water, bed: default(PieceSet).WithQuarter(1, 2, 3).WithQuarter(3, 0, 0).WithWhole(5, 1));
        tiles[9] = At(1, 2, bedKeys: lava, bed: default(PieceSet).WithWhole(4, 2), roomKeys: room, ambientTint: RoomTint,
            roomFloor: default(PieceSet).WithWhole(1, 1), walls: default(RoomWalls).WithWhole(2));
        tiles[10] = At(2, 2, pieces: default(TerrainPieces).WithFloor(), pieceKeys: rock, red: true,
            sightFogOverlay: AssetRef.ForTerrain("Fog", AssetKind.Mesh, "overlay"), sightFogOpacity: 96);
        tiles[11] = At(3, 2, pieces: default(TerrainPieces).WithFloor(), pieceKeys: rock, red: true, tagged: true);
        return tiles;
    }

    /// <summary>Four by three tiles, all tinted and all but (1, 1) tagged: a room on each edge with a wall facing off
    /// the map ((1, 0) north, (3, 1) east, (2, 2) south, (0, 1) west), whose row-major wrap lands on a tagged tile; the
    /// north room also has whole wall 6 facing (1, 1) and 7 facing east; (2, 1) has a top and a floor.</summary>
    private static TileView[] EdgeGrid()
    {
        var rock = new TerrainPieceKeys("Rock");
        var room = new RoomPieceKeys(Room());
        var tiles = new TileView[Width * Height];
        TileView At(int x, int y, RoomWalls walls = default, TerrainPieces pieces = default, bool tagged = true) =>
            new(new TilePos(x, y), default, default, default, tagged, false, 0, 1000, walls, pieces,
                walls.IsEmpty ? rock : null, roomKeys: walls.IsEmpty ? null : room,
                roomFloor: walls.IsEmpty ? default : default(PieceSet).WithWhole(1, 0), ambientTint: NeighbourTint);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                tiles[(y * Width) + x] = At(x, y, pieces: default(TerrainPieces).WithFloor());
            }
        }
        // Untagged, so the walls facing it show its tint, or none where the gate bound says so.
        tiles[5] = At(1, 1, pieces: default(TerrainPieces).WithFloor(), tagged: false);
        tiles[1] = At(1, 0, default(RoomWalls).WithWhole(0).WithWholePiece(2, 6, 2).WithWholePiece(1, 7, 1));
        tiles[7] = At(3, 1, default(RoomWalls).WithWhole(1));
        tiles[10] = At(2, 2, default(RoomWalls).WithWhole(2));
        tiles[4] = At(0, 1, default(RoomWalls).WithWhole(3));
        tiles[6] = At(2, 1, pieces: default(TerrainPieces).WithTop().WithFloor());
        return tiles;
    }

    /// <summary>A made-up piece per key and texture alternative, the same object each time: five vertices off the
    /// axes, two triangle lists, and recipes that cover opaque, alpha-blended and additive.</summary>
    internal sealed class FakePieces : ITerrainPieceSource
    {
        private readonly Dictionary<(AssetRef, int), TerrainPiece> _made = new();
        private readonly Dictionary<AssetRef, TerrainPiece> _fixed = new();

        public TerrainPiece this[AssetRef key] { set => _fixed[key] = value; }

        public TerrainPiece? Piece(AssetRef key, int textureIndex = -1)
        {
            if (key.IsEmpty)
            {
                return null;
            }

            if (_fixed.TryGetValue(key, out TerrainPiece? fixedPiece))
            {
                return fixedPiece;
            }

            if (textureIndex < 0)
            {
                textureIndex = 0;
            }

            if (_made.TryGetValue((key, textureIndex), out TerrainPiece? made))
            {
                return made;
            }

            _made[(key, textureIndex)] = made = Make(key, textureIndex);
            return made;
        }

        // Apart from Piece, whose cache hit must not allocate a closure.
        private static TerrainPiece Make(AssetRef key, int textureIndex)
        {
            uint h = 2166136261;
            foreach (char c in key.ToString())
            {
                h = (h ^ c) * 16777619;
            }

            h ^= (uint)textureIndex * 2654435761;
            float F(int shift) => ((h >> shift) & 0xFF) / 255f - 0.5f;
            Vector3[] positions =
            [
                new(F(0), 1f + F(8), F(16)), new(F(4) + 0.3f, 1.2f, F(12)), new(-0.37f, 2f + F(20), 0.41f),
                new(0.5f, 1f, -0.5f), new(F(24), 1.5f, F(2) - 0.1f),
            ];
            Vector3[] normals = [.. positions.Select((p, i) => Vector3.Normalize(new Vector3(0.3f + i, 1f, F(i) - 0.2f)))];
            Vector2[] uvs = [.. positions.Select((p, i) => new Vector2(i * 0.125f, 1f - (i * 0.25f)))];
            KmfBlend second = (h % 4) switch { 0 => KmfBlend.Additive, 1 => KmfBlend.AlphaBlend, _ => KmfBlend.Opaque };
            return new TerrainPiece(positions, normals, uvs, [[0, 1, 2, 0, 2, 3], [1, 4, 2]],
            [
                new ClassicMaterialRecipe("T" + (h % 3) + "_" + textureIndex, KmfBlend.Opaque, (h % 3) * 0.25f),
                new ClassicMaterialRecipe("U" + (h % 2), second, second == KmfBlend.Additive ? 1f : 0f),
            ]);
        }
    }
}
