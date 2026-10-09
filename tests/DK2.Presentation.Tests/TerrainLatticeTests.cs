// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>DK2's terrain lattice (terrain-lattice.md, Q72), on a dug cross carved into the shipped
/// Level 1. Points are in DK2's frame: X east, Y south, Z down, the floor at Z = −1.</summary>
public class TerrainLatticeTests
{
    private const int X0 = 10, Y0 = 10;

    private static (GameDatabase Database, WorldState World) Cross()
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")!;
        WorldState world = WorldState.FromMap(db, map, [0, 1]);
        byte rock = db.Terrain.Values.Single(t => t.Name == "Rock").TerrainId;
        byte dirt = db.Terrain.Values.Single(t => t.Name == "Dirt Path").TerrainId;
        for (int y = Y0; y < Y0 + 9; y++)
            for (int x = X0; x < X0 + 9; x++)
                world.Tile(x, y).TerrainId = x == X0 + 4 || y == Y0 + 4 ? dirt : rock;
        return (db, world);
    }

    [InstallFact]
    public void TheShippedLatticeBytesAreRead()
    {
        (GameDatabase db, _) = Cross();
        Assert.Equal(new TerrainWibble(255, 255, 255, 255, 128, 255, 255, 255),
            db.Terrain.Values.Single(t => t.Name == "Rock").Wibble);
        Assert.Equal(new TerrainWibble(255, 96, 96, 0, 128, 0, 0, 0),
            db.Terrain.Values.Single(t => t.Name == "Dirt Path").Wibble);
    }

    /// <summary>Q72: the bend is a function of the map alone, so two lattices agree.</summary>
    [InstallFact]
    public void TheSameMapBendsTheSameWay()
    {
        (GameDatabase db, WorldState world) = Cross();
        var first = new TerrainLattice(db, world, PlayerIds.Good);
        var second = new TerrainLattice(db, world, PlayerIds.Good);
        var p = new Vector3(X0 + 4.3f, Y0 + 3.6f, -1.7f);

        Assert.Equal(first.Deform(p, X0 + 4, Y0 + 3), second.Deform(p, X0 + 4, Y0 + 3));
        Assert.NotEqual(p, first.Deform(p, X0 + 4, Y0 + 3));
    }

    /// <summary>Levels 0 and 4 keep their rest positions, so a point on level 0 at a column of the
    /// lattice is returned exactly (HighWalls, the default, rests at the identity).</summary>
    [InstallFact]
    public void TheAnchorLevelsDoNotMove()
    {
        (GameDatabase db, WorldState world) = Cross();
        var lattice = new TerrainLattice(db, world, PlayerIds.Good);
        var p = new Vector3(X0 + 4f, Y0 + 4f, -2.5f);

        Assert.Equal(p, lattice.Deform(p, X0 + 4, Y0 + 4));
    }

    /// <summary>In the open, away from rock, the floor (level 3) only jitters: Dirt Path's wibble is
    /// at most 0.125 tile across and 0.125 down, and its lean there is 0.</summary>
    [InstallFact]
    public void OpenFloorStaysAtFloorHeightWithinItsJitter()
    {
        (GameDatabase db, WorldState world) = Cross();
        var lattice = new TerrainLattice(db, world, PlayerIds.Good);
        var p = new Vector3(X0 + 4.5f, Y0 + 4.5f, -1f);

        Vector3 bent = lattice.Deform(p, X0 + 4, Y0 + 4);
        Assert.InRange(bent.Z, -1.001f, -0.874f);
        Assert.InRange(bent.X - p.X, -0.13f, 0.13f);
        Assert.InRange(bent.Y - p.Y, -0.13f, 0.13f);
    }

    /// <summary>With HighWalls off the wall top (level 1) rests at 1.5, then lifts by 0.15 ± 0.125
    /// and jitters down by under 0.125; with HighWalls, the default, it rests at 2
    /// (terrain-lattice.md §3).</summary>
    [InstallFact]
    public void AWallTopRestsHalfATileLowerWithHighWallsOff()
    {
        (GameDatabase db, WorldState world) = Cross();
        var low = new TerrainLattice(db, world, PlayerIds.Good) { HighWalls = false };
        var high = new TerrainLattice(db, world, PlayerIds.Good);
        var top = new Vector3(X0 + 1.25f, Y0 + 1.25f, -2f);

        float lowHeight = -low.Deform(top, X0 + 1, Y0 + 1).Z;
        float highHeight = -high.Deform(top, X0 + 1, Y0 + 1).Z;
        Assert.InRange(lowHeight, 1.5f + 0.025f - 0.125f, 1.5f + 0.275f);
        Assert.InRange(highHeight - lowHeight, 0.49f, 0.51f);
    }

    /// <summary>The lean (0x005A37F0): the middle of a dirt tile's west edge, against rock, moves
    /// into the rock by Dirt Path's lean 96/512 = 0.1875 and the wibble's push 0.1245, give or take
    /// the wibble's own ±0.1245.</summary>
    [InstallFact]
    public void AWallTopLeansIntoTheRock()
    {
        (GameDatabase db, WorldState world) = Cross();
        var lattice = new TerrainLattice(db, world, PlayerIds.Good);
        var top = new Vector3(X0 + 4f, Y0 + 1.5f, -2f);

        float dx = lattice.Deform(top, X0 + 3, Y0 + 1).X - top.X;
        Assert.InRange(dx, -0.1875f - 0.1245f - 0.1246f, -0.1875f - 0.1245f + 0.1246f);
    }

    /// <summary>A cell is kept until invalidated: digging the rock leaves the old bend until the
    /// change is reported, then the bend follows the new map.</summary>
    [InstallFact]
    public void ACellKeepsItsBendUntilTheChangeAroundItIsReported()
    {
        (GameDatabase db, WorldState world) = Cross();
        var lattice = new TerrainLattice(db, world, PlayerIds.Good);
        var top = new Vector3(X0 + 4f, Y0 + 1.5f, -2f);
        Vector3 before = lattice.Deform(top, X0 + 3, Y0 + 1);

        world.Tile(X0 + 3, Y0 + 1).TerrainId = db.Terrain.Values.Single(t => t.Name == "Dirt Path").TerrainId;
        Assert.Equal(before, lattice.Deform(top, X0 + 3, Y0 + 1));

        lattice.Invalidate(X0 + 3, Y0 + 1);
        Assert.NotEqual(before, lattice.Deform(top, X0 + 3, Y0 + 1));
    }

    [InstallFact]
    public void SwitchingHighWallsRebuildsTheCells()
    {
        (GameDatabase db, WorldState world) = Cross();
        var lattice = new TerrainLattice(db, world, PlayerIds.Good);
        var top = new Vector3(X0 + 1.25f, Y0 + 1.25f, -2f);
        Vector3 high = lattice.Deform(top, X0 + 1, Y0 + 1);

        lattice.HighWalls = false;
        Assert.InRange(lattice.Deform(top, X0 + 1, Y0 + 1).Z - high.Z, 0.49f, 0.51f);
    }
}
