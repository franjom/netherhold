// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The Combat Pit routine (campaign bot stage 3 plan, Task 4; owner direction): fighters in, a hurt one
/// healed at two petals or fed, one at the cap out to its Lair or a Hatchery.</summary>
public partial class CampaignPlayerTests
{
    /// <summary>A 4x4 Combat Pit at (2..5, 2..5) whose interior is (3..4, 3..4); a Hatchery at (8..9, 2..3), a Lair
    /// at (8..9, 8..9); plain own floor elsewhere; the heart at (6,6).</summary>
    private static readonly string[] PitMap =
    [
        "RRRRRRRRRRRR",
        "R..........R",
        "R.CCCC..AA.R",
        "R.CCCC..AA.R",
        "R.CCCC.....R",
        "R.CCCC.....R",
        "R.....H....R",
        "R..........R",
        "R.......LL.R",
        "R.......LL.R",
        "R..........R",
        "RRRRRRRRRRRR",
    ];

    private static readonly PlayerRoomSummary[] PitRooms =
        [new(RoomTypeId.CombatPit, 16), new(RoomTypeId.Hatchery, 4), new(RoomTypeId.Lair, 4)];

    private static readonly PlayerSpellOffer Heal = new(10, "Heal", new ManaAmount(1000), true, OnCreature: true);

    /// <summary>One own Goblin at <paramref name="at"/> with the given flower.</summary>
    private static (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) Goblin1(TilePos at, int level, int petals,
        PlayerCreatureMood mood = PlayerCreatureMood.Happy, int ordinal = 0) =>
        (new PlayerVisibleThing(Handle(ordinal), PlayerThingKind.Creature, at, Me, PlayerThingActions.PickUp | PlayerThingActions.Slap, creatureType: Goblin),
         new PlayerCreatureSummary(Goblin, at, false, Handle(ordinal), level, petals, mood));

    private static PlayerObservation PitView((PlayerVisibleThing Thing, PlayerCreatureSummary Flower)[] creatures, int hand = 0,
        long revision = 1, IReadOnlyList<PlayerSpellOffer>? spells = null) =>
        Observe(revision, map: PitMap, hand: hand, rooms: [], owned: PitRooms, tick: revision * 1000,
            things: [.. creatures.Select(c => c.Thing)], creatures: [.. creatures.Select(c => c.Flower)], spells: spells);

    private static CampaignPlayer PitPlayer(int fighters = 2) =>
        Player(o => o with { CombatPitMaxLevel = 8 }).Also(p => p.Apply(p.Strategy with { PitFighters = fighters, Dig = DigMode.Off }));

    [Fact]
    public void AnIdleHealthyFighterIsLiftedOntoThePitsInterior()
    {
        CampaignPlayer player = PitPlayer();
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) idle = Goblin1(new TilePos(9, 6), level: 3, petals: 6, ordinal: 0);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) partner = Goblin1(new TilePos(10, 6), level: 4, petals: 6, ordinal: 1);      // two can pair
        PlayerDecision lift = player.Decide(PitView([idle, partner]));
        Assert.Contains(new PlayerAction.PickUp(idle.Thing.Handle), lift.Actions);       // the lower level first
        Assert.Contains("pit", lift.Reason, StringComparison.Ordinal);

        PlayerDecision drop = player.Decide(PitView([], hand: 1, revision: 2));
        PlayerAction.DropAt at = Assert.Single(drop.Actions.OfType<PlayerAction.DropAt>());
        Assert.Equal(new TilePos(4, 4), at.Tile);                                   // the interior tile nearest the heart
    }

    [Theory]
    [InlineData(4, PlayerCreatureMood.Happy, false)]     // hurt: not sent in
    [InlineData(6, PlayerCreatureMood.Angry, false)]     // angry: not sent in
    [InlineData(6, PlayerCreatureMood.Unhappy, false)]   // happy first (owner, 2026-11-15)
    [InlineData(6, PlayerCreatureMood.Happy, true)]
    public void OnlyAHealthyHappyCreatureBelowTheCapIsSentIn(int petals, PlayerCreatureMood mood, bool lifted)
    {
        // A fighter already on the floor waits for a partner, so one candidate is enough.
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) waiting = Goblin1(new TilePos(3, 3), level: 3, petals: 6, ordinal: 1);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) idle = Goblin1(new TilePos(9, 6), level: 3, petals, mood, ordinal: 0);
        Assert.Equal(lifted, PitPlayer().Decide(PitView([waiting, idle])).Actions.Contains(new PlayerAction.PickUp(idle.Thing.Handle)));
        Assert.DoesNotContain(new PlayerAction.PickUp(idle.Thing.Handle),
            PitPlayer().Decide(PitView([waiting, Goblin1(new TilePos(9, 6), level: 8, petals: 6)])).Actions);
    }

    [Fact]
    public void ALoneCandidateIsNotSentInAndALoneVictorGoesToItsLair()
    {
        // One idle creature and an empty floor: nobody to fight, so it stays where it is.
        Assert.Empty(PitPlayer().Decide(PitView([Goblin1(new TilePos(9, 6), level: 3, petals: 6)])).Actions.OfType<PlayerAction.PickUp>());

        // Two were fighting; one is left: the victor goes to its Lair before it grows unhappy (owner, 2026-11-15).
        CampaignPlayer player = PitPlayer();
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) a = Goblin1(new TilePos(3, 3), level: 3, petals: 6, ordinal: 0);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) b = Goblin1(new TilePos(4, 4), level: 3, petals: 6, ordinal: 1);
        Assert.Empty(player.Decide(PitView([a, b])).Actions.OfType<PlayerAction.PickUp>());
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) spare = Goblin1(new TilePos(9, 6), level: 2, petals: 6, ordinal: 2);
        Assert.Contains(new PlayerAction.PickUp(a.Thing.Handle), player.Decide(PitView([a, spare], revision: 2)).Actions);
        Assert.Equal(new TilePos(8, 8), Assert.Single(player.Decide(PitView([], hand: 1, revision: 3)).Actions.OfType<PlayerAction.DropAt>()).Tile);

        // A fighter alone on the floor with no one idle to join it goes to its Lair too.
        Assert.Contains(new PlayerAction.PickUp(a.Thing.Handle), PitPlayer().Decide(PitView([a])).Actions);
    }

    [Fact]
    public void AFullPitTakesNoMoreAndTheRoutineIsOffByDefault()
    {
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) a = Goblin1(new TilePos(3, 3), level: 3, petals: 6, ordinal: 0);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) b = Goblin1(new TilePos(4, 4), level: 3, petals: 6, ordinal: 1);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) idle = Goblin1(new TilePos(9, 6), level: 3, petals: 6, ordinal: 2);
        Assert.DoesNotContain(new PlayerAction.PickUp(idle.Thing.Handle), PitPlayer(fighters: 2).Decide(PitView([a, b, idle])).Actions);
        Assert.Contains(new PlayerAction.PickUp(idle.Thing.Handle), PitPlayer(fighters: 3).Decide(PitView([a, b, idle])).Actions);
        Assert.Equal(0, new PlayerStrategy().PitFighters);
        Assert.DoesNotContain(new PlayerAction.PickUp(idle.Thing.Handle), Player().Decide(PitView([idle])).Actions);
    }

    [Fact]
    public void AFighterAtTwoPetalsIsHealedWhenHealIsReadyElseFed()
    {
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) hurt = Goblin1(new TilePos(3, 3), level: 4, petals: 2);
        PlayerDecision healed = PitPlayer().Decide(PitView([hurt], spells: [Heal]));
        Assert.Contains(new PlayerAction.CastSpell(10, Target: hurt.Thing.Handle), healed.Actions);

        foreach (PlayerSpellOffer? offer in (PlayerSpellOffer?[])[null, Heal with { Affordable = false }, Heal with { Ready = false }])
        {
            CampaignPlayer player = PitPlayer();
            PlayerDecision lifted = player.Decide(PitView([hurt], spells: offer is PlayerSpellOffer o ? [o] : null));
            Assert.Contains(new PlayerAction.PickUp(hurt.Thing.Handle), lifted.Actions);
            Assert.Equal(new TilePos(8, 3), Assert.Single(player.Decide(PitView([], hand: 1, revision: 2)).Actions.OfType<PlayerAction.DropAt>()).Tile);   // the Hatchery tile nearest the heart
        }

    }

    [Fact]
    public void ABeatenFighterGetsAnImpDroppedOnItsBodyToCarryItToBed()
    {
        // One petal: collapsed on the Pit's rim (owner, 2026-11-15). No heal lands on it (Q823) and the hand does not lift it.
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) body = Goblin1(new TilePos(2, 4), level: 4, petals: 1, ordinal: 0);
        PlayerVisibleThing far = new(Handle(1), PlayerThingKind.Creature, new TilePos(10, 10), Me, PlayerThingActions.PickUp, creatureType: Imp);
        PlayerVisibleThing near = new(Handle(2), PlayerThingKind.Creature, new TilePos(7, 4), Me, PlayerThingActions.PickUp, creatureType: Imp);
        PlayerObservation view = Observe(1, map: PitMap, rooms: [], owned: PitRooms, tick: 1000, spells: [Heal],
            things: [body.Thing, far, near], creatures: [body.Flower]);
        CampaignPlayer player = PitPlayer();
        PlayerDecision send = player.Decide(view);
        Assert.Contains(new PlayerAction.PickUp(near.Handle), send.Actions);                       // the nearest Imp
        Assert.DoesNotContain(send.Actions, a => a is PlayerAction.CastSpell);
        Assert.Equal(body.Thing.Tile, Assert.Single(player.Decide(Observe(2, map: PitMap, hand: 1, rooms: [], owned: PitRooms, tick: 1100))
            .Actions.OfType<PlayerAction.DropAt>()).Tile);                                           // the body lies on the rim: beside it is its own tile

        // Sent once: within the rescue window no second Imp goes to the same body; after it, one does.
        PlayerObservation Later(long revision, long tick) => Observe(revision, map: PitMap, rooms: [], owned: PitRooms, tick: tick,
            things: [body.Thing, far, near], creatures: [body.Flower]);
        Assert.Empty(player.Decide(Later(3, 1300)).Actions.OfType<PlayerAction.PickUp>());
        Assert.Contains(new PlayerAction.PickUp(near.Handle), player.Decide(Later(4, 1500)).Actions);

        // Without a Lair there is no bed to carry it to: no Imp is sent, and the rest of the routine goes on.
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) idle = Goblin1(new TilePos(9, 6), level: 3, petals: 6, ordinal: 3);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) partner = Goblin1(new TilePos(10, 6), level: 4, petals: 6, ordinal: 4);
        PlayerDecision noBed = PitPlayer().Decide(Observe(1, map: PitMap, rooms: [], owned: [new(RoomTypeId.CombatPit, 16)], tick: 1000,
            things: [body.Thing, near, idle.Thing, partner.Thing], creatures: [body.Flower, idle.Flower, partner.Flower]));
        Assert.Equal([new PlayerAction.PickUp(idle.Thing.Handle)], noBed.Actions.OfType<PlayerAction.PickUp>());
    }

    [Fact]
    public void ABodyStillOnThePitsFloorIsLeftToRiseOnTheRimThenFetchedAfterTheEstimated40TicksBesideItWhereTheHandMayDropOne()
    {
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) body = Goblin1(new TilePos(3, 3), level: 4, petals: 1, ordinal: 0);                   // interior: a worker drop is refused there
        PlayerVisibleThing imp = new(Handle(1), PlayerThingKind.Creature, new TilePos(9, 9), Me, PlayerThingActions.PickUp, creatureType: Imp);
        CampaignPlayer player = PitPlayer();
        PlayerObservation Sinking(long revision, long tick) => Observe(revision, map: PitMap, rooms: [], owned: PitRooms, tick: tick,
            things: [body.Thing, imp], creatures: [body.Flower]);
        // It sinks and rises on the rim within seconds (state 217): no Imp yet, decision after decision.
        for (int i = 0; i < 10; i++)
            Assert.Empty(player.Decide(Sinking(1 + i, 1000 + 4 * i)).Actions.OfType<PlayerAction.PickUp>());
        // Still there after 40 ticks: fetched where it lies.
        Assert.Contains(new PlayerAction.PickUp(imp.Handle), player.Decide(Sinking(11, 1040)).Actions);
        TilePos drop = Assert.Single(player.Decide(PitView([], hand: 1, revision: 12)).Actions.OfType<PlayerAction.DropAt>()).Tile;

        // Unseen for a while (the routine did not run), it is timed afresh: no Imp at once.
        CampaignPlayer later = PitPlayer();
        later.Decide(Sinking(1, 1000));
        Assert.Empty(later.Decide(Sinking(2, 1100)).Actions.OfType<PlayerAction.PickUp>());
        Assert.Equal(1, Math.Abs(drop.X - 3) + Math.Abs(drop.Y - 3));                                // the rim beside it
        Assert.False(PlayerActionRoutines.IsInterior(Map(PitMap), drop, RoomTypeId.CombatPit, Me));
    }

    [Fact]
    public void ARescueDoesNotWaitForThePitsInterval()
    {
        // The routine has just sent a fighter in; a body rising on the rim a moment later is fetched at once, not after
        // the 200-tick interval (it dies within 60 s, 240 ticks).
        CampaignPlayer player = PitPlayer();
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) idle = Goblin1(new TilePos(9, 6), level: 3, petals: 6, ordinal: 0);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) partner = Goblin1(new TilePos(10, 6), level: 4, petals: 6, ordinal: 1);
        Assert.Contains(new PlayerAction.PickUp(idle.Thing.Handle), player.Decide(PitView([idle, partner])).Actions);
        player.Decide(Observe(2, map: PitMap, hand: 1, rooms: [], owned: PitRooms, tick: 1004,
            things: [partner.Thing], creatures: [partner.Flower]));
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) body = Goblin1(new TilePos(2, 4), level: 4, petals: 1, ordinal: 2);                   // the rim
        PlayerVisibleThing imp = new(Handle(3), PlayerThingKind.Creature, new TilePos(7, 4), Me, PlayerThingActions.PickUp, creatureType: Imp);
        PlayerObservation risen = Observe(3, map: PitMap, rooms: [], owned: PitRooms, tick: 1010,
            things: [body.Thing, imp, partner.Thing], creatures: [body.Flower, partner.Flower]);
        Assert.Contains(new PlayerAction.PickUp(imp.Handle), player.Decide(risen).Actions);
    }

    [Fact]
    public void TheImpNearestTheBodyIsSentWhereverItIsListed()
    {
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) body = Goblin1(new TilePos(2, 4), level: 4, petals: 1, ordinal: 0);                   // the rim
        PlayerVisibleThing near = new(Handle(1), PlayerThingKind.Creature, new TilePos(1, 6), Me, PlayerThingActions.PickUp, creatureType: Imp);
        PlayerVisibleThing far = new(Handle(2), PlayerThingKind.Creature, new TilePos(10, 10), Me, PlayerThingActions.PickUp, creatureType: Imp);
        foreach (PlayerVisibleThing[] order in (PlayerVisibleThing[][])[[near, far, body.Thing], [body.Thing, far, near], [near, body.Thing, far]])
            Assert.Contains(new PlayerAction.PickUp(near.Handle), PitPlayer().Decide(Observe(1, map: PitMap, rooms: [], owned: PitRooms, tick: 1000,
                things: order, creatures: [body.Flower])).Actions);
    }

    [Fact]
    public void ABodyBeingCarriedDrawsNoSecondImp()
    {
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) body = Goblin1(new TilePos(2, 4), level: 4, petals: 1, ordinal: 0);                   // the rim
        PlayerVisibleThing imp = new(Handle(1), PlayerThingKind.Creature, new TilePos(7, 4), Me, PlayerThingActions.PickUp, creatureType: Imp);
        CampaignPlayer player = PitPlayer();
        Assert.Contains(new PlayerAction.PickUp(imp.Handle), player.Decide(Observe(1, map: PitMap, rooms: [], owned: PitRooms, tick: 1000,
            things: [body.Thing, imp], creatures: [body.Flower])).Actions);
        player.Decide(Observe(2, map: PitMap, hand: 1, rooms: [], owned: PitRooms, tick: 1004));
        // The Imp carries it along the rim: the body is on the carrier's tile, a new tile each step.
        PlayerVisibleThing other = new(Handle(2), PlayerThingKind.Creature, new TilePos(9, 9), Me, PlayerThingActions.PickUp, creatureType: Imp);
        foreach ((int step, TilePos at) in (ReadOnlySpan<(int, TilePos)>)[(3, new TilePos(2, 3)), (4, new TilePos(2, 2))])
        {
            (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) carried = Goblin1(at, level: 4, petals: 1, ordinal: 0);
            PlayerVisibleThing carrier = new(Handle(1), PlayerThingKind.Creature, at, Me, PlayerThingActions.PickUp, creatureType: Imp);
            Assert.Empty(player.Decide(Observe(step, map: PitMap, rooms: [], owned: PitRooms, tick: 1040 + 4 * step,
                things: [carried.Thing, carrier, other], creatures: [carried.Flower])).Actions.OfType<PlayerAction.PickUp>());
        }

        // A body on a new tile soon after a rescue waits out the 40-tick gap, then gets its Imp.
        CampaignPlayer gap = PitPlayer();
        gap.Decide(Observe(1, map: PitMap, rooms: [], owned: PitRooms, tick: 1000, things: [body.Thing, imp], creatures: [body.Flower]));
        gap.Decide(Observe(2, map: PitMap, hand: 1, rooms: [], owned: PitRooms, tick: 1004));
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) moved = Goblin1(new TilePos(2, 2), level: 4, petals: 1, ordinal: 0);
        PlayerObservation Moved(long revision, long tick) => Observe(revision, map: PitMap, rooms: [], owned: PitRooms, tick: tick,
            things: [moved.Thing, other], creatures: [moved.Flower]);
        Assert.Empty(gap.Decide(Moved(3, 1020)).Actions.OfType<PlayerAction.PickUp>());
        Assert.Contains(new PlayerAction.PickUp(other.Handle), gap.Decide(Moved(4, 1040)).Actions);
    }

    [Fact]
    public void ALoneVictorIsLiftedAtTheRoutinesRealCadence()
    {
        // The routine sends a partner in at tick 1000; the pair's whole fight falls inside its 200-tick interval, and the
        // victor is left alone at 1100 with another recruit standing by. Decisions every 4 ticks.
        CampaignPlayer player = PitPlayer();
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) a = Goblin1(new TilePos(3, 3), level: 3, petals: 6, ordinal: 0);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) b = Goblin1(new TilePos(9, 6), level: 3, petals: 6, ordinal: 1);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) c = Goblin1(new TilePos(10, 6), level: 4, petals: 6, ordinal: 2);
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) bIn = Goblin1(new TilePos(4, 4), level: 3, petals: 6, ordinal: 1);
        PlayerObservation View(long tick, int hand, params (PlayerVisibleThing Thing, PlayerCreatureSummary Flower)[] cs) =>
            Observe(tick, map: PitMap, hand: hand, rooms: [], owned: PitRooms, tick: tick,
                things: [.. cs.Select(x => x.Thing)], creatures: [.. cs.Select(x => x.Flower)]);
        Assert.Contains(new PlayerAction.PickUp(b.Thing.Handle), player.Decide(View(1000, 0, a, b, c)).Actions);
        player.Decide(View(1004, 1, a, c));
        for (long t = 1008; t < 1100; t += 4) player.Decide(View(t, 0, a, bIn, c));
        bool lifted = false;
        for (long t = 1100; t < 1400 && !lifted; t += 4)
            lifted = player.Decide(View(t, 0, a, c)).Actions.Contains(new PlayerAction.PickUp(a.Thing.Handle));
        Assert.True(lifted, "the lone victor was never lifted");
    }

    [Fact]
    public void ACreatureWatchingFromTheRimIsNoFighterAndMayBeSentIn()
    {
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) watching = Goblin1(new TilePos(2, 3), level: 3, petals: 6, ordinal: 0);              // the rim
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) fighter = Goblin1(new TilePos(3, 3), level: 3, petals: 6, ordinal: 1);               // the floor
        CampaignPlayer player = PitPlayer(fighters: 2);
        Assert.Contains(new PlayerAction.PickUp(watching.Thing.Handle), player.Decide(PitView([watching, fighter])).Actions);
        Assert.Equal(new TilePos(4, 4), Assert.Single(player.Decide(PitView([fighter], hand: 1, revision: 2)).Actions.OfType<PlayerAction.DropAt>()).Tile);
        // A capped creature watching from the rim is left alone.
        Assert.Empty(PitPlayer().Decide(PitView([Goblin1(new TilePos(2, 3), level: 8, petals: 6)])).Actions.OfType<PlayerAction.PickUp>());
    }

    [Theory]
    [InlineData(6, PlayerCreatureMood.Happy, 8, 8)]     // whole and content: to its Lair
    [InlineData(4, PlayerCreatureMood.Happy, 8, 3)]     // hurt and content: to a Hatchery
    [InlineData(4, PlayerCreatureMood.Unhappy, 8, 8)]   // unhappy goes to its Lair (owner, 2026-11-15)
    [InlineData(6, PlayerCreatureMood.Angry, 8, 8)]
    public void AFighterAtTheCapLeavesForItsLairOrAHatchery(int petals, PlayerCreatureMood mood, int x, int y)
    {
        CampaignPlayer player = PitPlayer();
        (PlayerVisibleThing Thing, PlayerCreatureSummary Flower) capped = Goblin1(new TilePos(3, 3), level: 8, petals, mood);
        Assert.Contains(new PlayerAction.PickUp(capped.Thing.Handle), player.Decide(PitView([capped])).Actions);
        Assert.Equal(new TilePos(x, y), Assert.Single(player.Decide(PitView([], hand: 1, revision: 2)).Actions.OfType<PlayerAction.DropAt>()).Tile);
    }
}

internal static class PitTestExtensions
{
    public static T Also<T>(this T value, Action<T> act)
    {
        act(value);
        return value;
    }
}
