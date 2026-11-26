// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The gold budget, rooms kept ahead of the army, idle gold put to training, and the trap assault (economy and
/// assault plan E2, E3, A2; owner directions R11, R12, R14).</summary>
public partial class CampaignPlayerTests
{
    [Fact]
    public void ShortOfPaydayNothingGrowsAndNoExtraRoomIsBought()
    {
        // Ten goblins outgrow a 9-tile Lair, but 1100 gold is short of a 1000 wage bill plus 20%.
        PlayerDecision short_ = Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), creatures: Goblins(10),
            gold: 1100, wageBill: 1000));
        Assert.DoesNotContain(short_.Actions, a => a is PlayerAction.BuildRoom);
        Assert.Equal("keeping 1200 gold for payday (gold 1100)", short_.Thoughts.Single(t => t.Routine == "room").Note);
        // With the budget met it grows.
        Assert.Contains(Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), creatures: Goblins(10),
            gold: 2000, wageBill: 1000)).Actions, a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Lair });
    }

    [Fact]
    public void GrowthMustLeaveTheBudgetStandingOncePaid()
    {
        // 1300 gold meets a 1000 wage bill plus 20%, but a 3x3 Lair at 50 a tile would leave 850.
        PlayerDecision decision = Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), creatures: Goblins(10),
            gold: 1300, wageBill: 1000));
        Assert.DoesNotContain(decision.Actions, a => a is PlayerAction.BuildRoom);
        Assert.Contains("Lair 3x3 would leave less than 1200 gold", decision.Thoughts.Single(t => t.Routine == "room").Note);
        // Without the budget it grows as before the plan.
        CampaignPlayer spender = Player();
        spender.Apply(spender.Strategy with { GoldBudget = 0 });
        Assert.Contains(spender.Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), creatures: Goblins(10),
            gold: 1100, wageBill: 1000)).Actions, a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Lair });
    }

    /// <summary>Own floor walled in by impenetrable rock but for three water tiles leading to diggable rock (x 11).</summary>
    private static readonly string[] Moat =
    [
        "RRRRRRRRRRRR",
        "R....H..www#",
        "RRRRRRRRRRRR",
    ];

    private static readonly PlayerRoomOffer[] BridgeOffer = [new(RoomTypeId.WoodenBridge, new GoldAmount(100), true)];

    [Fact]
    public void WithNothingLeftToDigTheShortestSpanToNewGroundIsBridged()
    {
        PlayerDecision decision = Player().Decide(Observe(map: Moat, rooms: BridgeOffer, gold: 5000));
        PlayerAction.BuildRoom build = Assert.Single(decision.Actions.OfType<PlayerAction.BuildRoom>());
        Assert.Equal((RoomTypeId.WoodenBridge, new TilePos(8, 1), new TilePos(10, 1)), (build.Room, build.From, build.To));
        // Not while the budget would not stand after it (300 for three tiles, a 1000 wage bill).
        Assert.DoesNotContain(Player().Decide(Observe(map: Moat, rooms: BridgeOffer, gold: 1400, wageBill: 1000)).Actions,
            a => a is PlayerAction.BuildRoom);
        // Not while land is left to dig.
        string[] open = [.. Moat];
        open[0] = "RRR#RRRRRRRR";
        Assert.DoesNotContain(Player().Decide(Observe(map: open, rooms: BridgeOffer, gold: 5000)).Actions, a => a is PlayerAction.BuildRoom);
    }

    [Fact]
    public void ADryTreasuryIsFilledFromGoldLyingElsewhere()
    {
        // 600 gold in the Treasury against a 1000 wage bill, 5000 in all: the pile beside the heart goes to the Treasury.
        PlayerRoomSummary[] owned = CoreOwned();
        owned[1] = new PlayerRoomSummary(RoomTypeId.Treasury, 9, Capacity: 9000, Used: 600);
        PlayerVisibleThing pile = new(Handle(0), PlayerThingKind.StoreGold, new TilePos(6, 5), Me, PlayerThingActions.PickUp);
        PlayerVisibleThing kept = new(Handle(1), PlayerThingKind.StoreGold, new TilePos(10, 2), Me, PlayerThingActions.PickUp);   // in the Treasury
        CampaignPlayer player = Player();
        PlayerDecision lift = player.Decide(Observe(map: Developed, rooms: CoreOffers, owned: owned, things: [kept, pile], gold: 5000, wageBill: 1000, tick: 100));
        Assert.Contains(new PlayerAction.PickUp(Handle(0)), lift.Actions);
        Assert.DoesNotContain(new PlayerAction.PickUp(Handle(1)), lift.Actions);
        PlayerAction.DropAt drop = Assert.Single(player.Decide(Observe(2, map: Developed, rooms: CoreOffers, owned: owned, hand: 1, gold: 5000, wageBill: 1000, tick: 104))
            .Actions.OfType<PlayerAction.DropAt>());
        Assert.Equal(RoomTypeId.Treasury, Map(Developed)[drop.Tile].Room);
        // A Treasury holding the budget is left alone.
        owned[1] = new PlayerRoomSummary(RoomTypeId.Treasury, 9, Capacity: 9000, Used: 1300);
        Assert.DoesNotContain(Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: owned, things: [kept, pile], gold: 5000, wageBill: 1000, tick: 100))
            .Actions, a => a is PlayerAction.PickUp);
    }

    [Fact]
    public void TheLairGrowsByItsBedsWhenTheHoverShowsThem()
    {
        // Eleven tiles fit ten goblins by the old tiles rule, but the hover's 11 beds leave one spare, fewer than two.
        PlayerRoomSummary[] owned = CoreOwned(lair: 11);
        owned[2] = new PlayerRoomSummary(RoomTypeId.Lair, 11, Capacity: 11, Used: 9);
        PlayerDecision decision = Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: owned, creatures: Goblins(10)));
        Assert.Contains(decision.Actions, a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Lair });
        Assert.Contains("Lair 11 beds for 10 non-workers", decision.Thoughts.Single(t => t.Routine == "room").Note);

        // With FreeBeds 0 the old tiles rule decides: eleven tiles fit ten.
        CampaignPlayer old = Player();
        old.Apply(old.Strategy with { FreeBeds = 0 });
        Assert.DoesNotContain(old.Decide(Observe(map: Developed, rooms: CoreOffers, owned: owned, creatures: Goblins(10))).Actions,
            a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Lair });
        owned[2] = new PlayerRoomSummary(RoomTypeId.Lair, 12, Capacity: 12, Used: 9);
        Assert.DoesNotContain(Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: owned, creatures: Goblins(10))).Actions,
            a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Lair });
    }

    [Fact]
    public void HungryCreaturesGrowTheHatchery()
    {
        // Four goblins fit a 9-tile Hatchery by tiles; two of them show the chicken.
        PlayerCreatureSummary[] four = Goblins(4);
        Assert.DoesNotContain(Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), creatures: four)).Actions,
            a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Hatchery });
        four[0] = four[0] with { Hungry = true };
        four[1] = four[1] with { Hungry = true };
        PlayerDecision hungry = Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: CoreOwned(), creatures: four));
        Assert.Contains(hungry.Actions, a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Hatchery });
        Assert.Contains("2 of 4 non-workers hungry", hungry.Thoughts.Single(t => t.Routine == "room").Note);
        // Twelve Hatchery tiles for four is three each: hunger grows it no further while the chickens hatch.
        PlayerRoomSummary[] big = CoreOwned();
        big[3] = new PlayerRoomSummary(RoomTypeId.Hatchery, 12);
        Assert.DoesNotContain(Player().Decide(Observe(map: Developed, rooms: CoreOffers, owned: big, creatures: four)).Actions,
            a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Hatchery });
    }

    [Fact]
    public void IdleGoldTrainsFightersBelowTheTrainingCapOnly()
    {
        PlayerRoomSummary[] owned = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9));
        PlayerVisibleThing[] things = [Creature(0, 7, 7, Goblin)];
        // Poor: nobody trains (the default strategy names no room to use).
        Assert.DoesNotContain(Player().Decide(Observe(map: WithTraining, owned: owned, things: things, gold: 5000, tick: 100)).Actions,
            a => a is PlayerAction.PickUp);
        // Rich: the idle goblin goes to the Training Room.
        Assert.Contains(new PlayerAction.PickUp(Handle(0)),
            Player().Decide(Observe(map: WithTraining, owned: owned, things: things, gold: 40000, tick: 100)).Actions);
        // At the room's cap it stays.
        PlayerCreatureSummary capped = new(Goblin, new TilePos(7, 7), false, Handle(0), Level: 4, Petals: 6, Mood: PlayerCreatureMood.Happy);
        Assert.DoesNotContain(Player().Decide(Observe(map: WithTraining, owned: owned, things: things, creatures: [capped], gold: 40000, tick: 100)).Actions,
            a => a is PlayerAction.PickUp);
    }

    [Fact]
    public void ATrapCoveringOwnGroundGetsAPartyLiftedAndDroppedTogether()
    {
        // A Sentry at (5,9), in the seam, reaches the own floor at (5,8).
        PlayerVisibleTrap trap = new(new TilePos(5, 9), 14, Heroes, ReachTiles: 3);
        PlayerVisibleThing[] four = [Creature(0, 2, 2, Goblin), Creature(1, 3, 2, Goblin), Creature(2, 4, 2, Goblin), Creature(3, 6, 2, Goblin)];
        PlayerCreatureSummary[] fit = [.. four.Select(t => new PlayerCreatureSummary(Goblin, t.Tile, false, t.Handle, Level: 2, Petals: 6,
            Mood: PlayerCreatureMood.Happy))];
        CampaignPlayer player = Player();
        PlayerDecision lift = player.Decide(Observe(things: four, creatures: fit, gold: 0, enemyTraps: [trap]));
        Assert.Equal(4, lift.Actions.OfType<PlayerAction.PickUp>().Count());
        Assert.StartsWith("lift 4 fighters against the 14 trap at", lift.Thoughts.Single(t => t.Routine == "assault").Note);

        PlayerDecision drop = player.Decide(Observe(2, hand: 4, gold: 0, enemyTraps: [trap]));
        PlayerAction.DropAt[] drops = [.. drop.Actions.OfType<PlayerAction.DropAt>()];
        Assert.Equal(4, drops.Length);
        Assert.Single(drops.Select(d => d.Tile).Distinct());
        Assert.Equal(new TilePos(5, 8), drops[0].Tile);   // the own floor nearest the trap, inside its reach

        // Three fighters are not a party: none is sent alone.
        Assert.DoesNotContain(Player().Decide(Observe(things: four[..3], creatures: fit[..3], gold: 0, enemyTraps: [trap])).Actions, a => a is PlayerAction.PickUp);
        // Nor is part of one: with two actions left in the decision, nobody goes.
        PlayerDecision crowded = Player(o => o with { MaxActionsPerDecision = 4 }).Decide(Observe(things: four, creatures: fit, gold: 5000, enemyTraps: [trap]));
        Assert.Contains(crowded.Actions, a => a is PlayerAction.BuildRoom);   // the room took two of the four actions
        Assert.DoesNotContain(crowded.Actions, a => a is PlayerAction.PickUp);
        // A fighter whose flower is not known fit is not sent.
        PlayerCreatureSummary[] hurt = [fit[0] with { Petals = 3 }, .. fit[1..]];
        Assert.DoesNotContain(Player().Decide(Observe(things: four, gold: 0, enemyTraps: [trap], creatures: hurt)).Actions,
            a => a is PlayerAction.PickUp);
    }

    [Fact]
    public void NoPartyGoesWhileAnEnemyIsNearHome()
    {
        PlayerVisibleTrap trap = new(new TilePos(5, 9), 14, Heroes, ReachTiles: 3);
        // The enemy stands inside the trap's reach, so the hand does not answer it, yet it is near home.
        PlayerVisibleThing hero = new(Handle(4), PlayerThingKind.Creature, new TilePos(5, 8), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
        PlayerVisibleThing[] four = [Creature(0, 2, 2, Goblin), Creature(1, 3, 2, Goblin), Creature(2, 4, 2, Goblin), Creature(3, 6, 2, Goblin), hero];
        PlayerCreatureSummary[] fit = [.. four[..4].Select(t => new PlayerCreatureSummary(Goblin, t.Tile, false, t.Handle, Level: 2, Petals: 6,
            Mood: PlayerCreatureMood.Happy))];
        PlayerDecision decision = Player().Decide(Observe(things: four, creatures: fit, gold: 0, enemyTraps: [trap]));
        Assert.DoesNotContain(decision.Actions, a => a is PlayerAction.PickUp);
        Assert.Equal("enemy near home: defence first", decision.Thoughts.Single(t => t.Routine == "assault").Note);
    }
}
