// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The campaign bot's defence (defence plan; owner, 2026-11-15): it defends its whole dungeon, the attack
/// nearest the Heart first, and leaves enemies far from the dungeon alone.</summary>
public partial class CampaignPlayerTests
{
    /// <summary>Own floor round the heart at (2,2), a corridor along y 4, and a Treasury at x 17-19 far from it.</summary>
    private static readonly string[] DefenceMap =
    [
        "RRRRRRRRRRRRRRRRRRRRRRRR",
        "R....############TTT...R",
        "R.H..############TTT...R",
        "R....############TTT...R",
        "R......................R",
        "RRRRRRRRRRRRRRRRRRRRRRRR",
    ];

    private static PlayerVisibleThing Raider(int ordinal, TilePos at) =>
        new(Handle(ordinal), PlayerThingKind.Creature, at, Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));

    private static PlayerVisibleThing Defender(int ordinal, TilePos at) =>
        new(Handle(ordinal), PlayerThingKind.Creature, at, Me, PlayerThingActions.PickUp, creatureType: Goblin);

    private static PlayerObservation DefenceView(long revision, int hand = 0, params PlayerVisibleThing[] things) =>
        Observe(revision, map: DefenceMap, hand: hand, rooms: [], gold: 0, things: things);

    [Fact]
    public void AnAttackOnAFarRoomIsAnswered()
    {
        // Dwarves break into the Treasury, 19 tiles from the Heart: a defender is lifted and dropped there.
        CampaignPlayer player = Player();
        PlayerVisibleThing raider = Raider(0, new TilePos(21, 2));
        Assert.Contains(new PlayerAction.PickUp(Handle(1)), player.Decide(DefenceView(1, 0, raider, Defender(1, new TilePos(3, 3)))).Actions);
        TilePos drop = Assert.Single(player.Decide(DefenceView(2, 1, raider)).Actions.OfType<PlayerAction.DropAt>()).Tile;
        Assert.True(drop.X >= 17, $"dropped at {drop}, not at the attacked room");
    }

    [Theory]
    [InlineData(12, true)]    // 8 tiles from the nearest own floor (x 4): an attack
    [InlineData(13, false)]   // 9 tiles: not
    public void AnAttackIsAnEnemyWithinEightTilesOfOwnFloor(int x, bool answered)
    {
        string[] map =
        [
            "RRRRRRRRRRRRRRRRRRRRRRRRRRRRRR",
            "R....########################R",
            "R.H..########################R",
            "R....########################R",
            "RRRRRRRRRRRRRRRRRRRRRRRRRRRRRR",
        ];
        PlayerObservation view = Observe(1, map: map, rooms: [], gold: 0, things: [Raider(0, new TilePos(x, 2))]);
        Assert.Equal(answered, PlayerActionRoutines.NearestAttacker(view, new TilePos(2, 2), 8) is not null);
    }

    [Fact]
    public void AHurtDefenderTakenOutOfTheFightGoesToTheHatcheryEvenWithTheFightNearIt()
    {
        // The Hatchery at x 17-19 is 5 tiles from the raider at (12,4): it is not sent back to the front.
        string[] map = [.. DefenceMap.Select(r => r.Replace("TTT", "AAA"))];
        PlayerVisibleThing raider = Raider(0, new TilePos(12, 4)), tired = Own(1, new TilePos(11, 4), Goblin);
        PlayerRoomSummary[] owned = [new(RoomTypeId.Hatchery, 9)];
        CampaignPlayer player = Styled();
        Assert.Contains(new PlayerAction.PickUp(tired.Handle), player.Decide(Observe(1, map: map, rooms: [], owned: owned, gold: 0,
            things: [raider, tired], creatures: [Flower(tired, 2)])).Actions);
        TilePos drop = Assert.Single(player.Decide(Observe(2, map: map, hand: 1, rooms: [], owned: owned, gold: 0, things: [raider]))
            .Actions.OfType<PlayerAction.DropAt>()).Tile;
        Assert.InRange(drop.X, 17, 19);
        // Healed when the mana allows (owner, 2026-11-15, Q849); with Heal offered but too dear, to the Hatchery all the same.
        Assert.Contains(new PlayerAction.CastSpell(10, Target: tired.Handle), Styled().Decide(Observe(1, map: map, rooms: [], owned: owned, gold: 0,
            things: [raider, tired], creatures: [Flower(tired, 2)], spells: [Heal])).Actions);
        Assert.Contains(new PlayerAction.PickUp(tired.Handle), Styled().Decide(Observe(1, map: map, rooms: [], owned: owned, gold: 0,
            things: [raider, tired], creatures: [Flower(tired, 2)], spells: [Heal with { Affordable = false }])).Actions);
        // With no Hatchery it is not lifted at all.
        Assert.DoesNotContain(new PlayerAction.PickUp(tired.Handle), Styled().Decide(Observe(1, map: DefenceMap, rooms: [], gold: 0,
            things: [raider, tired], creatures: [Flower(tired, 2)])).Actions);
    }

    [Fact]
    public void AnEnemyFarFromTheWholeDungeonIsLeftAlone()
    {
        // In sight, but more than 8 tiles from every own tile (a guard in its own Prison across the map).
        PlayerDecision decision = Player().Decide(DefenceView(1, 0, Raider(0, new TilePos(40, 2)), Defender(1, new TilePos(3, 3))));
        Assert.DoesNotContain(decision.Actions, a => a is PlayerAction.PickUp);
    }

    [Fact]
    public void TheAttackNearestTheHeartIsAnsweredFirst()
    {
        CampaignPlayer player = Player();
        PlayerVisibleThing far = Raider(0, new TilePos(21, 2)), near = Raider(2, new TilePos(8, 4));
        player.Decide(DefenceView(1, 0, far, near, Defender(1, new TilePos(1, 1))));
        TilePos drop = Assert.Single(player.Decide(DefenceView(2, 1, far, near)).Actions.OfType<PlayerAction.DropAt>()).Tile;
        Assert.True(Math.Abs(drop.X - 8) <= 2, $"dropped at {drop}, not at the attack nearest the Heart");
    }

    private static readonly CreatureTypeId Flier = new(8), Healer = new(9);

    private static CampaignPlayer Styled() => Player(o => o with
    {
        FightStyles = new Dictionary<CreatureTypeId, FightStyle>
            { [Goblin] = FightStyle.Blocker, [Flier] = FightStyle.Flanker, [Healer] = FightStyle.Support },
    });

    private static PlayerVisibleThing Own(int ordinal, TilePos at, CreatureTypeId type) =>
        new(Handle(ordinal), PlayerThingKind.Creature, at, Me, PlayerThingActions.PickUp, creatureType: type);

    private static PlayerCreatureSummary Flower(PlayerVisibleThing own, int petals) =>
        new(own.CreatureType!.Value, own.Tile, false, own.Handle, 3, petals, PlayerCreatureMood.Happy);

    [Fact]
    public void TheFormationPutsBlockersInFrontSupportFurtherBackAndFlankersBehind()
    {
        TilePos enemy = new(10, 2), home = new(2, 2);
        Assert.Equal(new TilePos(9, 2), PlayerActionRoutines.FormationPoint(enemy, home, FightStyle.Blocker));   // 1.5 in front, halves away from zero
        Assert.Equal(new TilePos(7, 2), PlayerActionRoutines.FormationPoint(enemy, home, FightStyle.Support));   // 3.5 in front: 2 behind the Blockers
        Assert.Equal(new TilePos(12, 2), PlayerActionRoutines.FormationPoint(enemy, home, FightStyle.Flanker));  // 1.5 behind
        Assert.Equal(new TilePos(12, 2), PlayerActionRoutines.FormationPoint(enemy, home, FightStyle.Blitzer));
    }

    [Fact]
    public void BlockersAreSentFirstAndAHurtOrNonFightingCreatureNever()
    {
        PlayerVisibleThing raider = Raider(0, new TilePos(12, 4));
        PlayerVisibleThing flier = Own(1, new TilePos(2, 1), Flier), goblin = Own(2, new TilePos(4, 4), Goblin);
        PlayerObservation View(params PlayerCreatureSummary[] flowers) =>
            Observe(1, map: DefenceMap, rooms: [], gold: 0, things: [raider, flier, goblin], creatures: flowers);
        Assert.Contains(new PlayerAction.PickUp(goblin.Handle), Styled().Decide(View(Flower(flier, 6), Flower(goblin, 6))).Actions);
        // The Blocker is hurt (four petals): the Flanker goes.
        Assert.Contains(new PlayerAction.PickUp(flier.Handle), Styled().Decide(View(Flower(flier, 6), Flower(goblin, 4))).Actions);
        // A type with no fighting style listed (a NonFighter) is never sent.
        CampaignPlayer flierOnly = Player(o => o with { FightStyles = new Dictionary<CreatureTypeId, FightStyle> { [Flier] = FightStyle.NonFighter } });
        Assert.DoesNotContain(flierOnly.Decide(View()).Actions, a => a is PlayerAction.PickUp);
        // With no style data at all, every non-worker is a Blocker.
        Assert.Equal(FightStyle.Blocker, new CampaignPlayerOptions(Run, Me, [Imp]).StyleOf(Goblin));
    }

    [Theory]
    [InlineData(7, 11, 4)]   // Blocker (Goblin): in front of the raider, toward the Heart
    [InlineData(8, 13, 4)]   // Flanker: behind it
    [InlineData(9, 9, 4)]    // Support: two tiles behind the Blockers
    public void EachRoleGoesDownInItsPlaceInTheFormation(int type, int x, int y)
    {
        PlayerVisibleThing raider = Raider(0, new TilePos(12, 4)), own = Own(1, new TilePos(2, 1), new CreatureTypeId((byte)type));
        CampaignPlayer player = Styled();
        Assert.Contains(new PlayerAction.PickUp(own.Handle), player.Decide(Observe(1, map: DefenceMap, rooms: [], gold: 0, things: [raider, own])).Actions);
        Assert.Equal(new TilePos(x, y), Assert.Single(player.Decide(Observe(2, map: DefenceMap, hand: 1, rooms: [], gold: 0, things: [raider]))
            .Actions.OfType<PlayerAction.DropAt>()).Tile);
    }

    [Fact]
    public void AFlankerGoesBesideTheBlockersWhenTheGroundBehindIsNotOwn()
    {
        // Hero ground from x 13 on: the hand cannot put the Flanker behind the raider at (12,4).
        string[] map = [.. DefenceMap.Select(r => r[..13] + new string('e', r.Length - 14) + r[^1..])];
        PlayerVisibleThing raider = Raider(0, new TilePos(12, 4)), flier = Own(1, new TilePos(2, 1), Flier);
        CampaignPlayer player = Styled();
        player.Decide(Observe(1, map: map, rooms: [], gold: 0, things: [raider, flier]));
        Assert.Equal(new TilePos(11, 4), Assert.Single(player.Decide(Observe(2, map: map, hand: 1, rooms: [], gold: 0, things: [raider]))
            .Actions.OfType<PlayerAction.DropAt>()).Tile);
    }

    [Fact]
    public void NoMoreAreSentOnceTheDefendersOutnumberTheAttackersAndAHurtOneIsHealed()
    {
        PlayerVisibleThing raider = Raider(0, new TilePos(12, 4));
        PlayerVisibleThing a = Own(1, new TilePos(11, 4), Goblin), b = Own(2, new TilePos(13, 4), Goblin), spare = Own(3, new TilePos(2, 1), Goblin);
        Assert.DoesNotContain(Styled().Decide(Observe(1, map: DefenceMap, rooms: [], gold: 0, things: [raider, a, b, spare],
            creatures: [Flower(a, 6), Flower(b, 6), Flower(spare, 6)])).Actions, x => x is PlayerAction.PickUp);
        // One alone at the fight: another is sent.
        Assert.Contains(new PlayerAction.PickUp(spare.Handle), Styled().Decide(Observe(1, map: DefenceMap, rooms: [], gold: 0,
            things: [raider, a, spare], creatures: [Flower(a, 6), Flower(spare, 6)])).Actions);
        // A hurt defender (two petals) does not count: with one healthy and one hurt against one raider, a spare goes.
        PlayerVisibleThing hurt = Own(4, new TilePos(13, 4), Goblin);
        Assert.Contains(new PlayerAction.PickUp(spare.Handle), Styled().Decide(Observe(1, map: DefenceMap, rooms: [], gold: 0,
            things: [raider, a, hurt, spare], creatures: [Flower(a, 6), Flower(hurt, 2), Flower(spare, 6)])).Actions);
        // A defender at two petals is healed first.
        Assert.Contains(new PlayerAction.CastSpell(10, Target: a.Handle), Styled().Decide(Observe(1, map: DefenceMap, rooms: [], gold: 0,
            things: [raider, a, spare], creatures: [Flower(a, 2), Flower(spare, 6)], spells: [Heal])).Actions);
    }
}
