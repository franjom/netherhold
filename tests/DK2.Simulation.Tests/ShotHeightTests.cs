// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class ShotHeightTests
{
    private static (GameDatabase Db, WorldState World, ShotDef Fireball) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(40, 40),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world, db.Shots[Dk2TerrainFixture.FireballShotId] with { Speed = 3686, Mass = 0, Flags = 0, AirFriction = 0 });
    }

    // Two Wizard Fireballs of the Level 5 seed-1 capture, launched 1310 above the floor at an Imp's
    // half height: DK2's shots stand at 5316 and 5373 a turn later, the floor at 4096.
    [Theory]
    [InlineData(64989, 140417, 73495, 136618, -1448, 5316)]
    [InlineData(65073, 145370, 63568, 125065, -543, 5373)]
    public void TheLaunchPitchesTheShotAtTheAimHeight_ConfirmedInCapture(int x, int y, int aimX, int aimY, int velocity, int nativeHeight)
    {
        var (_, world, fireball) = Open();
        ShotState shot = world.SpawnShot(fireball.ShotId, PlayerIds.Good, x, y);
        shot.PosZ = 1310;
        ShotRules.Launch(shot, fireball, aimX, aimY, 1024);
        Assert.Equal(velocity, shot.VelZ);
        Assert.Equal(nativeHeight - Fp32.One, shot.PosZ + (shot.VelZ / 16));
    }

    [Fact]
    public void AShotWithMassOrNoAimHeightKeepsItsHeight_OursNotDk2()
    {
        var (_, world, fireball) = Open();
        ShotState heavy = world.SpawnShot(fireball.ShotId, PlayerIds.Good, 64989, 140417);
        heavy.PosZ = 1310;
        ShotRules.Launch(heavy, fireball with { Mass = 8192 }, 73495, 136618, 1024);
        Assert.Equal(0, heavy.VelZ);
        ShotState flat = world.SpawnShot(fireball.ShotId, PlayerIds.Good, 64989, 140417);
        flat.PosZ = 1310;
        ShotRules.Launch(flat, fireball, 73495, 136618);
        Assert.Equal(0, flat.VelZ);
    }

    [Fact]
    public void ALobbedLaunchAddsItsUpwardWord()
    {
        var (_, world, fireball) = Open();
        ShotState shot = world.SpawnShot(fireball.ShotId, PlayerIds.Good, 64989, 140417);
        shot.PosZ = 1310;
        ShotRules.Launch(shot, fireball with { Flags = ShotRules.Lobbed }, 73495, 136618, 1024);
        Assert.Equal(-1448 + 0x400, shot.VelZ);
    }

    [Fact]
    public void EachTickAddsASixteenthOfTheVerticalVelocityTruncatedTowardZero()
    {
        var (db, world, _) = Open();
        ShotState shot = ShotRules.Spawn(world, db.Shots[Dk2TerrainFixture.FireballShotId], PlayerIds.Good, 20 * Fp32.One, 20 * Fp32.One,
            0, 0, -1, FightTargetKind.None, CombatRules.NoTarget, PlayerIds.Neutral, -1, 30 * Fp32.One, 20 * Fp32.One);
        shot.Health = 100;
        shot.PosZ = 1310;
        shot.VelX = 100;
        shot.VelZ = -1448;
        var sim = new GameSimulation(db, world, 1, [new CombatSystem(), new CreatureMovementSystem(), new ShotSystem()]);
        sim.Tick();
        Assert.Equal(1220, shot.PosZ);
        sim.Tick();
        Assert.Equal(1130, shot.PosZ);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeightAndVerticalVelocityAreHashedSavedAndCopied(bool velocity)
    {
        var (db, world, _) = Open();
        ShotState shot = world.SpawnShot(Dk2TerrainFixture.FireballShotId, PlayerIds.Good, 20 * Fp32.One, 20 * Fp32.One);
        shot.Health = 100;
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        if (velocity) shot.VelZ = -77; else shot.PosZ = 1310;
        Assert.NotEqual(before, sim.StateHash());
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        ShotState loaded = Assert.Single(restored.World.Shots);
        Assert.Equal((shot.PosZ, shot.VelZ), (loaded.PosZ, loaded.VelZ));
        Assert.Equal(sim.StateHash(), restored.StateHash());
        var (_, copy, _) = Open();
        copy.CopyFrom(world);
        Assert.Equal((shot.PosZ, shot.VelZ), (Assert.Single(copy.Shots).PosZ, Assert.Single(copy.Shots).VelZ));
    }
}
