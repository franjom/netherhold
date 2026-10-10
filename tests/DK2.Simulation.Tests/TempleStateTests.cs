// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Temple's states 101–107 (temple-states.md, ADR-0403).</summary>
public class TempleStateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Vampire = Dk2TerrainFixture.VampireCreatureId;

    /// <summary>A Vampire with the prayer's art (slot 23) and the shipped step, 1257 ahead.</summary>
    private static GameDatabase Db(bool art = true)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var clip = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 20, FramesPerSecond = 20 };
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        if (art) slots[TempleStep.PrayerArtSlot] = clip;
        return new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Vampire ? c with { ArtSlots = slots, PrayerStepY = -1257 } : c),
            basis.Variables, basis.KeeperSpells.Values, objects: basis.Objects.Values, shots: basis.Shots.Values);
    }

    /// <summary>A square Temple of the keeper's from (1,1); a 5×5 one has a 3×3 pool.</summary>
    private static WorldState World(GameDatabase db, int size)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= size; y++)
        for (int x = 1; x <= size; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Temple), Keeper);
        return world;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, int seed = 42) =>
        new(db, world, seed: (ulong)seed, [new CreatureMovementSystem(), new CreatureJobSystem()]);

    private static CreatureState Worshipper(GameDatabase db, WorldState world, TilePos at, int state = TempleStep.EnteringState)
    {
        CreatureState it = world.SpawnCreature(db, Vampire, Keeper, at);
        it.CurrentJob = JobType.Praying;
        StatePlacement.As(it, CreatureActivity.AtWork);
        StatePlacement.Place(it, state);
        it.HeldPlace = new RoomPlace((at.Y * world.Width) + at.X, RoomCapacityRules.Units);   // the enter hook's claim
        return it;
    }

    private static HashSet<int> Watch(GameSimulation sim, CreatureState creature, int ticks)
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < ticks; i++)
        {
            sim.Tick();
            seen.Add(CreatureStateTable.Get(creature.Dk2StateId).IsUtility ? creature.UtilityNewState : creature.Dk2StateId);
        }

        return seen;
    }

    [Fact]
    public void AWorshipperChoosesASpotFacesTheCentrePraysAndChoosesAgain()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = Worshipper(db, world, new TilePos(1, 3));

        HashSet<int> seen = Watch(Simulation(db, world), it, 30 * SimulationTiming.TicksPerSecond);

        // 101 is left on the first turn, before the first sample.
        Assert.Superset(new HashSet<int> { TempleStep.ChoosingState, TempleStep.FacingState, TempleStep.PrayingState }, seen);
        Assert.Equal(JobType.Praying, it.CurrentJob);
    }

    [Fact]
    public void TheWorshipperFacesTheRoomsCentreToPray_OursEndsAWalkWithinItsOwnTileAtOnce()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = Worshipper(db, world, new TilePos(1, 3), TempleStep.ChoosingState);
        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 200 && it.Dk2StateId != TempleStep.PrayingState; i++) sim.Tick();

        // West of the centre (3,3), so it faces east. DK2 would first walk a quarter tile further
        // out; ours ends a walk to a point on its own tile at once (temple-states.md §8).
        Assert.Equal(TempleStep.PrayingState, it.Dk2StateId);
        Assert.Equal(0x200, it.Heading);
    }

    [Fact]
    public void ThePrayerIsActionTwentySevenTwoOrThreeMorePlays_AndWithoutItsArtThereIsNone()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = Worshipper(db, world, new TilePos(1, 3), TempleStep.ChoosingState);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world);
        sim.ObserveWith(sink);
        for (int i = 0; i < 200 && it.Dk2StateId != TempleStep.PrayingState; i++) sim.Tick();
        CreatureDef vampire = db.CreatureById(Vampire);
        SimEvent pray = Assert.Single(sink.Events, e => e.Kind == "temple_pray");
        int count = int.Parse(pray.Detail.Split(' ')[1]["count=".Length..]);
        Assert.InRange(count, 2, 3);
        Assert.Equal(sim.World.CurrentTick.Value - 1 + AnimationTimingRules.CountedHoldTicks(vampire, TempleStep.PrayerArtSlot, count),
            it.ActionUntilTick);

        GameDatabase bare = Db(art: false);
        WorldState world2 = World(bare, 5);
        CreatureState plain = Worshipper(bare, world2, new TilePos(1, 3), TempleStep.ChoosingState);
        var sink2 = new ListEventSink();
        GameSimulation sim2 = Simulation(bare, world2);
        sim2.ObserveWith(sink2);
        for (int i = 0; i < 200 && plain.Dk2StateId != TempleStep.PrayingState; i++) sim2.Tick();
        Assert.DoesNotContain(sink2.Events, e => e.Kind == "temple_pray");
    }

    [Fact]
    public void WhenThePrayerEndsTheWorshipperStepsItsPrayerStepAheadAndChoosesAgain()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = Worshipper(db, world, new TilePos(1, 3), TempleStep.PrayingState);
        it.Heading = 0x200;   // east, into the rim tile's own row
        (it.WaitPosX, it.WaitPosY) = (it.PosX, it.PosY);
        it.ActionUntilTick = 0;
        int x = it.PosX;

        new GameSimulation(db, world, seed: 1, [new CreatureJobSystem()]).Tick();

        Assert.Equal(TempleStep.ChoosingState, it.Dk2StateId);
        Assert.Equal(x + ((TrapRules.Sine(0x200) * 1257) >> 16), it.PosX);
    }

    [Fact]
    public void TheStepIsTakenEvenIntoThePool_DK2sTileTestComparesItsOwnTileWithItself()
    {
        GameDatabase db = Db() is var d ? new GameDatabase(d.Terrain.Values, d.Rooms.Values,
            d.Creatures.Values.Select(c => c.CreatureId == Vampire ? c with { PrayerStepY = -4096 } : c),
            d.Variables, d.KeeperSpells.Values, objects: d.Objects.Values, shots: d.Shots.Values) : null!;
        WorldState world = World(db, 5);
        CreatureState it = Worshipper(db, world, new TilePos(1, 3), TempleStep.PrayingState);
        it.Heading = 0x200;   // east: (2,3) is the pool
        (it.WaitPosX, it.WaitPosY) = (it.PosX, it.PosY);

        new GameSimulation(db, world, seed: 1, [new CreatureJobSystem()]).Tick();

        Assert.Equal(TempleStep.ChoosingState, it.Dk2StateId);
        Assert.Equal(new TilePos(2, 3), it.Tile);
    }

    [Fact]
    public void AWorshipperMovedDuringItsPrayerGoesBackTo101AndEndsThePrayer()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = Worshipper(db, world, new TilePos(1, 3), TempleStep.PrayingState);
        it.ActionUntilTick = long.MaxValue;
        (it.WaitPosX, it.WaitPosY) = (it.PosX - 100, it.PosY);
        it.StateEnteredTick = -5;

        new GameSimulation(db, world, seed: 1, [new CreatureJobSystem()]).Tick();

        Assert.Equal(TempleStep.EnteringState, it.Dk2StateId);
        Assert.Equal(0, it.ActionUntilTick);
    }

    [Fact]
    public void ASecondCreatureOnTheTileSendsTheChooserToAFreeRimNeighbour()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState first = Worshipper(db, world, new TilePos(1, 3), TempleStep.PrayingState);
        first.ActionUntilTick = long.MaxValue;
        (first.WaitPosX, first.WaitPosY) = (first.PosX, first.PosY);
        CreatureState it = Worshipper(db, world, new TilePos(1, 3), TempleStep.ChoosingState);
        GameSimulation sim = Simulation(db, world);

        for (int i = 0; i < 100 && it.Dk2StateId != TempleStep.FacingState; i++) sim.Tick();

        Assert.Equal(TempleStep.FacingState, it.Dk2StateId);
        Assert.Contains(it.Tile, new[] { new TilePos(1, 2), new TilePos(1, 4) });   // never the pool at (2,3)
    }

    /// <summary>101 on the pool (temple-states.md §3): the creature is the offering, as DK2's
    /// 101 makes it (<c>0x004F1600</c>), whatever the recipe says.</summary>
    [Fact]
    public void OnThePoolA101WorshipperIsTheOffering()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = Worshipper(db, world, new TilePos(3, 3));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world);
        sim.ObserveWith(sink);

        sim.Run(2);

        Assert.Null(world.CreatureById(it.Id));
        Assert.Contains(sink.Events, e => e.Kind == "sacrificed_in_pool" && e.Detail.Contains($"creature={it.Id} "));
        Assert.Equal(1, Assert.Single(world.TempleSacrifices).Count);
    }

    [Fact]
    public void ATempleRebuiltUnderItsHoldersSendsThemTo107AndBackThrough101()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = Worshipper(db, world, new TilePos(1, 3), TempleStep.PrayingState);
        it.ActionUntilTick = long.MaxValue;
        (it.WaitPosX, it.WaitPosY) = (it.PosX, it.PosY);
        GameSimulation sim = Simulation(db, world);
        sim.Tick();

        // A new tile joins the Temple, which stamps the rebuilt room (0x004E4480's slot +0x09).
        RoomRules.Place(db, ref world.Tile(6, 3), db.Room(RoomTypeId.Temple), Keeper);
        SlabChain.Changed(world, db, (3 * world.Width) + 6, Dk2TerrainFixture.ClaimedPath, Keeper);
        HashSet<int> seen = Watch(sim, it, 4);

        Assert.Contains(TempleStep.ReturningState, seen);
        Assert.Contains(TempleStep.EnteringState, seen);
        Assert.Equal(JobType.Praying, it.CurrentJob);
    }

    [Fact]
    public void In107OffAnyTempleOfItsOwnTheCreatureGoesIdle()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = Worshipper(db, world, new TilePos(8, 8), TempleStep.ReturningState);
        it.HeldPlace = RoomPlace.None;

        Simulation(db, world).Run(3);

        Assert.Equal(JobType.None, it.CurrentJob);
    }

    [Fact]
    public void ASummonedCreatureClaimsAPlaceAndWalksToARimTileThenIdles()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = world.SpawnCreature(db, Vampire, Keeper, new TilePos(1, 3));
        GameSimulation sim = Simulation(db, world);
        sim.ObserveWith(new ListEventSink());
        var summon = new GameSimulation(db, world, seed: 1, [new SummonProbe(it)]);
        summon.Tick();
        Assert.Equal(TempleStep.SummonedState, it.Dk2StateId);
        Assert.True(it.HeldPlace.IsHeld);

        for (int i = 0; i < 20 * SimulationTiming.TicksPerSecond && it.Dk2StateId != CreatureStateMachine.Idle; i++) sim.Tick();

        // Off the pool, on a rim tile; the job ended when the walk began (its resume is 1).
        Assert.Equal(CreatureStateMachine.Idle, it.Dk2StateId);
        RoomInstance temple = RoomIndex.Build(world, db).Single();
        Assert.True(temple.Contains((it.Tile.Y * world.Width) + it.Tile.X));
        Assert.False(AutoFurnitureRules.IsFloorSlot(world, temple, (it.Tile.Y * world.Width) + it.Tile.X));
        Assert.Equal(JobType.None, it.CurrentJob);
    }

    private sealed class SummonProbe(CreatureState creature) : ISimulationSystem
    {
        public string Name => "Probe";
        public void Execute(SimulationContext context)
        {
            if (creature.Dk2StateId != TempleStep.SummonedState) TempleStep.Summon(context, Name, creature);
        }
    }

    [Fact]
    public void SummonedRouteDoesNotCrossThePoolBoundary()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = world.SpawnCreature(db, Vampire, Keeper, new TilePos(3, 3));
        var summon = new GameSimulation(db, world, seed: 1, [new SummonProbe(it)]);
        summon.Tick();
        Assert.Equal(TempleStep.SummonedState, it.Dk2StateId);
        Simulation(db, world).Tick();
        Assert.Equal(CreatureStateMachine.Idle, it.Dk2StateId);
        Assert.Equal(new TilePos(3, 3), it.Tile);
    }
}
