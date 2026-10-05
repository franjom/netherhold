// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public class PortalCreatureActionTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(255, 10)]
    public void BinaryInferredActionBypassesOrdinaryGatesAndPromotes(byte requested, int expected)
    {
        var (db, world) = Setup();
        Paint(db, world, RoomTypeId.Portal, 5, 5, 3);
        for (int i = 0; i < PortalRules.CreatureCapacity(db, 1); i++)
            world.SpawnCreature(db, 7, 3, new TilePos(1, 1));
        int before = world.Creatures.Count;
        ref TileState centre = ref world.Tile(6, 6);
        centre.EntranceCreaturesGenerated = 9;
        centre.EntranceIdleTicks = 37;
        Assert.False(PortalRules.IsUnlocked(world, 3, 7));
        Assert.Equal(0, PortalRules.PoolLimit(db, 3, 7));
        Assert.False(PortalRules.IsAttracted(db.CreatureById(7), RoomIndex.Build(world, db)));

        TriggerRunResult result = Run(db, world, requested);

        CreatureState creature = world.Creatures.Last();
        Assert.Equal(before + 1, world.Creatures.Count);
        Assert.Equal((byte)7, creature.CreatureTypeId);
        Assert.Equal((byte)3, creature.OwnerPlayerId);
        Assert.Equal(new TilePos(6, 6), creature.Tile);
        Assert.Equal(expected, creature.Level);
        Assert.Equal(CreatureLevelRules.MaxHealth(db, db.CreatureById(7), expected), creature.Health);
        Assert.Equal(0, creature.ExperiencePoints);
        Assert.Equal(10, centre.EntranceCreaturesGenerated);
        Assert.Equal(37, centre.EntranceIdleTicks);
        Assert.Empty(result.Partial);
        Assert.Equal(1, world.Triggers.Read(4));
    }

    [Fact]
    public void MissingOwnedKeeperPortalStillDescends()
    {
        var (db, world) = Setup();
        Paint(db, world, RoomTypeId.Portal, 5, 5, 4);
        TriggerRunResult result = Run(db, world, 1);
        Assert.Empty(world.Creatures);
        Assert.Empty(result.Partial);
        Assert.Equal(1, world.Triggers.Read(4));
    }

    [Fact]
    public void WorldContextRefusesActionAndItsChild()
    {
        var (db, world) = Setup();
        Paint(db, world, RoomTypeId.Portal, 5, 5, 3);
        var tree = new TriggerTree(Records(1), 0xFFFF);
        TriggerRunResult result = new TriggerExecutor(tree, world.Triggers, world, db).Run(1);
        Assert.Empty(world.Creatures);
        Assert.Single(result.Unhandled);
        Assert.Equal(0, world.Triggers.Read(4));
    }

    [Fact]
    public void AreaContextUsesTheWorldDispatcherAndRefusesAction()
    {
        var (db, world) = Setup();
        ThingProjection.Place(world, db,
        [
            new ActionPointThing(new byte[56])
            {
                StartX = 5, StartY = 5, EndX = 7, EndY = 7,
                TriggerId = 1, ActionPointId = 1, Name = "portal action",
            },
        ]);
        TriggerRecord condition = new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 1, Type = 1, RepeatCount = 255, ChildTriggerId = 0xFFFF,
            Parameters = new Dictionary<string, long>
            {
                ["Comparison"] = 3, ["FlagId"] = 0, ["Flag"] = 1,
                ["IndirectFlagId"] = 0, ["Value"] = 0,
            },
        };
        var tree = new TriggerTree([condition, .. Records(1)], 0);
        TriggerRunResult result = new TriggerExecutor(tree, world.Triggers, world, db).Run(1);
        Assert.Empty(result.Partial);
        Assert.Equal(TriggerContext.Area, Assert.Single(result.Unhandled).Context);
        Assert.Equal(0, world.Triggers.Read(4));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreatureContextDelegatesToItsOwnerWithoutInvalidatingTheWalk(bool creationEvent)
    {
        var (db, world) = Setup();
        Paint(db, world, RoomTypeId.Portal, 5, 5, 3);
        CreatureState caller = world.SpawnCreature(db, 7, 3, new TilePos(1, 1));
        caller.TriggerId = 1;
        TriggerRecord condition = new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 1, Type = creationEvent ? (byte)3 : (byte)1, RepeatCount = 255,
            ChildTriggerId = 0xFFFF, Parameters = new Dictionary<string, long>
            {
                ["Comparison"] = 3, ["FlagId"] = 0, ["Flag"] = 1,
                ["IndirectFlagId"] = 0, ["Value"] = 0,
            },
        };
        var tree = new TriggerTree([condition, .. Records(2)], 0);
        var executor = new TriggerExecutor(tree, world.Triggers, world, db);
        if (!creationEvent) executor.Run(10);
        CreatureState spawned = world.Creatures.Last();
        Assert.Equal(2, world.Creatures.Count);
        Assert.Equal((byte)3, spawned.OwnerPlayerId);
        Assert.Equal(2, spawned.Level);
        Assert.Equal(new TilePos(6, 6), spawned.Tile);
    }

    [Fact]
    public void DerivedRoomOrderIsAnExplicitApproximationForMultiplePortals()
    {
        var (db, world) = Setup();
        Paint(db, world, RoomTypeId.Portal, 10, 8, 3);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 3);
        Run(db, world, 1);
        Assert.Equal(new TilePos(6, 6), Assert.Single(world.Creatures).Tile);
        Assert.Equal(1, world.Tile(6, 6).EntranceCreaturesGenerated);
        Assert.Equal(0, world.Tile(11, 9).EntranceCreaturesGenerated);
    }

    /// <summary>State 193 as read from 0x00459C20, timed as the Level 2 recording shows it: the
    /// Entrance animation runs to its end, the creature leaves the tick after, one step off the
    /// portal's centre, and chooses in the same tick. 147 frames at 30 is a Warlock.</summary>
    [Fact]
    public void APortalCreatureWaitsOutItsEntranceAnimation_ThenStepsOffTheHole_TimingInferredFromTwoTypes()
    {
        CreatureDef joiner = Dk2TerrainFixture.Database().CreatureById(7) with
        {
            CreatureId = 91, EntranceOffsetX = 0, EntranceOffsetY = 2929,
            EntranceFrames = 147, EntranceFramesPerSecond = 30,
        };
        GameDatabase db = Dk2TerrainFixture.Database(joiner);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, 2, 3, 4]);
        Paint(db, world, RoomTypeId.Portal, 5, 5, 3);
        var sim = new GameSimulation(db, world, seed: 1, Dk2TerrainFixture.DefaultSystems());

        CreatureState creature = PortalRules.CreateScriptedArrival(new StateHookContext(world, db), 3, 91, 1)!;
        Assert.Equal(PortalRules.JoiningState, creature.Dk2StateId);

        // Counted as the recording counts it: ticks that end with the creature still in 193.
        int joining = 0;
        while (joining < 100)
        {
            sim.Tick();
            if (creature.Dk2StateId != PortalRules.JoiningState) break;
            Assert.Equal(new TilePos(6, 6), creature.Tile);
            joining++;
        }

        Assert.Equal(21, joining);
        Assert.Equal(CreatureState.CentreOf(6), creature.PosX);
        Assert.Equal(CreatureState.CentreOf(6) + 2929, creature.PosY);
        Assert.NotEqual(PortalRules.JoiningState, creature.Dk2StateId);
    }

    [Theory]
    [InlineData(147, 30, 20)]
    [InlineData(197, 30, 27)]
    [InlineData(120, 30, 16)]
    [InlineData(0, 30, 0)]
    [InlineData(147, 0, 0)]
    public void JoiningLastsTheEntranceAnimationRoundedUpToATick(int frames, int fps, int ticks) =>
        Assert.Equal(ticks, PortalRules.JoiningTicks(
            Dk2TerrainFixture.Database().CreatureById(7) with { EntranceFrames = frames, EntranceFramesPerSecond = fps }));

    /// <summary>Q508: a portal creature's death lowers its portal's tally, never below zero
    /// (<c>0x00487C70</c>, <c>0x00488150</c>–<c>0x00488170</c>). Ours never lowered it.</summary>
    [Theory]
    [InlineData(9, 9)]
    [InlineData(-1, 0)]
    public void APortalCreaturesDeath_GivesItsPortalASlotBack(int tallyBeforeDeath, int expectedAfter)
    {
        var (db, world) = Setup();
        Paint(db, world, RoomTypeId.Portal, 5, 5, 3);
        Run(db, world, 1);
        CreatureState creature = world.Creatures.Last();
        ref TileState centre = ref world.Tile(6, 6);
        Assert.Equal(1, centre.EntranceCreaturesGenerated);
        Assert.Equal((6 * world.Width) + 6, creature.PortalEntranceTile);
        if (tallyBeforeDeath >= 0) centre.EntranceCreaturesGenerated = tallyBeforeDeath + 1;
        else centre.EntranceCreaturesGenerated = 0;

        creature.DieInstantly = true;
        creature.Health = 0;
        new GameSimulation(db, world, 42, [new Systems.CreatureHealthSystem()]).Run(8);

        Assert.Equal(CreatureActivity.Dead, creature.Activity);
        Assert.Equal(expectedAfter, world.Tile(6, 6).EntranceCreaturesGenerated);
    }

    private static TriggerRunResult Run(GameDatabase db, WorldState world, byte level)
    {
        var tree = new TriggerTree(Records(level), 0,
            playerRoots: [new TriggerPlayerRoot(3, 0xFFFF)]);
        return new TriggerExecutor(tree, world.Triggers, world, db).Run(10);
    }

    private static TriggerRecord[] Records(byte level) =>
    [
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFF, Type = 28, ChildTriggerId = 0xFFFE,
            Parameters = new Dictionary<string, long> { ["CreatureId"] = 7, ["Level"] = level },
        },
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFE, Type = 7,
            Parameters = new Dictionary<string, long> { ["FlagId"] = 4, ["Flag"] = 0x0A, ["Value"] = 1 },
        },
    ];

    private static (GameDatabase, WorldState) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        return (db, WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, 2, 3, 4]));
    }

    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, int x, int y, byte owner)
    {
        for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
                RoomRules.Place(db, ref world.Tile(x + dx, y + dy), db.Room(type), owner);
    }
}
