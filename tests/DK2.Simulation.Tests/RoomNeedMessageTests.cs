// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Room messages 41 to 43: the shipped subtype rows, and the Treasury and Lair producers,
/// whose gates differ from each other (computer-keeper-messages.md section 2, ADR-0327). The world
/// is the fixture map: the keeper's 3×3 claimed room at (1..3, 1..3) in rock.</summary>
public class RoomNeedMessageTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int Second = SimulationTiming.TicksPerSecond;
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static WorldState World() =>
        WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static void Availability(WorldState world, RoomTypeId room, int value)
    {
        List<AvailabilityState> rows = world.Player(Keeper).Availabilities;
        rows.RemoveAll(a => a.Kind == RoomNeedRules.RoomAvailabilityKind && a.TypeId == (int)room);
        if (value != 1) rows.Add(new AvailabilityState(RoomNeedRules.RoomAvailabilityKind, (int)room, value));
    }

    private static void Room(WorldState world, RoomTypeId room, int x, int y, int gold = 0)
    {
        ref TileState tile = ref world.Tile(x, y);
        RoomRules.Place(Db, ref tile, Db.Room(room), Keeper);
        tile.GoldHeld = gold;
    }

    private sealed class Probe(Action<SimulationContext> act) : ISimulationSystem
    {
        public string Name => "Probe";
        public void Execute(SimulationContext context) => act(context);
    }

    /// <summary>Runs one tick whose only system is <paramref name="act"/>, and returns what it posted.</summary>
    private static PlayerMessage? Once(WorldState world, Func<SimulationContext, PlayerMessage?> act)
    {
        PlayerMessage? posted = null;
        new GameSimulation(Db, world, 1, [new Probe(c => posted = act(c))]).Tick();
        return posted;
    }

    // ---- the queue ---------------------------------------------------------------

    [Theory]
    [InlineData(PlayerMessageKind.RoomNeeded, RoomTypeId.Treasury, 420)]
    [InlineData(PlayerMessageKind.RoomNeeded, RoomTypeId.Lair, 180)]
    [InlineData(PlayerMessageKind.RoomNeeded, RoomTypeId.Prison, 600)]
    [InlineData(PlayerMessageKind.RoomNeeded, RoomTypeId.Library, 300)]
    [InlineData(PlayerMessageKind.RoomFull, RoomTypeId.Treasury, 240)]
    [InlineData(PlayerMessageKind.RoomFull, RoomTypeId.Lair, 90)]
    [InlineData(PlayerMessageKind.RoomFull, RoomTypeId.Hatchery, 240)]
    [InlineData(PlayerMessageKind.RoomUnreachable, RoomTypeId.Treasury, 300)]
    [InlineData(PlayerMessageKind.RoomUnreachable, RoomTypeId.Prison, 360)]
    public void ARoomMessageLivesForItsShippedSubtypeRow(PlayerMessageKind kind, RoomTypeId room, int seconds)
    {
        WorldState world = World();
        PlayerMessage posted = world.Messages.PostRoom(world, Keeper, kind, room)!.Value;
        Assert.Equal(((int)room, 0, (uint)(seconds * Second), true), (posted.Data1, posted.Data2, posted.Timeout, posted.NeedsTab));
    }

    [Fact]
    public void ARoomMessageIsADuplicateOnlyForTheSameKindAndRoom()
    {
        WorldState world = World();
        uint first = world.Messages.PostRoom(world, Keeper, PlayerMessageKind.RoomFull, RoomTypeId.Treasury)!.Value.Id;
        Assert.Equal(first, world.Messages.PostRoom(world, Keeper, PlayerMessageKind.RoomFull, RoomTypeId.Treasury)!.Value.Id);
        Assert.NotEqual(first, world.Messages.PostRoom(world, Keeper, PlayerMessageKind.RoomFull, RoomTypeId.Lair)!.Value.Id);
        Assert.NotEqual(first, world.Messages.PostRoom(world, Keeper, PlayerMessageKind.RoomNeeded, RoomTypeId.Treasury)!.Value.Id);
        Assert.Equal(3, world.Messages.Entries.Count);
    }

    [Fact]
    public void AnUnlistedRoomAndTheSuppressedGraveyardRequestAreRefused()
    {
        WorldState world = World();
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Messages.PostRoom(world, Keeper, PlayerMessageKind.RoomNeeded, RoomTypeId.Portal));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Messages.PostRoom(world, Keeper, PlayerMessageKind.RoomFull, RoomTypeId.Portal));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Messages.PostRoom(world, Keeper, PlayerMessageKind.RoomUnreachable, RoomTypeId.DungeonHeart));
        Assert.Throws<NotSupportedException>(() => world.Messages.PostRoom(world, Keeper, PlayerMessageKind.RoomNeeded, RoomTypeId.Graveyard));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Messages.PostRoom(world, Keeper, PlayerMessageKind.CombatStarted, RoomTypeId.Lair));
        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void AMessageThatNamesNoRoomHasNoRoomAndStillPrints()
    {
        WorldState world = World();
        PlayerMessage combat = world.Messages.Post(world, Keeper, PlayerMessageKind.CombatStarted, 2, 3)!.Value;
        Assert.Null(combat.Room);
        Assert.Contains("CombatStarted", combat.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFullLairMessageIsGoneOnTheSweepThatReachesNinetySeconds()
    {
        var sim = new GameSimulation(Db, World(), 1);
        sim.World.Messages.PostRoom(sim.World, Keeper, PlayerMessageKind.RoomFull, RoomTypeId.Lair);
        sim.Run((90 * Second) - 1);
        Assert.Equal((uint)(90 * Second) - 1, Assert.Single(sim.World.Messages.Entries).Age);
        sim.Tick();
        Assert.Empty(sim.World.Messages.Entries);
    }

    // ---- the gold carrier's store chooser (0x00464400) ---------------------------------

    private static PlayerMessage? CarrierFindsNoStore(WorldState world)
    {
        CreatureState imp = world.SpawnCreature(Db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        imp.GoldHeld = 300;
        return Once(world, c => RoomNeedRules.NoStoreForCarrier(c, imp));
    }

    [Fact]
    public void ACarrierWithNoTreasuryAsksForOneOnlyAtAvailabilityThree()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Treasury, 3);
        PlayerMessage needed = CarrierFindsNoStore(world)!.Value;
        Assert.Equal((PlayerMessageKind.RoomNeeded, RoomTypeId.Treasury), (needed.Kind, needed.Room));

        WorldState researchable = World();
        Availability(researchable, RoomTypeId.Treasury, 2);
        Assert.Null(CarrierFindsNoStore(researchable));
        Assert.Empty(researchable.Messages.Entries);
    }

    [Fact]
    public void ACarrierWhoseTreasuryIsFullPostsFullWhateverTheAvailability()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Treasury, 2);
        Room(world, RoomTypeId.Treasury, 1, 1, gold: 3000);
        PlayerMessage full = CarrierFindsNoStore(world)!.Value;
        Assert.Equal((PlayerMessageKind.RoomFull, RoomTypeId.Treasury), (full.Kind, full.Room));
    }

    [Fact]
    public void ACarrierWhoCannotReachATreasuryWithSpacePostsUnreachable()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Treasury, 3);
        Room(world, RoomTypeId.Treasury, 1, 1, gold: 3000);
        Room(world, RoomTypeId.Treasury, 9, 8); // in the rock, with space and no way in
        PlayerMessage unreachable = CarrierFindsNoStore(world)!.Value;
        Assert.Equal((PlayerMessageKind.RoomUnreachable, RoomTypeId.Treasury), (unreachable.Kind, unreachable.Room));
    }

    [Fact]
    public void ACarrierThatCanReachATreasuryWithSpacePostsNothing()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Treasury, 3);
        Room(world, RoomTypeId.Treasury, 1, 1);
        Assert.Null(CarrierFindsNoStore(world));
    }

    // ---- gold no store could take (0x004BA420, from a sale's refund) ---------------------

    [Fact]
    public void LeftOverGoldAsksForATreasuryUnlessItsAvailabilityIsOne()
    {
        WorldState none = World();
        Assert.Null(Once(none, c => RoomNeedRules.GoldLeftOver(c, Keeper)));

        WorldState researchable = World();
        Availability(researchable, RoomTypeId.Treasury, 2);
        PlayerMessage needed = Once(researchable, c => RoomNeedRules.GoldLeftOver(c, Keeper))!.Value;
        Assert.Equal((PlayerMessageKind.RoomNeeded, RoomTypeId.Treasury), (needed.Kind, needed.Room));

        WorldState owned = World();
        Availability(owned, RoomTypeId.Treasury, 2);
        Room(owned, RoomTypeId.Treasury, 1, 1, gold: 3000);
        PlayerMessage full = Once(owned, c => RoomNeedRules.GoldLeftOver(c, Keeper))!.Value;
        Assert.Equal((PlayerMessageKind.RoomFull, RoomTypeId.Treasury), (full.Kind, full.Room));
    }

    [Fact]
    public void SellingARoomWhoseRefundNoStoreTakesPostsFullAndOneThatFitsPostsNothing()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Treasury, 3);
        Room(world, RoomTypeId.Treasury, 1, 1, gold: 3000);
        Room(world, RoomTypeId.Lair, 3, 3);
        var sim = new GameSimulation(Db, world, 1, [new RoomSellSystem()]);
        sim.Tick(InputFrame.Of(new SellRoomCommand(Keeper, [new TilePos(3, 3)])));
        PlayerMessage full = Assert.Single(world.Messages.Entries);
        Assert.Equal((PlayerMessageKind.RoomFull, RoomTypeId.Treasury), (full.Kind, full.Room));

        WorldState roomy = World();
        Availability(roomy, RoomTypeId.Treasury, 3);
        Room(roomy, RoomTypeId.Treasury, 1, 1);
        Room(roomy, RoomTypeId.Lair, 3, 3);
        new GameSimulation(Db, roomy, 1, [new RoomSellSystem()])
            .Tick(InputFrame.Of(new SellRoomCommand(Keeper, [new TilePos(3, 3)])));
        Assert.Empty(roomy.Messages.Entries);
    }

    // ---- the Lair finder (0x004E0470) ----------------------------------------------------

    private static PlayerMessage? GoblinFindsNoLair(WorldState world, int ticksBefore = 0)
    {
        CreatureState goblin = world.SpawnCreature(Db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(2, 2));
        CreatureDef definition = Db.CreatureById(goblin.CreatureTypeId);
        PlayerMessage? posted = null;
        var sim = new GameSimulation(Db, world, 1, [new Probe(c =>
        {
            if (c.Tick.Value == ticksBefore) posted = RoomNeedRules.NoLairFor(c, goblin, definition);
        })]);
        sim.Run(ticksBefore + 1);
        return posted;
    }

    /// <summary>A one-tile Lair at (3, 3), its bed taken by a lodger, stamped as rebuilt at tick 0.</summary>
    private static WorldState WithAFullLair()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Lair, 3);
        Room(world, RoomTypeId.Lair, 3, 3);
        CreatureState lodger = world.SpawnCreature(Db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));
        LairRules.PlaceBed(world, Db, lodger, (3 * world.Width) + 3);
        return world;
    }

    [Fact]
    public void NoLairAsksForOneAtAvailabilityThreeAndNothingOtherwise()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Lair, 3);
        PlayerMessage needed = GoblinFindsNoLair(world)!.Value;
        Assert.Equal((PlayerMessageKind.RoomNeeded, RoomTypeId.Lair), (needed.Kind, needed.Room));

        WorldState researchable = World();
        Availability(researchable, RoomTypeId.Lair, 2);
        Assert.Null(GoblinFindsNoLair(researchable));
    }

    [Fact]
    public void AFullLairPostsOnlyStrictlyMoreThanFourSecondsAfterItsLastSizeChange()
    {
        Assert.Null(GoblinFindsNoLair(WithAFullLair(), ticksBefore: 4 * Second));
        PlayerMessage full = GoblinFindsNoLair(WithAFullLair(), ticksBefore: (4 * Second) + 1)!.Value;
        Assert.Equal((PlayerMessageKind.RoomFull, RoomTypeId.Lair), (full.Kind, full.Room));

        WorldState rebuilt = WithAFullLair();
        rebuilt.Tile(3, 3).RoomSizeChangedTick = 10;
        Assert.Null(GoblinFindsNoLair(rebuilt, ticksBefore: (4 * Second) + 10));
    }

    [Fact]
    public void ALairWithASlotAndNoRoutePostsUnreachableAndAReachableOnePostsNothing()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Lair, 3);
        Room(world, RoomTypeId.Lair, 9, 8);
        PlayerMessage unreachable = GoblinFindsNoLair(world)!.Value;
        Assert.Equal((PlayerMessageKind.RoomUnreachable, RoomTypeId.Lair), (unreachable.Kind, unreachable.Room));

        WorldState reachable = World();
        Availability(reachable, RoomTypeId.Lair, 3);
        Room(reachable, RoomTypeId.Lair, 3, 3);
        Assert.Null(GoblinFindsNoLair(reachable));
    }

    // ---- the size-change stamp, room+0x9F ------------------------------------------------

    [Fact]
    public void ARoomTileBuiltOnATickStampsItsWholeRoomWithThatTick()
    {
        WorldState world = World();
        Room(world, RoomTypeId.Lair, 1, 1);
        var sim = new GameSimulation(Db, world, 1, [new Probe(c =>
        {
            if (c.Tick.Value != 7) return;
            ref TileState tile = ref c.World.Tile(2, 1);
            byte terrain = tile.TerrainId, owner = tile.OwnerPlayerId;
            RoomRules.Place(Db, ref tile, Db.Room(RoomTypeId.Lair), Keeper);
            SlabChain.Changed(c.World, Db, (1 * c.World.Width) + 2, terrain, owner);
        })]);
        sim.Run(8);
        Assert.Equal((7L, 7L), (world.Tile(1, 1).RoomSizeChangedTick, world.Tile(2, 1).RoomSizeChangedTick));
        RoomInstance lair = RoomIndex.Build(world, Db).Single(r => r.Definition.TypeId == RoomTypeId.Lair);
        Assert.Equal(7L, SlabChain.SizeChangedTick(world, lair));
    }

    [Fact]
    public void TheStampIsHashedAndSurvivesASnapshot()
    {
        GameSimulation a = new(Db, World(), 1), b = new(Db, World(), 1);
        a.World.Tile(1, 1).RoomSizeChangedTick = 5;
        Assert.NotEqual(a.StateHash(), b.StateHash());
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(a), Db, []);
        Assert.Equal((5L, a.StateHash()), (copy.World.Tile(1, 1).RoomSizeChangedTick, copy.StateHash()));
    }
}
