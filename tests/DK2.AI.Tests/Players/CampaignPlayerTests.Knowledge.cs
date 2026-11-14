// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>Keeper knowledge (campaign bot stage 3 plan, Task 3a): rooms sized by what they do, from
/// facts shaped like the generated ones (a Library's attraction 2/5/13 at 3/4/5, a Temple's 0 below 5x5).</summary>
public partial class CampaignPlayerTests
{
    private static readonly KeeperKnowledge Facts = new(
    [
        new RoomFacts(RoomTypeId.Library, 600, [new(3, 1, 2, 0), new(4, 4, 5, 0), new(5, 9, 13, 0)], capsCreatures: true),
        new RoomFacts(RoomTypeId.Temple, 3000, [new(3, 1, 0, 0), new(4, 4, 0, 0), new(5, 9, 2, 0)], capsCreatures: true),
        new RoomFacts(RoomTypeId.Hatchery, 300, [new(3, 1, 9, 0), new(4, 4, 16, 0)], capsCreatures: false),
        new RoomFacts(RoomTypeId.Treasury, 200, [new(3, 1, 9, 27000), new(4, 4, 16, 48000)], capsCreatures: false),
    ]);

    /// <summary>Open own floor x1..10, y1..10, the heart at (1,1).</summary>
    private static readonly string[] Open =
    [
        "RRRRRRRRRRRR",
        "RH.........R",
        "R..........R",
        "R..........R",
        "R..........R",
        "R..........R",
        "R..........R",
        "R..........R",
        "R..........R",
        "R..........R",
        "R..........R",
        "RRRRRRRRRRRR",
    ];

    private static PlayerRoomOffer Offer(RoomTypeId room) => new(room, new GoldAmount(50), true);

    private static PlayerAction.BuildRoom? Built(PlayerDecision decision, RoomTypeId room) =>
        decision.Actions.OfType<PlayerAction.BuildRoom>().FirstOrDefault(b => b.Room == room);

    [Fact]
    public void FactsNameTheSmallestUsefulSideAndTheSideAnAttractionNeeds()
    {
        Assert.Equal(3, Facts.Room(RoomTypeId.Library)!.SmallestUsefulSide);
        Assert.Equal(5, Facts.Room(RoomTypeId.Temple)!.SmallestUsefulSide);
        Assert.Equal(3, Facts.Room(RoomTypeId.Treasury)!.SmallestUsefulSide);         // stores gold from 3x3
        Assert.Null(Facts.Room(RoomTypeId.Hatchery)!.SmallestUsefulSide);             // works by area alone
        Assert.Equal(4, Facts.Room(RoomTypeId.Library)!.SideFor(5));
        Assert.Equal(5, Facts.Room(RoomTypeId.Library)!.SideFor(6));
        Assert.Null(Facts.Room(RoomTypeId.Library)!.SideFor(14));
        Assert.Throws<ArgumentException>(() => new KeeperKnowledge([Facts.Room(RoomTypeId.Library)!, Facts.Room(RoomTypeId.Library)!]));
    }

    [Fact]
    public void ALibraryIsBuiltAtTheSideItsAttractionTargetNeeds()
    {
        PlayerObservation o = Observe(map: Open, rooms: [Offer(RoomTypeId.Library)], owned: CoreOwned(), gold: 20000);
        Dictionary<RoomTypeId, int> five = new() { [RoomTypeId.Library] = 5 };

        PlayerAction.BuildRoom plain = Built(Player(p => p with { Knowledge = Facts }).Decide(o), RoomTypeId.Library)!;
        PlayerAction.BuildRoom sized = Built(Player(p => p with { Knowledge = Facts, AttractionTargets = five }).Decide(o), RoomTypeId.Library)!;

        Assert.Equal(2, plain.To.X - plain.From.X);                                     // 3x3 without a target
        Assert.Equal((3, 3), (sized.To.X - sized.From.X, sized.To.Y - sized.From.Y));   // 4x4 for attraction 5
        // Gold for 3x3 (9 x 50) but not 4x4 (16 x 50) while a 4x4 square is free: it waits rather than settle.
        Assert.Null(Built(Player(p => p with { Knowledge = Facts, AttractionTargets = five }).Decide(
            Observe(map: Open, rooms: [Offer(RoomTypeId.Library)], owned: CoreOwned(), gold: 799)), RoomTypeId.Library));
    }

    [Fact]
    public void WithNoSquareForItsTargetALibraryDigsOneOutWaitsForItThenSettlesButATempleNeverGoesBelow5x5()
    {
        // The Dungeon's 7x7 floor has its heart in the middle, so no free square is larger than 3x3.
        Dictionary<RoomTypeId, int> five = new() { [RoomTypeId.Library] = 5 };
        CampaignPlayer player = Player(p => p with { Knowledge = Facts, AttractionTargets = five });
        PlayerRoomOffer[] offers = [Offer(RoomTypeId.Library)];

        PlayerDecision site = player.Decide(Observe(1, rooms: offers, owned: CoreOwned(), tick: 4));
        Assert.Null(Built(site, RoomTypeId.Library));
        // Four rock tiles (the top wall) and twelve floor: the least digging, (3,1) and (1,3) tie at distance 4, Y first.
        Assert.Contains(new PlayerAction.TagArea(new TilePos(3, 1), new TilePos(6, 4)), site.Actions);

        PlayerDecision waiting = player.Decide(Observe(2, rooms: offers, owned: CoreOwned(), tick: 8));
        Assert.Null(Built(waiting, RoomTypeId.Library));
        Assert.DoesNotContain(new PlayerAction.TagArea(new TilePos(3, 1), new TilePos(6, 4)), waiting.Actions);

        // Not dug out within the site's time: given up, and the largest square that fits goes up.
        PlayerAction.BuildRoom library = Built(player.Decide(Observe(3, rooms: offers, owned: CoreOwned(), tick: 2410)), RoomTypeId.Library)!;
        Assert.Equal(2, library.To.X - library.From.X);

        PlayerDecision temple = Player(p => p with { Knowledge = Facts }).Decide(Observe(rooms: [Offer(RoomTypeId.Temple)], owned: CoreOwned(), gold: 100000));
        Assert.Null(Built(temple, RoomTypeId.Temple));
        // On open floor the Temple goes up at 5x5.
        PlayerAction.BuildRoom open = Built(Player(p => p with { Knowledge = Facts }).Decide(
            Observe(map: Open, rooms: [Offer(RoomTypeId.Temple)], owned: CoreOwned(), gold: 100000)), RoomTypeId.Temple)!;
        Assert.Equal(4, open.To.X - open.From.X);
        // Without knowledge the Temple is a plain 3x3, as before Task 3a.
        Assert.NotNull(Built(Player().Decide(Observe(rooms: [Offer(RoomTypeId.Temple)], owned: CoreOwned(), gold: 100000)), RoomTypeId.Temple));
    }

    /// <summary>The Dungeon with the 4x4 site at (3,1)-(6,4) dug out.</summary>
    private static readonly string[] SiteDug =
    [
        "RRRRRRRRRRR",
        "R##....###R",
        "R#.......#R",
        "R#.......#R",
        "R#.......#R",
        "R#...H...#R",
        "R#.......#R",
        "R#.......#R",
        "R#.......#R",
        "R####$####R",
        "RRRRRRRRRRR",
    ];

    [Fact]
    public void ASiteDugOutInTimeButWaitingForGoldIsClosedWithoutSettling()
    {
        Dictionary<RoomTypeId, int> five = new() { [RoomTypeId.Library] = 5 };
        CampaignPlayer player = Player(p => p with { Knowledge = Facts, AttractionTargets = five });
        PlayerRoomOffer[] offers = [Offer(RoomTypeId.Library)];
        var site = new PlayerAction.TagArea(new TilePos(3, 1), new TilePos(6, 4));
        Assert.Contains(site, player.Decide(Observe(1, rooms: offers, owned: CoreOwned(), tick: 4)).Actions);

        // Dug out, but 4x4 (800) is beyond the gold while 3x3 (450) is not: it waits for the free square.
        Assert.Null(Built(player.Decide(Observe(2, map: SiteDug, rooms: offers, owned: CoreOwned(), gold: 500, tick: 2410)), RoomTypeId.Library));

        // The square is lost before the gold comes. The room did not settle for 3x3: it digs its site again.
        PlayerDecision again = player.Decide(Observe(3, rooms: offers, owned: CoreOwned(), gold: 5000, tick: 2420));
        Assert.Null(Built(again, RoomTypeId.Library));
        Assert.Contains(site, again.Actions);
    }

    /// <summary>The Dungeon with five Library tiles of the 3x3 at (4,2)-(6,4).</summary>
    private static readonly string[] PartLibrary =
    [
        "RRRRRRRRRRR",
        "R#########R",
        "R#..BBB..#R",
        "R#..BB...#R",
        "R#.......#R",
        "R#...H...#R",
        "R#.......#R",
        "R#.......#R",
        "R#.......#R",
        "R####$####R",
        "RRRRRRRRRRR",
    ];

    [Fact]
    public void ALibraryLeftShortIsFinishedOnceItStopsGrowingButAnAreaRoomIsNot()
    {
        PlayerRoomOffer[] offers = [Offer(RoomTypeId.Library)];
        CampaignPlayer player = Player(p => p with { Knowledge = Facts });
        PlayerAction.BuildRoom first = Built(player.Decide(Observe(1, rooms: offers, owned: CoreOwned(), tick: 4)), RoomTypeId.Library)!;
        Assert.Equal((new TilePos(4, 2), new TilePos(6, 4)), (first.From, first.To));

        PlayerRoomSummary[] part = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.Library, 5));
        // Still rising: pending, nothing sent again.
        Assert.Null(Built(player.Decide(Observe(2, map: PartLibrary, rooms: offers, owned: part, tick: 100)), RoomTypeId.Library));
        Assert.Null(Built(player.Decide(Observe(3, map: PartLibrary, rooms: offers, owned: part, tick: 300)), RoomTypeId.Library));
        // No rise for the pending window: the same square again, to finish it.
        PlayerAction.BuildRoom again = Built(player.Decide(Observe(4, map: PartLibrary, rooms: offers, owned: part, tick: 520)), RoomTypeId.Library)!;
        Assert.Equal((first.From, first.To), (again.From, again.To));

        // Without knowledge the Library is an area room: five tiles are a Library, nothing is finished.
        CampaignPlayer plain = Player();
        plain.Decide(Observe(1, rooms: offers, owned: CoreOwned(), tick: 4));
        plain.Decide(Observe(2, map: PartLibrary, rooms: offers, owned: part, tick: 100));
        Assert.Null(Built(plain.Decide(Observe(3, map: PartLibrary, rooms: offers, owned: part, tick: 520)), RoomTypeId.Library));
    }

    [Fact]
    public void ACoreRoomGetsASiteOnlyWhenTheStrategyAsksForEveryRoom()
    {
        // The Corridor's own floor has no free 3x3, and rock below and beside it.
        string[] cramped = ["RRRRRRR", "R..#..R", "R.H#..R", "R..#..R", "R####RR", "R####RR", "RRRRRRR"];
        PlayerObservation o = Observe(map: cramped, rooms: [Offer(RoomTypeId.Treasury)], owned: [new(RoomTypeId.DungeonHeart, 1)]);
        Assert.DoesNotContain(Player(p => p with { Knowledge = Facts }).Decide(o).Actions, a => a is PlayerAction.TagArea t && t.From != t.To);
        CampaignPlayer all = Player(p => p with { Knowledge = Facts });
        all.Apply(all.Strategy with { Sites = RoomSites.All });
        Assert.Contains(all.Decide(o).Actions, a => a is PlayerAction.TagArea t && t.From != t.To);
    }

    [Fact]
    public void AListedRoomWaitingForGoldForItsTargetKeepsTheGoldFromExtras()
    {
        Dictionary<RoomTypeId, int> five = new() { [RoomTypeId.Library] = 5 };
        CampaignPlayer player = Player(p => p with { Knowledge = Facts, AttractionTargets = five });
        player.Apply(player.Strategy with { Rooms = [RoomTypeId.Treasury, RoomTypeId.Lair, RoomTypeId.Hatchery, RoomTypeId.Library] });
        // 4x4 costs 800 and a free 4x4 stands; a 3x3 Training Room (450) would fit the 799.
        PlayerDecision d = player.Decide(Observe(map: Open, rooms: [Offer(RoomTypeId.Library), Offer(RoomTypeId.TrainingRoom)], owned: CoreOwned(), gold: 799));
        Assert.DoesNotContain(d.Actions, a => a is PlayerAction.BuildRoom);
    }

    [Fact]
    public void AnotherRoomsOpenSiteMakesARoomWaitNotSettle()
    {
        var facts = new KeeperKnowledge(
        [
            Facts.Room(RoomTypeId.Library)!,
            new RoomFacts(RoomTypeId.Workshop, 600, [new(3, 1, 2, 0), new(4, 2, 3, 0), new(5, 5, 9, 0)], capsCreatures: true),
        ]);
        Dictionary<RoomTypeId, int> targets = new() { [RoomTypeId.Library] = 5, [RoomTypeId.Workshop] = 3 };
        CampaignPlayer player = Player(p => p with { Knowledge = facts, AttractionTargets = targets });
        PlayerRoomOffer[] offers = [Offer(RoomTypeId.Library), Offer(RoomTypeId.Workshop)];

        Assert.Contains(player.Decide(Observe(1, rooms: offers, owned: CoreOwned(), tick: 4)).Actions, a => a is PlayerAction.TagArea t && t.From != t.To);
        // The Library's site is open: the Workshop (4x4 for attraction 3, none free) waits instead of a 3x3.
        Assert.DoesNotContain(player.Decide(Observe(2, rooms: offers, owned: CoreOwned(), tick: 8)).Actions, a => a is PlayerAction.BuildRoom);
    }

    [Fact]
    public void WithoutSitesARoomSettlesAtOnce()
    {
        Dictionary<RoomTypeId, int> five = new() { [RoomTypeId.Library] = 5 };
        CampaignPlayer player = Player(p => p with { Knowledge = Facts, AttractionTargets = five });
        player.Apply(player.Strategy with { Sites = RoomSites.None });
        PlayerAction.BuildRoom library = Built(player.Decide(Observe(rooms: [Offer(RoomTypeId.Library)], owned: CoreOwned())), RoomTypeId.Library)!;
        Assert.Equal(2, library.To.X - library.From.X);
    }

    [Fact]
    public void ATileTargetGrowsAListedRoomBesideItself()
    {
        Dictionary<RoomTypeId, int> hatchery = new() { [RoomTypeId.Hatchery] = 25 };
        PlayerDecision grow = Player(p => p with { TileTargets = hatchery }).Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned()));
        Assert.Equal((RoomTypeId.Hatchery, new TilePos(6, 5)), (Built(grow, RoomTypeId.Hatchery)!.Room, Built(grow, RoomTypeId.Hatchery)!.From));
        Assert.Null(Built(Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned())), RoomTypeId.Hatchery));
    }

    [Fact]
    public void TargetsAreValidatedAndKnowledgeIsRequired()
    {
        Assert.Contains(new PlayerStrategy { AttractionTargets = new Dictionary<RoomTypeId, int> { [RoomTypeId.Library] = 0 } }.Validate(),
            p => p.Contains("AttractionTargets", StringComparison.Ordinal));
        Assert.Contains(new PlayerStrategy { TileTargets = new Dictionary<RoomTypeId, int> { [RoomTypeId.Hatchery] = -1 } }.Validate(),
            p => p.Contains("TileTargets", StringComparison.Ordinal));
        Assert.Throws<ArgumentNullException>(() => Player(p => p with { Knowledge = null! }));
    }
}
