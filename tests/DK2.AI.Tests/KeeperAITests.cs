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

public class KeeperAITests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();

    [Theory]
    [InlineData(5, 15, 1, 3)]   // heart left of centre: defend west, open east
    [InlineData(25, 15, 3, 1)]  // right: defend east, open west
    [InlineData(15, 25, 2, 0)]  // below: defend south, open north
    [InlineData(15, 5, 0, 2)]   // above: defend north, open south
    public void DirectionsPointFromTheCentreTowardTheHeart(int x, int y, int defend, int open)
    {
        (int d, int o) = KeeperDirections.AtCreation(new TilePos(x, y), 30, 30);
        Assert.Equal(defend, d);
        Assert.Equal(open, o);
    }

    [Fact]
    public void SiteFinderLaysACentredSquareNorthOfTheHeartInRock()
    {
        WorldState world = KeeperWorld.World(Db);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);

        RoomSite? site = SiteFinder.Find(view, KeeperWorld.HeartAt, startDirection: 0, roomSize: 9,
            tilesBetweenRooms: 0, opennessPercent: 20, BuildArea.WholeMap(30, 30));

        Assert.NotNull(site);
        RoomSite s = site!.Value;
        Assert.Equal(3, s.Width);
        Assert.Equal(3, s.Height);
        // The walk leaves the heart at (15,14), the first tile that is not a room;
        // with no gap the square starts there, centred on x=15, and extends north.
        // Two of its rows lie on the keeper's own floor, which the original allows.
        Assert.Equal(new TilePos(14, 12), s.Origin);
        Assert.All(s.Tiles(), t => Assert.True(view.IsDiggable(t) || view.IsMine(t)));
    }

    [Fact]
    public void OpennessGatesTheSiteOnlyWhenRoomsKeepAGap()
    {
        WorldState world = KeeperWorld.World(Db);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        // Only the northern strip is allowed, so a single direction decides.
        var north = new BuildArea(0, 0, 29, 14);

        // Gap of one: the square's rear perimeter row is the claimed patch (5 of
        // 16 tiles open, plus two flank corners): 43 % open.
        Assert.Null(SiteFinder.Find(view, KeeperWorld.HeartAt, 0, 9, 1, 20, north));
        Assert.NotNull(SiteFinder.Find(view, KeeperWorld.HeartAt, 0, 9, 1, 50, north));

        // No gap: the perimeter is not consulted at all, as in the original.
        Assert.NotNull(SiteFinder.Find(view, KeeperWorld.HeartAt, 0, 9, 0, 0, north));
    }

    [Fact]
    public void LineWalkerReturnsDiggableTilesInAStraightLine()
    {
        WorldState world = KeeperWorld.World(Db);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        var state = new KeeperAIState
        {
            DigRoomAtCurrent = new TilePos(15, 13),
            DigRoomAt = new TilePos(15, 5),
        };

        var dug = new List<TilePos>();
        for (int i = 0; i < 20; i++)
        {
            WalkStep step = LineWalker.Step(view, state);
            if (step.Outcome == WalkOutcome.Arrived) break;
            Assert.Equal(WalkOutcome.Dig, step.Outcome);
            dug.Add(step.Tile);
            KeeperWorld.Set(world, Db, step.Tile, KeeperWorld.ClaimedPath, KeeperWorld.Keeper);
            view = new KeeperView(Db, world, KeeperWorld.Keeper);
        }

        Assert.Equal(Enumerable.Range(5, 8).Reverse().Select(y => new TilePos(15, y)), dug);
    }

    [Fact]
    public void LineWalkerGoesRoundAnImpenetrableWall()
    {
        WorldState world = KeeperWorld.World(Db);
        for (int x = 11; x <= 19; x++)
            KeeperWorld.Set(world, Db, new TilePos(x, 9), KeeperWorld.Impenetrable);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        var state = new KeeperAIState
        {
            DigRoomAtCurrent = new TilePos(15, 13),
            DigRoomAt = new TilePos(15, 4),
        };

        bool arrived = false;
        for (int i = 0; i < 200 && !arrived; i++)
        {
            WalkStep step = LineWalker.Step(view, state);
            switch (step.Outcome)
            {
                case WalkOutcome.Arrived:
                    arrived = true;
                    break;
                case WalkOutcome.Dig:
                    Assert.NotEqual(KeeperWorld.Impenetrable, world.Tile(step.Tile).TerrainId);
                    KeeperWorld.Set(world, Db, step.Tile, KeeperWorld.ClaimedPath, KeeperWorld.Keeper);
                    view = new KeeperView(Db, world, KeeperWorld.Keeper);
                    break;
                case WalkOutcome.Moved:
                    break;
                default:
                    Assert.Fail($"walker gave up: {step}");
                    break;
            }
        }

        Assert.True(arrived, "the walker never reached the far side of the wall");
        Assert.True(view.IsPassable(new TilePos(15, 4)) || view.IsTaggedByMe(new TilePos(15, 4)) || view.IsDiggable(new TilePos(15, 4)));
        // Something was dug on the far side of the wall.
        Assert.Contains(Enumerable.Range(1, 8), y => world.Tile(new TilePos(15, y)).TerrainId == KeeperWorld.ClaimedPath
                                                   || world.Tile(new TilePos(10, y)).TerrainId == KeeperWorld.ClaimedPath
                                                   || world.Tile(new TilePos(20, y)).TerrainId == KeeperWorld.ClaimedPath);
    }

    [Fact]
    public void GoldScannerFindsSeamsAndKeepsTheNearestFour()
    {
        WorldState world = KeeperWorld.World(Db);
        var seams = new[] { new TilePos(4, 4), new TilePos(25, 4), new TilePos(4, 25), new TilePos(25, 25), new TilePos(15, 3) };
        foreach (TilePos s in seams)
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                    KeeperWorld.Set(world, Db, new TilePos(s.X + dx, s.Y + dy), KeeperWorld.Gold);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);

        List<GoldDeposit> deposits = GoldScanner.Scan(view);

        Assert.Equal(4, deposits.Count);
        Assert.All(deposits, d => Assert.Equal(4, d.Tiles));
        Assert.Equal(new TilePos(15, 3), deposits[0].Centre); // nearest to the heart first
    }

    [Fact]
    public void KeeperDigsAndBuildsItsFirstRoomsInBuildOrder()
    {
        WorldState world = KeeperWorld.World(Db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        var sim = new GameSimulation(Db, world, 1, [new TileTagSystem(), new RoomBuildSystem()]);
        var keeper = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(speed: 4), new SimRandom(7));

        var kinds = new List<string>();
        for (int tick = 0; tick < 300; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            kinds.AddRange(commands.Select(c => c.GetType().Name));
            sim.Tick(new InputFrame(commands));
            KeeperWorld.DigWhatIsTagged(world, Db, KeeperWorld.Keeper);
        }

        List<RoomInstance> rooms = RoomIndex.Build(world, Db).Where(r => r.OwnerPlayerId == KeeperWorld.Keeper).ToList();
        Assert.Contains(rooms, r => r.Definition.TypeId == RoomTypeId.Treasury && r.TileCount == 9);
        Assert.Contains(rooms, r => r.Definition.TypeId == RoomTypeId.Lair && r.TileCount == 9);
        Assert.Contains(rooms, r => r.Definition.TypeId == RoomTypeId.Hatchery && r.TileCount == 9);
        Assert.Contains(nameof(TagTilesCommand), kinds);
        Assert.Contains(nameof(BuildRoomCommand), kinds);
        Assert.DoesNotContain(nameof(CreateImpCommand), kinds); // MaximumImps is 0 here
        Assert.True(keeper.State.AllRoomsBuilt);
    }

    [Fact]
    public void KeeperStopsToDigGoldWhenItCannotAffordTheRoom()
    {
        WorldState world = KeeperWorld.World(Db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 100; // below 9 × 50 for a Treasury
        for (int y = 4; y <= 6; y++)
            for (int x = 14; x <= 16; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.Gold);
        var sim = new GameSimulation(Db, world, 1, [new TileTagSystem()]);
        var keeper = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(speed: 4), new SimRandom(7));

        bool taggedGold = false;
        for (int tick = 0; tick < 200 && !taggedGold; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            sim.Tick(new InputFrame(commands));
            for (int y = 4; y <= 6; y++)
                for (int x = 14; x <= 16; x++)
                    if (world.Tile(new TilePos(x, y)).IsTaggedBy(KeeperWorld.Keeper)) taggedGold = true;
            KeeperWorld.DigWhatIsTagged(world, Db, KeeperWorld.Keeper);
        }

        Assert.True(taggedGold, "the keeper never tagged the gold seam");
    }

    [Fact]
    public void KeeperCreatesImpsUpToItsRatio()
    {
        WorldState world = KeeperWorld.World(Db);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        Assert.Equal(25, view.OwnedTiles);

        // 25 owned tiles / 10 per imp = 2 imps wanted; none exist, so one Create Imp per tick.
        KeeperAIParameters p = KeeperWorld.Parameters(maxImps: 4) with { TilesPerImpRatio = 10 };
        var keeper = new KeeperAI(KeeperWorld.Keeper, p, new SimRandom(1));
        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        Assert.Contains(commands, c => c is CreateImpCommand);
    }

    [Fact]
    public void SameSeedsGiveTheSameCommandStream()
    {
        static ulong Run(ulong seed)
        {
            WorldState world = KeeperWorld.World(Db);
            world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
            var sim = new GameSimulation(Db, world, 1, [new TileTagSystem(), new RoomBuildSystem()]);
            var keeper = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(speed: 2), new SimRandom(seed));
            Hash64 hash = Hash64.Start();
            for (int tick = 0; tick < 120; tick++)
            {
                var frame = new InputFrame(keeper.Think(Db, world));
                frame.HashInto(ref hash);
                sim.Tick(frame);
                KeeperWorld.DigWhatIsTagged(world, Db, KeeperWorld.Keeper);
            }
            return hash.Value;
        }

        Assert.Equal(Run(42), Run(42));
    }
}
