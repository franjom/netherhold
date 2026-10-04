// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class PossessionActionTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static (GameDatabase Db, WorldState World, CreatureState Me, GameSimulation Sim) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId,
            Keeper, new TilePos(10, 10));
        me.Possessed = true;
        world.Player(Keeper).PossessedCreatureId = me.Id;
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem()]);
        return (db, world, me, sim);
    }

    private static MovePossessedCreatureCommand Move(int x, int y, int z, ushort heading) =>
        new(Keeper, new(new(new(x), new(y), new(z)), new(heading)));

    [Fact]
    public void MoveInstallsPoseMasksHeadingAndPreservesStateAndJob_StronglyInferredFromBinary()
    {
        var (_, _, me, sim) = Setup();
        me.Dk2StateId = 199;
        me.TargetTileIndex = 123;
        me.SwingInProgress = true;
        me.PosZ = 100;
        me.LastPosX = 12;
        sim.Tick(InputFrame.Of(Move(45000, 46000, 300, 0xFFFF)));
        Assert.Equal((45000, 46000, 300, 0x7FF), (me.PosX, me.PosY, me.PosZ, me.Heading));
        Assert.Equal(199, me.Dk2StateId);
        Assert.Equal(123, me.TargetTileIndex);
        Assert.True(me.SwingInProgress);
        Assert.Equal(12, me.LastPosX);
    }

    [Fact]
    public void ZeroPackedWordCopiesGroundPoseAndPreviousPosition_StronglyInferredFromBinary()
    {
        var (_, _, me, sim) = Setup();
        me.Heading = 17;
        sim.Tick(InputFrame.Of(Move(45000, 46000, 0, 0)));
        Assert.Equal((45000, 46000, 0), (me.PosX, me.PosY, me.PosZ));
        Assert.Equal((45000, 46000, 0), (me.LastPosX, me.LastPosY, me.LastPosZ));
        Assert.Equal(17, me.Heading);
    }

    [Fact]
    public void ZeroPackedWordGroundsAnElevatedCreature_FromBinary()
    {
        var (_, _, me, sim) = Setup();
        me.PosZ = 1;
        sim.Tick(InputFrame.Of(Move(45000, 46000, 0, 0)));
        Assert.Equal((45000, 0, 0), (me.PosX, me.PosZ, me.LastPosZ));
    }

    [Fact]
    public void NonzeroRawHeadingStillUsesSetterAfterMaskingToZero_StronglyInferredFromBinary()
    {
        var (_, _, me, sim) = Setup();
        me.PosZ = 100;
        sim.Tick(InputFrame.Of(Move(45000, 46000, 0, 2048)));
        Assert.Equal((45000, 46000, 0, 0), (me.PosX, me.PosY, me.PosZ, me.Heading));
    }

    [Theory]
    [InlineData(0x3000, 0x3000)]
    [InlineData(0x3001, 0x2000)]
    [InlineData(0x10301, 0x301)]
    public void SetterNarrowsHeightThenUsesItsStrictCap_StronglyInferredFromBinary(int supplied, int expected)
    {
        var (_, _, me, sim) = Setup();
        sim.Tick(InputFrame.Of(Move(45000, 46000, supplied, 1)));
        Assert.Equal(expected, me.PosZ);
    }

    [Fact]
    public void MovementRelinksOnTileChangeAndKeepsSameTileOrder_StronglyInferredFromBinary()
    {
        var (_, world, me, sim) = Setup();
        TrapRules.RelinkCreatures(world);
        int before = me.ChainOrder;
        sim.Tick(InputFrame.Of(Move(me.PosX + 1, me.PosY, 1, 1)));
        Assert.Equal(before, me.ChainOrder);
        sim.Tick(InputFrame.Of(Move(11 * Fp32.One + 1, me.PosY, 1, 1)));
        Assert.True(me.ChainOrder > before);
        Assert.Equal(10 * world.Width + 11, me.ChainTileIndex);
    }

    [Fact]
    public void NoPossessedPointerLeavesPoseAlone()
    {
        var (_, world, me, sim) = Setup();
        world.Player(Keeper).PossessedCreatureId = 0;
        int x = me.PosX;
        sim.Tick(InputFrame.Of(Move(45000, 46000, 100, 1)));
        Assert.Equal(x, me.PosX);
    }

    [Fact]
    public void PossessedCreatureThatNoLongerExistsIgnoresTheMove()
    {
        var (_, world, me, sim) = Setup();
        world.Player(Keeper).PossessedCreatureId = me.Id + 1000;
        int x = me.PosX;
        sim.Tick(InputFrame.Of(Move(45000, 46000, 100, 1)));
        Assert.Equal(x, me.PosX);
    }

    [Fact]
    public void PoseHeightAndPreviousHeightSurviveSnapshot()
    {
        var (db, _, me, sim) = Setup();
        me.PosZ = 123;
        me.LastPosZ = 456;
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new PossessionSystem()]);
        CreatureState restored = copy.World.CreatureById(me.Id)!;
        Assert.Equal((123, 456), (restored.PosZ, restored.LastPosZ));
    }
}
