// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class RescuePriorityTests
{
    [Fact]
    public void CommandHashDistinguishesPlayerTargetAndOperation()
    {
        static ulong Hash(SetRescuePriorityCommand command)
        {
            var hash = Hash64.Start();
            command.HashInto(ref hash);
            return hash.Value;
        }
        ulong original = Hash(new(3, 12, true));
        Assert.NotEqual(original, Hash(new(1, 12, true)));
        Assert.NotEqual(original, Hash(new(3, 13, true)));
        Assert.NotEqual(original, Hash(new(3, 12, false)));
    }

    [Fact]
    public void CommandsPreserveOtherPlayersAndDoNotCaptureTheLivingTarget()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)1, (byte)3]);
        var body = world.SpawnCreature(db, 7, 1, new TilePos(3, 3));
        body.RescuePriorityMask = 0x8001;
        int health = body.Health;
        var sim = new GameSimulation(db, world, 42, [new RescuePrioritySystem()]);
        sim.Tick(InputFrame.Of(new SetRescuePriorityCommand(3, body.Id, true)));
        Assert.Equal((ushort)0x8005, body.RescuePriorityMask);
        Assert.Equal((byte)1, body.OwnerPlayerId);
        Assert.Equal(health, body.Health);
        Assert.False(PrisonRules.IsImprisoned(body));
        sim.Tick(InputFrame.Of(new SetRescuePriorityCommand(3, body.Id, false),
            new SetRescuePriorityCommand(3, body.Id, true),
            new SetRescuePriorityCommand(1, body.Id, false)));
        Assert.Equal((ushort)0x8004, body.RescuePriorityMask);
        sim.Tick(InputFrame.Of(new SetRescuePriorityCommand(7, body.Id, true),
            new SetRescuePriorityCommand(3, -1, true)));
        Assert.Equal((ushort)0x8004, body.RescuePriorityMask);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 4)]
    [InlineData(7, 64)]
    [InlineData(0, 0)]
    [InlineData(8, 0)]
    public void MarksUseCompactPlayerIds(byte player, ushort bit)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)1]);
        var body = world.SpawnCreature(db, 7, 1, new TilePos(3, 3));
        RescuePriorityRules.Set(body, player, true);
        Assert.Equal(bit, body.RescuePriorityMask);
        RescuePriorityRules.Set(body, player, false);
        Assert.Equal((ushort)0, body.RescuePriorityMask);
    }

    [Fact]
    public void NonzeroMarksAreHashedAndSurviveSaveAndContinuation()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)1, (byte)3]);
        var body = world.SpawnCreature(db, 7, 1, new TilePos(3, 3));
        var sim = new GameSimulation(db, world, 42, [new RescuePrioritySystem()]);
        ulong before = sim.StateHash();
        body.RescuePriorityMask = 0x8045;
        Assert.NotEqual(before, sim.StateHash());
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new RescuePrioritySystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        var input = InputFrame.Of(new SetRescuePriorityCommand(3, body.Id, false));
        sim.Tick(input);
        restored.Tick(input);
        Assert.Equal((ushort)0x8041, body.RescuePriorityMask);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }
}
