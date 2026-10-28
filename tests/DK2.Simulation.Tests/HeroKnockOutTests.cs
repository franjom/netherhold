// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A hero party's knock-out re-lead, <c>0x004B2FBA</c>–<c>0x004B30F3</c> (hero-party-walk.md §5).</summary>
public class HeroKnockOutTests
{
    private const byte Knight = Dk2TerrainFixture.KnightCreatureId;
    private const byte Party = 3;

    [Fact]
    public void AFallenLeadersFirstFollowerLeads_WithItsJobAndTrigger_AndTheRestAreRenumbered()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = Member(db, world, 0x02, 3);
        leader.GoodJob = JobType.SendToActionPoint;
        leader.GoodJobPlayerId = PlayerIds.FirstKeeper;
        leader.GoodJobActionPointId = 7;
        leader.TriggerId = 41;
        CreatureState heir = Member(db, world, 0x04, 4);
        heir.GoodJob = JobType.Wait;
        CreatureState second = Member(db, world, 0x04, 5);
        CreatureState third = Member(db, world, 0x04, 6);
        HeroPartyRules.LinkFollowers(world, Party);
        CreatureStateMachine.TrySetState(new StateHookContext(world, db), heir, HeroJobRules.FollowingState);

        KnockOut(db, world, leader);

        Assert.Equal(0x02, heir.Flags & 0x06);
        Assert.Equal(JobType.SendToActionPoint, heir.GoodJob);
        Assert.Equal(PlayerIds.FirstKeeper, heir.GoodJobPlayerId);
        Assert.Equal(7, heir.GoodJobActionPointId);
        Assert.Equal(41, heir.TriggerId);
        Assert.Equal(CreatureStateMachine.Idle, heir.Dk2StateId);
        Assert.Equal(2, heir.PositionInParty);
        Assert.Equal(leader.Id, heir.LeaderId);   // DK2 never rewrites the heir's own MyLeadersId
        Assert.Equal((heir.Id, 1), (second.LeaderId, (int)second.PositionInParty));
        Assert.Equal((heir.Id, 2), (third.LeaderId, (int)third.PositionInParty));
        // The fallen leader is not detached.
        Assert.Equal((Party, 0x02), (leader.PartyId, leader.Flags & 0x06));
    }

    /// <summary><c>0x0049A950</c> snapshots the root's repeat byte beside the id; the heir held no root.</summary>
    [Fact]
    public void TheHeirsInheritedRootCarriesItsRecordsRepeatByte()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = Member(db, world, 0x02, 3);
        leader.TriggerId = 41;
        CreatureState heir = Member(db, world, 0x04, 4);
        HeroPartyRules.LinkFollowers(world, Party);
        TriggerRecord root = new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 41, Type = 72, RepeatCount = 7, Parameters = new Dictionary<string, long>(),
        };
        var sim = new GameSimulation(db, world, 7, [new CreatureHealthSystem()], new TriggerTree([root], 0));

        leader.Health = 0;
        sim.Run(SimulationTiming.TicksPerSecond * 2);

        Assert.Equal((41, 7), (heir.TriggerId, (int)heir.TriggerRepeat));
    }

    [Fact]
    public void AFallenFollowerIsDetached_AndTheOthersKeepTheirNumbers()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = Member(db, world, 0x02, 3);
        CreatureState first = Member(db, world, 0x04, 4);
        CreatureState second = Member(db, world, 0x04, 5);
        HeroPartyRules.LinkFollowers(world, Party);

        KnockOut(db, world, first);

        Assert.Equal((0, -1, 0), (first.PartyId, first.LeaderId, first.Flags & 0x06));
        Assert.Equal((leader.Id, 1), (second.LeaderId, (int)second.PositionInParty));
        Assert.Equal(2, leader.PositionInParty);
    }

    [Fact]
    public void AHeroInNoPartyIsDetachedToPartyZero_ForTheTestIsPartyIdNotZero()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState loner = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(5, 5));
        loner.Flags = 0x20;

        KnockOut(db, world, loner);

        Assert.Equal((0, 0x20), (loner.PartyId, (int)loner.Flags));
    }

    [Fact]
    public void ALeaderInPartyZeroIsNotReplaced()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = Member(db, world, 0x02, 3, party: 0);
        CreatureState follower = Member(db, world, 0x04, 4, party: 0);
        HeroPartyRules.LinkFollowers(world, 0);

        KnockOut(db, world, leader);

        Assert.Equal(0x04, follower.Flags & 0x06);
        Assert.Equal(leader.Id, follower.LeaderId);
    }

    [Fact]
    public void ALeaderThatDiesWithNoDyingWindowIsNotReplaced()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = Member(db, world, 0x02, 3);
        leader.DieInstantly = true;
        CreatureState follower = Member(db, world, 0x04, 4);
        HeroPartyRules.LinkFollowers(world, Party);

        KnockOut(db, world, leader);

        Assert.Equal(0x04, follower.Flags & 0x06);
        Assert.Equal(leader.Id, follower.LeaderId);
    }

    [Fact]
    public void ACorpseIsNotAnHeir_TheFirstLivingFollowerIs()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = Member(db, world, 0x02, 3);
        CreatureState dead = Member(db, world, 0x04, 4);
        CreatureState heir = Member(db, world, 0x04, 5);
        HeroPartyRules.LinkFollowers(world, Party);
        dead.Health = 0;
        CreatureDeathRules.BecomeCorpse(world, db, dead);

        KnockOut(db, world, leader);

        Assert.Equal(0x04, dead.Flags & 0x06);
        Assert.Equal(0x02, heir.Flags & 0x06);
    }

    /// <summary>The heir leaves 222 for 1, and from there the hero system runs it as a leader: it
    /// takes its inherited standing job and never follows again.</summary>
    [Fact]
    public void TheHeirRunsItsInheritedJob_NotTheFollowersWalk()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = Member(db, world, 0x02, 3);
        leader.GoodJob = JobType.Wait;
        CreatureState heir = Member(db, world, 0x04, 6);
        HeroPartyRules.LinkFollowers(world, Party);
        var sim = new GameSimulation(db, world, 7, [new CreatureHealthSystem(), new HeroSystem(), new CreatureMovementSystem()]);
        sim.Run(8);
        leader.Health = 0;

        var seen = new HashSet<int>();
        for (int t = 0; t < 40; t++) { sim.Tick(); seen.Add(heir.Dk2StateId); }

        Assert.Contains(HeroJobRules.WaitingState, seen);
        Assert.DoesNotContain(HeroJobRules.FollowingState, seen);
        Assert.Equal(JobType.Wait, heir.CurrentJob);
    }

    private static void KnockOut(GameDatabase db, WorldState world, CreatureState creature)
    {
        creature.Health = 0;
        var sim = new GameSimulation(db, world, 7, [new CreatureHealthSystem()]);
        sim.Run(SimulationTiming.TicksPerSecond * 2);
    }

    private static CreatureState Member(GameDatabase db, WorldState world, byte flags, int x, byte party = Party)
    {
        CreatureState c = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(x, 5));
        c.PartyId = party;
        c.Flags = flags;
        return c;
    }

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                if (world.Tile(x, y).TerrainId != Dk2TerrainFixture.ClaimedPath)
                    world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }
}
