// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using Xunit;

namespace DK2.AI.Tests;

/// <summary>The gold step (<c>0x004FD590</c>, docs/research/computer-keeper-spell-sites.md §2): a
/// stored deposit becomes task 5's target; with none stored the keeper rescans and casts Create Gold
/// at the heart's centre plus (1, 1), spending no allowance and ignoring the result.</summary>
public class KeeperGoldStepTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private const int Centre = 0x800;

    /// <summary>A keeper short of gold whose room planner declines without its cost test: no room
    /// is available, so task 0 falls through to the gold step.</summary>
    private static WorldState ShortOfGold(int createGold = 3)
    {
        WorldState world = KeeperWorld.World(Db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 100;
        foreach (RoomTypeId room in new[] { RoomTypeId.Treasury, RoomTypeId.Lair, RoomTypeId.Hatchery })
            world.SetAvailability(Keeper, 1, (byte)room, 1);
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.CreateGold, (byte)createGold);
        return world;
    }

    private static void AddSeam(WorldState world)
    {
        for (int y = 4; y <= 6; y++)
            for (int x = 14; x <= 16; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.Gold);
    }

    private static KeeperAI Keeper1(SimRandom? rng = null, byte speed = 1) =>
        new(Keeper, KeeperWorld.Parameters(speed: speed, money: 1000), rng ?? new SimRandom(7));

    private static CastSpellAtPlaceCommand ExpectedCast() =>
        new(Keeper, KeeperSpellRules.CreateGold,
            ((KeeperWorld.HeartAt.X + 1) << 12) + Centre, ((KeeperWorld.HeartAt.Y + 1) << 12) + Centre);

    [Fact]
    public void WithNoStoredDepositTheKeeperCastsCreateGoldAtTheCentreSouthEastOfTheHeart()
    {
        WorldState world = ShortOfGold();

        IReadOnlyList<PlayerCommand> commands = Keeper1().Think(Db, world);

        Assert.Equal(ExpectedCast(), Assert.Single(commands.OfType<CastSpellAtPlaceCommand>()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void OnlyAvailabilityThreeCasts(int availability)
    {
        WorldState world = ShortOfGold(availability);

        Assert.Empty(Keeper1().Think(Db, world).OfType<CastSpellAtPlaceCommand>());
    }

    [Fact]
    public void AKeeperWithItsTendancyMoneyTakesNoGoldStep()
    {
        WorldState world = ShortOfGold();
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 1000;

        Assert.Empty(Keeper1().Think(Db, world).OfType<CastSpellAtPlaceCommand>());
    }

    [Fact]
    public void TheCastIsIssuedWithoutAManaTestTheSimulationMakes()
    {
        WorldState world = ShortOfGold();
        world.Player(Keeper).Mana = 0;

        Assert.Single(Keeper1().Think(Db, world).OfType<CastSpellAtPlaceCommand>());
    }

    [Fact]
    public void TheCastSpendsNoAllowance()
    {
        // Each task-0 pass spends one; the cast spends none, so two passes cast twice.
        WorldState world = ShortOfGold();

        Assert.Equal(2, Keeper1(speed: 2).Think(Db, world).OfType<CastSpellAtPlaceCommand>().Count());
    }

    [Fact]
    public void TheGoldStepDrawsNoRandomNumber()
    {
        WorldState world = ShortOfGold();
        var rng = new SimRandom(7);
        var twin = new SimRandom(7);

        Keeper1(rng).Think(Db, world);
        twin.NextInt(100); // step 9's training roll, drawn every tick

        Assert.Equal(twin.NextInt(int.MaxValue), rng.NextInt(int.MaxValue));
    }

    [Fact]
    public void TheFirstGoldStepCastsBesideASeamBecauseCreationLeavesNothingStored_CreationZeroInferred()
    {
        WorldState world = ShortOfGold();
        AddSeam(world);
        KeeperAI keeper = Keeper1();

        Assert.Single(keeper.Think(Db, world).OfType<CastSpellAtPlaceCommand>());
        Assert.Equal(KeeperTask.Decide, keeper.State.Task);
        Assert.Equal(new TilePos(15, 5), Assert.Single(keeper.State.GoldDeposits).Centre);

        Assert.Empty(keeper.Think(Db, world).OfType<CastSpellAtPlaceCommand>());
        Assert.Equal(KeeperTask.RouteToGold, keeper.State.Task);
        Assert.Equal(new TilePos(15, 5), keeper.State.DigRoomAt);
    }

    [Fact]
    public void TheStoredDepositIsCopiedBeforeTheRescanAndTheIndexStaysZero()
    {
        WorldState world = ShortOfGold();
        AddSeam(world);
        KeeperAI keeper = Keeper1();
        keeper.State.GoldDeposits.Add(new GoldDeposit(new TilePos(5, 5), 1));

        keeper.Think(Db, world);

        Assert.Equal(KeeperTask.RouteToGold, keeper.State.Task);
        Assert.Equal(new TilePos(5, 5), keeper.State.DigRoomAt);
        Assert.Equal(new TilePos(15, 5), Assert.Single(keeper.State.GoldDeposits).Centre);
        Assert.Equal(0, keeper.State.CurrentGoldArea);
    }

    [Fact]
    public void TheSimulationMakesTheGoldBesideAOneTileHeartAndCharges()
    {
        WorldState world = ShortOfGold();
        int objects = world.Objects.Count;
        CastSpellAtPlaceCommand cast = Assert.Single(Keeper1().Think(Db, world).OfType<CastSpellAtPlaceCommand>());

        new GameSimulation(Db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]).Tick(InputFrame.Of(cast));

        Assert.Equal(100000 - 15000, world.Player(Keeper).Mana);
        ObjectState gold = Assert.Single(world.Objects.Skip(objects));
        Assert.Equal((FloorGold.FloorGoldObjectId, 1000), (gold.ObjectTypeId, gold.GoldHeld));
    }

    [Fact]
    public void AFiveByFiveHeartRefusesTheCastAtItsOwnMiddle()
    {
        WorldState world = ShortOfGold();
        for (int y = KeeperWorld.HeartAt.Y - 2; y <= KeeperWorld.HeartAt.Y + 2; y++)
            for (int x = KeeperWorld.HeartAt.X - 2; x <= KeeperWorld.HeartAt.X + 2; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.HeartFloor, Keeper);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 100;
        int objects = world.Objects.Count;
        CastSpellAtPlaceCommand cast = Assert.Single(Keeper1().Think(Db, world).OfType<CastSpellAtPlaceCommand>());
        Assert.Equal(ExpectedCast(), cast);

        new GameSimulation(Db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]).Tick(InputFrame.Of(cast));

        Assert.Equal(100000, world.Player(Keeper).Mana);
        Assert.Equal(objects, world.Objects.Count);
    }

    [Fact]
    public void ARunOnAMapWithoutSeamsCastsAtEveryGoldStepUntilTheManaRunsOut()
    {
        // Floor gold is not stored gold, so the keeper stays short and casts every tick; the
        // simulation pays 15,000 a cast until the mana falls below it.
        WorldState world = ShortOfGold();
        var sim = new GameSimulation(Db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        KeeperAI keeper = Keeper1();
        int issued = 0;

        for (int tick = 0; tick < 10; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            issued += commands.OfType<CastSpellAtPlaceCommand>().Count();
            sim.Tick(new InputFrame(commands));
        }

        Assert.Equal(10, issued);
        Assert.Equal(100000 - (6 * 15000), world.Player(Keeper).Mana);
        Assert.Equal(6 * 1000, world.Objects.Where(o => o.ObjectTypeId == FloorGold.FloorGoldObjectId).Sum(o => o.GoldHeld));
    }
}
