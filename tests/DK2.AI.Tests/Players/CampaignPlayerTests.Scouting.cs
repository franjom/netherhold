// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The Imp scouting routine (campaign bot stage 3 plan, Task 4; owner direction): an Imp dropped on neutral
/// floor at the edge of the known ground, lifted again after a look, and never left where it can die.</summary>
public partial class CampaignPlayerTests
{
    /// <summary>Own floor x 1..4 with the heart at (2,2); neutral floor x 5, dirt path (no owner shows) x 6; unknown ground from x 7.</summary>
    private static readonly string[] ScoutMap =
    [
        "RRRRRRRRRRRR",
        "R....nd????R",
        "R.H..nd????R",
        "R....nd????R",
        "R....nd????R",
        "RRRRRRRRRRRR",
    ];

    private static readonly PlayerVisibleThing HomeImp =
        new(Handle(0), PlayerThingKind.Creature, new TilePos(3, 3), Me, PlayerThingActions.PickUp, creatureType: Imp);

    /// <summary>Two more own Imps at work far from the edge: scouting takes one only while the keeper has three.</summary>
    private static readonly PlayerVisibleThing[] Crew =
    [
        new(Handle(5), PlayerThingKind.Creature, new TilePos(1, 1), Me, PlayerThingActions.PickUp, creatureType: Imp),
        new(Handle(6), PlayerThingKind.Creature, new TilePos(1, 4), Me, PlayerThingActions.PickUp, creatureType: Imp),
    ];

    private static PlayerVisibleThing ImpAt(TilePos at) => new(Handle(0), PlayerThingKind.Creature, at, Me, PlayerThingActions.PickUp, creatureType: Imp);

    private static PlayerObservation ScoutView(long revision, long tick, int hand = 0, PlayerVisibleThing[]? things = null,
        PlayerCreatureSummary[]? creatures = null) =>
        Observe(revision, map: ScoutMap, hand: hand, rooms: [], tick: tick, things: things ?? [], creatures: creatures);

    private static CampaignPlayer Scouter() =>
        Player().Also(p => p.Apply(p.Strategy with { Scouts = 1, Dig = DigMode.Off }));

    [Fact]
    public void AnImpIsDroppedOnNeutralFloorAtTheEdgeOfTheKnownGroundAndMovedOnAfterALook()
    {
        CampaignPlayer player = Scouter();
        PlayerDecision lift = player.Decide(ScoutView(1, 1000, things: [HomeImp, .. Crew]));
        Assert.Contains(new PlayerAction.PickUp(HomeImp.Handle), lift.Actions);
        Assert.Contains("scout", lift.Reason, StringComparison.Ordinal);
        // The edge tile (beside unknown ground) nearest the heart; x 5 is neutral but not at the edge.
        Assert.Equal(new TilePos(6, 2), Assert.Single(player.Decide(ScoutView(2, 1004, hand: 1)).Actions.OfType<PlayerAction.DropAt>()).Tile);

        // It looks for the dwell (8 ticks), then is lifted and taken to the next edge tile.
        Assert.Empty(player.Decide(ScoutView(3, 1008, things: [ImpAt(new TilePos(6, 2))])).Actions.OfType<PlayerAction.PickUp>());
        // Followed when it walks off (within 3 tiles), and lifted where it now stands once the dwell is up.
        Assert.Contains(new PlayerAction.PickUp(HomeImp.Handle), player.Decide(ScoutView(4, 1012, things: [ImpAt(new TilePos(5, 3))])).Actions);
        Assert.Equal(new TilePos(6, 1), Assert.Single(player.Decide(ScoutView(5, 1016, hand: 1)).Actions.OfType<PlayerAction.DropAt>()).Tile);
    }

    [Fact]
    public void AScoutIsLiftedHomeAtOnceWhenAnEnemyComesNearOrItIsHit()
    {
        foreach (bool hit in (bool[])[false, true])
        {
            CampaignPlayer player = Scouter();
            player.Decide(ScoutView(1, 1000, things: [HomeImp, .. Crew]));
            player.Decide(ScoutView(2, 1004, hand: 1));
            PlayerVisibleThing scout = ImpAt(new TilePos(6, 2));
            PlayerVisibleThing hero = new(Handle(1), PlayerThingKind.Creature, new TilePos(6, 4), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
            PlayerDecision lifted = player.Decide(hit
                ? ScoutView(3, 1006, things: [scout], creatures: [new(Imp, scout.Tile, false, scout.Handle, 1, 5, PlayerCreatureMood.Happy)])
                : ScoutView(3, 1006, things: [scout, hero]));
            Assert.Contains(new PlayerAction.PickUp(scout.Handle), lifted.Actions);
            // Home, not on to the next edge tile, and never toward the enemy: the own floor nearest the hero is x 4.
            TilePos drop = Assert.Single(player.Decide(ScoutView(4, 1010, hand: 1, things: hit ? null : [hero])).Actions.OfType<PlayerAction.DropAt>()).Tile;
            Assert.True(drop.X < 4, $"dropped at {drop}, not on own floor near home");
        }
    }

    [Fact]
    public void NoScoutGoesNearAnEnemyAndTheRoutineIsOffByDefault()
    {
        PlayerVisibleThing hero = new(Handle(1), PlayerThingKind.Creature, new TilePos(9, 2), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
        Assert.Empty(Scouter().Decide(ScoutView(1, 1000, things: [HomeImp, .. Crew, hero])).Actions.OfType<PlayerAction.PickUp>());
        Assert.Equal(0, new PlayerStrategy().Scouts);
        Assert.Empty(Player().Decide(ScoutView(1, 1000, things: [HomeImp, .. Crew])).Actions.OfType<PlayerAction.PickUp>());
        // Fewer than three Imps: none is taken from work.
        Assert.Empty(Scouter().Decide(ScoutView(1, 1000, things: [HomeImp, Crew[0]])).Actions.OfType<PlayerAction.PickUp>());
        Assert.Contains("Scouts 2 is not 0 or 1", new PlayerStrategy { Scouts = 2 }.Validate());
    }

    [Fact]
    public void AHurtImpIsNotSentToScout()
    {
        PlayerCreatureSummary scratched = new(Imp, HomeImp.Tile, false, HomeImp.Handle, 1, 5, PlayerCreatureMood.Happy);
        PlayerDecision lift = Scouter().Decide(ScoutView(1, 1000, things: [HomeImp, .. Crew], creatures: [scratched]));
        PlayerAction.PickUp sent = Assert.Single(lift.Actions.OfType<PlayerAction.PickUp>());
        Assert.NotEqual(HomeImp.Handle, sent.Thing);
    }

    [Fact]
    public void ScoutingKeepsAwayFromWhereAnEnemyWasSeenOnlyForAWhile()
    {
        CampaignPlayer player = Scouter();
        PlayerVisibleThing hero = new(Handle(1), PlayerThingKind.Creature, new TilePos(9, 2), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
        Assert.Empty(player.Decide(ScoutView(1, 1000, things: [HomeImp, .. Crew, hero])).Actions.OfType<PlayerAction.PickUp>());
        // Gone from view, but every edge tile is within 8 of where it stood: remembered for 1200 ticks.
        Assert.Empty(player.Decide(ScoutView(2, 1300, things: [HomeImp, .. Crew])).Actions.OfType<PlayerAction.PickUp>());
        Assert.Contains(new PlayerAction.PickUp(HomeImp.Handle), player.Decide(ScoutView(3, 2300, things: [HomeImp, .. Crew])).Actions);
    }

    [Fact]
    public void NoScoutIsSentBesideGroundAnotherKeeperOrTheHeroesHold()
    {
        // Hero ground at x 9-10 beside the unknown: every edge tile at x 6 is within 8 tiles of it.
        string[] map =
        [
            "RRRRRRRRRRRR",
            "R....nd??hhR",
            "R.H..nd??hhR",
            "R....nd????R",
            "R....nd????R",
            "RRRRRRRRRRRR",
        ];
        PlayerObservation view = Observe(1, map: map, rooms: [], tick: 1000, things: [HomeImp, .. Crew]);
        Assert.Empty(Scouter().Decide(view).Actions.OfType<PlayerAction.PickUp>());
    }

    [Fact]
    public void OnePitFighterIsRefused()
    {
        Assert.Contains("PitFighters 1 is neither 0 nor 2 or more", new PlayerStrategy { PitFighters = 1 }.Validate());
        Assert.Empty(new PlayerStrategy { PitFighters = 2 }.Validate());
    }
}
