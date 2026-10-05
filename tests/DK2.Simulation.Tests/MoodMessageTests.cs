// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>Messages 17 and 18 from the anger wrapper <c>0x00478B80</c> and their early expiry in
/// the sweep <c>0x004C41E0</c> (computer-keeper-messages.md, the census; ADR-0353).</summary>
public class MoodMessageTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = 7;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static (GameDatabase, WorldState, CreatureDef, CreatureState) Goblin()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        return (db, world, db.CreatureById(GoblinId), goblin);
    }

    [Fact]
    public void ACrossingToUnhappyPosts17ToTheOwnerWithTheComplaintAndTheCreature()
    {
        var (_, world, goblin, state) = Goblin();
        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Lair,
            CreatureAngerRules.UnhappyAt(goblin), AngerComplaint.NoLair);

        PlayerMessage message = Assert.Single(world.Messages.Entries);
        Assert.Equal((PlayerMessageKind.CreatureUnhappy, Keeper, 0, state.Id),
            (message.Kind, message.PlayerId, message.Data1, message.Data2));
        Assert.Equal((uint)SimulationTiming.TicksFromSeconds(50), message.Timeout);
        Assert.True(message.NeedsTab);
    }

    [Fact]
    public void Data1IsTheComplaintNotTheBucket()
    {
        // The pay charge lands in bucket 4 and sends complaint 2 (0x00478EF0).
        var (_, world, goblin, state) = Goblin();
        var delta = new CreatureAnger { Pay = CreatureAngerRules.UnhappyAt(goblin) };
        CreatureAngerRules.Apply(world, goblin, state, delta, CreatureAngerRules.NeedComplaints);

        Assert.Equal((int)AngerComplaint.Money, Assert.Single(world.Messages.Entries).Data1);
        Assert.Equal(4, (int)AngerBucket.Pay);
    }

    [Fact]
    public void ACrossingFromUnhappyToAngryPosts18()
    {
        var (_, world, goblin, state) = Goblin();
        CreatureAngerRules.Apply(goblin, state, AngerBucket.Lair, CreatureAngerRules.UnhappyAt(goblin));
        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Treatment,
            CreatureAngerRules.Ceiling + 1, AngerComplaint.Slapped);

        PlayerMessage message = Assert.Single(world.Messages.Entries);
        Assert.Equal((PlayerMessageKind.CreatureAngry, (int)AngerComplaint.Slapped),
            (message.Kind, message.Data1));
    }

    [Fact]
    public void AJumpFromContentToAngryPostsOnly18()
    {
        var (_, world, goblin, state) = Goblin();
        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Work,
            CreatureAngerRules.Ceiling + 1, AngerComplaint.NoWork);

        Assert.Equal(PlayerMessageKind.CreatureAngry, Assert.Single(world.Messages.Entries).Kind);
    }

    [Fact]
    public void AnAddThatLeavesTheBandWhereItWasPostsNothing()
    {
        var (_, world, goblin, state) = Goblin();
        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Lair, 100, AngerComplaint.NoLair);
        Assert.Empty(world.Messages.Entries);

        CreatureAngerRules.Apply(goblin, state, AngerBucket.Lair, CreatureAngerRules.UnhappyAt(goblin));
        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Lair, 100, AngerComplaint.NoLair);
        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void ANegativeAmountPostsNothingEvenWhenTheBandFallsToUnhappy()
    {
        var (_, world, goblin, state) = Goblin();
        CreatureAngerRules.Apply(goblin, state, AngerBucket.Treatment, 40000);
        Assert.Equal(CreatureMood.Angry, CreatureAngerRules.MoodOf(goblin, state));

        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Treatment, -20000, AngerComplaint.NoLair);

        Assert.Equal(CreatureMood.Unhappy, CreatureAngerRules.MoodOf(goblin, state));
        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void ANeverAngeredCreaturePostsNothing()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState skeleton = world.SpawnCreature(db, Dk2TerrainFixture.SkeletonCreatureId, Keeper, new TilePos(2, 2));
        CreatureAngerRules.Apply(world, db.CreatureById(Dk2TerrainFixture.SkeletonCreatureId), skeleton,
            AngerBucket.Lair, 40000, AngerComplaint.NoLair);

        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void WithAngerSwitchedOffNothingIsAddedOrPosted()
    {
        var (_, world, goblin, state) = Goblin();
        PaydayTests.SwitchAngerOff(world);
        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Lair, 40000, AngerComplaint.NoLair);

        Assert.True(state.Anger.IsCalm);
        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void ForgettingABucketPostsNothing()
    {
        var (_, world, goblin, state) = Goblin();
        CreatureAngerRules.Apply(goblin, state, AngerBucket.Lair, 40000);
        CreatureAngerRules.Forget(world, goblin, state, AngerBucket.Lair);

        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void ASecondCreaturesCrossingIsSwallowedByTheFirstUnexpired17()
    {
        var (db, world, goblin, first) = Goblin();
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 2));
        int unhappy = CreatureAngerRules.UnhappyAt(goblin);
        CreatureAngerRules.Apply(world, goblin, first, AngerBucket.Lair, unhappy, AngerComplaint.NoLair);
        CreatureAngerRules.Apply(world, goblin, second, AngerBucket.Pay, unhappy, AngerComplaint.Money);

        PlayerMessage message = Assert.Single(world.Messages.Entries);
        Assert.Equal((first.Id, (int)AngerComplaint.NoLair), (message.Data2, message.Data1));
    }

    [Fact]
    public void AFull256QueueEvictsItsOldestRecordForA17()
    {
        var (_, world, goblin, state) = Goblin();
        for (int i = 0; i < PlayerMessageQueue.Capacity; i++)
            world.Messages.Post(world, Keeper, PlayerMessageKind.CombatStarted, i, 0);
        uint oldest = world.Messages.Entries[0].Id;

        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Lair,
            CreatureAngerRules.UnhappyAt(goblin), AngerComplaint.NoLair);

        Assert.Equal(PlayerMessageQueue.Capacity, world.Messages.Entries.Count);
        Assert.DoesNotContain(world.Messages.Entries, m => m.Id == oldest);
        Assert.Equal(PlayerMessageKind.CreatureUnhappy, world.Messages.Entries[^1].Kind);
    }

    [Fact]
    public void TheSweepFreesA17OnceItsCreatureIsNoLongerUnhappy()
    {
        var (db, world, goblin, state) = Goblin();
        var sim = new GameSimulation(db, world, seed: 1, []);
        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Lair,
            CreatureAngerRules.UnhappyAt(goblin), AngerComplaint.NoLair);

        sim.Tick();
        Assert.Equal(1u, Assert.Single(world.Messages.Entries).Age);

        CreatureAngerRules.Forget(world, goblin, state, AngerBucket.Lair);
        sim.Tick();
        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void TheSweepFreesAn18WhoseCreatureFellBackToUnhappy()
    {
        var (db, world, goblin, state) = Goblin();
        var sim = new GameSimulation(db, world, seed: 1, []);
        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Lair, 40000, AngerComplaint.NoLair);
        Assert.Equal(PlayerMessageKind.CreatureAngry, Assert.Single(world.Messages.Entries).Kind);

        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Lair, -20000, AngerComplaint.NoLair);
        sim.Tick();

        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void A17WhoseCreatureIsGoneLastsItsFiftySeconds()
    {
        var (db, world, goblin, state) = Goblin();
        var sim = new GameSimulation(db, world, seed: 1, []);
        CreatureAngerRules.Apply(world, goblin, state, AngerBucket.Lair,
            CreatureAngerRules.UnhappyAt(goblin), AngerComplaint.NoLair);
        world.RemoveCreature(state.Id);

        sim.Run(SimulationTiming.TicksFromSeconds(50) - 1);
        Assert.Single(world.Messages.Entries);
        sim.Tick();
        Assert.Empty(world.Messages.Entries);
    }
}
