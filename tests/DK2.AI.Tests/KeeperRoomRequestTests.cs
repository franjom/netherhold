// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.AI.Tests;

/// <summary>The keeper's room messages: 41 and 42's arms of the message walk (<c>0x004FA400</c>), the
/// planner taking a request (<c>0x004FC690</c>) and the expansion rectangle (<c>0x004FBE00</c>),
/// ADR-0327. The world is <see cref="KeeperWorld"/>'s: the heart at (15, 15) in a 5×5 claimed patch.</summary>
public class KeeperRoomRequestTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private const byte Treasury = (byte)RoomTypeId.Treasury, Lair = (byte)RoomTypeId.Lair, Portal = (byte)RoomTypeId.Portal;

    private static KeeperAIParameters Policy(int policy) =>
        KeeperWorld.Parameters(speed: 4) with { BuildNewRoomWhenMoreSpaceRequired = (byte)policy, RoomList = [Treasury] };

    /// <summary>A 3×3 Treasury at (10..12, 14..16), joined to the claimed patch on its east side.</summary>
    private static WorldState WithTreasury(int goldPerTile = 0)
    {
        WorldState world = KeeperWorld.World(Db);
        for (int y = 14; y <= 16; y++)
            for (int x = 10; x <= 12; x++)
            {
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.TreasuryFloor, Keeper);
                world.Tile(x, y).GoldHeld = goldPerTile;
            }
        return world;
    }

    private static (KeeperRoomRequests Rooms, List<PlayerCommand> Commands) Arms(
        WorldState world, KeeperAIState state, KeeperAIParameters p, Func<byte, bool>? available = null)
    {
        var commands = new List<PlayerCommand>();
        var view = new KeeperView(Db, world, Keeper);
        return (new KeeperRoomRequests(view, state, p, available ?? (_ => true), commands), commands);
    }

    private static PlayerMessage Post(WorldState world, PlayerMessageKind kind, RoomTypeId room) =>
        world.Messages.PostRoom(world, Keeper, kind, room)!.Value;

    // ---- 41 ---------------------------------------------------------------------------

    [Fact]
    public void ANeededRoomThatIsAvailableBecomesTheNextRoomAndIsHandled()
    {
        WorldState world = KeeperWorld.World(Db);
        var state = new KeeperAIState();
        (KeeperRoomRequests rooms, _) = Arms(world, state, Policy(1));
        Assert.True(rooms.RoomNeeded(Post(world, PlayerMessageKind.RoomNeeded, RoomTypeId.Lair)));
        Assert.Equal((Lair, true), (state.NextRoomToBuild, state.NextRoomToBuildSet));
    }

    [Fact]
    public void ANeededRoomThatIsNotAvailableIsHandledAndForgotten()
    {
        WorldState world = KeeperWorld.World(Db);
        var state = new KeeperAIState();
        (KeeperRoomRequests rooms, _) = Arms(world, state, Policy(1), id => id != Lair);
        Assert.True(rooms.RoomNeeded(Post(world, PlayerMessageKind.RoomNeeded, RoomTypeId.Lair)));
        Assert.False(state.NextRoomToBuildSet);
    }

    [Fact]
    public void ARequestAlreadyWaitingLeavesTheMessageUnhandledButAPortalPlaceholderIsReplaced()
    {
        WorldState world = KeeperWorld.World(Db);
        var waiting = new KeeperAIState { NextRoomToBuild = (byte)RoomTypeId.Hatchery, NextRoomToBuildSet = true };
        (KeeperRoomRequests rooms, _) = Arms(world, waiting, Policy(1));
        Assert.False(rooms.RoomNeeded(Post(world, PlayerMessageKind.RoomNeeded, RoomTypeId.Lair)));
        Assert.Equal((byte)RoomTypeId.Hatchery, waiting.NextRoomToBuild);

        var placeholder = new KeeperAIState { NextRoomToBuild = Portal, NextRoomToBuildSet = true };
        (rooms, _) = Arms(world, placeholder, Policy(1));
        Assert.True(rooms.RoomNeeded(Post(world, PlayerMessageKind.RoomNeeded, RoomTypeId.Lair)));
        Assert.Equal((Lair, true), (placeholder.NextRoomToBuild, placeholder.NextRoomToBuildSet));
    }

    [Fact]
    public void TheRoomBeingBuiltIsNotAskedForAgainButTheMessageIsHandled()
    {
        WorldState world = KeeperWorld.World(Db);
        var state = new KeeperAIState { CurrentRoomToBuild = (byte)(Lair | KeeperRoomRequests.ExpandBit) };
        (KeeperRoomRequests rooms, _) = Arms(world, state, Policy(1));
        Assert.True(rooms.RoomNeeded(Post(world, PlayerMessageKind.RoomNeeded, RoomTypeId.Lair)));
        Assert.False(state.NextRoomToBuildSet);
    }

    // ---- 42 ---------------------------------------------------------------------------

    [Theory]
    [InlineData(0, RoomTypeId.Treasury, 0x81)]
    [InlineData(1, RoomTypeId.Treasury, 0x01)]
    [InlineData(3, RoomTypeId.Treasury, 0x01)]
    [InlineData(0, RoomTypeId.Lair, 0x82)]
    [InlineData(1, RoomTypeId.Lair, 0x02)]
    public void AFullRoomAsksToExpandUnderPolicyZeroAndForANewRoomOtherwise(int policy, RoomTypeId room, int request)
    {
        WorldState world = KeeperWorld.World(Db);
        var state = new KeeperAIState();
        (KeeperRoomRequests rooms, _) = Arms(world, state, Policy(policy));
        Assert.True(rooms.RoomFull(Post(world, PlayerMessageKind.RoomFull, room)));
        Assert.Equal(((byte)request, true), (state.NextRoomToBuild, state.NextRoomToBuildSet));
    }

    [Fact]
    public void PolicyTwoHandlesAFullRoomWithoutARequest()
    {
        WorldState world = KeeperWorld.World(Db);
        var state = new KeeperAIState();
        (KeeperRoomRequests rooms, _) = Arms(world, state, Policy(2));
        Assert.True(rooms.RoomFull(Post(world, PlayerMessageKind.RoomFull, RoomTypeId.Lair)));
        Assert.False(state.NextRoomToBuildSet);
    }

    [Fact]
    public void AFullRoomUntagsEveryGoldDigFirstWhicheverRoomItNames()
    {
        WorldState world = KeeperWorld.World(Db);
        KeeperWorld.Set(world, Db, new TilePos(15, 11), KeeperWorld.Gold);
        world.Tile(15, 11).SetTaggedBy(Keeper, true);
        world.Tile(15, 10).SetTaggedBy(Keeper, true); // plain rock: not a gold dig
        var state = new KeeperAIState();
        (KeeperRoomRequests rooms, List<PlayerCommand> commands) = Arms(world, state, Policy(1), _ => false);
        Assert.True(rooms.RoomFull(Post(world, PlayerMessageKind.RoomFull, RoomTypeId.Lair)));
        var untag = Assert.IsType<TagTilesCommand>(Assert.Single(commands));
        Assert.Equal((false, new TilePos(15, 11)), (untag.Tagged, Assert.Single(untag.Tiles)));

        // A second 42 in the same walk finds no gold dig left to untag.
        Assert.True(rooms.RoomFull(Post(world, PlayerMessageKind.RoomFull, RoomTypeId.Treasury)));
        Assert.Single(commands);
    }

    [Fact]
    public void ARefusedUntagWhichDK2CannotProduceWouldEndOnlyATreasuryRequest()
    {
        WorldState world = KeeperWorld.World(Db);
        var treasury = new KeeperAIState();
        (KeeperRoomRequests rooms, _) = Arms(world, treasury, Policy(0));
        Assert.True(rooms.RoomFull(Post(world, PlayerMessageKind.RoomFull, RoomTypeId.Treasury), untagRefused: true));
        Assert.False(treasury.NextRoomToBuildSet);

        var lair = new KeeperAIState();
        (rooms, _) = Arms(world, lair, Policy(0));
        Assert.True(rooms.RoomFull(Post(world, PlayerMessageKind.RoomFull, RoomTypeId.Lair), untagRefused: true));
        Assert.Equal((byte)(Lair | KeeperRoomRequests.ExpandBit), lair.NextRoomToBuild);

        // DK2 1.70's untag never reports a refusal, so a Treasury request goes on as well.
        var accepted = new KeeperAIState();
        (rooms, _) = Arms(world, accepted, Policy(0));
        Assert.True(rooms.RoomFull(Post(world, PlayerMessageKind.RoomFull, RoomTypeId.Treasury)));
        Assert.Equal((byte)0x81, accepted.NextRoomToBuild);
    }

    // ---- the walk -----------------------------------------------------------------------

    [Fact]
    public void TheWalkTakesCombatAndRoomMessagesInQueueOrderAndLeavesASecondRequestUnhandled()
    {
        WorldState world = WithTreasury();
        uint heart = world.Messages.Post(world, Keeper, PlayerMessageKind.HeartAttacked, 15, 15)!.Value.Id;
        uint lair = Post(world, PlayerMessageKind.RoomNeeded, RoomTypeId.Lair).Id;
        Post(world, PlayerMessageKind.RoomNeeded, RoomTypeId.Hatchery);
        var keeper = new KeeperAI(Keeper, Policy(1), new SimRandom(3));

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
        Assert.Equal([heart, lair], commands.OfType<AcknowledgePlayerMessageCommand>().Select(c => c.MessageId));
        Assert.True(keeper.State.FightGoingOn);
        // The attack's event spends the tick (0x004FE37D), so the request waits for a later planner pass.
        Assert.Equal((Lair, true), (keeper.State.NextRoomToBuild, keeper.State.NextRoomToBuildSet));
    }

    [Fact]
    public void AnUnhandledRequestIsTakenOnTheTickAfterThePlannerTakesTheFirst()
    {
        WorldState world = WithTreasury(goldPerTile: 3000);
        uint lair = Post(world, PlayerMessageKind.RoomNeeded, RoomTypeId.Lair).Id;
        uint hatchery = Post(world, PlayerMessageKind.RoomNeeded, RoomTypeId.Hatchery).Id;
        var keeper = new KeeperAI(Keeper, Policy(1), new SimRandom(3));

        IReadOnlyList<PlayerCommand> first = keeper.Think(Db, world);
        Assert.Equal([lair], first.OfType<AcknowledgePlayerMessageCommand>().Select(c => c.MessageId));
        // The planner took the Lair in the same tick and left the Portal placeholder behind it.
        Assert.Equal((Lair, Portal, true), (keeper.State.CurrentRoomToBuild, keeper.State.NextRoomToBuild, keeper.State.NextRoomToBuildSet));

        new GameSimulation(Db, world, 1, []).Tick(new InputFrame(first));
        IReadOnlyList<PlayerCommand> second = keeper.Think(Db, world);
        Assert.Equal([hatchery], second.OfType<AcknowledgePlayerMessageCommand>().Select(c => c.MessageId));
        Assert.Equal(((byte)RoomTypeId.Hatchery, true), (keeper.State.NextRoomToBuild, keeper.State.NextRoomToBuildSet));
    }

    // ---- the expansion rectangle ---------------------------------------------------------------

    private static KeeperRoomView TreasuryView(KeeperView view) =>
        view.OwnRooms.Single(r => r.Definition.TypeId == RoomTypeId.Treasury);

    [Fact]
    public void AnExpansionKeepsEverySideThatPassesAndRefusesOneWhoseGapLineIsOpenTwice()
    {
        // North, south and west are rock; east is the claimed patch, whose second line is open floor.
        WorldState world = WithTreasury();
        var view = new KeeperView(Db, world, Keeper);
        RoomSite grown = RoomExpansion.Find(view, TreasuryView(view))!.Value;
        Assert.Equal((new TilePos(9, 13), 4, 5), (grown.Origin, grown.Width, grown.Height));
    }

    [Fact]
    public void AnImpenetrableStripOrOneOpenGapTooManyRefusesThatSideAlone()
    {
        WorldState world = WithTreasury();
        for (int x = 9; x <= 13; x++) KeeperWorld.Set(world, Db, new TilePos(x, 13), KeeperWorld.Impenetrable);
        KeeperWorld.Set(world, Db, new TilePos(8, 14), KeeperWorld.ClaimedPath, Keeper); // one open gap: allowed
        var view = new KeeperView(Db, world, Keeper);
        RoomSite grown = RoomExpansion.Find(view, TreasuryView(view))!.Value;
        Assert.Equal((new TilePos(9, 14), 4, 4), (grown.Origin, grown.Width, grown.Height));

        KeeperWorld.Set(world, Db, new TilePos(8, 15), KeeperWorld.ClaimedPath, Keeper); // two: west refused
        view = new KeeperView(Db, world, Keeper);
        grown = RoomExpansion.Find(view, TreasuryView(view))!.Value;
        Assert.Equal((new TilePos(10, 14), 3, 4), (grown.Origin, grown.Width, grown.Height));
    }

    [Fact]
    public void ARoomWalledInOnEverySideCannotExpand()
    {
        WorldState world = WithTreasury();
        for (int y = 13; y <= 17; y++)
            for (int x = 9; x <= 13; x++)
                if (x is 9 or 13 || y is 13 or 17) KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.Impenetrable);
        var view = new KeeperView(Db, world, Keeper);
        Assert.Null(RoomExpansion.Find(view, TreasuryView(view)));
    }

    [Fact]
    public void AOneTileWideRoomAcceptsItsNorthAndSouthStripsUntestedAsTheBinaryScanIsEmpty()
    {
        WorldState world = KeeperWorld.World(Db);
        for (int y = 5; y <= 7; y++) KeeperWorld.Set(world, Db, new TilePos(15, y), KeeperWorld.TreasuryFloor, Keeper);
        KeeperWorld.Set(world, Db, new TilePos(15, 4), KeeperWorld.Impenetrable);
        var view = new KeeperView(Db, world, Keeper);
        RoomSite grown = RoomExpansion.Find(view, TreasuryView(view))!.Value;
        Assert.Equal((new TilePos(14, 4), 3, 5), (grown.Origin, grown.Width, grown.Height));
    }

    [Theory]
    [InlineData(11, 14, 11, 13, 1, 4)] // (11,14) first: a one-wide column, grown north and south
    [InlineData(10, 15, 9, 14, 3, 3)]  // (10,15) first: a one-high row, grown north, south and west
    public void TheFirstOfTwoEqualCornersInChainOrderDecidesTheExpansionAndSurvivesTheObservationPacket(
        int firstX, int firstY, int originX, int originY, int width, int height)
    {
        WorldState world = KeeperWorld.World(Db);
        TilePos first = new(firstX, firstY);
        foreach (TilePos tile in new[] { first, new TilePos(11, 15), first == new TilePos(11, 14) ? new TilePos(10, 15) : new TilePos(11, 14) })
        {
            KeeperWorld.Set(world, Db, tile, KeeperWorld.TreasuryFloor, Keeper);
            SlabChain.Changed(world, Db, world.Width * tile.Y + tile.X, KeeperWorld.Rock, 0);
        }
        var view = new KeeperView(Db, world, Keeper);
        KeeperView restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(view), Db);
        foreach (KeeperView seen in new[] { view, restored })
        {
            Assert.Equal(world.Width * first.Y + first.X, TreasuryView(seen).ChainOrder[0]);
            RoomSite grown = RoomExpansion.Find(seen, TreasuryView(seen))!.Value;
            Assert.Equal((new TilePos(originX, originY), width, height), (grown.Origin, grown.Width, grown.Height));
        }
    }

    // ---- the planner taking a request -------------------------------------------------------------

    /// <summary>One allowance a tick, so a tick's task machine runs task 0 alone.</summary>
    private static KeeperAI KeeperAsking(byte request, int policy = 0)
    {
        var keeper = new KeeperAI(Keeper, Policy(policy) with { TendancySpeed = 1 }, new SimRandom(5));
        keeper.State.NextRoomToBuild = request;
        keeper.State.NextRoomToBuildSet = true;
        return keeper;
    }

    [Fact]
    public void AnExpansionRequestDigsTheGrownRectangleNext()
    {
        WorldState world = WithTreasury(goldPerTile: 3000);
        KeeperAI keeper = KeeperAsking(0x81);
        keeper.Think(Db, world);
        Assert.Equal((0x81, Portal, true), (keeper.State.CurrentRoomToBuild, keeper.State.NextRoomToBuild, keeper.State.NextRoomToBuildSet));
        Assert.Equal((KeeperTask.DigRectangle, new TilePos(9, 13), 4, 5),
            (keeper.State.Task, keeper.State.RectangleOrigin, keeper.State.RectangleWidth, keeper.State.RectangleHeight));
    }

    [Fact]
    public void AnExpansionWithNoRoomOrNoSideBuildsANewRoomInstead()
    {
        WorldState none = KeeperWorld.World(Db);
        none.Tile(KeeperWorld.HeartAt).GoldHeld = 1000;
        KeeperAI keeper = KeeperAsking(0x81);
        keeper.Think(Db, none);
        Assert.Equal((Treasury, KeeperTask.FindSite), (keeper.State.CurrentRoomToBuild, keeper.State.Task));

        WorldState walled = WithTreasury(goldPerTile: 3000);
        for (int y = 13; y <= 17; y++)
            for (int x = 9; x <= 13; x++)
                if (x is 9 or 13 || y is 13 or 17) KeeperWorld.Set(walled, Db, new TilePos(x, y), KeeperWorld.Impenetrable);
        keeper = KeeperAsking(0x81);
        keeper.Think(Db, walled);
        Assert.Equal(Treasury, keeper.State.CurrentRoomToBuild);
    }

    [Fact]
    public void ARequestTheKeeperCannotAffordGoesForGoldAndIsLost()
    {
        WorldState world = WithTreasury(goldPerTile: 10);
        KeeperWorld.Set(world, Db, new TilePos(15, 9), KeeperWorld.Gold);
        KeeperAI keeper = KeeperAsking(0x81);
        keeper.Think(Db, world);
        Assert.Equal((Portal, true), (keeper.State.NextRoomToBuild, keeper.State.NextRoomToBuildSet));
        Assert.Contains(keeper.State.Task, new[] { KeeperTask.RouteToGold, KeeperTask.DigPath, KeeperTask.MineSeam });
    }

    // ---- end to end: a producer, the keeper and the simulation ----------------------------------------

    /// <summary>The keeper's Treasury and Heart full, so a sold Lair tile's refund has nowhere to go: the
    /// sale posts 42 for a Treasury (0x004C5DB0, 0x004BA420).</summary>
    private static (WorldState World, GameSimulation Sim) FullStoresAndALairToSell()
    {
        WorldState world = WithTreasury(goldPerTile: 3000);
        int heart = (KeeperWorld.HeartAt.Y * world.Width) + KeeperWorld.HeartAt.X;
        world.TileAtIndex(heart).GoldHeld = GoldStore.Capacity(world, Db, heart);
        KeeperWorld.Set(world, Db, new TilePos(17, 17), KeeperWorld.LairFloor, Keeper);
        var sim = new GameSimulation(Db, world, 1, [new TileTagSystem(), new RoomBuildSystem(), new RoomSellSystem()]);
        return (world, sim);
    }

    private static int TreasuryTiles(WorldState world) =>
        RoomIndex.Build(world, Db).Where(r => r.OwnerPlayerId == Keeper && r.Definition.TypeId == RoomTypeId.Treasury)
            .Sum(r => r.TileCount);

    private static void Play(WorldState world, GameSimulation sim, KeeperAI keeper, int ticks, InputFrame? first = null)
    {
        InputFrame frame = first ?? InputFrame.Empty;
        for (int tick = 0; tick < ticks; tick++)
        {
            sim.Tick(frame);
            KeeperWorld.DigWhatIsTagged(world, Db, Keeper);
            frame = new InputFrame(keeper.Think(Db, world));
        }
    }

    [Fact]
    public void ASaleWhoseRefundNoStoreTakesMakesAPolicyZeroKeeperExpandItsTreasuryAndTheNextRefundFits()
    {
        (WorldState world, GameSimulation sim) = FullStoresAndALairToSell();
        var keeper = new KeeperAI(Keeper, Policy(0), new SimRandom(9));
        Play(world, sim, keeper, 120, InputFrame.Of(new SellRoomCommand(Keeper, [new TilePos(17, 17)])));

        PlayerMessage full = Assert.Single(world.Messages.Entries);
        Assert.Equal((PlayerMessageKind.RoomFull, RoomTypeId.Treasury, true), (full.Kind, full.Room, full.Handled));
        Assert.Equal(20, TreasuryTiles(world)); // 3×3 grown to 4×5: the east side's gap line is open floor
        Assert.Single(RoomIndex.Build(world, Db), r => r.OwnerPlayerId == Keeper && r.Definition.TypeId == RoomTypeId.Treasury);

        // The need is met: another refund is stored whole.
        KeeperWorld.Set(world, Db, new TilePos(17, 13), KeeperWorld.LairFloor, Keeper);
        int before = GoldStore.Total(world, Keeper);
        sim.Tick(InputFrame.Of(new SellRoomCommand(Keeper, [new TilePos(17, 13)])));
        Assert.Equal(before + RoomRules.RefundPerTile(Db, Db.Room(RoomTypeId.Lair)), GoldStore.Total(world, Keeper));
    }

    [Fact]
    public void ThePolicyOneKeeperBuildsASecondTreasuryForTheSameSale()
    {
        (WorldState world, GameSimulation sim) = FullStoresAndALairToSell();
        var keeper = new KeeperAI(Keeper, Policy(1), new SimRandom(9));
        Play(world, sim, keeper, 200, InputFrame.Of(new SellRoomCommand(Keeper, [new TilePos(17, 17)])));

        List<RoomInstance> treasuries = RoomIndex.Build(world, Db)
            .Where(r => r.OwnerPlayerId == Keeper && r.Definition.TypeId == RoomTypeId.Treasury).ToList();
        Assert.Equal(2, treasuries.Count);
        Assert.Contains(treasuries, r => r.TileCount == 9 && r.MinX != 10);
    }

    [Fact]
    public void ACreatureWithNoLairMakesTheKeeperBuildOneItsRoomListWouldNeverChooseAndThenSleepsInIt()
    {
        WorldState world = WithTreasury(goldPerTile: 3000);
        CreatureState sleeper = world.SpawnCreature(Db, KeeperWorld.SleeperCreature, Keeper, new TilePos(14, 14));
        var sim = new GameSimulation(Db, world, 1,
            [new TileTagSystem(), new RoomBuildSystem(), new CreatureMovementSystem(), new CreatureSleepSystem()]);
        var keeper = new KeeperAI(Keeper, Policy(1), new SimRandom(9));
        Play(world, sim, keeper, 600);

        Assert.Contains(world.Messages.Entries, m => m.Kind == PlayerMessageKind.RoomNeeded && m.Room == RoomTypeId.Lair);
        Assert.Contains(RoomIndex.Build(world, Db), r => r.OwnerPlayerId == Keeper && r.Definition.TypeId == RoomTypeId.Lair);
        Assert.True(LairRules.HasBed(world, Db, sleeper));
    }
}
