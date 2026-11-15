// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;
using Xunit;

namespace DK2.AI.Tests.Players;

/// <summary>Traps on the routes attackers take to the Heart (campaign bot defence plan, Task D4).</summary>
public partial class CampaignPlayerTests
{
    /// <summary>The Heart at (2,2) and a corridor of own floor x 4..14 on row 2 to hero ground at (15,2).</summary>
    private static readonly string[] TrapRun =
    [
        "RRRRRRRRRRRRRRRR",
        "R...############",
        "R.H............e",
        "R...############",
        "RRRRRRRRRRRRRRRR",
    ];

    /// <summary>A short corridor: x 4..5 on row 2, then hero ground at (6,2).</summary>
    private static readonly string[] ShortTrapRun =
    [
        "RRRRRRR",
        "R...###",
        "R.H...e",
        "R...###",
        "RRRRRRR",
    ];

    private static PlayerWorkshopOffer TrapOffer(byte id, int cost, bool affordable = true, bool needsWorkshop = false, string? name = null) =>
        new(PlayerWorkshopKind.Trap, id, name ?? "trap " + id, new GoldAmount(cost), affordable, needsWorkshop);

    private static readonly PlayerWorkshopOffer Boulder = TrapOffer(2, 1000, name: "Boulder Trap"), Gas = TrapOffer(1, 2000, name: "Gas Trap");

    private static List<TilePos> TrapSites(string[] map, params TilePos[] entries) =>
        PlayerTrapRoutines.Sites(Observe(1, map: map, rooms: []), entries, 3, new HashSet<TilePos>());

    private static PlayerAction.PlaceTrap? Placed(CampaignPlayer player, PlayerObservation observation) =>
        player.Decide(observation).Actions.OfType<PlayerAction.PlaceTrap>().SingleOrDefault();

    [Fact]
    public void TrapSitesAreCorridorTilesOnTheRouteOutermostFirstThreeApart()
    {
        Assert.Equal([new TilePos(14, 2), new TilePos(11, 2), new TilePos(8, 2), new TilePos(5, 2)], TrapSites(TrapRun, new TilePos(15, 2)));
        // (3,2) is on the route but open above and below: no corridor. Hero ground is no site.
    }

    [Fact]
    public void OpenFloorOnTheRouteIsNoSite()
    {
        // x 3..10 is a hall (open above and below); only the corridor x 11..14 holds sites.
        string[] hall =
        [
            "RRRRRRRRRRRRRRRR",
            "R..........#####",
            "R.H............e",
            "R..........#####",
            "RRRRRRRRRRRRRRRR",
        ];
        Assert.Equal([new TilePos(14, 2), new TilePos(11, 2)], TrapSites(hall, new TilePos(15, 2)));
    }

    [Fact]
    public void ATrapGreyedForWantOfAWorkshopIsNeverChosen() =>
        Assert.Null(PlayerTrapRoutines.Choose(Observe(1, map: ShortTrapRun, rooms: [], workshop: [TrapOffer(3, 500, needsWorkshop: true)]),
            new TilePos(5, 2), 1000));

    [Fact]
    public void ATrapAlreadyThereKeepsNewSitesThreeTilesAway()
    {
        string[] map = [.. TrapRun];
        map[2] = "R.H..........k.e";
        Assert.Equal([new TilePos(10, 2), new TilePos(7, 2), new TilePos(4, 2)], TrapSites(map, new TilePos(15, 2)));
    }

    [Fact]
    public void NoEntryOrNoRouteGivesNoSite()
    {
        Assert.Empty(TrapSites(TrapRun));
        string[] walled = [.. TrapRun];
        walled[2] = "R.H.......#....e";   // the corridor is cut: hero ground has no way to the Heart
        Assert.Empty(TrapSites(walled, new TilePos(15, 2)));
    }

    [Fact]
    public void ABoulderIsPreferredWhereTheCorridorRunsLongAndOnlyAFallbackElsewhere()
    {
        // (14,2): (13,2)..(10,2) run straight on, so the Boulder is taken over the dearer trap.
        Assert.Equal(Boulder, PlayerTrapRoutines.Choose(Observe(1, map: TrapRun, rooms: [], workshop: [Boulder, Gas]), new TilePos(14, 2), 1000));
        // (5,2) on the short corridor: the dearer trap; with only the Boulder, the Boulder all the same (owner: "let it decide").
        Assert.Equal(Gas, PlayerTrapRoutines.Choose(Observe(1, map: ShortTrapRun, rooms: [], workshop: [Boulder, Gas]), new TilePos(5, 2), 1000));
        Assert.Equal(Boulder, PlayerTrapRoutines.Choose(Observe(1, map: ShortTrapRun, rooms: [], workshop: [Boulder]), new TilePos(5, 2), 1000));
    }

    /// <summary>A hall x 1..10 and a corridor x 11..14 to hero ground at (15,2).</summary>
    private static readonly string[] HallRun =
    [
        "RRRRRRRRRRRRRRRR",
        "R..........#####",
        "R.H............e",
        "R..........#####",
        "RRRRRRRRRRRRRRRR",
    ];

    [Fact]
    public void StrikeTrapsGoOnChokePointsAndFearInNarrowCorridors()
    {
        Assert.Equal(TrapSite.LongCorridor, PlayerTrapRoutines.SiteKind(Map(TrapRun), new TilePos(14, 2)));
        Assert.Equal(TrapSite.ChokePoint, PlayerTrapRoutines.SiteKind(Map(HallRun), new TilePos(11, 2)));
        Assert.Equal(TrapSite.NarrowCorridor, PlayerTrapRoutines.SiteKind(Map(HallRun), new TilePos(13, 2)));
        PlayerWorkshopOffer fear = TrapOffer(8, 800, name: "Fear Trap"), spike = TrapOffer(10, 900, name: "Spike Trap");
        PlayerWorkshopOffer[] offers = [Gas, fear, spike, Boulder];
        Assert.Equal(spike, PlayerTrapRoutines.Choose(Observe(1, map: HallRun, rooms: [], workshop: offers), new TilePos(11, 2), 1000));
        Assert.Equal(fear, PlayerTrapRoutines.Choose(Observe(1, map: HallRun, rooms: [], workshop: offers), new TilePos(13, 2), 1000));
        // With neither, the dearest other (Gas over the Boulder on a short run).
        Assert.Equal(Gas, PlayerTrapRoutines.Choose(Observe(1, map: HallRun, rooms: [], workshop: [Gas, Boulder]), new TilePos(13, 2), 1000));
        Assert.Equal(TrapRole.Strike, PlayerTrapRoutines.RoleOf(TrapOffer(3, 1, name: "Sentry Trap")));
        Assert.Equal(TrapRole.Strike, PlayerTrapRoutines.RoleOf(TrapOffer(7, 1, name: "Freeze Trap")));
        Assert.Equal(TrapRole.Strike, PlayerTrapRoutines.RoleOf(TrapOffer(5, 1, name: "Fireburst Trap")));
    }

    [Fact]
    public void AnUnclaimedPortalIsNoEntryButARivalsIs()
    {
        var neutral = new PlayerTile(PlayerTileFlags.Known, new PlayerId(PlayerIds.Neutral), RoomTypeId.Portal);
        Assert.False(PlayerTrapRoutines.IsEntryRoom(neutral, Me));
        Assert.False(PlayerTrapRoutines.IsEntryRoom(neutral with { Owner = Me }, Me));
        Assert.True(PlayerTrapRoutines.IsEntryRoom(neutral with { Owner = new PlayerId(4) }, Me));
        Assert.True(PlayerTrapRoutines.IsEntryRoom(new PlayerTile(PlayerTileFlags.Known, Heroes, RoomTypeId.HeroGate2By2), Me));
    }

    [Fact]
    public void TheDearestTrapThatLeavesTheGoldReserveIsChosen()
    {
        PlayerWorkshopOffer cheap = TrapOffer(3, 500), dear = TrapOffer(4, 900), unaffordable = TrapOffer(5, 4000, affordable: false);
        Assert.Equal(dear, PlayerTrapRoutines.Choose(Observe(1, map: ShortTrapRun, rooms: [], gold: 5000, workshop: [cheap, dear, unaffordable]), new TilePos(5, 2), 1000));
        // Exactly the reserve left is enough; 1600 gold keeping 1000: only the 500 trap fits.
        Assert.Equal(cheap, PlayerTrapRoutines.Choose(Observe(1, map: ShortTrapRun, rooms: [], gold: 1500, workshop: [cheap]), new TilePos(5, 2), 1000));
        Assert.Equal(cheap, PlayerTrapRoutines.Choose(Observe(1, map: ShortTrapRun, rooms: [], gold: 1600, workshop: [cheap, dear]), new TilePos(5, 2), 1000));
        Assert.Null(PlayerTrapRoutines.Choose(Observe(1, map: ShortTrapRun, rooms: [], gold: 1400, workshop: [cheap, dear]), new TilePos(5, 2), 1000));
    }

    [Fact]
    public void TheBotPlacesATrapOnTheOutermostSite()
    {
        Assert.Equal(new PlayerAction.PlaceTrap(Boulder.Id, new TilePos(14, 2)),
            Placed(Player(), Observe(1, map: TrapRun, rooms: [], workshop: [Boulder, Gas])));
    }

    [Fact]
    public void NoTrapWithoutAWorkshopOrGoldOrRoomUnderTheCap()
    {
        Assert.Null(Placed(Player(), Observe(1, map: TrapRun, rooms: [], workshop: [TrapOffer(3, 500, needsWorkshop: true)])));
        Assert.Null(Placed(Player(), Observe(1, map: TrapRun, rooms: [], gold: 1400, workshop: [TrapOffer(3, 500)])));
        Assert.NotNull(Placed(Player(), Observe(1, map: TrapRun, rooms: [], gold: 1600, workshop: [TrapOffer(3, 500)])));
        string[] trapped = [.. TrapRun];
        trapped[2] = "R.H..........k.e";
        Assert.Null(Placed(Player().Also(p => p.Apply(p.Strategy with { MaxTraps = 1 })), Observe(1, map: trapped, rooms: [], workshop: [Gas])));
        Assert.NotNull(Placed(Player().Also(p => p.Apply(p.Strategy with { MaxTraps = 2 })), Observe(1, map: trapped, rooms: [], workshop: [Gas])));
        Assert.Null(Placed(Player().Also(p => p.Apply(p.Strategy with { MaxTraps = 0 })), Observe(1, map: TrapRun, rooms: [], workshop: [Gas])));
    }

    [Fact]
    public void AnEnemySeenOnceStillMarksARoute()
    {
        string[] closed = [.. TrapRun];
        closed[2] = "R.H.............";   // no enemy ground in view
        CampaignPlayer player = Player();
        Assert.Null(Placed(player, Observe(1, map: closed, rooms: [], workshop: [Gas])));
        // A raider seen at the far end (own floor on the map's border: no site); placements are paced, so the next look comes after the gap.
        var raider = new PlayerVisibleThing(Handle(1, 2), PlayerThingKind.Creature, new TilePos(15, 2), Heroes, PlayerThingActions.None, Goblin);
        player.Decide(Observe(2, map: closed, rooms: [], tick: 100, things: [raider]));
        Assert.Equal(new TilePos(14, 2), Placed(player, Observe(3, map: closed, rooms: [], tick: 200, workshop: [Gas]))?.Tile);
    }

    [Fact]
    public void TrapsArePacedAndARefusedSiteIsNotChosenAgain()
    {
        CampaignPlayer player = Player();
        PlayerDecision first = player.Decide(Observe(1, map: TrapRun, rooms: [], tick: 100, workshop: [Gas]));
        int index = first.Actions.ToList().FindIndex(a => a is PlayerAction.PlaceTrap);
        Assert.Equal(new TilePos(14, 2), ((PlayerAction.PlaceTrap)first.Actions[index]).Tile);
        Assert.Null(Placed(player, Observe(2, map: TrapRun, rooms: [], tick: 104, workshop: [Gas])));
        PlayerReceipt refused = new(new PlayerActionId(index + 1), PlayerReceiptStatus.Refused, PlayerRefusalReason.NotOffered);
        Assert.Equal(new TilePos(13, 2), Placed(player, Observe(3, map: TrapRun, rooms: [], tick: 200, receipts: [refused], workshop: [Gas]))?.Tile);
    }

    [Fact]
    public void ADoorOrADigMarkedWallKeepsATrapAway()
    {
        var door = new PlayerVisibleThing(Handle(1), PlayerThingKind.Door, new TilePos(13, 2), Me, PlayerThingActions.None);
        Assert.Equal([new TilePos(10, 2), new TilePos(7, 2), new TilePos(4, 2)],
            PlayerTrapRoutines.Sites(Observe(1, map: TrapRun, rooms: [], things: [door]), [new TilePos(15, 2)], 3, new HashSet<TilePos>()));
        // A wall marked for digging will open: (14,2) beside it is no corridor.
        string[] marked = [.. TrapRun];
        marked[1] = "R...##########x#";
        Assert.Equal([new TilePos(13, 2), new TilePos(10, 2), new TilePos(7, 2), new TilePos(4, 2)], TrapSites(marked, new TilePos(15, 2)));
    }

    [Fact]
    public void NoTrapUnderAnEnemyOrBesideIt()
    {
        var raider = new PlayerVisibleThing(Handle(1), PlayerThingKind.Creature, new TilePos(13, 2), Heroes, PlayerThingActions.None, Goblin);
        Assert.Equal(new TilePos(11, 2), Placed(Player(), Observe(1, map: TrapRun, rooms: [], things: [raider], workshop: [Gas]))?.Tile);
    }

    [Fact]
    public void APlacementThatNeverShowsIsNotTriedAgain()
    {
        CampaignPlayer player = Player();
        Assert.Equal(new TilePos(14, 2), Placed(player, Observe(1, map: TrapRun, rooms: [], tick: 100, workshop: [Gas]))?.Tile);
        // Accepted, but no trap shows on (14,2) a gap later: the world refused it.
        Assert.Equal(new TilePos(13, 2), Placed(player, Observe(2, map: TrapRun, rooms: [], tick: 200, workshop: [Gas]))?.Tile);
        // Shown this time: the next goes three tiles on.
        string[] shown = [.. TrapRun];
        shown[2] = "R.H..........k.e";
        Assert.Equal(new TilePos(10, 2), Placed(player, Observe(3, map: shown, rooms: [], tick: 300, workshop: [Gas]))?.Tile);
    }
}
