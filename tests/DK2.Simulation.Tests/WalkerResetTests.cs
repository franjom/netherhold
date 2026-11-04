// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary><c>0x004E8D30</c> (ADR-0654): a closed room rectangle or a placed door or trap blueprint
/// stops the keeper's creatures in state 3, 4 or 7, or in 6 resuming 1, and kicks them to 1.</summary>
public sealed class WalkerResetTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte GoblinId = 7;
    private const int TravelToPoint = 6, TravelToRoom = 7;   // DK2's two travel states (travel-states.md)

    private static (GameDatabase Db, WorldState World) World()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        return (db, world);
    }

    private static CreatureState Walker(GameDatabase db, WorldState world, byte owner, int travel, int resume)
    {
        CreatureState creature = world.SpawnCreature(db, GoblinId, owner, new TilePos(2, 2));
        CreatureStateMachine.BeginTravel(new StateHookContext(world, db), creature, travel, resume);
        return creature;
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(1, false)]
    public void TheResetTakesStatesThreeAndFourAndNoOtherNonTravelState(int state, bool resets)
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState creature = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        StatePlacement.Place(creature, state);
        Assert.Equal(resets, WalkerResetRules.Resets(creature));
    }

    [Fact]
    public void AWalkToARoomOrToAPointResumingIdleIsReset_AWalkToAChickenIsNot()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState toLair = Walker(db, world, Keeper, TravelToRoom, LairRules.MakingAHomeState);
        CreatureState wandering = Walker(db, world, Keeper, TravelToPoint, CreatureStateMachine.Idle);
        CreatureState toBird = Walker(db, world, Keeper, TravelToPoint, EatingRules.EatState);
        CreatureState rivals = Walker(db, world, Rival, TravelToRoom, LairRules.MakingAHomeState);

        WalkerResetRules.Reset(new StateHookContext(world, db), world, tick: 10, Keeper);

        Assert.Equal(CreatureStateMachine.Idle, toLair.Dk2StateId);
        Assert.Equal(CreatureStateMachine.Idle, wandering.Dk2StateId);
        Assert.Equal(TravelToPoint, toBird.Dk2StateId);
        Assert.Equal(TravelToRoom, rivals.Dk2StateId);
        Assert.Equal(10, toLair.StateEnteredTick);
    }

    [Fact]
    public void TheBuildWaveResetsWhenItClosesItsRectangle_NotBefore()
    {
        (GameDatabase db, WorldState world) = World();
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 1)), db.Room(RoomTypeId.Treasury), Keeper);
        GoldStore.Deposit(world, db, Keeper, 10000);
        CreatureState walker = Walker(db, world, Keeper, TravelToRoom, LairRules.MakingAHomeState);
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, seed: 1, [new RoomBuildSystem(), new RoomBuildWaveSystem()]);
        sim.ObserveWith(events);

        // A 2x2 closes on its third diagonal (RoomBuildWaveTests).
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        sim.Tick();
        Assert.Equal(TravelToRoom, walker.Dk2StateId);
        sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, walker.Dk2StateId);
        Assert.Single(events.Events, e => e.Kind == "walker_reset");
    }

    [Fact]
    public void APlacedDoorBlueprintResets_ARefusedOneDoesNot()
    {
        (GameDatabase db, WorldState world) = World();
        for (int x = 1; x <= 8; x++)
        for (int y = 3; y <= 5; y++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = y == 4 ? Dk2TerrainFixture.ClaimedPath : Dk2TerrainFixture.Rock;
            tile.OwnerPlayerId = y == 4 ? Keeper : (byte)0;
        }
        world.Tile(1, 4).GoldHeld = 10_000;
        CreatureState walker = Walker(db, world, Keeper, TravelToRoom, LairRules.MakingAHomeState);
        var sim = new GameSimulation(db, world, seed: 1, [new BlueprintSystem()]);

        // Off the corridor there are no walls to hang a Wooden Door between: refused, no reset.
        sim.Tick(InputFrame.Of(new PlaceDoorCommand(Keeper, new TilePos(5, 7), 1)));
        Assert.Equal(TravelToRoom, walker.Dk2StateId);

        sim.Tick(InputFrame.Of(new PlaceDoorCommand(Keeper, new TilePos(5, 4), 1)));
        Assert.Single(world.Doors);
        Assert.Equal(CreatureStateMachine.Idle, walker.Dk2StateId);
    }

    [Fact]
    public void APlacedTrapBlueprintResets()
    {
        (GameDatabase db, WorldState world) = World();
        for (int x = 1; x <= 8; x++)
        {
            ref TileState tile = ref world.Tile(x, 7);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }
        world.Tile(1, 7).GoldHeld = 10_000;
        CreatureState walker = Walker(db, world, Keeper, TravelToRoom, LairRules.MakingAHomeState);
        var sim = new GameSimulation(db, world, seed: 1, [new BlueprintSystem()]);

        sim.Tick(InputFrame.Of(new PlaceTrapCommand(Keeper, new TilePos(5, 7), 7)));

        Assert.Single(world.Traps);
        Assert.Equal(CreatureStateMachine.Idle, walker.Dk2StateId);
    }

    [Fact]
    public void AWaveThatRunsOutOfGoldClosesAndResetsAsWell()
    {
        // A -1 refusal jumps to the press-corner close (0x004C775F) like a finished wave.
        (GameDatabase db, WorldState world) = World();
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 1)), db.Room(RoomTypeId.Treasury), Keeper);
        GoldStore.Deposit(world, db, Keeper, 10000);
        CreatureState walker = Walker(db, world, Keeper, TravelToRoom, LairRules.MakingAHomeState);
        var sim = new GameSimulation(db, world, seed: 1, [new RoomBuildSystem(), new RoomBuildWaveSystem()]);

        // Admitted with the whole drag paid for; the gold is gone before the second diagonal.
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        Assert.Contains(world.RoomBuildWaves, slot => slot.HasValue);
        Assert.Equal(TravelToRoom, walker.Dk2StateId);
        Assert.True(GoldStore.TryWithdraw(world, Keeper, GoldStore.Total(world, Keeper)));
        sim.Tick();

        Assert.DoesNotContain(world.RoomBuildWaves, slot => slot.HasValue);
        Assert.Equal(CreatureStateMachine.Idle, walker.Dk2StateId);
    }

    [Fact]
    public void TheComputerKeepersDirectBuildResetsAWanderer()
    {
        // 0x004C5B40 closes a one-tile rectangle after each tile; ours sends that path as a Direct order.
        (GameDatabase db, WorldState world) = World();
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 1)), db.Room(RoomTypeId.Treasury), Keeper);
        GoldStore.Deposit(world, db, Keeper, 10000);
        CreatureState wanderer = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        StatePlacement.Place(wanderer, 3);
        var sim = new GameSimulation(db, world, seed: 1, [new RoomBuildSystem()]);

        sim.Tick(InputFrame.Of(BuildRoomCommand.At(Keeper, RoomTypeId.Lair, new TilePos(1, 1))));

        Assert.Equal(CreatureStateMachine.Idle, wanderer.Dk2StateId);
    }

    [Fact]
    public void SellingARoomResetsNobody()
    {
        // The sell closer 0x004F09B0 does not reach 0x004E8D30 (blueprint-placement.md section 5).
        (GameDatabase db, WorldState world) = World();
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 1)), db.Room(RoomTypeId.Treasury), Keeper);
        RoomRules.Place(db, ref world.Tile(new TilePos(1, 1)), db.Room(RoomTypeId.Lair), Keeper);
        CreatureState walker = Walker(db, world, Keeper, TravelToRoom, LairRules.MakingAHomeState);
        var sim = new GameSimulation(db, world, seed: 1, [new RoomSellSystem()]);

        sim.Tick(InputFrame.Of(SellRoomCommand.At(Keeper, new TilePos(1, 1))));

        Assert.NotEqual(Dk2TerrainFixture.LairTerrain, world.Tile(1, 1).TerrainId);
        Assert.Equal(TravelToRoom, walker.Dk2StateId);
    }
}
