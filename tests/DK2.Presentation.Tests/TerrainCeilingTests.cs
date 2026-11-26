// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation.Tests;

/// <summary>The possession view's roof (kwd-terrain-rooms.md, Ceiling: the possession-view roof).</summary>
public sealed class TerrainCeilingTests
{
    private const int Size = 24;

    /// <summary>An open square map with closed tiles at the given places.</summary>
    private static bool[] Map(params (int X, int Y)[] closed)
    {
        bool[] map = new bool[Size * Size];
        foreach ((int x, int y) in closed) map[(y * Size) + x] = true;
        return map;
    }

    private static int Nibble(bool[] map, int x, int y) => TerrainCeiling.Nibble(Size, Size, map, x, y);

    /// <summary>One closed tile at (12, 12); the nibble of a tile is the distance from its least
    /// corner to that tile's square: 0 touching, then 4, 5, 8, 8, 11, 12, 12, 14, and 15 past it.</summary>
    [Theory]
    [InlineData(12, 12, 0)]   // its own corner
    [InlineData(13, 13, 0)]   // the far corner of the closed tile is this tile's least corner
    [InlineData(11, 11, 5)]   // one tile off on both axes: d2 = 2
    [InlineData(14, 12, 4)]   // the square's far edge is at 13, so one tile: d2 = 1
    [InlineData(14, 14, 5)]   // d2 = 2
    [InlineData(15, 12, 8)]   // d2 = 4
    [InlineData(15, 14, 8)]   // d2 = 5
    [InlineData(15, 15, 11)]  // d2 = 8
    [InlineData(16, 12, 12)]  // d2 = 9
    [InlineData(16, 14, 12)]  // d2 = 10
    [InlineData(16, 15, 14)]  // d2 = 13
    [InlineData(17, 12, 15)]  // d2 = 16: past what is counted
    [InlineData(16, 16, 15)]  // d2 = 18
    [InlineData(10, 12, 8)]   // from the other side the square's near edge is at 12: d2 = 4
    [InlineData(9, 12, 12)]
    [InlineData(8, 12, 15)]
    public void TheNibbleIsTheDistanceFromTheLeastCornerToTheNearestClosedTile_StronglyInferredFromBinary(int x, int y, int expected)
    {
        Assert.Equal(expected, Nibble(Map((12, 12)), x, y));
    }

    [Fact]
    public void OutsideTheMapCountsAsClosedAndHasNoNibble()
    {
        bool[] open = Map();

        Assert.Equal(0, Nibble(open, 0, 5));
        Assert.Equal(4, Nibble(open, 1, 5));
        Assert.Equal(15, Nibble(open, 12, 12));
        Assert.Equal(4, Nibble(open, Size - 1, 5));
        Assert.Equal(0, Nibble(open, Size, 5));
        Assert.Equal(0, Nibble(open, 5, -1));
    }

    private static float[] Heights(bool[] map, int x, int y)
    {
        float[] z = new float[TerrainCeiling.Vertices];
        TerrainCeiling.Heights(Size, Size, map, x, y, z);
        return z;
    }

    [Fact]
    public void FarFromAnyWallThePieceIsFlatJustUnderThreeTilesOverTheFloor_StronglyInferredFromBinary()
    {
        float[] z = Heights(Map(), 12, 12);

        Assert.All(z, value => Assert.Equal(-3.9921875f, value));
    }

    /// <summary>A wall along x = 12 and the open tile east of it: the west corners sit on the wall
    /// top, the east corners a tile out, and the midpoints between are the exact blend.</summary>
    [Fact]
    public void BesideAWallTheRoofRisesFromTheWallTop_StronglyInferredFromBinary()
    {
        bool[] map = Map(Enumerable.Range(0, Size).Select(y => (12, y)).ToArray());

        float[] z = Heights(map, 13, 12);

        // Corners: nibble 0 at x = 13, nibble 4 at x = 14.
        Assert.Equal(-2f, z[0]);
        Assert.Equal(-2f, z[6]);
        Assert.Equal(-2.875f, z[2]);
        Assert.Equal(-2.875f, z[8]);
        // The west edge's midpoint faces the wall: it stays on the wall top.
        Assert.Equal(-2f, z[3]);
        // S = 8 for the column between: -2 - 8/16 + 64/2048, already over the limit.
        Assert.Equal(-2.46875f, (-(8 * 128) - 4096 + 64) / 2048f);
        // The north and south midpoints face open tiles and the centre is always held: -2.5.
        Assert.Equal(TerrainCeiling.OpenLimit, z[1]);
        Assert.Equal(TerrainCeiling.OpenLimit, z[7]);
        Assert.Equal(TerrainCeiling.OpenLimit, z[4]);
        // The east midpoint is already higher up than the limit.
        Assert.Equal(-2.875f, z[5]);
    }

    [Fact]
    public void ATileWalledAllRoundKeepsItsEdgesOnTheWallTopsAndLiftsOnlyItsCentre()
    {
        bool[] map = Map((11, 11), (12, 11), (13, 11), (11, 12), (13, 12), (11, 13), (12, 13), (13, 13));

        float[] z = Heights(map, 12, 12);

        for (int i = 0; i < 9; i++)
            Assert.Equal(i == 4 ? TerrainCeiling.OpenLimit : -2f, z[i]);
        Assert.True(TerrainCeiling.Has(Size, Size, map, 12, 12));
        Assert.False(TerrainCeiling.Has(Size, Size, map, 12, 11));
        Assert.False(TerrainCeiling.Has(Size, Size, map, -1, 12));
    }

    [Fact]
    public void TheTrianglesAlternateTheirDiagonalByQuadParity_StronglyInferredFromBinary()
    {
        Assert.Equal(
            new byte[]
            {
                1, 0, 4, 4, 0, 3,   // quad (0, 0), even
                2, 1, 4, 5, 2, 4,   // quad (0, 1), odd
                4, 3, 6, 7, 4, 6,   // quad (1, 0), odd
                5, 4, 8, 8, 4, 7,   // quad (1, 1), even
            },
            TerrainCeiling.Triangles.ToArray());
        Assert.Equal((1f, 0.5f), TerrainCeiling.Uv(1, 2));
    }

    private static TerrainCeilingField Field(bool[] map)
    {
        var field = new TerrainCeilingField(Size, Size);
        Assert.True(field.Refresh(map, []));
        return field;
    }

    private static (Vector3[] Positions, Vector3[] Normals) Piece(TerrainCeilingField field, int x, int y)
    {
        var positions = new Vector3[TerrainCeiling.Vertices];
        var normals = new Vector3[TerrainCeiling.Vertices];
        field.Piece(x, y, null, positions, normals);
        return (positions, normals);
    }

    private static void AssertSame((Vector3[] Positions, Vector3[] Normals) expected, (Vector3[] Positions, Vector3[] Normals) actual)
    {
        Assert.Equal(expected.Positions, actual.Positions);
        Assert.Equal(expected.Normals, actual.Normals);
    }

    private static bool[] Scattered()
    {
        var random = new Random(679);
        bool[] map = new bool[Size * Size];
        for (int i = 0; i < map.Length; i++) map[i] = random.Next(5) == 0;
        return map;
    }

    [Fact]
    public void TheFieldGivesEveryPieceTheHeightsOfTheRule()
    {
        bool[] map = Scattered();
        TerrainCeilingField field = Field(map);

        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                Assert.Equal(!map[(y * Size) + x], field.Has(x, y));
                if (!field.Has(x, y)) continue;
                float[] z = Heights(map, x, y);
                Vector3[] positions = Piece(field, x, y).Positions;
                for (int i = 0; i < TerrainCeiling.Vertices; i++)
                    Assert.Equal(new Vector3(x + (i % 3 * 0.5f), y + (i / 3 * 0.5f), z[i]), positions[i]);
            }
    }

    /// <summary>In DK2's frame (z down) every triangle's cross product of its first two edges points
    /// up, away from the side the roof is seen from, and the normals point down at that side.</summary>
    [Fact]
    public void EveryTriangleTurnsTheSameWayAndTheNormalsFaceDown()
    {
        (Vector3[] positions, Vector3[] normals) = Piece(Field(Map((12, 12))), 14, 12);

        ReadOnlySpan<byte> triangles = TerrainCeiling.Triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = positions[triangles[i]], b = positions[triangles[i + 1]], c = positions[triangles[i + 2]];
            Assert.True(Vector3.Cross(b - a, c - a).Z < 0);
        }
        Assert.All(normals, normal =>
        {
            Assert.True(normal.Z > 0);
            Assert.Equal(1f, normal.Length(), 5);
        });
        // The roof rises to the east here, so its underside also faces east.
        Assert.True(normals[4].X > 0);
        Assert.Equal(new Vector3(0, 0, 1), Piece(Field(Map()), 12, 12).Normals[4]);
    }

    [Fact]
    public void ARefreshReportsTheTilesThatChangedAndMatchesAFreshRead()
    {
        bool[] map = Scattered();
        TerrainCeilingField field = Field(map);
        var changed = new List<int>();
        Assert.False(field.Refresh(map, changed));

        map[(12 * Size) + 12] ^= true;
        map[(3 * Size) + 20] ^= true;
        Assert.True(field.Refresh(map, changed));

        Assert.Equal([(3 * Size) + 20, (12 * Size) + 12], changed);
        TerrainCeilingField fresh = Field(map);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                AssertSame(Piece(fresh, x, y), Piece(field, x, y));
    }

    [Fact]
    public void AChangeReachesNoPieceBeyondTheChangeReach()
    {
        TerrainCeilingField open = Field(Map()), closed = Field(Map((12, 12)));

        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (Math.Max(Math.Abs(x - 12), Math.Abs(y - 12)) > TerrainCeiling.ChangeReach)
                    AssertSame(Piece(open, x, y), Piece(closed, x, y));
        Assert.NotEqual(Piece(open, 12 - TerrainCeiling.ChangeReach, 12).Normals, Piece(closed, 12 - TerrainCeiling.ChangeReach, 12).Normals);
        Assert.NotEqual(Piece(open, 12 + TerrainCeiling.ChangeReach, 12).Normals, Piece(closed, 12 + TerrainCeiling.ChangeReach, 12).Normals);
    }
}
