// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>Development (campaign bot stage 3 plan, Task 2): the player keeps building, growing and
/// using what the level offers, and its digging stays clear of other players' ground.</summary>
public partial class CampaignPlayerTests
{
    /// <summary>A 3x3 Lair (x1..3), Treasury (x9..11, y1..3) and Hatchery (x9..11, y5..7) around the
    /// heart at (6,4), with free floor between.</summary>
    private static readonly string[] Developed =
    [
        "RRRRRRRRRRRRR",
        "RLLL.....TTTR",
        "RLLL.....TTTR",
        "RLLL.....TTTR",
        "R.....H.....R",
        "R........AAAR",
        "R........AAAR",
        "R........AAAR",
        "RRRRRRRRRRRRR",
    ];

    private static readonly PlayerRoomOffer[] CoreOffers =
    [
        new PlayerRoomOffer(RoomTypeId.Treasury, new GoldAmount(50), true),
        new PlayerRoomOffer(RoomTypeId.Lair, new GoldAmount(50), true),
        new PlayerRoomOffer(RoomTypeId.Hatchery, new GoldAmount(50), true),
    ];

    private static PlayerRoomSummary[] CoreOwned(int lair = 9, int treasury = 9, params PlayerRoomSummary[] more)
    {
        return [
        new PlayerRoomSummary(RoomTypeId.DungeonHeart, 1), new PlayerRoomSummary(RoomTypeId.Treasury, treasury), new PlayerRoomSummary(RoomTypeId.Lair, lair),
        new PlayerRoomSummary(RoomTypeId.Hatchery, 9), .. more,
    ];
    }

    private static PlayerCreatureSummary[] Goblins(int count)
    {
        return [.. Enumerable.Range(0, count).Select(i => new PlayerCreatureSummary(Goblin, new TilePos(5, 5), false))];
    }

    /// <summary>A player whose strategy works the Training Room.</summary>
    private static CampaignPlayer Trainer()
    {
        CampaignPlayer player = Player();
        player.Apply(player.Strategy with { UseRooms = [RoomTypeId.TrainingRoom] });
        return player;
    }

    private static PlayerVisibleThing Creature(int ordinal, int x, int y, CreatureTypeId type, PlayerId? owner = null)
    {
        return new PlayerVisibleThing(Handle(ordinal), PlayerThingKind.Creature, new TilePos(x, y), owner ?? Me, PlayerThingActions.PickUp, type);
    }

    [Fact]
    public void AnOutgrownLairGrowsBesideItselfAndIsNotRepeatedWhilePending()
    {
        CampaignPlayer player = Player();
        PlayerObservation crowded = Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), creatures: Goblins(10));

        PlayerDecision grow = player.Decide(crowded);

        Assert.Equal(new PlayerAction.SelectPanel(GuiButtonTarget.Room(RoomTypeId.Lair)), grow.Actions[0]);
        PlayerAction.BuildRoom build = Assert.IsType<PlayerAction.BuildRoom>(grow.Actions[1]);
        // (5,1) is nearer the heart, but (4,1) touches the Lair, so the Lair grows rather than splits.
        Assert.Equal((RoomTypeId.Lair, new TilePos(4, 1), new TilePos(6, 3)), (build.Room, build.From, build.To));
        Assert.DoesNotContain(player.Decide(Observe(2, map: Developed, rooms: CoreOffers, owned: CoreOwned(), creatures: Goblins(10))).Actions,
            a => a is PlayerAction.BuildRoom);
        // Ten creatures in eighteen Lair tiles fit: nothing more to grow.
        Assert.DoesNotContain(player.Decide(Observe(3, map: Developed, rooms: CoreOffers, owned: CoreOwned(lair: 18), creatures: Goblins(10))).Actions,
            a => a is PlayerAction.BuildRoom);
    }

    [Fact]
    public void ATreasuryGrowsBesideItselfOnceGoldNearsWhatTheHeartAndTreasuriesHold()
    {
        static CampaignPlayer Banker()
        {
            return Player(o => o with { GoldPerTreasuryTile = 3000, HeartGold = 16000 });
        }
        // Heart 16000 + nine tiles x 3000 = 43000; 80% is 34400.
        PlayerDecision full = Banker().Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), gold: 34400));
        PlayerAction.BuildRoom build = Assert.IsType<PlayerAction.BuildRoom>(full.Actions[1]);
        Assert.Equal((RoomTypeId.Treasury, new TilePos(6, 1)), (build.Room, build.From));

        Assert.DoesNotContain(Banker().Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), gold: 34399)).Actions,
            a => a is PlayerAction.BuildRoom);
        // Without the capacities the player cannot tell, so it does not grow a Treasury for gold.
        Assert.DoesNotContain(Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), gold: 40000)).Actions,
            a => a is PlayerAction.BuildRoom);
    }

    [Fact]
    public void OfferedRoomsOutsideTheListAreBuiltOnceEachInTheDefaultOrder()
    {
        PlayerRoomOffer[] offers =
        [
            .. CoreOffers, new PlayerRoomOffer(RoomTypeId.Workshop, new GoldAmount(50), true), new PlayerRoomOffer(RoomTypeId.TrainingRoom, new GoldAmount(50), true),
        ];
        CampaignPlayer player = Player();

        PlayerAction.BuildRoom first = Assert.IsType<PlayerAction.BuildRoom>(player.Decide(Observe(rooms: offers, owned: CoreOwned())).Actions[1]);
        Assert.Equal(RoomTypeId.TrainingRoom, first.Room);                     // Training Room before Workshop, whatever the panel's order
        PlayerAction.BuildRoom second = Assert.IsType<PlayerAction.BuildRoom>(player.Decide(Observe(2, rooms: offers,
            owned: CoreOwned(more: new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9)))).Actions[1]);
        Assert.Equal(RoomTypeId.Workshop, second.Room);
        Assert.DoesNotContain(player.Decide(Observe(3, rooms: offers, owned: CoreOwned(more:
            [new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9), new PlayerRoomSummary(RoomTypeId.Workshop, 9)]))).Actions,
            a => a is PlayerAction.BuildRoom);
        // A room left out of both lists is never built.
        CampaignPlayer narrow = Player();
        narrow.Apply(narrow.Strategy with { ExtraRooms = [RoomTypeId.Workshop] });
        Assert.Equal(RoomTypeId.Workshop,
            Assert.IsType<PlayerAction.BuildRoom>(narrow.Decide(Observe(rooms: offers, owned: CoreOwned())).Actions[1]).Room);
    }

    /// <summary>The Dungeon with a 3x3 Training Room at x2..4, y2..4.</summary>
    private static readonly string[] WithTraining =
    [
        "RRRRRRRRRRR",
        "R#########R",
        "R#GGG....#R",
        "R#GGG....#R",
        "R#GGG....#R",
        "R#...H...#R",
        "R#.......#R",
        "R#.......#R",
        "R#.......#R",
        "R####$####R",
        "RRRRRRRRRRR",
    ];

    [Fact]
    public void TheDefaultStrategyCarriesNoOneToTrain()
    {
        PlayerRoomSummary[] owned = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9));
        Assert.Empty(new PlayerStrategy().UseRooms);
        Assert.DoesNotContain(Player().Decide(Observe(map: WithTraining, owned: owned, things: [Creature(0, 7, 7, Goblin)])).Actions,
            a => a is PlayerAction.PickUp);
    }

    [Fact]
    public void AnIdleFighterIsCarriedToTheTrainingRoomNeverAWorkerNorOneAlreadyThere()
    {
        PlayerRoomSummary[] owned = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9));
        PlayerVisibleThing[] things = [Creature(0, 6, 6, Imp), Creature(1, 3, 3, Goblin), Creature(2, 7, 7, Goblin)];
        CampaignPlayer player = Trainer();

        PlayerDecision lift = player.Decide(Observe(map: WithTraining, owned: owned, things: things, tick: 100));
        Assert.Contains(new PlayerAction.PickUp(Handle(2)), lift.Actions);
        Assert.DoesNotContain(lift.Actions, a => a is PlayerAction.PickUp up && up.Thing != Handle(2));

        PlayerDecision drop = player.Decide(Observe(2, map: WithTraining, owned: owned, hand: 1, tick: 104));
        Assert.Contains(new PlayerAction.DropAt(new TilePos(4, 4)), drop.Actions);   // the room's tile nearest the heart

        // Another idle fighter waits out the interval.
        PlayerVisibleThing[] again = [Creature(0, 7, 7, Goblin, null)];
        Assert.DoesNotContain(player.Decide(Observe(3, map: WithTraining, owned: owned, things: again, tick: 200)).Actions,
            a => a is PlayerAction.PickUp);
        Assert.Contains(new PlayerAction.PickUp(Handle(0, 4)),
            player.Decide(Observe(4, map: WithTraining, owned: owned, things: [new PlayerVisibleThing(Handle(0, 4), PlayerThingKind.Creature,
                new TilePos(7, 7), Me, PlayerThingActions.PickUp, Goblin)], tick: 300)).Actions);
    }

    [Fact]
    public void NoFighterIsCarriedWhenTheRoomIsFullGoldIsShortOrAnEnemyIsInSight()
    {
        PlayerRoomSummary[] owned = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9));
        PlayerVisibleThing idle = Creature(0, 7, 7, Goblin);
        PlayerVisibleThing[] full = [idle, Creature(1, 2, 2, Goblin), Creature(2, 3, 3, Goblin), Creature(3, 4, 4, Goblin)];

        Assert.DoesNotContain(Trainer().Decide(Observe(map: WithTraining, owned: owned, things: full)).Actions, a => a is PlayerAction.PickUp);
        Assert.DoesNotContain(Trainer().Decide(Observe(map: WithTraining, owned: owned, things: [idle], gold: 999)).Actions,
            a => a is PlayerAction.PickUp);
        // With fighting off the hand does not answer the enemy, and work still waits while one is in sight.
        CampaignPlayer pacifist = Trainer();
        pacifist.Apply(pacifist.Strategy with { Fight = FightMode.Off });
        Assert.DoesNotContain(pacifist.Decide(Observe(map: WithTraining, owned: owned, things: [idle, Creature(1, 8, 2, Goblin, Heroes)])).Actions,
            a => a is PlayerAction.PickUp);
    }

    [Fact]
    public void AnEnemyAppearingWhileACreatureIsCarriedToWorkGetsItInstead()
    {
        PlayerRoomSummary[] owned = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9));
        CampaignPlayer player = Trainer();
        Assert.Contains(new PlayerAction.PickUp(Handle(0)), player.Decide(Observe(map: WithTraining, owned: owned, things: [Creature(0, 7, 7, Goblin)])).Actions);

        PlayerDecision drop = player.Decide(Observe(2, map: WithTraining, owned: owned, hand: 1, things: [Creature(0, 8, 2, Goblin, Heroes)]));
        Assert.Contains(new PlayerAction.DropAt(new TilePos(7, 3)), drop.Actions);    // in front of the enemy, toward the Heart, not the room
    }

    [Fact]
    public void ARefusedWorkDropIsNotTriedOnThatTileAgain()
    {
        PlayerRoomSummary[] owned = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9));
        CampaignPlayer player = Trainer();
        player.Decide(Observe(1, map: WithTraining, owned: owned, things: [Creature(0, 7, 7, Goblin)], tick: 100));
        Assert.Contains(new PlayerAction.DropAt(new TilePos(4, 4)), player.Decide(Observe(2, map: WithTraining, owned: owned, hand: 1, tick: 104)).Actions);
        // Still held: the drop failed, and the creature goes down near home instead.
        Assert.DoesNotContain(new PlayerAction.DropAt(new TilePos(4, 4)), player.Decide(Observe(3, map: WithTraining, owned: owned, hand: 1, tick: 108)).Actions);

        player.Decide(Observe(4, map: WithTraining, owned: owned, things: [new PlayerVisibleThing(Handle(0, 4), PlayerThingKind.Creature, new TilePos(7, 7), Me,
            PlayerThingActions.PickUp, Goblin)], tick: 400));
        // (4,3) and (3,4) are the next nearest the heart; Y then X.
        Assert.Contains(new PlayerAction.DropAt(new TilePos(4, 3)), player.Decide(Observe(5, map: WithTraining, owned: owned, hand: 1, tick: 404)).Actions);
    }

    [Fact]
    public void ACaptiveOnTheOwnPrisonIsNoEnemy()
    {
        string[] map = [.. WithTraining];
        map[7] = "R#PPP....#R";
        PlayerRoomSummary[] owned = CoreOwned(more: [new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9), new PlayerRoomSummary(RoomTypeId.Prison, 3)]);
        PlayerObservation o = Observe(map: map, owned: owned, things: [Creature(0, 7, 6, Goblin), Creature(1, 3, 7, Goblin, Heroes)]);

        Assert.Null(PlayerActionRoutines.NearestEnemy(o, PlayerActionRoutines.Home(o)));
        Assert.Contains(new PlayerAction.PickUp(Handle(0)), Trainer().Decide(o).Actions);           // carried to train, not to fight
    }

    [Fact]
    public void AnUnaffordableMissingListedRoomKeepsTheGoldFromGrowthAndExtras()
    {
        PlayerRoomOffer[] offers = [new PlayerRoomOffer(RoomTypeId.Lair, new GoldAmount(500), false), new PlayerRoomOffer(RoomTypeId.TrainingRoom, new GoldAmount(50), true)];
        PlayerRoomSummary[] noLair = [new PlayerRoomSummary(RoomTypeId.DungeonHeart, 1), new PlayerRoomSummary(RoomTypeId.Treasury, 9), new PlayerRoomSummary(RoomTypeId.Hatchery, 9)];
        Assert.DoesNotContain(Player().Decide(Observe(rooms: offers, owned: noLair, gold: 3000)).Actions, a => a is PlayerAction.BuildRoom);
        // A listed room the panel does not offer yet holds nothing back.
        Assert.Equal(RoomTypeId.TrainingRoom, Assert.IsType<PlayerAction.BuildRoom>(
            Player().Decide(Observe(rooms: [offers[1]], owned: noLair, gold: 3000)).Actions[1]).Room);
    }

    [Fact]
    public void AnOutgrownHatcheryGrowsBesideItselfAndAPartRiseReleasesThePendingBuild()
    {
        CampaignPlayer player = Player();
        PlayerDecision grow = player.Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(lair: 20), creatures: Goblins(20)));
        PlayerAction.BuildRoom build = Assert.IsType<PlayerAction.BuildRoom>(grow.Actions[1]);
        Assert.Equal((RoomTypeId.Hatchery, new TilePos(6, 5)), (build.Room, build.From));     // 9 tiles feed 18 of 20

        // Two of the nine tiles stand (gold ran short): the build is done, and 11 tiles still feed only 22 of 24.
        PlayerRoomSummary[] part = [new PlayerRoomSummary(RoomTypeId.DungeonHeart, 1), new PlayerRoomSummary(RoomTypeId.Treasury, 9), new PlayerRoomSummary(RoomTypeId.Lair, 24), new PlayerRoomSummary(RoomTypeId.Hatchery, 11)];
        Assert.Contains(player.Decide(Observe(2, map: Developed, rooms: CoreOffers, owned: part, creatures: Goblins(24))).Actions,
            a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Hatchery });
    }

    [Fact]
    public void GoldCapacitiesAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Player(o => o with { GoldPerTreasuryTile = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Player(o => o with { HeartGold = -1 }));
    }

    [Fact]
    public void AWorkshopIsWorkedWhenTheStrategyNamesIt()
    {
        string[] map = [.. WithTraining.Select(r => r.Replace('G', 'W'))];
        PlayerRoomSummary[] owned = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.Workshop, 9));
        CampaignPlayer player = Player();
        Assert.DoesNotContain(player.Decide(Observe(map: map, owned: owned, things: [Creature(0, 7, 7, Goblin)])).Actions,
            a => a is PlayerAction.PickUp);

        player.Apply(player.Strategy with { UseRooms = [RoomTypeId.Workshop] });
        Assert.Contains(new PlayerAction.PickUp(Handle(0, 2)), player.Decide(Observe(2, map: map, owned: owned,
            things: [new PlayerVisibleThing(Handle(0, 2), PlayerThingKind.Creature, new TilePos(7, 7), Me, PlayerThingActions.PickUp, Goblin)], tick: 400)).Actions);
        Assert.Contains(new PlayerAction.DropAt(new TilePos(4, 4)), player.Decide(Observe(3, map: map, owned: owned, hand: 1, tick: 404)).Actions);
    }

    /// <summary>Own floor x1..2 behind a wall at x3, enemy ground beyond; one safe wall at (1,4).</summary>
    private static readonly string[] Corridor =
    [
        "RRRRRRR",
        "R..#eeR",
        "R.H#eeR",
        "R..#eeR",
        "R#RRRRR",
        "RRRRRRR",
    ];

    private static TilePos[] Marks(PlayerDecision decision)
    {
        return [.. decision.Actions.OfType<PlayerAction.TagArea>().Select(t => t.From).OrderBy(t => t.Y).ThenBy(t => t.X)];
    }

    private static CampaignPlayer Breaching(BreachMode mode)
    {
        CampaignPlayer player = Player();
        player.Apply(player.Strategy with { Breach = mode });
        return player;
    }

    [Fact]
    public void AClosedCorridorStaysClosedWhileSafeGroundRemainsAndAlwaysOpens()
    {
        PlayerObservation o = Observe(map: Corridor, rooms: []);
        Assert.Equal([new TilePos(1, 4)], Marks(Player().Decide(o)));             // the default develops first
        Assert.Equal([new TilePos(1, 4)], Marks(Breaching(BreachMode.Never).Decide(o)));
        // Once (1,4) is marked but not yet dug, the default still waits rather than opening the corridor.
        string[] marked = [.. Corridor];
        marked[4] = "RtRRRRR";
        Assert.Empty(Marks(Player().Decide(Observe(map: marked, rooms: []))));

        CampaignPlayer bold = Breaching(BreachMode.Always);
        Assert.Equal([new TilePos(3, 1), new TilePos(3, 3), new TilePos(1, 4)],
#pragma warning disable IDE0305 // Preserve the original collection construction or overload.
            bold.Decide(o).Actions.OfType<PlayerAction.TagArea>().Select(t => t.From).OrderBy(t => t.Y).ThenBy(t => t.X).ToArray());
#pragma warning restore IDE0305
    }

    [Fact]
    public void WithNoSafeGroundLeftTheDefaultOpensTheCorridorAndNeverLeavesItForGood()
    {
        string[] sealedIn = ["RRRRRRR", "R..#eeR", "R.H#eeR", "R..#eeR", "RRRRRRR"];
        Assert.Equal([new TilePos(3, 1), new TilePos(3, 3)], Marks(Player().Decide(Observe(map: sealedIn, rooms: []))));

        // Never: the walled-in frontier is no pending work, so long idleness is no limitation.
        CampaignPlayer player = Breaching(BreachMode.Never);
        Assert.Empty(player.Decide(Observe(map: sealedIn, rooms: [], tick: 0)).Actions);
        PlayerDecision later = player.Decide(Observe(2, map: sealedIn, rooms: [], tick: 5000));
        Assert.Empty(later.Actions);
        Assert.StartsWith("wait", later.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GroundNearAnEnemySightingIsNotMarkedEvenAfterItLeaves()
    {
        CampaignPlayer player = Player(o => o with { MaxActionsPerDecision = 8 });
        player.Apply(player.Strategy with { Fight = FightMode.Off });
        var seen = new TilePos(9, 5);
        IEnumerable<TilePos> Marks(PlayerDecision d)
        {
            return d.Actions.OfType<PlayerAction.TagArea>().Select(t => t.From);
        }

        TilePos[] first = [.. Marks(player.Decide(Observe(things: [Creature(0, seen.X, seen.Y, Goblin, Heroes)])))];
        TilePos[] after = [.. Marks(player.Decide(Observe(2)))];

        Assert.NotEmpty(first);
        Assert.NotEmpty(after);
        Assert.All(first.Concat(after), t => Assert.True(PlayerActionRoutines.DistanceSquared(t, seen) > 9, $"({t.X},{t.Y}) is near the sighting"));
        // Without the sighting (9,5) is among the first marks.
        Assert.Contains(seen, Marks(Player(o => o with { MaxActionsPerDecision = 8 }).Decide(Observe())));
    }
}
