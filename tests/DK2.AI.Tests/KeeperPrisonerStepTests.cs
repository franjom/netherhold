// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.AI.Tests;

/// <summary>The prisoner step against <c>0x005006D0</c> (computer-keeper-management.md §1): the
/// Prison draw, the occupant walk from the newest claim, the enemy roll, the allowance, the four
/// destinations, the guards and the Torture slot's occupancy test.</summary>
public class KeeperPrisonerStepTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database(torture: true);
    private const byte Keeper = KeeperWorld.Keeper;
    private const byte Enemy = 4;

    private static readonly TilePos PrisonCell = new(20, 20);
    private static readonly TilePos TortureSlot = new(25, 15);

    private static WorldState World(bool prison = true, bool lair = true, bool temple = true, bool pit = true, bool torture = true)
    {
        WorldState world = KeeperWorld.World(Db, morePlayers: [Enemy]);
        if (prison) Room(world, KeeperWorld.PrisonFloor, PrisonCell.X - 1, PrisonCell.Y - 1);
        if (lair) Room(world, KeeperWorld.LairFloor, 8, 20);
        if (temple) Room(world, KeeperWorld.TempleFloor, 20, 8);
        if (pit) Room(world, KeeperWorld.CombatPitFloor, 8, 8);
        if (torture) Room(world, KeeperWorld.TortureFloor, TortureSlot.X - 1, TortureSlot.Y - 1);
        return world;
    }

    /// <summary>A 3×3 room laid tile by tile through the slab chain; its one interior tile is its only floor slot.</summary>
    private static void Room(WorldState world, byte floor, int x, int y)
    {
        for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
            {
                var at = new TilePos(x + dx, y + dy);
                int index = (at.Y * world.Width) + at.X;
                byte terrain = world.Tile(at).TerrainId, owner = world.Tile(at).OwnerPlayerId;
                KeeperWorld.Set(world, Db, at, floor, Keeper);
                SlabChain.Changed(world, Db, index, terrain, owner);
            }
    }

    /// <summary>A creature holding a place in the Prison, claimed as the <paramref name="serial"/>th.</summary>
    private static CreatureState Occupant(WorldState world, byte owner, long serial, int dx = 0)
    {
        var at = new TilePos(PrisonCell.X + dx, PrisonCell.Y);
        CreatureState creature = world.SpawnCreature(Db, KeeperWorld.FighterCreature, owner, at);
        creature.PrisonOwnerPlayerId = Keeper;
        creature.Dk2StateId = PrisonRules.InTheCellState;
        creature.HeldPlace = new RoomPlace((at.Y * world.Width) + at.X, 1);
        creature.HeldPlaceSerial = serial;
        return creature;
    }

    private static (int Left, List<PlayerCommand> Commands) Run(
        WorldState world, byte action = 3, int allowance = 4, ulong seed = 7, HashSet<int>? prepared = null,
        SimRandom? rng = null)
    {
        var view = new KeeperView(Db, world, Keeper);
        KeeperAIParameters p = KeeperWorld.Parameters() with { ImprisonedCreatureAction = action };
        var commands = new List<PlayerCommand>();
        return (new KeeperPrisonerStep(view, p, rng ?? new SimRandom(seed), commands, prepared ?? []).Run(allowance), commands);
    }

    /// <summary>The stream after the given draws, as a fresh generator from <paramref name="seed"/> makes them.</summary>
    private static int NextAfter(ulong seed, params int[] draws)
    {
        var rng = new SimRandom(seed);
        foreach (int range in draws) rng.NextInt(range);
        return rng.NextInt(1 << 30);
    }

    /// <summary>Lines the chamber's west side with a room wall, giving it a wall slot west of its floor slot.</summary>
    private static int WallTheTortureChamber(WorldState world)
    {
        for (int y = TortureSlot.Y - 2; y <= TortureSlot.Y + 2; y++)
            KeeperWorld.Set(world, Db, new TilePos(TortureSlot.X - 2, y), KeeperWorld.WallRock);
        return (TortureSlot.Y * world.Width) + TortureSlot.X - 1;
    }

    private static TilePos PositionOf(WorldState world, RoomTypeId type)
    {
        var view = new KeeperView(Db, world, Keeper);
        return view.PositionOf(view.OwnRooms.Single(r => r.Definition.RoomId == (byte)type));
    }

    /// <summary>A seed whose first <paramref name="rolls"/> enemy rolls, after the Prison draw, all
    /// fail and whose next passes (<paramref name="pass"/>) or fails too.</summary>
    private static ulong SeedWhere(int rolls, bool pass)
    {
        for (ulong seed = 1; ; seed++)
        {
            var rng = new SimRandom(seed);
            rng.NextInt(1);
            bool ok = true;
            for (int i = 0; i < rolls && ok; i++) ok = rng.NextInt(100) >= KeeperPrisonerStep.EnemySelectPercent;
            if (ok && (rng.NextInt(100) < KeeperPrisonerStep.EnemySelectPercent) == pass) return seed;
        }
    }

    private static int[] Lifted(IEnumerable<PlayerCommand> commands) =>
        commands.OfType<PickUpCreatureCommand>().Select(c => c.CreatureId).ToArray();

    private static TilePos[] Drops(IEnumerable<PlayerCommand> commands) =>
        commands.OfType<DropCommand>().Select(c => c.Tile).ToArray();

    [Fact]
    public void WithoutAPrisonOrAnOccupantNothingIsSpent()
    {
        Assert.Equal((4, 0), (Run(World(prison: false)).Left, Run(World(prison: false)).Commands.Count));
        (int left, List<PlayerCommand> commands) = Run(World());
        Assert.Equal((4, 0), (left, commands.Count));
    }

    [Fact]
    public void ANonEnemyIsTakenOutrightToALair()
    {
        WorldState world = World();
        CreatureState own = Occupant(world, Keeper, serial: 1);

        (int left, List<PlayerCommand> commands) = Run(world);

        Assert.Equal([own.Id], Lifted(commands));
        Assert.Equal([PositionOf(world, RoomTypeId.Lair)], Drops(commands));
        Assert.Equal(3, left);
    }

    [Fact]
    public void TheWalkStartsAtTheNewestClaim()
    {
        WorldState world = World();
        Occupant(world, Keeper, serial: 1);
        CreatureState newer = Occupant(world, Keeper, serial: 2, dx: 1);

        Assert.Equal([newer.Id], Lifted(Run(world).Commands));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEnemyIsTakenOnlyOnTheFivePercentRoll(bool pass)
    {
        WorldState world = World();
        CreatureState enemy = Occupant(world, Enemy, serial: 1);

        (int left, List<PlayerCommand> commands) = Run(world, seed: SeedWhere(0, pass));

        // Action 3 takes it to the Temple; a failed roll selects nobody and spends nothing.
        Assert.Equal(pass ? [enemy.Id] : Array.Empty<int>(), Lifted(commands));
        Assert.Equal(pass ? [PositionOf(world, RoomTypeId.Temple)] : Array.Empty<TilePos>(), Drops(commands));
        Assert.Equal(pass ? 3 : 4, left);
    }

    [Fact]
    public void AnEnemyWhoseRollFailsPassesTheWalkOnToTheNextOccupant()
    {
        WorldState world = World();
        Occupant(world, Enemy, serial: 2);
        CreatureState own = Occupant(world, Keeper, serial: 1, dx: 1);

        Assert.Equal([own.Id], Lifted(Run(world, seed: SeedWhere(0, pass: false)).Commands));
    }

    [Theory]
    [InlineData(2, RoomTypeId.CombatPit)]
    [InlineData(3, RoomTypeId.Temple)]
    public void AnEnemyGoesWhereImprisonedCreatureActionSays(byte action, RoomTypeId room)
    {
        WorldState world = World();
        Occupant(world, Enemy, serial: 1);

        Assert.Equal([PositionOf(world, room)], Drops(Run(world, action, seed: SeedWhere(0, pass: true)).Commands));
    }

    [Fact]
    public void ActionOneAndAMissingRoomStillSpendTheSelection()
    {
        WorldState world = World(temple: false);
        Occupant(world, Enemy, serial: 1);
        ulong seed = SeedWhere(0, pass: true);

        Assert.Equal((3, 0), (Run(world, action: 1, seed: seed).Left, Run(world, action: 1, seed: seed).Commands.Count));
        (int left, List<PlayerCommand> commands) = Run(world, action: 3, seed: seed);
        Assert.Equal((3, 0), (left, commands.Count));
    }

    [Fact]
    public void TortureTakesAFurnitureSlotNobodyIsTorturedOn()
    {
        WorldState world = World();
        Occupant(world, Enemy, serial: 1);
        ulong seed = SeedWhere(0, pass: true);
        var view = new KeeperView(Db, world, Keeper);
        KeeperRoomView chamber = view.OwnRooms.Single(r => r.Definition.RoomId == (byte)RoomTypeId.TortureChamber);
        Assert.Equal([(TortureSlot.Y * world.Width) + TortureSlot.X], chamber.FurnitureSlots());

        Assert.Equal([TortureSlot], Drops(Run(world, action: 0, seed: seed).Commands));

        CreatureState victim = world.SpawnCreature(Db, KeeperWorld.FighterCreature, Enemy, TortureSlot);
        victim.Dk2StateId = TortureRules.ReleasedState;
        (int left, List<PlayerCommand> commands) = Run(world, action: 0, seed: seed);
        Assert.Equal((3, 0), (left, commands.Count));
    }

    [Fact]
    public void TortureDrawsAmongFloorAndWallSlotsInChainOrder()
    {
        WorldState world = World();
        int wall = WallTheTortureChamber(world), floor = (TortureSlot.Y * world.Width) + TortureSlot.X;
        var view = new KeeperView(Db, world, Keeper);
        KeeperRoomView chamber = view.OwnRooms.Single(r => r.Definition.RoomId == (byte)RoomTypeId.TortureChamber);

        Assert.Equal([wall], chamber.WallSlots);
        Assert.Equal(chamber.ChainOrder.Where(t => t == wall || t == floor), chamber.FurnitureSlots());
        Assert.Equal(chamber.WallSlots, KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(view), Db).OwnRooms
            .Single(r => r.Definition.RoomId == (byte)RoomTypeId.TortureChamber).WallSlots);
    }

    [Fact]
    public void ThePlaceAndTheTortureDeviceSurviveTheSnapshot()
    {
        WorldState world = World();
        CreatureState own = Occupant(world, Keeper, serial: 9);
        own.Dk2StateId = TortureRules.ReleasedState;
        var view = new KeeperView(Db, world, Keeper);

        KeeperCreatureView before = view.Creatures.Single(c => c.Id == own.Id);
        KeeperCreatureView after = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(view), Db).Creatures.Single(c => c.Id == own.Id);

        Assert.Equal((own.HeldPlace.TileIndex, 9L, true), (before.PlaceTile, before.PlaceSerial, before.OnTortureDevice));
        Assert.Equal(before, after);
    }

    [Fact]
    public void TheTortureSlotIsDrawnAmongEverySlot()
    {
        WorldState world = World();
        int wall = WallTheTortureChamber(world);
        Occupant(world, Enemy, serial: 1);
        var view = new KeeperView(Db, world, Keeper);
        int[] slots = view.OwnRooms.Single(r => r.Definition.RoomId == (byte)RoomTypeId.TortureChamber).FurnitureSlots().ToArray();
        var reached = new HashSet<TilePos>();

        // Prison, roll, chamber, then the slot among two: each slot is drawn under some seed, and the
        // one taken is the draw's.
        for (ulong seed = 1; seed < 400 && reached.Count < 2; seed++)
        {
            var rng = new SimRandom(seed);
            rng.NextInt(1);
            if (rng.NextInt(100) >= KeeperPrisonerStep.EnemySelectPercent) continue;
            rng.NextInt(1);
            TilePos expected = view.FromIndex(slots[rng.NextInt(slots.Length)]);
            Assert.Equal([expected], Drops(Run(world, action: 0, seed: seed).Commands));
            reached.Add(expected);
        }
        Assert.Equal(new HashSet<TilePos> { view.FromIndex(wall), TortureSlot }, reached);
    }

    [Fact]
    public void ATortureDeviceOnAnotherTileDoesNotBlockTheSlot()
    {
        WorldState world = World();
        Occupant(world, Enemy, serial: 1);
        CreatureState victim = world.SpawnCreature(Db, KeeperWorld.FighterCreature, Enemy, new TilePos(TortureSlot.X + 1, TortureSlot.Y));
        victim.Dk2StateId = TortureRules.ReleasedState;

        Assert.Equal([TortureSlot], Drops(Run(world, action: 0, seed: SeedWhere(0, pass: true)).Commands));
    }

    [Fact]
    public void EachEnemyRollsUntilOneIsTaken()
    {
        WorldState world = World();
        Occupant(world, Enemy, serial: 2);
        CreatureState older = Occupant(world, Enemy, serial: 1, dx: 1);

        Assert.Equal([older.Id], Lifted(Run(world, seed: SeedWhere(1, pass: true)).Commands));
    }

    [Fact]
    public void AnAlliedOccupantIsNoEnemyAndGoesToALair()
    {
        WorldState world = World();
        world.SetAllied(Keeper, Enemy, allied: true);
        CreatureState ally = Occupant(world, Enemy, serial: 1);

        (int left, List<PlayerCommand> commands) = Run(world, seed: SeedWhere(0, pass: false));

        Assert.Equal([ally.Id], Lifted(commands));
        Assert.Equal([PositionOf(world, RoomTypeId.Lair)], Drops(commands));
        Assert.Equal(3, left);
    }

    public static TheoryData<string> Guards => ["landing", "stunned", "unpermitted", "held"];

    [Theory]
    [MemberData(nameof(Guards))]
    public void AGuardRefusesTheMoveButNotTheSpending(string guard)
    {
        WorldState world = World();
        CreatureState own = Occupant(world, Keeper, serial: 1);
        switch (guard)
        {
            case "landing": own.Dk2StateId = DropLandingRules.LandingState; break;
            case "stunned": own.Dk2StateId = BoulderRules.StunnedState; break;
            case "unpermitted": own.ManaRemovalStamp = 1; break;
            case "held":
                // Still listed in the Prison while the hand holds it, as the walk could meet it.
                RoomPlace place = own.HeldPlace;
                new GameSimulation(Db, world, 1, [new HandSystem()]).Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, own.Id)));
                Assert.True(own.InHand);
                (own.HeldPlace, own.HeldPlaceSerial) = (place, 1);
                break;
        }

        (int left, List<PlayerCommand> commands) = Run(world);

        Assert.Equal((3, 0), (left, commands.Count));
    }

    [Theory]
    [InlineData(300, 301, false)]
    [InlineData(301, 301, true)]
    [InlineData(300, 300, true)]
    public void TheDroppedCounterGuardHoldsFromTurnThreeHundredAndOne(int counter, int turn, bool moved)
    {
        WorldState world = World();
        var clock = new GameSimulation(Db, world, 1, Array.Empty<ISimulationSystem>());
        while (world.CurrentTick.Value + 1 < turn) clock.Tick(new InputFrame(Array.Empty<PlayerCommand>()));
        CreatureState own = Occupant(world, Keeper, serial: 1);
        own.DroppedTick = world.CurrentTick.Value - counter;

        (int left, List<PlayerCommand> commands) = Run(world);

        Assert.Equal(moved ? [own.Id] : Array.Empty<int>(), Lifted(commands));
        Assert.Equal(3, left);
    }

    [Fact]
    public void TheLairIsDrawnBeforeTheGuards()
    {
        WorldState world = World();
        Room(world, KeeperWorld.LairFloor, 8, 24);
        CreatureState own = Occupant(world, Keeper, serial: 1);
        own.Dk2StateId = DropLandingRules.LandingState;
        var rng = new SimRandom(7);

        Run(world, rng: rng);

        // The Prison among one, then the Lair among two, though the guard refuses the move.
        Assert.Equal(NextAfter(7, 1, 2), rng.NextInt(1 << 30));
    }

    [Fact]
    public void TheTortureSlotIsDrawnOnlyAfterTheGuards()
    {
        WorldState world = World();
        WallTheTortureChamber(world);
        CreatureState enemy = Occupant(world, Enemy, serial: 1);
        enemy.Dk2StateId = BoulderRules.StunnedState;
        ulong seed = SeedWhere(0, pass: true);
        var rng = new SimRandom(seed);

        Run(world, action: 0, rng: rng);

        // Prison, roll, chamber; the guard refuses before the slot among two is drawn.
        Assert.Equal(NextAfter(seed, 1, 100, 1), rng.NextInt(1 << 30));
    }

    [Fact]
    public void AnEarlierStepsPreparedCreatureIsLandingAndRefused()
    {
        WorldState world = World();
        CreatureState own = Occupant(world, Keeper, serial: 1);

        (int left, List<PlayerCommand> commands) = Run(world, prepared: [own.Id]);

        Assert.Equal((3, 0), (left, commands.Count));
    }

    [Fact]
    public void ARefusedLiftIsAttemptedWithoutADrop()
    {
        GameDatabase full = KeeperWorld.Database(handCapacity: 0, torture: true);
        WorldState world = World();
        CreatureState own = Occupant(world, Keeper, serial: 1);
        var view = new KeeperView(full, world, Keeper);
        var commands = new List<PlayerCommand>();

        int left = new KeeperPrisonerStep(view, KeeperWorld.Parameters(), new SimRandom(7), commands, []).Run(4);

        Assert.Equal([own.Id], Lifted(commands));
        Assert.Empty(Drops(commands));
        Assert.Equal(3, left);
    }
}
