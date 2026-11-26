// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>Enemy traps in view (economy and assault plan A2): the player neither answers an enemy standing in a
/// trap's reach, nor drops or marks digging there (Level 4, 2026-11-16: a lookout between two Sentry traps drew
/// thousands of drops and most of the army's deaths).</summary>
public partial class CampaignPlayerTests
{
    private static readonly PlayerVisibleTrap Sentry = new(new TilePos(9, 9), 14, Heroes, ReachTiles: 3);

    [Fact]
    public void AnEnemyInATrapsReachIsNotAnswered()
    {
        PlayerVisibleThing hero = new(Handle(0), PlayerThingKind.Creature, new TilePos(9, 9), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
        PlayerVisibleThing goblin = new(Handle(1), PlayerThingKind.Creature, new TilePos(3, 2), Me, PlayerThingActions.PickUp | PlayerThingActions.Slap, creatureType: Goblin);

        Assert.Contains(new PlayerAction.PickUp(Handle(1)), Player().Decide(Observe(things: [hero, goblin], gold: 0)).Actions);
        PlayerDecision guarded = Player().Decide(Observe(things: [hero, goblin], gold: 0, enemyTraps: [Sentry]));
        Assert.DoesNotContain(guarded.Actions, a => a is PlayerAction.PickUp);
        Assert.Equal("no attack to answer", guarded.Thoughts.Single(t => t.Routine == "hand").Note);
    }

    [Fact]
    public void NothingHeldIsDroppedInATrapsReach()
    {
        // Nothing to answer: the held creature goes down near home, never on the trap's covered floor.
        PlayerVisibleTrap nearHome = Sentry with { Tile = new TilePos(5, 6), ReachTiles = 2 };
        PlayerAction.DropAt drop = Assert.IsType<PlayerAction.DropAt>(Player().Decide(Observe(hand: 1, gold: 0, enemyTraps: [nearHome])).Actions[0]);
        long dx = drop.Tile.X - 5, dy = drop.Tile.Y - 6;
        Assert.True(4 * (dx * dx + dy * dy) > 25, $"dropped at {drop.Tile}, inside the trap's reach");
    }

    [Fact]
    public void NoDigMarkIsLaidInATrapsReach()
    {
        PlayerVisibleTrap seam = Sentry with { Tile = new TilePos(5, 9), ReachTiles = 2 };
        PlayerDecision decision = Player().Decide(Observe(gold: 0, enemyTraps: [seam]));
        Assert.Contains(decision.Actions, a => a is PlayerAction.TagArea);   // other ground is marked
        foreach (PlayerAction.TagArea tag in decision.Actions.OfType<PlayerAction.TagArea>())
        {
            long dx = tag.From.X - 5, dy = tag.From.Y - 9;
            Assert.True(4 * (dx * dx + dy * dy) > 25, $"marked {tag.From}, inside the trap's reach");
        }
        Assert.Contains(decision.Beliefs, b => b.Name == "enemy_traps_seen" && b.Value == 1);
    }
}
