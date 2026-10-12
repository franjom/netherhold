// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The possessed creature's followers (possession-arms-and-inputs.md §4): the slot-3
/// recruit, state 246, the dismissal, the leader's count and the shared melee target.</summary>
public sealed class PossessionFollowerTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const ushort East = 0x200, Level = 0x200;

    private static (GameDatabase Db, WorldState World) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Possessor());
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(30, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static CreatureState Possess(GameDatabase db, WorldState world, int x, int y)
    {
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(x, y));
        me.Possessed = true;
        me.Heading = East;
        world.Player(Keeper).PossessedCreatureId = me.Id;
        return me;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, params ISimulationSystem[] more)
    {
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem(), .. more]);
        sim.Tick();
        return sim;
    }

    /// <summary>Starts each follow inside a tick, where the state machine's hooks live.</summary>
    private static void Follow(GameDatabase db, WorldState world, CreatureState leader, params CreatureState[] followers)
    {
        var sim = new GameSimulation(db, world, 1, [new Probe(context =>
        {
            foreach (CreatureState c in followers) Assert.True(PossessionRules.StartFollowing(context, c, leader.Id));
        })]);
        sim.Tick();
    }

    private sealed class Probe(Action<SimulationContext> act) : ISimulationSystem
    {
        public string Name => "probe";
        public void Execute(SimulationContext context) => act(context);
    }

    private static ActPossessedCreatureCommand Recruit(CreatureState me, ushort yaw = East) =>
        new(Keeper, new(new(new(me.PosX), new(me.PosY), new(me.PosZ)), new(yaw), new(Level)), false, new(PossessedSlot.FollowerArm));

    [Fact]
    public void SlotThreeRecruitsTheBestAimedHappyFriendNotTheNearestAndDoesNotStamp_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        CreatureState nearOffAxis = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(7, 11));
        CreatureState farOnAxis = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        GameSimulation sim = Sim(db, world);
        long[] stamps = [.. me.SpellStamps];

        sim.Tick(InputFrame.Of(Recruit(me)));

        Assert.Equal(1, me.FollowerCount);
        Assert.Equal(PossessionRules.FollowingState, farOnAxis.Dk2StateId);
        Assert.Equal(me.Id, farOnAxis.FollowLeaderId);
        Assert.Equal(0, nearOffAxis.FollowLeaderId);
        Assert.Equal(stamps, me.SpellStamps);
    }

    [Fact]
    public void ATieKeepsTheFirstSeenWhichIsTheNewestCreature_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        CreatureState older = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 10));
        CreatureState newer = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        GameSimulation sim = Sim(db, world);

        sim.Tick(InputFrame.Of(Recruit(me)));

        Assert.Equal(me.Id, newer.FollowLeaderId);
        Assert.Equal(0, older.FollowLeaderId);
    }

    [Fact]
    public void TheRecruitScansSevenTilesFromTheCreatureAndSkipsEnemiesAndTheUnhappy_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        CreatureState far = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(11, 10));
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 10));
        CreatureState sulking = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 10));
        CreatureAngerRules.Apply(db.CreatureById(sulking.CreatureTypeId), sulking, AngerBucket.Treatment, 1_000_000);
        GameSimulation sim = Sim(db, world);

        sim.Tick(InputFrame.Of(Recruit(me)));

        Assert.Equal(0, me.FollowerCount);
        Assert.All([far, enemy, sulking], c => Assert.Equal(0, c.FollowLeaderId));
    }

    [Fact]
    public void TheCapStopsTheRecruitAtTheMaximumGroupSize_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        CreatureState ahead = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 10));
        GameSimulation sim = Sim(db, world);
        me.FollowerCount = db[VariableTypeId.MaximumGroupNumberOfFollowers];

        sim.Tick(InputFrame.Of(Recruit(me)));

        Assert.Equal(0, ahead.FollowLeaderId);
        Assert.Equal(db[VariableTypeId.MaximumGroupNumberOfFollowers], me.FollowerCount);
    }

    [Fact]
    public void AFollowerRoutesToItsLeaderAndRunsWhenFar_ConfirmedFromBinary_ArrivalCycleUnread()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        CreatureState near = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        CreatureState far = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(9, 10));
        Follow(db, world, me, near, far);
        GameSimulation sim = Sim(db, world, new CreatureJobSystem());

        sim.Tick();

        Assert.Equal(6, near.Dk2StateId);   // travel to a point
        Assert.Equal(PossessionRules.FollowingState, near.UtilityNewState);
        Assert.False(near.Running);
        Assert.True(far.Running);
    }

    [Fact]
    public void TheFollowEndsWhenTheLeaderIsNoLongerPossessedAndTheCountIsZeroed_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        CreatureState follower = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        Follow(db, world, me, follower);
        GameSimulation sim = Sim(db, world, new CreatureJobSystem());
        me.FollowerCount = 1;

        sim.Tick(InputFrame.Of(new LeavePossessionCommand(Keeper)));
        Assert.Equal(0, me.FollowerCount);
        follower.StopMoving();
        if (follower.Dk2StateId == 6) sim.Tick();
        sim.Tick();

        Assert.Equal(0, follower.FollowLeaderId);
        Assert.NotEqual(PossessionRules.FollowingState, follower.Dk2StateId);
    }

    [Fact]
    public void DismissLetsEveryFollowerGoAndCountsDownOnceEach_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        CreatureState counted = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        CreatureState uncounted = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 10));
        Follow(db, world, me, counted, uncounted);
        GameSimulation sim = Sim(db, world);
        me.FollowerCount = 1;

        sim.Tick(InputFrame.Of(new DismissFollowersCommand(Keeper)));

        Assert.Equal(0, counted.FollowLeaderId);
        Assert.Equal(0, uncounted.FollowLeaderId);
        Assert.Equal(-1, me.FollowerCount);
    }

    [Fact]
    public void TheCountRuleLowersOnceAndFrightAlsoLetsGo_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        CreatureState follower = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        follower.FollowLeaderId = me.Id;
        me.FollowerCount = 2;

        PossessionRules.DropFollowFromFear(world, follower);
        Assert.Equal(1, me.FollowerCount);
        Assert.Equal(0, follower.FollowLeaderId);
        PossessionRules.LeaderLosesFollower(world, follower);
        Assert.Equal(1, me.FollowerCount);
    }

    [Fact]
    public void AMeleeHitIsTheFollowersTargetUntilThreeSecondsPass_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 10, 10);
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        CreatureState follower = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 10));
        follower.FollowLeaderId = me.Id;
        GameSimulation sim = Sim(db, world, new CombatSystem());
        long now = sim.CurrentTick.Value;

        sim.Tick(InputFrame.Of(new ActPossessedCreatureCommand(Keeper,
            new(new(new(me.PosX), new(me.PosY), new(me.PosZ)), new(East), new(Level)), true, new(0))));

        Assert.Equal(FightTargetKind.Creature, me.PossessedTargetKind);
        Assert.Equal(enemy.Id, me.PossessedTargetId);
        Assert.Equal(now, me.PossessedTargetTick);
        Assert.Same(enemy, PossessionRules.LeaderTarget(world, follower));

        for (int i = 0; i <= PossessionRules.PossessedTargetTicks; i++) sim.Tick();
        Assert.Equal(FightTargetKind.None, me.PossessedTargetKind);
        Assert.Null(PossessionRules.LeaderTarget(world, follower));
    }

    [Fact]
    public void AFollowerIsBusyToItsNeeds_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState follower = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        Assert.False(NeedRules.IsBusy(world, follower, 0));
        follower.FollowLeaderId = 99;
        Assert.True(NeedRules.IsBusy(world, follower, 0));
    }

    [Fact]
    public void TheScanStopsAtSevenTilesStrictly_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        CreatureState atSeven = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Recruit(me)));
        Assert.Equal(0, atSeven.FollowLeaderId);

        atSeven.PosX -= 1;
        sim.Tick(InputFrame.Of(Recruit(me)));
        Assert.Equal(me.Id, atSeven.FollowLeaderId);
    }

    [Fact]
    public void APitFighterIsNotRecruited_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        CreatureState fighter = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 10));
        fighter.CombatPitFighter = true;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Recruit(me)));
        Assert.Equal(0, fighter.FollowLeaderId);
    }

    [Fact]
    public void AFollowerRemovedFromTheWorldLowersItsLeadersCountOnce_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        CreatureState follower = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        follower.FollowLeaderId = me.Id;
        me.FollowerCount = 1;

        Assert.True(world.RemoveCreature(follower.Id));

        Assert.Equal(0, me.FollowerCount);
    }

    [Fact]
    public void AnIdleFollowerReFollowsAndARefusedOneLetsGoWithoutCounting_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        CreatureState idle = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        CreatureState sulking = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 10));
        idle.FollowLeaderId = me.Id;
        sulking.FollowLeaderId = me.Id;
        CreatureAngerRules.Apply(db.CreatureById(sulking.CreatureTypeId), sulking, AngerBucket.Treatment, 1_000_000);
        me.FollowerCount = 2;
        GameSimulation sim = Sim(db, world, new CreatureJobSystem());

        Assert.Equal(PossessionRules.FollowingState, idle.Dk2StateId);
        Assert.Equal(me.Id, idle.FollowLeaderId);
        Assert.Equal(0, sulking.FollowLeaderId);
        Assert.Equal(2, me.FollowerCount);
    }

    [Fact]
    public void AnImpFollowsThroughItsOwnSystem_ConfirmedFromBinary_ArrivalCycleUnread()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(8, 10));
        Follow(db, world, me, imp);
        GameSimulation sim = Sim(db, world, new ImpTaskSystem());

        Assert.Equal(6, imp.Dk2StateId);   // travel to a point
        Assert.Equal(PossessionRules.FollowingState, imp.UtilityNewState);
    }

    [Fact]
    public void AFollowerTakesNoLairAnger_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState bedless = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        CreatureDef goblin = db.CreatureById(bedless.CreatureTypeId);
        Assert.True(CreatureAngerRules.PerSecond(world, db, goblin, bedless).Lair > 0);
        bedless.FollowLeaderId = 99;
        Assert.Equal(0, CreatureAngerRules.PerSecond(world, db, goblin, bedless).Lair);
    }

    [Fact]
    public void AFollowerFightsItsLeadersTargetOverANearerEnemy_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 10, 10);
        CreatureState struck = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(9, 10));
        CreatureState follower = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 10));
        CreatureState nearer = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 10));
        follower.FollowLeaderId = me.Id;
        me.PossessedTargetKind = FightTargetKind.Creature;
        me.PossessedTargetId = struck.Id;
        GameSimulation sim = new(db, world, 1, [new CombatSystem()]);
        me.PossessedTargetTick = sim.CurrentTick.Value;

        sim.Tick();

        Assert.Equal(FightTargetKind.Creature, follower.FightTargetKind);
        Assert.Equal(struck.Id, follower.FightTargetId);
        _ = nearer;
    }

    [Fact]
    public void FollowerFieldsSurviveASnapshot()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        me.FollowLeaderId = 7;
        me.FollowRouteTick = 11;
        me.FollowerCount = -2;
        me.PossessedTargetKind = FightTargetKind.Door;
        me.PossessedTargetId = 13;
        me.PossessedTargetTick = 17;

        var sim = new GameSimulation(db, world, 1, []);
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        CreatureState restored = copy.World.CreatureById(me.Id)!;
        Assert.Equal(sim.StateHash(), copy.StateHash());

        Assert.Equal(7, restored.FollowLeaderId);
        Assert.Equal(11, restored.FollowRouteTick);
        Assert.Equal(-2, restored.FollowerCount);
        Assert.Equal(FightTargetKind.Door, restored.PossessedTargetKind);
        Assert.Equal(13, restored.PossessedTargetId);
        Assert.Equal(17, restored.PossessedTargetTick);
    }
}
