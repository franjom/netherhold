// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>Recruiting (Level 5, 2026-11-17): a Portal that brings nobody while gold lies idle has its creatures' rooms
/// built or grown, the room that caps creatures with the fewest tiles first.</summary>
public partial class CampaignPlayerTests
{
    private static readonly KeeperKnowledge GuardFacts = new(
    [
        new RoomFacts(RoomTypeId.GuardRoom, 600, [new(3, 1, 9, 0), new(4, 2, 16, 0)], capsCreatures: true),
        new RoomFacts(RoomTypeId.Hatchery, 300, [new(3, 1, 9, 0)], capsCreatures: false),
    ]);

    private static PlayerObservation RecruitView(long tick, int gold = 50_000, bool portal = true, IReadOnlyList<PlayerCreatureSummary>? creatures = null) =>
        Observe(map: Open, rooms: [Offer(RoomTypeId.GuardRoom), Offer(RoomTypeId.Hatchery)], gold: gold, tick: tick, revision: tick,
            owned: portal ? CoreOwned(more: new PlayerRoomSummary(RoomTypeId.Portal, 9)) : CoreOwned(), creatures: creatures);

    private static CampaignPlayer Recruiter(Func<PlayerStrategy, PlayerStrategy>? tune = null) =>
        Player(o => o with { Knowledge = GuardFacts }).Also(p => p.Apply((tune ?? (s => s))(p.Strategy with { CreateImps = 0 })));

    [Fact]
    public void APortalThatBringsNobodyWhileGoldIdlesGetsTheRoomItsCreaturesNeed()
    {
        PlayerDecision decision = Recruiter().Decide(RecruitView(tick: 1300));
        Assert.NotNull(Built(decision, RoomTypeId.GuardRoom));
        Assert.Contains("recruiting: no new creature for 1300 ticks, GuardRoom had 0 tiles", decision.Thoughts.Single(t => t.Routine == "room").Note);
        // The Hatchery caps nobody: never a recruiting room.
        Assert.Null(Built(decision, RoomTypeId.Hatchery));
    }

    [Fact]
    public void RecruitingWaitsForTheGoldThePortalAndAQuietSpell()
    {
        Assert.Null(Built(Recruiter().Decide(RecruitView(tick: 1100)), RoomTypeId.GuardRoom));                 // too soon
        Assert.Null(Built(Recruiter().Decide(RecruitView(tick: 1300, gold: 19_000)), RoomTypeId.GuardRoom));   // gold not idle
        Assert.Null(Built(Recruiter().Decide(RecruitView(tick: 1300, portal: false)), RoomTypeId.GuardRoom));  // nothing to recruit by
        Assert.Null(Built(Recruiter(s => s with { RecruitGold = 0 }).Decide(RecruitView(tick: 1300)), RoomTypeId.GuardRoom));
    }

    [Fact]
    public void RecruitingBuildsOnlyWhileTheGoldPaysSixPaydays()
    {
        // A bill of 10000: 50000 pays five paydays, 61000 six (and leaves 49000 over the 12000 budget).
        PlayerObservation View(int gold) => Observe(map: Open, rooms: [Offer(RoomTypeId.GuardRoom)], gold: gold, tick: 1300, revision: 1300,
            owned: CoreOwned(more: new PlayerRoomSummary(RoomTypeId.Portal, 9)), wageBill: 10_000);
        Assert.Null(Built(Recruiter().Decide(View(50_000)), RoomTypeId.GuardRoom));
        Assert.NotNull(Built(Recruiter().Decide(View(61_000)), RoomTypeId.GuardRoom));
    }

    [Fact]
    public void TheCheaperRoomGoesFirstAndAFullSizedOneNoMore()
    {
        var facts = new KeeperKnowledge(
        [
            new RoomFacts(RoomTypeId.GuardRoom, 600, [new(3, 1, 9, 0)], capsCreatures: true),
            new RoomFacts(RoomTypeId.Temple, 3000, [new(3, 1, 9, 0)], capsCreatures: true),
        ]);
        CampaignPlayer Make() => Player(o => o with { Knowledge = facts }).Also(p => p.Apply(p.Strategy with { CreateImps = 0, ExtraRooms = [] }));
        PlayerRoomOffer guard = new(RoomTypeId.GuardRoom, new GoldAmount(600), true), temple = new(RoomTypeId.Temple, new GoldAmount(3000), true);
        PlayerObservation View(int guardTiles) => Observe(map: Open, rooms: [temple, guard], gold: 90_000, tick: 1300, revision: 1300,
            owned: CoreOwned(more: [new PlayerRoomSummary(RoomTypeId.Portal, 9), .. guardTiles > 0 ? [new PlayerRoomSummary(RoomTypeId.GuardRoom, guardTiles)] : Array.Empty<PlayerRoomSummary>()]));
        PlayerDecision first = Make().Decide(View(0));
        Assert.NotNull(Built(first, RoomTypeId.GuardRoom));
        Assert.Null(Built(first, RoomTypeId.Temple));
        // A Guard Room of 49 tiles grows no more: the Temple is next.
        Assert.NotNull(Built(Make().Decide(View(49)), RoomTypeId.Temple));
    }

    [Fact]
    public void ANewCreatureRestartsTheQuietSpell()
    {
        CampaignPlayer player = Recruiter();
        PlayerCreatureSummary goblin = new(Goblin, new TilePos(5, 5), false);
        player.Decide(RecruitView(tick: 1000, creatures: [goblin]));
        Assert.Null(Built(player.Decide(RecruitView(tick: 2100, creatures: [goblin])), RoomTypeId.GuardRoom));
        Assert.NotNull(Built(player.Decide(RecruitView(tick: 2200, creatures: [goblin])), RoomTypeId.GuardRoom));
    }
}
