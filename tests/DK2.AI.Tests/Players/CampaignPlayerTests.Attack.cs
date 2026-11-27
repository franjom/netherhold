// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The attack (R20, R19, R15; Level 5 plan step 6, 2026-11-17): with an objective, the army goes once enough
/// fit fighters are free, or with any four once the gold is short of a few paydays; fearless creatures first.</summary>
public partial class CampaignPlayerTests
{
    private static readonly CreatureTypeId Skeleton = new CreatureTypeId(9);
    private static readonly TilePos Objective = new TilePos(9, 1);

    /// <summary>Fit fighters of <paramref name="types"/> along rows 7 and 8, each seen and in the creature panel at level 3.</summary>
    private static (PlayerVisibleThing[] Things, PlayerCreatureSummary[] Creatures) Army(params CreatureTypeId[] types)
    {
        var things = new PlayerVisibleThing[types.Length];
        var creatures = new PlayerCreatureSummary[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            var at = new TilePos(2 + i % 4, 7 + i / 4);
            things[i] = new PlayerVisibleThing(Handle(10 + i), PlayerThingKind.Creature, at, Me, PlayerThingActions.PickUp, types[i]);
            creatures[i] = new PlayerCreatureSummary(types[i], at, false, Handle(10 + i), Level: 3, Petals: 6);
        }
        return (things, creatures);
    }

    private static CampaignPlayer Attacker(int party = 4)
    {
        return new CampaignPlayer(new CampaignPlayerOptions(Run, Me, [Imp]) { Fearless = [Skeleton] })
            .Also(p => p.Apply(p.Strategy with { AttackAt = Objective, AttackParty = party, CreateImps = 0 }));
    }

    private static PlayerDecision Attack(CampaignPlayer player, int gold = 50_000, int wageBill = 0, params CreatureTypeId[] types)
    {
        (PlayerVisibleThing[] things, PlayerCreatureSummary[] creatures) = Army(types);
        return player.Decide(Observe(map: Dungeon, rooms: [], gold: gold, wageBill: wageBill, things: things, creatures: creatures));
    }

    private static string AttackNote(PlayerDecision decision)
    {
        return decision.Thoughts.Single(t => t.Routine == "hand").Note;
    }

    [Fact]
    public void TheArmyWaitsForItsPartyThenTheFearlessGoFirst()
    {
        PlayerDecision waiting = Attack(Attacker(party: 4), types: [Goblin, Goblin, Skeleton]);
        Assert.Empty(waiting.Actions.OfType<PlayerAction.PickUp>());

        PlayerDecision ready = Attack(Attacker(party: 4), types: [Goblin, Goblin, Skeleton, Goblin]);
        // The Skeleton alone: a Fear trap does not turn it (R15).
        Assert.Equal(new PlayerAction.PickUp(Handle(12)), Assert.Single(ready.Actions.OfType<PlayerAction.PickUp>()));
        Assert.Contains("the army is ready", AttackNote(ready));
    }

    [Fact]
    public void ShortGoldSendsAnyFourBeforeThePartyIsWhole()
    {
        // A bill of 5000: 12000 covers fewer than three paydays.
        PlayerDecision poor = Attack(Attacker(party: 10), gold: 12_000, wageBill: 5_000, types: [Goblin, Goblin, Goblin, Goblin]);
        Assert.Equal(4, poor.Actions.OfType<PlayerAction.PickUp>().Count());
        Assert.Contains("the gold is short", AttackNote(poor));
        Assert.Empty(Attack(Attacker(party: 10), gold: 16_000, wageBill: 5_000, types: [Goblin, Goblin, Goblin, Goblin])
            .Actions.OfType<PlayerAction.PickUp>());
    }

    [Fact]
    public void NoObjectiveNoAttack()
    {
        CampaignPlayer none = new CampaignPlayer(new CampaignPlayerOptions(Run, Me, [Imp])).Also(p => p.Apply(p.Strategy with { CreateImps = 0 }));
        Assert.Empty(Attack(none, types: [Goblin, Goblin, Goblin, Goblin, Goblin]).Actions.OfType<PlayerAction.PickUp>());
    }

    private static readonly PlayerSpellOffer Bolt = new PlayerSpellOffer(6, "Thunderbolt", new ManaAmount(1000), Affordable: true, OnCreature: true);

    [Fact]
    public void ThunderboltHitsTheStrongestEnemyInAFightOnOwnGroundWhileTheManaHolds()
    {
        PlayerVisibleThing Hero(int ordinal, TilePos at, int level)
        {
            return new PlayerVisibleThing(Handle(ordinal), PlayerThingKind.Creature, at, Heroes, PlayerThingActions.None, Goblin) { Level = level, Petals = 6 };
        }

        PlayerVisibleThing weak = Hero(3, new TilePos(4, 3), 2), strong = Hero(4, new TilePos(5, 3), 5), onRock = Hero(6, new TilePos(5, 1), 9);
        var fighter = new PlayerVisibleThing(Handle(5), PlayerThingKind.Creature, new TilePos(5, 4), Me, PlayerThingActions.None, Goblin);
        PlayerDecision Cast(int mana, params PlayerVisibleThing[] things)
        {
            return Player(o => o with { FightStyles = new Dictionary<CreatureTypeId, FightStyle>() }).Also(p => p.Apply(p.Strategy with { EnemyNearTiles = 0 }))
                .Decide(Observe(map: Dungeon, rooms: [], mana: mana, spells: [Bolt], things: things));
        }

        Assert.Contains(new PlayerAction.CastSpell(6, Target: Handle(4)), Cast(50_000, weak, strong, fighter).Actions);
        // The level 9 on rock is out of reach: Thunderbolt lands only on the caster's own ground.
        Assert.Contains(new PlayerAction.CastSpell(6, Target: Handle(4)), Cast(50_000, weak, strong, onRock, fighter).Actions);
        // No own creature near them: no fight, no strike.
        Assert.DoesNotContain(Cast(50_000, weak, strong).Actions, a => a is PlayerAction.CastSpell);
        // The reserve (15000) holds.
        Assert.DoesNotContain(Cast(15_500, weak, strong, fighter).Actions, a => a is PlayerAction.CastSpell);
    }

    [Fact]
    public void AnImpCarryingABodyIsNotLiftedForAnother()
    {
        // The Imp at (7,7) shares its tile with a body: it carries it. Only the free Imp at (2,8) goes.
        var carried = new PlayerVisibleThing(Handle(3), PlayerThingKind.Creature, new TilePos(7, 7), Heroes, PlayerThingActions.None, Goblin) { Level = 2, Petals = 1 };
        var free = new PlayerVisibleThing(Handle(4), PlayerThingKind.Creature, new TilePos(2, 8), Me, PlayerThingActions.PickUp, Imp);
        PlayerDecision decision = Player().Decide(Observe(map: Dungeon, rooms: [], owned: [OwnPrison],
            things: [Body(new TilePos(6, 6)), OwnImp(new TilePos(7, 7)), carried, free]));
        Assert.Equal(new PlayerAction.PickUp(Handle(4)), Assert.Single(decision.Actions.OfType<PlayerAction.PickUp>()));
    }
}
