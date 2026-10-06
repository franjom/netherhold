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
    public void SiteFinderConsumesOnlyTheRequestedDirection()
    {
        WorldState world = KeeperWorld.World(Db);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        var eastOnly = new BuildArea(16, 0, 29, 29);

        Assert.Null(SiteFinder.Find(view, KeeperWorld.HeartAt, 0, 9, 0, 20, eastOnly));
        Assert.NotNull(SiteFinder.Find(view, KeeperWorld.HeartAt, 3, 9, 0, 20, eastOnly));
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
    public void GoldScannerFindsCardinalSeamsAndSumsTheirGold()
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
        Assert.All(deposits, d => Assert.Equal(4 * 1024, d.GoldAmount));
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
        var roomCommands = new List<BuildRoomCommand>();
        for (int tick = 0; tick < 300; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            kinds.AddRange(commands.Select(c => c.GetType().Name));
            roomCommands.AddRange(commands.OfType<BuildRoomCommand>());
            sim.Tick(new InputFrame(commands));
            KeeperWorld.DigWhatIsTagged(world, Db, KeeperWorld.Keeper);
        }

        List<RoomInstance> rooms = RoomIndex.Build(world, Db).Where(r => r.OwnerPlayerId == KeeperWorld.Keeper).ToList();
        Assert.Contains(rooms, r => r.Definition.TypeId == RoomTypeId.Treasury && r.TileCount == 9);
        Assert.Contains(rooms, r => r.Definition.TypeId == RoomTypeId.Lair && r.TileCount == 9);
        Assert.Contains(rooms, r => r.Definition.TypeId == RoomTypeId.Hatchery && r.TileCount == 9);
        Assert.Contains(nameof(TagTilesCommand), kinds);
        Assert.Contains(nameof(BuildRoomCommand), kinds);
        Assert.All(roomCommands, command => Assert.Single(command.Tiles));
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
    public void CampaignUnlocksAreReadFromCurrentPlayerAvailability()
    {
        WorldState world = KeeperWorld.World(Db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        foreach (byte room in new byte[] { 1, 2, 4 }) world.SetAvailability(KeeperWorld.Keeper, AvailabilityKind.Room, room, 4);
        world.SetAvailability(KeeperWorld.Keeper, AvailabilityKind.Spell, 4, 1);

        KeeperAIParameters parameters = KeeperWorld.Parameters(speed: 2, maxImps: 4)
            with { TilesPerImpRatio = 10 };
        var keeper = new KeeperAI(KeeperWorld.Keeper, parameters, new SimRandom(7));
        IReadOnlyList<PlayerCommand> closed = keeper.Think(Db, world);
        Assert.DoesNotContain(closed, c => c is TagTilesCommand or BuildRoomCommand or CreateImpCommand);

        world.SetAvailability(KeeperWorld.Keeper, AvailabilityKind.Room, 1, 3);
        world.SetAvailability(KeeperWorld.Keeper, AvailabilityKind.Spell, 4, 3);
        var opened = new List<PlayerCommand>();
        for (int tick = 0; tick < 8; tick++) opened.AddRange(keeper.Think(Db, world));
        Assert.Contains(opened, c => c is TagTilesCommand);
        Assert.Contains(opened, c => c is CreateImpCommand);
    }

    [Fact]
    public void PlannerRetriesTheRoomThatItsPreviousPlanTried()
    {
        WorldState world = KeeperWorld.World(Db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        var keeper = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(), new SimRandom(7));
        keeper.State.CurrentRoomToBuild = (byte)RoomTypeId.Treasury;
        keeper.Think(Db, world);
        Assert.Equal((byte)RoomTypeId.Treasury, keeper.State.CurrentRoomToBuild);
        Assert.Equal(KeeperTask.FindSite, keeper.State.Task);
    }

    [Theory]
    [InlineData(24, 15, true)]
    [InlineData(25, 15, false)]
    [InlineData(23, 23, false)]
    public void OrdinaryNeutralRoomsUseStrictSquaredHeartDistance(int x, int y, bool accepted)
    {
        WorldState world = KeeperWorld.World(Db);
        KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.TreasuryFloor, PlayerIds.Neutral);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        Assert.Equal(accepted, RoomPlanner.ClaimableNeutralRoom(view, KeeperWorld.Parameters(),
            (byte)RoomTypeId.Treasury, BuildArea.WholeMap(30, 30)) is not null);
    }

    [Theory]
    [InlineData(-3, 4, true)]
    [InlineData(3, 4, true)]
    [InlineData(4, -3, true)]
    [InlineData(4, 3, true)]
    [InlineData(4, 4, false)]
    [InlineData(-4, -3, false)]
    public void BoundaryIncludesExtendedEdgeEndsButNotTheOuterCorner(int dx, int dy, bool accepted)
    {
        WorldState world = KeeperWorld.World(Db);
        var centre = new TilePos(23, 23);
        var owned = new TilePos(centre.X + dx, centre.Y + dy);
        KeeperWorld.Set(world, Db, owned, KeeperWorld.ClaimedPath, KeeperWorld.Keeper);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        Assert.Equal(accepted, RoomPlanner.HasOwnLandOnBoundary(view, centre, 2));
        if (accepted) Assert.Equal(owned, RoomPlanner.OwnLandOnBoundary(view, centre, 2));
    }

    [Theory]
    [InlineData(KeeperWorld.PortalFloor)]
    [InlineData(KeeperWorld.HeroPortalFloor)]
    public void CompletedBuildOrderStillPlansToClaimANeutralPortal(byte floor)
    {
        WorldState world = KeeperWorld.World(Db);
        var portal = new TilePos(25, 15); // Portal branch includes equality at range ten.
        KeeperWorld.Set(world, Db, portal, floor, PlayerIds.Neutral);
        var keeper = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(), new SimRandom(7), _ => false);
        keeper.Think(Db, world);
        Assert.True(keeper.State.AllRoomsBuilt);
        Assert.Equal(KeeperTask.DigPath, keeper.State.Task);
        // The target is the portal's stored position (+0x27), its first slab plus one (0x004E4590):
        // a 3x3 portal's centre, and here, for a one-tile test portal, the tile beyond it (ADR-0394).
        Assert.Equal(new TilePos(portal.X + 1, portal.Y + 1), keeper.State.DigRoomAt);
    }

    /// <summary>Task 7 (<c>0x004FDC50</c>) tags the rock its cursor stands on and stays there until
    /// the tile is dug; only then does the walker step on. Ours used to walk past its tag.</summary>
    [Fact]
    public void TheTunnelWaitsOnItsTaggedRockUntilItIsDug()
    {
        WorldState world = KeeperWorld.World(Db);
        KeeperWorld.Set(world, Db, new TilePos(25, 15), KeeperWorld.PortalFloor, PlayerIds.Neutral);
        var keeper = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(), new SimRandom(7), _ => false);

        // The tag is never applied here, so every pass asks for the same tile again.
        HashSet<TilePos> Tagged(int passes)
        {
            var tiles = new HashSet<TilePos>();
            for (int i = 0; i < passes; i++)
                foreach (TagTilesCommand tag in keeper.Think(Db, world).OfType<TagTilesCommand>()) tiles.UnionWith(tag.Tiles);
            return tiles;
        }
        TilePos first = Assert.Single(Tagged(8));
        Assert.Equal((KeeperTask.DigPath, first), (keeper.State.Task, keeper.State.DigRoomAtCurrent));

        KeeperWorld.Set(world, Db, first, KeeperWorld.DirtPath, PlayerIds.Neutral);
        TilePos second = Assert.Single(Tagged(8));
        Assert.NotEqual(first, second);
    }

    /// <summary>The site search starts at a room's stored position (+0x27, <c>0x004FC690</c>), not its
    /// bounding-box middle: for a keeper with only its heart, the heart room's first slab plus three.
    /// On Level 11A, Morgana's Lair site and the path to it sat one row north of DK2's until this
    /// (ADR-0394).</summary>
    [Fact]
    public void TheSiteSearchStartsAtTheRoomsStoredPositionNotItsMiddle()
    {
        WorldState world = KeeperWorld.World(Db);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        KeeperRoomView heart = Assert.Single(view.OwnRooms);
        TilePos first = view.FirstSlabOf(heart);
        TilePos stored = view.PositionOf(heart);
        Assert.Equal(new TilePos(first.X + 3, first.Y + 3), stored);
        Assert.NotEqual(KeeperView.Centre(heart), stored);

        TilePos? origin = RoomPlanner.Origin(view, KeeperWorld.Parameters(), (byte)RoomTypeId.Lair, new SimRandom(7));

        Assert.Equal(stored, origin);
    }

    /// <summary><c>0x004FCE70</c> tries the preferred direction, then +1, -1 and +2, and only then
    /// reseeds; ours used to step +1 each time.</summary>
    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 1, 3)]
    [InlineData(0, 3, 2)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 0, 2)]
    [InlineData(3, 2, 1)]
    public void AFailedSiteSearchTriesPlusOneThenMinusOneThenPlusTwo(int preferred, int current, int next) =>
        Assert.Equal(next, RoomPlanner.NextSearchDirection(current, preferred));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(3, 1)]
    public void AfterPlusTwoTheSiteSearchReseeds(int preferred, int current) =>
        Assert.Null(RoomPlanner.NextSearchDirection(current, preferred));

    /// <summary>The preferred direction is the defend direction for a room id below 6 and the open
    /// one from 6 on (<c>0x004FCE70</c>, the +0x98 byte against 6).</summary>
    [Theory]
    [InlineData((byte)RoomTypeId.Lair, 1)]
    [InlineData((byte)RoomTypeId.DungeonHeart, 1)]
    [InlineData((byte)RoomTypeId.Library, 3)]
    [InlineData((byte)RoomTypeId.Prison, 3)]
    public void ThePreferredDirectionSplitsAtRoomSix(byte room, int expected) =>
        Assert.Equal(expected, RoomPlanner.PreferredDirection(room, new KeeperAIState { DefendDirection = 1, OpenDirection = 3 }));

    [Fact]
    public void PortalSearchUsesOnePoolAndDoesNotRetryAnOutOfAreaCandidate()
    {
        WorldState world = KeeperWorld.World(Db);
        KeeperWorld.Set(world, Db, new TilePos(24, 15), KeeperWorld.PortalFloor, PlayerIds.Neutral);
        KeeperWorld.Set(world, Db, new TilePos(22, 15), KeeperWorld.HeroPortalFloor, PlayerIds.Neutral);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        KeeperRoomView? chosen = RoomPlanner.ClaimableNeutralRoom(view, KeeperWorld.Parameters(),
            (byte)RoomTypeId.Portal, BuildArea.WholeMap(30, 30));
        Assert.Equal(RoomTypeId.HeroPortal, chosen!.Definition.TypeId);
        // The area tests the stored position, (23,16) for the one-tile hero portal at (22,15).
        Assert.Null(RoomPlanner.ClaimableNeutralRoom(view, KeeperWorld.Parameters(),
            (byte)RoomTypeId.Portal, new BuildArea(24, 0, 29, 29)));
    }

    [Fact]
    public void NeutralRoomProximityTestsOwnedLandOnTheSquareBoundary()
    {
        WorldState world = KeeperWorld.World(Db);
        var target = new TilePos(22, 15);
        KeeperWorld.Set(world, Db, target, KeeperWorld.ClaimedPath, KeeperWorld.Keeper);
        var view = new KeeperView(Db, world, KeeperWorld.Keeper);
        Assert.False(RoomPlanner.HasOwnLandOnBoundary(view, target, claimedRange: 2));

        KeeperWorld.Set(world, Db, new TilePos(19, 15), KeeperWorld.ClaimedPath, KeeperWorld.Keeper);
        view = new KeeperView(Db, world, KeeperWorld.Keeper);
        Assert.True(RoomPlanner.HasOwnLandOnBoundary(view, target, claimedRange: 2));
    }

    [Fact]
    public void ConqueredAndLostKeepersDoNotIssueCommands()
    {
        foreach (byte status in new[] { PlayerStatus.Conquered, PlayerStatus.Lost })
        {
            WorldState world = KeeperWorld.World(Db);
            world.Player(KeeperWorld.Keeper).Status = status;
            var keeper = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(maxImps: 4), new SimRandom(7));
            Assert.Empty(keeper.Think(Db, world));
        }
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
