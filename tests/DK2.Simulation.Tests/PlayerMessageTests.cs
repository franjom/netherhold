// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

public class PlayerMessageTests
{
    [Fact]
    public void DuplicatesKeepTheOriginalPayloadAgeAndHandledBit()
    {
        GameSimulation sim = TestWorld.Simulation();
        PlayerMessage first = sim.World.Messages.Post(sim.World, 1, PlayerMessageKind.HeartAttacked, 3, 4)!.Value;
        sim.Tick(InputFrame.Of(new AcknowledgePlayerMessageCommand(1, first.Id)));
        PlayerMessage duplicate = sim.World.Messages.Post(sim.World, 1, PlayerMessageKind.HeartAttacked, 9, 8)!.Value;
        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal((3, 4, 1u, true), (duplicate.Data1, duplicate.Data2, duplicate.Age, duplicate.Handled));
        Assert.Single(sim.World.Messages.Entries);
    }

    [Fact]
    public void CapacityIsSharedAndEvictionIgnoresRecipientAndHandledState()
    {
        WorldState world = TestWorld.World(TestWorld.Database());
        var oldest = world.Messages.Post(world, 1, PlayerMessageKind.HeartAttacked, 0, 0)!.Value;
        for (int i = 0; i < PlayerMessageQueue.Capacity; i++)
            world.Messages.Post(world, 2, PlayerMessageKind.CombatStarted, i, 0);
        Assert.Equal(256, world.Messages.Entries.Count);
        Assert.DoesNotContain(world.Messages.Entries, m => m.Id == oldest.Id);
        Assert.Equal(2u, world.Messages.Entries[0].Id);
        Assert.Equal(257u, world.Messages.Entries[^1].Id);
    }

    [Fact]
    public void ExpirationAndTabThresholdAreExactAndAcknowledgmentDoesNotRemove()
    {
        var sim = new GameSimulation(TestWorld.Database(), TestWorld.World(TestWorld.Database()), 1);
        var message = sim.World.Messages.Post(sim.World, 1, PlayerMessageKind.HeartAttacked, 0, 0)!.Value;
        sim.Tick(InputFrame.Of(new AcknowledgePlayerMessageCommand(2, message.Id)));
        Assert.False(Assert.Single(sim.World.Messages.Entries).Handled);
        sim.Tick(InputFrame.Of(new AcknowledgePlayerMessageCommand(1, message.Id)));
        Assert.True(Assert.Single(sim.World.Messages.Entries).Handled);
        sim.Run(197);
        Assert.True(Assert.Single(sim.World.Messages.Entries).NeedsTab);
        sim.Tick();
        Assert.False(Assert.Single(sim.World.Messages.Entries).NeedsTab);
        sim.Run(279);
        Assert.Equal(479u, Assert.Single(sim.World.Messages.Entries).Age);
        sim.Tick();
        Assert.Empty(sim.World.Messages.Entries);
    }

    [Theory]
    [InlineData(PlayerStatus.Won)]
    [InlineData(PlayerStatus.Conquered)]
    [InlineData(PlayerStatus.Lost)]
    public void NonPlayingPlayersCannotReceiveMessages(byte status)
    {
        WorldState world = TestWorld.World(TestWorld.Database());
        world.Player(1).Status = status;
        Assert.Null(world.Messages.Post(world, 1, PlayerMessageKind.CombatStarted, 0, 0));
        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void DisabledNotificationsSuppressBothCombatKinds()
    {
        WorldState world = TestWorld.World(TestWorld.Database());
        SetNotifications(world, false);
        Assert.Null(world.Messages.Post(world, 1, PlayerMessageKind.CombatStarted, 0, 0));
        Assert.Null(world.Messages.Post(world, 1, PlayerMessageKind.HeartAttacked, 0, 0));
        SetNotifications(world, true);
        Assert.Equal(1u, world.Messages.Post(world, 1, PlayerMessageKind.HeartAttacked, 0, 0)!.Value.Id);
    }

    [Fact]
    public void SnapshotContinuesTheQueueIncludingIdsAndHandledDuplicates()
    {
        GameSimulation a = TestWorld.Simulation();
        var message = a.World.Messages.Post(a.World, 1, PlayerMessageKind.HeartAttacked, 2, 3)!.Value;
        a.Tick(InputFrame.Of(new AcknowledgePlayerMessageCommand(1, message.Id)));
        a.World.Messages.Post(a.World, 2, PlayerMessageKind.CombatStarted, 4, 5);
        GameSimulation b = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(a), a.Database, [new ChaosSystem()]);
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int i = 0; i < 490; i++)
        {
            a.World.Messages.Post(a.World, 2, PlayerMessageKind.CombatStarted, i, 0);
            b.World.Messages.Post(b.World, 2, PlayerMessageKind.CombatStarted, i, 0);
            a.Tick(); b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }

    [Fact]
    public void ZeroTimeoutCombatMessagesDoNotSatisfyTheDuplicateAgeTest()
    {
        GameSimulation sim = TestWorld.Simulation();
        var first = sim.World.Messages.Post(sim.World, 1, PlayerMessageKind.CombatStarted, 2, 3)!.Value;
        var next = sim.World.Messages.Post(sim.World, 1, PlayerMessageKind.CombatStarted, 2, 3)!.Value;
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal(2, sim.World.Messages.Entries.Count);
        sim.Tick();
        Assert.Empty(sim.World.Messages.Entries);
    }

    [Fact]
    public void WorldHashDetectsMessagePayloadRecipientOrderAndAcknowledgment()
    {
        GameSimulation Make(int x = 2, byte owner = 1, bool reverse = false)
        {
            GameSimulation sim = TestWorld.Simulation();
            foreach (int y in reverse ? new[] { 4, 3 } : new[] { 3, 4 })
                sim.World.Messages.Post(sim.World, owner, PlayerMessageKind.CombatStarted, x, y);
            return sim;
        }
        GameSimulation baseline = Make();
        Assert.NotEqual(baseline.StateHash(), Make(x: 9).StateHash());
        Assert.NotEqual(baseline.StateHash(), Make(owner: 2).StateHash());
        Assert.NotEqual(baseline.StateHash(), Make(reverse: true).StateHash());
        Assert.NotEqual(baseline.StateHash(), TestWorld.Simulation().StateHash());
        GameSimulation a = TestWorld.Simulation(), b = TestWorld.Simulation();
        uint id = a.World.Messages.Post(a.World, 1, PlayerMessageKind.HeartAttacked, 2, 3)!.Value.Id;
        b.World.Messages.Post(b.World, 1, PlayerMessageKind.HeartAttacked, 2, 3);
        a.Tick(InputFrame.Of(new AcknowledgePlayerMessageCommand(1, id))); b.Tick();
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    private static void SetNotifications(WorldState world, bool enabled)
    {
        var action = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFF, Type = 45,
            Parameters = new Dictionary<string, long> { ["Available"] = enabled ? 1 : 0 },
        };
        new TriggerExecutor(new TriggerTree([action], 0xFFFF), world.Triggers).Run(1);
    }
}
