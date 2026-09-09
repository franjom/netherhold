// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>
/// Picking a tile from a pointer ray. The case that matters is a camera looking down at
/// an angle onto a dungeon with walls in it: a ray aimed at a wall's top face carries on
/// past it, and anything that reads the ray where it crosses the ground plane answers
/// with a tile further away than the one under the pointer.
/// </summary>
public class TilePickerTests
{
    private const float SolidHeight = 1f;
    private const float OpenHeight = 0.06f;

    /// <summary>The pitch the Unity runner defaults to, in the plane where the error is
    /// largest: straight along +z, so a one-tile wall displaces the ground hit by
    /// <c>1 / tan(55°)</c>, about 0.7 tiles.</summary>
    private static PickRay LookingAt(float x, float z, float height, float pitchDegrees = 55f)
    {
        double pitch = pitchDegrees * Math.PI / 180.0;
        var dir = (X: 0f, Y: (float)-Math.Sin(pitch), Z: (float)Math.Cos(pitch));
        // Walk back up the ray from the point being aimed at, so the ray provably passes
        // through it whatever the pitch.
        const float back = 40f;
        return new PickRay(
            x - (dir.X * back), height - (dir.Y * back), z - (dir.Z * back),
            dir.X, dir.Y, dir.Z);
    }

    [Fact]
    public void APointerOnAWallTopPicksThatWallAndNotTheTileBehindIt()
    {
        (WorldView view, WorldState world) = Dungeon();

        // Aimed at the middle of the wall's top face.
        TilePos? picked = TilePicker.Pick(
            LookingAt(5.5f, 5.5f, SolidHeight), view, world, SolidHeight, OpenHeight);

        Assert.Equal(new TilePos(5, 5), picked);
    }

    [Fact]
    public void TheGroundPlaneReadingIsTheOneThisReplaces()
    {
        // Not a test of the picker: a demonstration that the bug was real, so nobody
        // "simplifies" this back to a plane cast. Where the same ray crosses y=0 is
        // roughly 0.7 tiles beyond the wall it was aimed at.
        PickRay ray = LookingAt(5.5f, 5.5f, SolidHeight);
        float toGround = -ray.OriginY / ray.DirectionY;
        float z = ray.OriginZ + (ray.DirectionZ * toGround);

        Assert.True(z > 6f, $"expected the ground hit past tile 5, got z={z}");
    }

    [Fact]
    public void APointerOnOpenFloorPicksTheFloor()
    {
        (WorldView view, WorldState world) = Dungeon();

        TilePos? picked = TilePicker.Pick(
            LookingAt(9.5f, 9.5f, OpenHeight), view, world, SolidHeight, OpenHeight);

        Assert.Equal(new TilePos(9, 9), picked);
    }

    /// <summary>A ray that grazes over a wall must not report the wall: the pointer is
    /// past it, on the floor beyond.</summary>
    [Fact]
    public void ARayThatClearsTheWallLandsBeyondIt()
    {
        (WorldView view, WorldState world) = Dungeon();

        TilePos? picked = TilePicker.Pick(
            LookingAt(9.5f, 12.5f, OpenHeight), view, world, SolidHeight, OpenHeight);

        Assert.Equal(new TilePos(9, 12), picked);
    }

    [Fact]
    public void ARayPointingAwayFromTheDungeonPicksNothing()
    {
        (WorldView view, WorldState world) = Dungeon();

        var upward = new PickRay(5.5f, 2f, 5.5f, 0f, 1f, 0f);
        Assert.Null(TilePicker.Pick(upward, view, world, SolidHeight, OpenHeight));

        var sideways = new PickRay(-50f, 5f, -50f, -1f, -0.01f, -1f);
        Assert.Null(TilePicker.Pick(sideways, view, world, SolidHeight, OpenHeight));
    }

    [Fact]
    public void AVerticalRayPicksTheTileUnderIt()
    {
        (WorldView view, WorldState world) = Dungeon();

        var down = new PickRay(3.25f, 30f, 7.75f, 0f, -1f, 0f);
        Assert.Equal(new TilePos(3, 7), TilePicker.Pick(down, view, world, SolidHeight, OpenHeight));
    }

    /// <summary>Rock everywhere, with a hole dug through the middle rows, which is the
    /// shape every real dungeon has: walls to point at and floor behind them.</summary>
    private static (WorldView View, WorldState World) Dungeon()
    {
        GameDatabase database = Database();
        var tiles = new MapTile[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                tiles[(y * Width) + x] = new MapTile(y >= 8 ? Floor : Wall, 0, 0, 0);

        WorldState world = WorldState.FromMap(database, new LevelMap(Width, Height, tiles), [0, 1]);
        return (new WorldView(database), world);
    }

    private const int Width = 16;
    private const int Height = 16;
    private const byte Wall = 2;
    private const byte Floor = 3;

    private static GameDatabase Database() => new(
        [
            Terrain(Wall, "Rock", TerrainFlags.Solid | TerrainFlags.Taggable),
            Terrain(Floor, "Dirt Path", TerrainFlags.Ownable),
        ],
        rooms: [], creatures: [], variables: new GlobalVariables(), keeperSpells: []);

    private static TerrainDef Terrain(byte id, string name, TerrainFlags flags) => new()
    {
        Name = name,
        TerrainId = id,
        Flags = flags,
        StartingHealth = 100,
        MaxHealth = 100,
        GoldValue = 0,
        ManaGain = 0,
        MaxManaGain = 0,
        Damage = 0,
        MaxHealthTypeTerrainId = 0,
        DestroyedTypeTerrainId = 0,
        Depth = new Fp32(0),
        NameStringId = 0,
        TooltipStringId = 0,
    };
}
