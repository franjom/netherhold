// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class PartyArrivalTests
{
    // 004B7A20 frees the creature slot while a separate native corpse remains.
    // 00519F90/004B7900 count live slots rather than our retained corpse records.
    [Fact]
    public void RetainedCorpseDoesNotPreventAPartyFromReusingItsFreedSlot()
    {
        var (db, world, _) = Spawn();
        CreatureState? last = null;
        for (int i = 1; i < 448; i++) last = world.SpawnCreature(db, Type, Keeper, new TilePos(7, 7));
        CreatureDeathRules.BecomeCorpse(world, db, last!);
        Assert.True(HeroPartyRules.Spawn(new StateHookContext(world, db), new SimRandom(7),
            new TriggerTree([], 0, playerRoots: []), Assert.Single(world.HeroParties), 3, 0, _ => { }));
        Assert.Equal(449, world.Creatures.Count);
        Assert.Equal(8, world.Creatures.Last().FightProcessCounter);
    }

    private const byte Type = Dk2TerrainFixture.WarlockCreatureId;
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static (GameDatabase Db, WorldState World, CreatureState Hero) Spawn(byte terrain = Dk2TerrainFixture.DirtPath)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Type ? c with
            {
                KnockdownAnimation = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 26, FramesPerSecond = 30 },
                GetUpAnimation = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 43, FramesPerSecond = 30 },
            } : c), basis.Variables, objects: basis.Objects.Values, shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < 19; y++)
            for (int x = 1; x < 19; x++) world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        foreach (TilePos t in new[] { new TilePos(5, 5), new TilePos(6, 5), new TilePos(5, 6), new TilePos(6, 6) })
        {
            world.Tile(t).TerrainId = terrain;
            world.Tile(t).OwnerPlayerId = PlayerIds.Good;
        }
        ThingProjection.Place(world, db,
        [
            new ActionPointThing(new byte[56]) { StartX = 5, EndX = 5, StartY = 5, EndY = 5, ActionPointId = 3, Name = "" },
            new HeroPartyThing(new byte[555])
            {
                Name = "arrival", PartyId = 0, Members =
                [new GoodCreatureThing(new byte[32]) { CreatureId = Type, Level = 1, InitialHealth = 100, Flags = 0x0B, Objective = 19, ObjectiveTargetPlayerId = Keeper }],
            },
        ]);
        var tree = new TriggerTree([], 0, playerRoots: []);
        Assert.True(HeroPartyRules.Spawn(new StateHookContext(world, db), new SimRandom(7), tree,
            Assert.Single(world.HeroParties), 3, 0, _ => { }));
        return (db, world, Assert.Single(world.Creatures));
    }

    /// <summary>0051A01B defaults to1; only terrain33/37 clear it at0051A107.
    /// 0051A5A6 takes269 on nonzero and clears the draw bit.</summary>
    [Theory]
    [InlineData(Dk2TerrainFixture.DirtPath, 269)]
    [InlineData(Dk2TerrainFixture.HeroGate2By2Terrain, 1)]
    [InlineData(Dk2TerrainFixture.HeroGate3By1Terrain, 1)]
    [InlineData(Dk2TerrainFixture.HeroGateTileTerrain, 269)]
    public void OriginalSpawnTerrainSelectsArrival_TheTwoLargeGatesBypassIt(byte terrain, int expected)
    {
        var (_, _, hero) = Spawn(terrain);
        Assert.Equal(expected, hero.Dk2StateId);
        Assert.Equal(expected == 269, hero.ReaperHidden);
        Assert.Equal(0u, hero.ReaperStateAgeTicks);
    }

    /// <summary>004626A0 tests exactly8 after00476FC0 increments age.
    /// The dispatcher calls only one handler, so210 cannot start64 that same turn.</summary>
    [Theory]
    [InlineData(6u, 269)]
    [InlineData(7u, 210)]
    [InlineData(8u, 269)]
    public void ArrivalUsesExactCounterEightAndOneHandlerPerUpdate(uint age, int expected)
    {
        var (db, world, hero) = Spawn();
        hero.Dk2StateId = 269;
        hero.ReaperHidden = true;
        hero.ReaperStateAgeTicks = age;
        new GameSimulation(db, world, 1, [new TremorCreatureSystem()]).Tick();
        Assert.Equal(expected, hero.Dk2StateId);
        Assert.Equal(expected == 269, hero.ReaperHidden);
        Assert.Equal(0, hero.ActionUntilTick);
        if (expected == 210) Assert.Equal((Fp32.One, Fp32.One), (hero.PosZ, hero.LastPosZ));
    }

    /// <summary>269's44 flags vs210/263/264's42:0045A6D0 scan gate,
    /// 0045A8B0 targeting gate,0048A248 steering gate. No hit/swing exception.</summary>
    [Fact]
    public void ArrivalWaitPreventsScanningTargetingAndSteering_RecoveryIsTargetable()
    {
        var (db, world, hero) = Spawn();
        var sim = new GameSimulation(db, world, 1,
            [new TremorCreatureSystem(), new HeroSystem(), new CreatureMovementSystem()]);
        int x = hero.PosX, y = hero.PosY;
        for (int i = 0; i < 7; i++)
        {
            Assert.False(CombatRules.MayLookForAFight(world, db, hero, force: false));
            Assert.False(CombatRules.IsAttackable(hero));
            sim.Tick();
            Assert.Equal(269, hero.Dk2StateId);
            Assert.Equal((x, y), (hero.PosX, hero.PosY));
        }
        sim.Tick();
        Assert.Equal(210, hero.Dk2StateId);
        Assert.True(CombatRules.IsAttackable(hero));
        Assert.False(CombatRules.MayLookForAFight(world, db, hero, force: false));
        Assert.Equal(0x100, hero.PosZ);
        sim.Tick();
        Assert.Equal(263, hero.Dk2StateId);
        Assert.Equal(0, hero.PosZ);
        sim.Tick();
        Assert.Equal(0, hero.LastPosZ);
    }

    /// <summary>00481520 starts64,004815A0 polls it then53,00458680 waits then idles.
    /// Clips chosen here derive holds5/7; the same holds appear in native439.</summary>
    [Fact]
    public void NonGatePartyRunsTheFullActionDrivenSequenceBeforeItsHeroJob()
    {
        var (db, world, hero) = Spawn();
        var sim = new GameSimulation(db, world, 1, [new TremorCreatureSystem(), new HeroSystem()]);
        sim.Run(8);
        Assert.Equal(210, hero.Dk2StateId);
        sim.Tick();
        Assert.Equal(263, hero.Dk2StateId);
        sim.Run(4);
        Assert.Equal(263, hero.Dk2StateId);
        sim.Tick();
        Assert.Equal(264, hero.Dk2StateId);
        sim.Run(6);
        Assert.Equal(264, hero.Dk2StateId);
        sim.Tick();
        Assert.Equal(1, hero.Dk2StateId);
        sim.Tick();
        Assert.Equal(90, hero.Dk2StateId);
    }

    /// <summary>00481520 explicitly ends a current action before starting64.</summary>
    [Fact]
    public void StartingKnockdownEndsTheOldSwingAndTunnelAction()
    {
        var (db, world, hero) = Spawn();
        hero.Dk2StateId = 210;
        hero.SwingInProgress = true;
        hero.Tunnel.ActionUntilTick = 100;
        new GameSimulation(db, world, 1, [new TremorCreatureSystem()]).Tick();
        Assert.Equal(263, hero.Dk2StateId);
        Assert.False(hero.SwingInProgress);
        Assert.Equal(0, hero.Tunnel.ActionUntilTick);
        Assert.Equal(5, hero.ActionUntilTick);
    }

    /// <summary>The native269 age/pose and004B2030 vertical vector survive a save.</summary>
    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void SaveAtHiddenWaitOrFirstAirborneStepContinuesIdentically(int ticks)
    {
        var (db, world, hero) = Spawn();
        ISimulationSystem[] systems = [new TremorCreatureSystem(), new CreatureMovementSystem()];
        var sim = new GameSimulation(db, world, 1, systems);
        sim.Run(ticks);
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new TremorCreatureSystem(), new CreatureMovementSystem()]);
        Assert.Equal(ticks == 7 ? 269 : 210, hero.Dk2StateId);
        Assert.Equal(ticks == 7 ? 0 : 0x100, hero.PosZ);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        for (int i = 0; i < 16; i++)
        {
            sim.Tick(); restored.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
    }
}
