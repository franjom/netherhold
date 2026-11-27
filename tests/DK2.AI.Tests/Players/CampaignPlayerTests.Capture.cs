// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>Capture (R16, Level 5 plan step 2, 2026-11-17): an Imp choosing its own work takes a body last, so the
/// player drops one on an enemy collapsed on own ground while a Prison is owned. A body is no threat.</summary>
public partial class CampaignPlayerTests
{
    private static readonly PlayerRoomSummary OwnPrison = new PlayerRoomSummary(RoomTypeId.Prison, 9);

    private static PlayerVisibleThing Body(TilePos at, int petals = 1)
    {
        return new PlayerVisibleThing(Handle(1), PlayerThingKind.Creature, at, Heroes, PlayerThingActions.None, Goblin) { Level = 2, Petals = petals };
    }

    private static PlayerVisibleThing OwnImp(TilePos at)
    {
        return new PlayerVisibleThing(Handle(2), PlayerThingKind.Creature, at, Me, PlayerThingActions.PickUp, Imp);
    }

    [Fact]
    public void AnImpIsLiftedAndDroppedOnAnEnemyCollapsedOnOwnGround()
    {
        CampaignPlayer player = Player();
        PlayerDecision lift = player.Decide(Observe(map: Dungeon, rooms: [], owned: [OwnPrison],
            things: [Body(new TilePos(3, 3)), OwnImp(new TilePos(7, 7))]));
        Assert.Equal(new PlayerAction.PickUp(Handle(2)), Assert.Single(lift.Actions.OfType<PlayerAction.PickUp>()));
        Assert.Contains("to capture the body at (3,3)", lift.Thoughts.Single(t => t.Routine == "capture").Note);

        PlayerDecision drop = player.Decide(Observe(revision: 2, map: Dungeon, rooms: [], owned: [OwnPrison], hand: 1,
            things: [Body(new TilePos(3, 3))]));
        Assert.Equal(new PlayerAction.DropAt(new TilePos(3, 3)), drop.Actions.OfType<PlayerAction.DropAt>().First());
    }

    [Fact]
    public void NoImpGoesWithoutAPrisonOrToAStandingEnemyOrABodyOffOwnGround()
    {
        PlayerAction.PickUp? Lifted(IReadOnlyList<PlayerRoomSummary> owned, PlayerVisibleThing body, string[]? map = null)
        {
            return Player().Decide(Observe(map: map ?? Dungeon, rooms: [], owned: owned, things: [body, OwnImp(new TilePos(7, 7))]))
                .Actions.OfType<PlayerAction.PickUp>().FirstOrDefault();
        }

        Assert.Null(Lifted([], Body(new TilePos(3, 3))));                              // no Prison
        Assert.Null(Lifted([OwnPrison], Body(new TilePos(3, 3), petals: 4)));          // standing, not down
        Assert.Null(Lifted([OwnPrison], Body(new TilePos(1, 3))));                     // on rock, not own ground
    }

    [Fact]
    public void ACollapsedEnemyIsNoThreatButOneInItsLastQuarterIs()
    {
        PlayerObservation seen = Observe(map: Dungeon, things: [Body(new TilePos(3, 3))]);
        Assert.False(PlayerActionRoutines.IsThreat(seen, seen.Things[0]));
        // Two petals: alive in its last quarter of health (status-flowers.md section 3), still fighting.
        PlayerObservation hurt = Observe(map: Dungeon, things: [Body(new TilePos(3, 3), petals: 2)]);
        Assert.True(PlayerActionRoutines.IsThreat(hurt, hurt.Things[0]));
    }

    [Fact]
    public void NoImpGoesToAPrisonerInItsCellOrToABodyAnImpStandsOn()
    {
        string[] withCell = [.. Dungeon];
        withCell[3] = "R#.PP....#R";
        PlayerAction.PickUp? Lifted(string[] map, params PlayerVisibleThing[] things)
        {
            return Player().Decide(Observe(map: map, rooms: [], owned: [OwnPrison], things: things))
                .Actions.OfType<PlayerAction.PickUp>().FirstOrDefault();
        }

        Assert.Null(Lifted(withCell, Body(new TilePos(3, 3)), OwnImp(new TilePos(7, 7))));          // dying in its cell
        // An Imp on the body's tile carries it (a carried body shows at its carrier) or was dropped there.
        Assert.Null(Lifted(Dungeon, Body(new TilePos(3, 3)), OwnImp(new TilePos(3, 3))));
    }

    [Fact]
    public void ALiftThatNeverLandsLeavesNoDropBehindForTheNextHold()
    {
        CampaignPlayer player = Player();
        player.Decide(Observe(map: Dungeon, rooms: [], owned: [OwnPrison], things: [Body(new TilePos(3, 3)), OwnImp(new TilePos(7, 7))]));
        // The lift was refused: the hand is empty at the next look, and then holds something else.
        player.Decide(Observe(revision: 2, map: Dungeon, rooms: [], owned: [OwnPrison]));
        PlayerDecision later = player.Decide(Observe(revision: 3, map: Dungeon, rooms: [], owned: [OwnPrison], hand: 1));
        Assert.DoesNotContain(new PlayerAction.DropAt(new TilePos(3, 3)), later.Actions);
    }
}
