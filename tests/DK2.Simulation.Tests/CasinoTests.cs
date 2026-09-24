// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Casino's lever and the gold it settles there (ADR-0188): generous calms and pays
/// the gambler at the keeper's cost, stingy angers and fleeces it and leaves the takings beneath
/// the lever.</summary>
public class CasinoTests
{
    private const byte Keeper = 4;
    private const byte Rival = 5;
    private const byte GoblinId = 7;
    private static readonly TilePos Gambling = new(2, 2);
    private static readonly TilePos LeverAt = new(2, 2);
    private static readonly TilePos Vault = new(5, 3);

    private static int Second => SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    /// <summary>A 3×3 Casino over the fixture's claimed square, (1,1) to (3,3).</summary>
    private static WorldState World(GameDatabase db, int width = 3, int height = 3)
    {
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y <= height; y++)
        for (int x = 1; x <= width; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Casino), Keeper);
        return world;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new AutoFurnitureSystem(),
            new CasinoSystem(),
            new CreatureMovementSystem(),
            new CreatureJobSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static CreatureState Gambler(GameDatabase db, WorldState world, int purse)
    {
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Gambling);
        goblin.CurrentJob = JobType.Drink;
        goblin.Activity = CreatureActivity.AtWork;
        CreatureStateMachine.Begin(goblin, JobRules.StateFor(goblin.CurrentJob)!.Value);
        goblin.GoldHeld = purse;
        return goblin;
    }

    private static ObjectState Lever(WorldState world) =>
        world.Objects.Single(o => o.ObjectTypeId == AutoFurnitureRules.CasinoLeverObjectId);

    private static int Index(WorldState world, TilePos at) => (at.Y * world.Width) + at.X;

    private static void Fund(WorldState world, int amount)
    {
        ref TileState tile = ref world.Tile(Vault.X, Vault.Y);
        tile.OwnerPlayerId = Keeper;
        tile.GoldHeld = amount;
    }

    /// <summary>Ticks until the condition holds, failing after two settlements' worth.</summary>
    private static void RunUntil(GameSimulation sim, Func<bool> done)
    {
        for (int i = 0; i < 2 * CasinoRules.SettlementSeconds * Second; i++)
        {
            if (done()) return;
            sim.Tick();
        }
        Assert.True(done(), "the condition never held");
    }

    private static void Pull(GameSimulation sim, byte player) =>
        sim.Tick(InputFrame.Of(new PullCasinoLeverCommand(player, LeverAt)));

    [Fact]
    public void ACasinosLeverStandsOnTheCentreOfAThreeByThreeAndStartsGenerous()
    {
        // 0x004D6ED0: the first furniture slot (an interior slab of the room's parity) whose
        // north-west, north and north-east neighbours are not slots. A 3x3 has one.
        GameDatabase db = Db;
        WorldState world = World(db);
        Simulation(db, world).Run(1);

        ObjectState lever = Lever(world);
        Assert.Equal(Index(world, LeverAt), lever.TileIndex);
        Assert.Equal(Keeper, lever.OwnerPlayerId);
        Assert.Equal(CasinoRules.Generous, lever.Setting);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(2, 3)]
    public void ACasinoWithNoInteriorHasNoLever(int width, int height)
    {
        // The owner: "lever appears only when a casino is at minimum 3x3".
        GameDatabase db = Db;
        WorldState world = World(db, width, height);
        Simulation(db, world).Run(1);

        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == AutoFurnitureRules.CasinoLeverObjectId);
    }

    [Fact]
    public void ABiggerCasinosLeverIsTheFirstSlotOfItsTopInteriorRowAndNoTableStandsOnIt()
    {
        // 5x5 over (1,1)-(5,5): its slots are the interior tiles of (2,2)'s parity, and the
        // first with no slot to its north-west and north-east is (2,2) itself.
        GameDatabase db = Db;
        WorldState world = World(db, 5, 5);
        Simulation(db, world).Run(1);

        int at = Index(world, new TilePos(2, 2));
        Assert.Equal(at, Lever(world).TileIndex);

        // The rule itself, since the fixture has no records for the tables: the other slots
        // get furniture, the lever's gets nothing else.
        var furniture = new List<(int TileIndex, byte ObjectTypeId)>();
        RoomInstance room = RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == RoomTypeId.Casino);
        AutoFurnitureRules.DesiredFurniture(world, db, room, furniture);
        Assert.Equal([AutoFurnitureRules.CasinoLeverObjectId],
            furniture.Where(f => f.TileIndex == at).Select(f => f.ObjectTypeId));
        Assert.Contains(furniture, f => f.TileIndex == Index(world, new TilePos(3, 3)));
    }

    [Fact]
    public void PullingTheLeverTogglesItAndARivalsPullDoesNothing()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);

        Pull(sim, Keeper);
        Assert.Equal(CasinoRules.Stingy, Lever(world).Setting);
        Pull(sim, Rival);
        Assert.Equal(CasinoRules.Stingy, Lever(world).Setting);
        Pull(sim, Keeper);
        Assert.Equal(CasinoRules.Generous, Lever(world).Setting);
    }

    [Fact]
    public void AStingyCasinoAngersAndFleecesItsGamblerAndPutsThirtyTwoBeneathTheLever()
    {
        // Each second +100 to buckets 5, 0 and 3 and 8 out of the purse. Every four seconds the
        // room puts 4 x 8 = 32 on a pile at the lever, where no pile was before.
        GameDatabase db = Db;
        WorldState world = World(db);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        Pull(sim, Keeper);

        CreatureState goblin = Gambler(db, world, purse: 100);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        RunUntil(sim, () => sink.Events.Count(e => e.Kind == "settled") == 2);

        int seconds = sink.Events.Count(e => e.Kind == "gambled");
        Assert.Equal(100 - (8 * seconds), goblin.GoldHeld);
        Assert.Equal(seconds * 100, goblin.Anger.Work);
        Assert.Equal(goblin.Anger.Work, goblin.Anger.Treatment);
        Assert.Equal(goblin.Anger.Work, goblin.Anger.Company);
        Assert.Equal(0, goblin.Anger.Pay);
        Assert.Equal(64, FloorGold.OnTile(world, db, Index(world, LeverAt)));

        // Four game seconds apart: the room's turn modulo variable 19 x 4.
        long[] settled = sink.Events.Where(e => e.Kind == "settled").Select(e => e.Tick.Value).ToArray();
        Assert.Equal(CasinoRules.SettlementSeconds * Second, settled[1] - settled[0]);
    }

    [Fact]
    public void AGenerousCasinoPaysOutFourAGamblerFromThePileBeneathTheLeverThenTheTreasury()
    {
        // 0x004D7180 with -4: the pile first, then the keeper's gold for the rest.
        GameDatabase db = Db;
        WorldState world = World(db);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        FloorGold.Drop(world, db, Index(world, LeverAt), 6);
        Fund(world, 1000);

        Gambler(db, world, purse: 100);
        Gambler(db, world, purse: 100);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        RunUntil(sim, () => sink.Events.Any(e => e.Kind == "settled"));

        // Two gamblers, 8 owed: 4 and 2 off the pile, which is then gone, and 2 from the vault.
        Assert.Equal(0, FloorGold.OnTile(world, db, Index(world, LeverAt)));
        Assert.Equal(998, world.Tile(Vault.X, Vault.Y).GoldHeld);
        Assert.Contains(sink.Events, e => e.Kind == "settled"
            && e.Detail.Contains("gamblers=2") && e.Detail.Contains("paid=8"));
    }

    [Fact]
    public void AStingyCasinoSendsItsGamblerIdleOnceThePurseIsDownToTheStake()
    {
        // 24 in the purse: 16 after one second, 8 after two, and the third second's test
        // fails the hook.
        GameDatabase db = Db;
        WorldState world = World(db);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        Pull(sim, Keeper);

        CreatureState goblin = Gambler(db, world, purse: 24);
        sim.Run(Second * 4);

        Assert.Equal(8, goblin.GoldHeld);
        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
    }
}
