// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The whole-army push (economy and assault plan A4): an attack that holds its ground far from home while the
/// army shrinks is answered by a party dropped together, never by one fighter at a time; a raid on the dungeon is
/// always answered.</summary>
public partial class CampaignPlayerTests
{
    /// <summary>A long strip of own floor from home (2,2) to (30,2), an enemy standing at its far end.</summary>
    private static readonly string[] LongStrip =
    [
        "RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR",
        "R################################R",
        "R.H.............................eR",
        "R################################R",
        "RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR",
    ];

    private static (PlayerVisibleThing[] Things, PlayerCreatureSummary[] Creatures) Army(int fighters, TilePos enemy, long revision)
    {
        var things = new List<PlayerVisibleThing> { new(Handle(0, revision), PlayerThingKind.Creature, enemy, Heroes, PlayerThingActions.None,
            creatureType: new CreatureTypeId(20)) };
        var creatures = new List<PlayerCreatureSummary>();
        for (int i = 0; i < fighters; i++)
        {
            var at = new TilePos(3 + i, 2);
            things.Add(new(Handle(1 + i, revision), PlayerThingKind.Creature, at, Me, PlayerThingActions.PickUp, Goblin));
            creatures.Add(new PlayerCreatureSummary(Goblin, at, false, Handle(1 + i, revision), Level: 2, Petals: 6, Mood: PlayerCreatureMood.Happy));
        }
        return ([.. things], [.. creatures]);
    }

    /// <summary>Holds an attack at <paramref name="enemy"/> from tick 100 to 1400 while the army falls from 12 to 7.</summary>
    private static CampaignPlayer Besieged(TilePos enemy, string[] map)
    {
        CampaignPlayer player = Player();
        var (start, startCreatures) = Army(12, enemy, 1);
        player.Decide(Observe(1, map: map, things: start, creatures: startCreatures, gold: 0, tick: 100));
        var (later, laterCreatures) = Army(7, enemy, 2);
        player.Decide(Observe(2, map: map, things: later, creatures: laterCreatures, gold: 0, tick: 1400));
        return player;
    }

    [Fact]
    public void AFarStandThatCostsFightersBecomesASiegeAndNoneGoesAlone()
    {
        var enemy = new TilePos(32, 2);
        CampaignPlayer player = Besieged(enemy, LongStrip);
        // Three fit fighters left: fewer than a party of four, so nobody is sent.
        var (few, fewCreatures) = Army(3, enemy, 3);
        PlayerDecision wait = player.Decide(Observe(3, map: LongStrip, things: few, creatures: fewCreatures, gold: 0, tick: 1500));
        Assert.DoesNotContain(wait.Actions, a => a is PlayerAction.PickUp);
        Assert.Contains("none goes alone", wait.Thoughts.Single(t => t.Routine == "hand").Note);
    }

    [Fact]
    public void ASiegePartyIsLiftedThenDroppedWholeWithoutLiftingAgain()
    {
        var enemy = new TilePos(32, 2);
        CampaignPlayer player = Besieged(enemy, LongStrip);
        var (army, armyCreatures) = Army(7, enemy, 3);
        PlayerDecision lift = player.Decide(Observe(3, map: LongStrip, things: army, creatures: armyCreatures, gold: 0, tick: 1500));
        Assert.Equal(4, lift.Actions.OfType<PlayerAction.PickUp>().Count());   // the party: max(4, 2/3 of 7)
        // Four in hand: the party is whole, so all go down together and no more are lifted.
        PlayerDecision drop = player.Decide(Observe(4, map: LongStrip, things: army[..1], creatures: armyCreatures, hand: 4, gold: 0, tick: 1504));
        Assert.Equal(4, drop.Actions.OfType<PlayerAction.DropAt>().Count());
        Assert.DoesNotContain(drop.Actions, a => a is PlayerAction.PickUp);
        Assert.Single(drop.Actions.OfType<PlayerAction.DropAt>().Select(d => d.Tile).Distinct());
    }

    [Fact]
    public void APartyLargerThanOneDecisionsActionsGoesDownOverTwoWithoutLiftingAgain()
    {
        var enemy = new TilePos(32, 2);
        CampaignPlayer player = Besieged(enemy, LongStrip);
        player.Apply(player.Strategy with { RallyParty = 6 });
        // Nine fighters: a party of six, lifted four and then two.
        var (army, armyCreatures) = Army(9, enemy, 3);
        Assert.Equal(4, player.Decide(Observe(3, map: LongStrip, things: army, creatures: armyCreatures, gold: 0, tick: 1500)).Actions
            .OfType<PlayerAction.PickUp>().Count());
        var (rest, restCreatures) = Army(9, enemy, 4);
        Assert.Equal(2, player.Decide(Observe(4, map: LongStrip, things: [rest[0], .. rest[5..]], creatures: restCreatures, hand: 4, gold: 0, tick: 1504))
            .Actions.OfType<PlayerAction.PickUp>().Count());
        // Six in hand: four go down, then the other two, and nobody is lifted in between.
        PlayerDecision first = player.Decide(Observe(5, map: LongStrip, things: [rest[0], .. rest[7..]], creatures: restCreatures, hand: 6, gold: 0, tick: 1508));
        Assert.Equal(4, first.Actions.OfType<PlayerAction.DropAt>().Count());
        PlayerDecision second = player.Decide(Observe(6, map: LongStrip, things: [rest[0], .. rest[7..]], creatures: restCreatures, hand: 2, gold: 0, tick: 1512));
        Assert.Equal(2, second.Actions.OfType<PlayerAction.DropAt>().Count());
        Assert.DoesNotContain(second.Actions, a => a is PlayerAction.PickUp);
    }

    [Fact]
    public void APartyThatWentInIsNotLiftedStraightBackOut()
    {
        var enemy = new TilePos(32, 2);
        CampaignPlayer player = Besieged(enemy, LongStrip);
        var (army, armyCreatures) = Army(7, enemy, 3);
        player.Decide(Observe(3, map: LongStrip, things: army, creatures: armyCreatures, gold: 0, tick: 1500));
        player.Decide(Observe(4, map: LongStrip, things: army[..1], creatures: armyCreatures, hand: 4, gold: 0, tick: 1504));
        // The hand is empty again and seven fit fighters stand about: the party fights before another is gathered.
        var (after, afterCreatures) = Army(7, enemy, 5);
        PlayerDecision next = player.Decide(Observe(5, map: LongStrip, things: after, creatures: afterCreatures, gold: 0, tick: 1508));
        Assert.DoesNotContain(next.Actions, a => a is PlayerAction.PickUp);
    }

    [Fact]
    public void WhileAPartyGathersAHurtDefenderIsStillHealed()
    {
        var enemy = new TilePos(32, 2);
        CampaignPlayer player = Besieged(enemy, LongStrip);
        var (few, fewCreatures) = Army(3, enemy, 3);
        var hurtAt = new TilePos(30, 2);
        PlayerVisibleThing hurt = new(Handle(9, 3), PlayerThingKind.Creature, hurtAt, Me, PlayerThingActions.PickUp, Goblin);
        PlayerCreatureSummary hurtFlower = new(Goblin, hurtAt, false, Handle(9, 3), Level: 2, Petals: 2, Mood: PlayerCreatureMood.Happy);
        PlayerSpellOffer heal = new(10, "Heal", new ManaAmount(1000), true, OnCreature: true);
        PlayerDecision decision = player.Decide(Observe(3, map: LongStrip, things: [.. few, hurt], creatures: [.. fewCreatures, hurtFlower],
            gold: 0, tick: 1500, spells: [heal], mana: 5000));
        Assert.Contains(new PlayerAction.CastSpell(10, Target: Handle(9, 3)), decision.Actions);
    }

    [Fact]
    public void AStandThatPacesStaysOneStand()
    {
        // The attack walks from x 32 to 26 and back in steps of three: never a jump, so the siege still comes.
        CampaignPlayer player = Player();
        int[] path = [32, 29, 26, 29, 32];
        for (int i = 0; i < path.Length; i++)
        {
            var (army, armyCreatures) = Army(i < path.Length - 1 ? 12 : 7, new TilePos(path[i], 2), i + 1);
            player.Decide(Observe(i + 1, map: LongStrip, things: army, creatures: armyCreatures, gold: 0, tick: 100 + 350L * i));
        }
        var (few, fewCreatures) = Army(3, new TilePos(32, 2), 9);
        Assert.Contains("none goes alone", player.Decide(Observe(9, map: LongStrip, things: few, creatures: fewCreatures, gold: 0, tick: 1600))
            .Thoughts.Single(t => t.Routine == "hand").Note);
    }

    [Fact]
    public void ARaidNearHomeIsAlwaysAnsweredEvenWhileItCostsFighters()
    {
        var enemy = new TilePos(11, 2);   // nine tiles from home: within twice the home guard, beyond the fighters' reach
        CampaignPlayer player = Besieged(enemy, LongStrip);
        var (few, fewCreatures) = Army(3, enemy, 3);
        PlayerDecision answer = player.Decide(Observe(3, map: LongStrip, things: few, creatures: fewCreatures, gold: 0, tick: 1500));
        Assert.Contains(answer.Actions, a => a is PlayerAction.PickUp);
    }

    [Fact]
    public void WithRallyPartyZeroEveryAttackIsAnsweredAsBefore()
    {
        var enemy = new TilePos(32, 2);
        CampaignPlayer player = Player();
        player.Apply(player.Strategy with { RallyParty = 0 });
        var (start, startCreatures) = Army(12, enemy, 1);
        player.Decide(Observe(1, map: LongStrip, things: start, creatures: startCreatures, gold: 0, tick: 100));
        var (few, fewCreatures) = Army(3, enemy, 2);
        Assert.Contains(player.Decide(Observe(2, map: LongStrip, things: few, creatures: fewCreatures, gold: 0, tick: 1500)).Actions,
            a => a is PlayerAction.PickUp);
    }
}
