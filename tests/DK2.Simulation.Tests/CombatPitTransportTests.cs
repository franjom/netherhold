// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;
namespace DK2.Simulation.Tests;

public sealed class CombatPitTransportTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos Floor = new(6, 6);

    private static ISimulationSystem[] Steps(bool movement) => movement
        ? [new PreviousPositionStep(), new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureHealthSystem()]
        : [new PreviousPositionStep(), new CreatureStatusSystem(), new CreatureHealthSystem()];

    private static (GameSimulation Sim, CreatureState Loser) Setup(bool movement = true)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(16, 16), [PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < 15; y++)
        for (int x = 1; x < 15; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = Keeper;
        }
        for (int y = 4; y <= 8; y++)
        for (int x = 4; x <= 8; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.CombatPit), Keeper);
        CreatureState loser = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, Floor);
        loser.PosZ = loser.LastPosZ = -0xC00;
        loser.CombatPitFighter = true;
        CreatureDeathRules.Collapse(new StateHookContext(world, db), loser);
        return (new GameSimulation(db, world, 42, Steps(movement)), loser);
    }

    [Fact]
    public void TheDefeatedCreatureSinksBeforeItMovesToTheRimAndRisesThere()
    {
        (GameSimulation sim, CreatureState loser) = Setup();
        sim.Tick();
        Assert.Equal(CreatureDeathRules.DyingState, loser.Dk2StateId);
        int floorX = loser.PosX, floorY = loser.PosY;
        sim.Run(11);
        Assert.Equal((floorX, floorY), (loser.PosX, loser.PosY));
        Assert.Equal(-0xC00, loser.PosZ);
        sim.Tick();
        Assert.Equal(-0xE00, loser.PosZ);
        int elapsed = loser.DyingTicks;
        sim.Tick();
        Assert.Equal(-0x1000, loser.PosZ);
        Assert.NotEqual(Floor, loser.Tile);
        Assert.InRange(loser.Tile.X, 4, 8);
        Assert.InRange(loser.Tile.Y, 4, 8);
        Assert.True(loser.Tile.X is 4 or 8 || loser.Tile.Y is 4 or 8);
        Assert.Equal((0x400, 0x400), (loser.PosX & 0xFFF, loser.PosY & 0xFFF));
        Assert.Equal(0x300, loser.Heading);
        Assert.Equal(elapsed, loser.DyingTicks);
        sim.Run(10);
        Assert.Equal(0, loser.PosZ);
        Assert.Equal(CreatureDeathRules.DyingState, loser.Dk2StateId);
        Assert.False(loser.CombatPitFighter);
    }

    [Fact]
    public void TheHandlerRequestsAnEffectAtEachEndAndPausesTheCountdownThroughoutBothPhases()
    {
        (GameSimulation sim, CreatureState loser) = Setup(movement: false);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Run(11); // 216 on turn 0; the strict turn > 10 gate still holds.
        Assert.Equal(PitTransportPhase.None, loser.PitTransportPhase);
        Assert.Equal(10, loser.DyingTicks);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);
        sim.Tick();
        Assert.Equal(PitTransportPhase.Sinking, loser.PitTransportPhase);
        sim.Run(2);
        ViewEffectRequest[] effects = events.Events.Where(e => e.EffectRequest.HasValue)
            .Select(e => e.EffectRequest!.Value).ToArray();
        Assert.Equal(2, effects.Length);
        Assert.All(effects, effect => Assert.Equal(new EffectId(0x149), effect.EffectId));
        Assert.Equal(0x400, effects[0].Position.Z.Raw);
        Assert.Equal(0, effects[1].Position.Z.Raw);
        Assert.Equal(loser.Tile, effects[1].Tile);
        Assert.Equal(new Dk2Angle(0x300), effects[1].Heading);
        Assert.Equal(10, loser.DyingTicks);
        for (int z = -0xE00; z <= 0; z += 0x200)
        {
            sim.Tick();
            Assert.Equal(z, loser.PosZ);
            Assert.Equal(10, loser.DyingTicks);
        }
        Assert.Equal(PitTransportPhase.None, loser.PitTransportPhase);
        sim.Tick();
        Assert.Equal(11, loser.DyingTicks);
    }

    [Fact]
    public void ThePhysicalPassKeepsTheOldFloorUntilARisingBodyCommitsOntoTheRim()
    {
        (GameSimulation sim, CreatureState loser) = Setup();
        sim.Run(14);
        Assert.Equal((-0x1000, 0x400), (loser.PosZ, loser.PitTransportFloorHeight.Raw));
        sim.Run(2);
        Assert.Equal((-0xC00, 0x400), (loser.PosZ, loser.PitTransportFloorHeight.Raw));
        sim.Tick();
        Assert.Equal((0, 0x1000), (loser.PosZ, loser.PitTransportFloorHeight.Raw));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(16)]
    public void SavingAndCopyingDuringTransportPreservesItsContinuation(int checkpoint)
    {
        (GameSimulation sim, CreatureState loser) = Setup();
        sim.Run(checkpoint);
        WorldState copy = WorldState.CopyOf(sim.World);
        CreatureState copied = copy.CreatureById(loser.Id)!;
        Assert.Equal((loser.PitTransportPhase, loser.PitTransportAfter, loser.PitTransportFloorHeight),
            (copied.PitTransportPhase, copied.PitTransportAfter, copied.PitTransportFloorHeight));
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, Steps(movement: true));
        Assert.Equal(sim.StateHash(), restored.StateHash());
        for (int tick = 0; tick < 24; tick++)
        {
            sim.Tick();
            restored.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EachTransportContinuationFieldParticipatesInTheStateHash(int field)
    {
        (GameSimulation sim, CreatureState loser) = Setup();
        sim.Run(13);
        ulong before = sim.StateHash();
        switch (field)
        {
            case 0: loser.PitTransportPhase = PitTransportPhase.Rising; break;
            case 1: loser.PitTransportAfter = new GameTick(loser.PitTransportAfter.Value + 1); break;
            default: loser.PitTransportFloorHeight = new Fp32(0x1000); break;
        }
        Assert.NotEqual(before, sim.StateHash());
    }

    [Fact]
    public void ADefeatedCreatureOnTheRimDoesNotStartAnotherTransport()
    {
        (GameSimulation sim, CreatureState loser) = Setup();
        loser.PosX = loser.LastPosX = CreatureState.CentreOf(4);
        loser.PosZ = loser.LastPosZ = 0;
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Run(30);
        Assert.Equal(PitTransportPhase.None, loser.PitTransportPhase);
        Assert.Equal(new TilePos(4, 6), loser.Tile);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);
        Assert.Equal(29, loser.DyingTicks);
    }

    [Fact]
    public void AnImpCanCollectTheRimLoserAndCarryItBackToItsBed()
    {
        (GameSimulation sim, CreatureState loser) = Setup();
        var lair = new TilePos(2, 2);
        RoomRules.Place(sim.Database, ref sim.World.Tile(lair), sim.Database.Room(RoomTypeId.Lair), Keeper);
        Assert.True(LairRules.TryPlaceBedInAnyLair(sim.World, sim.Database, loser));
        sim.Run(24);
        Assert.True(loser.Tile.X is 4 or 8 || loser.Tile.Y is 4 or 8);
        sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 6));
        var events = new ListEventSink();
        GameSimulation rescue = GameSimulation.Resume(sim.Database, sim.World, sim.Rng,
            [new CreatureStatusSystem(), new ImpTaskSystem(), new CreatureMovementSystem(), new CreatureHealthSystem()]);
        rescue.ObserveWith(events);
        for (int tick = 0; tick < 200 && loser.Dk2StateId != LairRules.SleepingState; tick++) rescue.Tick();
        Assert.Equal(LairRules.SleepingState, loser.Dk2StateId);
        Assert.Equal(lair, loser.Tile);
        Assert.Contains(events.Events, e => e.Kind == "creature_rescued");
    }
}
