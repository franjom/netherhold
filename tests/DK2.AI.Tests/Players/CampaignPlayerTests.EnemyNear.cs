// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>Only an enemy near home (or the Combat Pit) stops the peaceful routines (Q843): on Level 15a hero Guards
/// stand in their Prison nine tiles and more from the Heart all game, and an enemy merely in sight froze them.</summary>
public partial class CampaignPlayerTests
{
    private static PlayerVisibleThing HeroAt(TilePos at)
    {
        return new(Handle(9), PlayerThingKind.Creature, at, Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
    }

    [Fact]
    public void AFarEnemyDoesNotStopThePitButOneNearHomeOrThePitDoes()
    {
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) idle = Goblin1(new TilePos(9, 6), level: 3, petals: 6, ordinal: 0);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) partner = Goblin1(new TilePos(10, 6), level: 4, petals: 6, ordinal: 1);
        PlayerObservation With(TilePos hero)
        {
            return Observe(1, map: PitMap, rooms: [], owned: PitRooms, tick: 1000,
            things: [idle.Thing, partner.Thing, HeroAt(hero)], creatures: [idle.Flower, partner.Flower]);
        }
        // Fighting off: a near hero would otherwise draw a lift of its own, to fight it.
        CampaignPlayer Near(int tiles)
        {
            return PitPlayer().Also(p => p.Apply(p.Strategy with { EnemyNearTiles = tiles, Fight = FightMode.Off }));
        }

        bool Sends(int tiles, TilePos hero)
        {
            return Near(tiles).Decide(With(hero)).Actions.OfType<PlayerAction.PickUp>().Any();
        }

        // The heart is at (6,6); the Pit's tile nearest it is (5,5).
        Assert.False(Sends(8, new TilePos(10, 10)));                // 4 from home: near
        Assert.True(Sends(2, new TilePos(10, 10)));                 // 4 from home and 5 from the Pit: far at 2
        Assert.False(Sends(2, new TilePos(4, 3)));                  // 3 from home but 2 from the Pit's tile (5,5): near the Pit
        Assert.False(Sends(4, new TilePos(10, 10)));                // exactly 4 from home: near (the edge counts)
        Assert.True(Sends(3, new TilePos(10, 10)));                 // one tile beyond: far
    }

    [Fact]
    public void AFarEnemyDoesNotStopScouting()
    {
        // The heart is at (2,2) and the edge tiles at x 6; a hero 18 tiles away, in sight but far from both, used to stop it.
        PlayerObservation view = ScoutView(1, 1000, things: [HomeImp, .. Crew, HeroAt(new TilePos(20, 2))]);
        Assert.Contains(new PlayerAction.PickUp(HomeImp.Handle), Scouter().Decide(view).Actions);
    }

    [Fact]
    public void TheEnemyNearDistanceDefaultsToEightAndIsNeverNegative()
    {
        Assert.Equal(8, new PlayerStrategy().EnemyNearTiles);
        Assert.Contains("EnemyNearTiles -1 is negative", new PlayerStrategy { EnemyNearTiles = -1 }.Validate());
    }

    [Fact]
    public void ACreatureCarriedToItsRoomStillGoesThereWhenAFarEnemyComesIntoSight()
    {
        // Lifted to the Training Room with no enemy about; then a hero appears far off (default fighting): it goes on to
        // the room, not to the hero. One near home still takes it (AnEnemyAppearingWhileACreatureIsCarriedToWorkGetsItInstead).
        PlayerRoomSummary[] owned = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9));
        CampaignPlayer player = Trainer();
        Assert.Contains(new PlayerAction.PickUp(Handle(0)), player.Decide(Observe(map: WithTraining, owned: owned, things: [Creature(0, 7, 7, Goblin)])).Actions);
        PlayerDecision drop = player.Decide(Observe(2, map: WithTraining, owned: owned, hand: 1, things: [HeroAt(new TilePos(40, 40))]));
        Assert.Contains(new PlayerAction.DropAt(new TilePos(4, 4)), drop.Actions);
    }

    [Fact]
    public void NoCreatureIsSentToARoomAnEnemyStandsNearEvenWithHomeClear()
    {
        PlayerRoomSummary[] owned = CoreOwned(more: new PlayerRoomSummary(RoomTypeId.TrainingRoom, 9));
        CampaignPlayer Trainer1()
        {
            return Trainer().Also(p => p.Apply(p.Strategy with { EnemyNearTiles = 1, Fight = FightMode.Off }));
        }
        // The heart is at (5,5) and the room's tile nearest it (4,4). At (3,3) the hero is 1 from the room, 2 from home.
        Assert.Empty(Trainer1().Decide(Observe(map: WithTraining, owned: owned, things: [Creature(0, 7, 7, Goblin), HeroAt(new TilePos(3, 3))]))
            .Actions.OfType<PlayerAction.PickUp>());
        Assert.Contains(new PlayerAction.PickUp(Handle(0)), Trainer1().Decide(Observe(map: WithTraining, owned: owned,
            things: [Creature(0, 7, 7, Goblin), HeroAt(new TilePos(8, 8))])).Actions);
    }
}
