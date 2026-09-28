// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>Action 14 raising a hero party, against the read of <c>0x00519F90</c> in
/// hero-parties-and-effect-generators.md.</summary>
public class HeroPartyTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Goblin = Dk2TerrainFixture.GoblinCreatureId;
    private const byte Invader = 30;
    private const byte PointId = 3;

    [Fact]
    public void EachOccupiedSlotBecomesAHeroAtTheActionPointsCentre_AndTheActionIsNoLongerPartial()
    {
        (WorldState world, GameDatabase database) = World();
        Place(world, database, partyId: 0, members:
        [
            // A level-3 leader at half gold and half health, sent to action point 9 of player 3.
            Member(Goblin, level: 3, gold: 50, health: 50, flags: 0x0B, objective: 23, target: 9, player: 3, trigger: 7, flags2: 0x05),
            Member(Goblin, level: 1, gold: 0, health: 100, flags: 0x0D, objective: 22),
            new GoodCreatureThing(new byte[32]),
        ]);

        TriggerRunResult result = Raise(world, database, partyId: 0, partyType: 0);

        Assert.Empty(result.Partial);
        Assert.Equal(2, world.Creatures.Count);
        CreatureState leader = world.Creatures[0];
        CreatureState follower = world.Creatures[1];
        Assert.All(world.Creatures, c =>
        {
            Assert.Equal(PlayerIds.Good, c.OwnerPlayerId);
            Assert.Equal(new TilePos(7, 5), c.Tile);
            Assert.Equal((byte)0, c.PartyId);
        });

        Assert.Equal(3, leader.Level);
        Assert.Equal((byte)0x0B, leader.Flags);
        Assert.Equal(JobType.SendToActionPoint, leader.GoodJob);
        Assert.Equal(9, leader.GoodJobActionPointId);
        Assert.Equal((byte)3, leader.GoodJobPlayerId);
        Assert.Equal((ushort)7, leader.TriggerId);
        Assert.True(leader.DestroysRooms);
        Assert.True(leader.DieInstantly);
        Assert.False(leader.IsTool);
        Assert.False(leader.IsMercenary);

        Assert.Equal(1, follower.Level);
        Assert.Equal(JobType.Wait, follower.GoodJob);
        Assert.Equal(leader.Id, follower.LeaderId);
        Assert.Equal((byte)1, follower.PositionInParty);
        Assert.Equal((byte)1, leader.PositionInParty);
        Assert.Equal(-1, leader.LeaderId);
        Assert.Equal((byte)1, world.HeroParties[0].SpawnCount);
    }

    /// <summary>The percentages land on a level-1 creature and the level-ups then carry health up
    /// by the maximum's growth, so a half-health level-3 member does not end at half of level 3's
    /// maximum. Gold is capped by level 1's purse. Whether DK2 really plays it that way is Q427; the
    /// arithmetic here is the handler's, instruction for instruction.</summary>
    [Fact]
    public void HalfHealthAtLevelThree_IsAppliedAtLevelOneThenCarriedUp_TheDoubleApplicationOfQ427()
    {
        (WorldState world, GameDatabase database) = World();
        CreatureDef goblin = database.CreatureById(Goblin);
        Assert.Equal(1500, goblin.Hp);
        Assert.Equal(5000, goblin.MaxGoldHeld);
        Place(world, database, partyId: 0, members: [Member(Goblin, level: 3, gold: 50, health: 50, flags: 0x0B)]);

        Raise(world, database, partyId: 0, partyType: 0);

        CreatureState hero = Assert.Single(world.Creatures);
        // 50 % of level 3's 2250 is 1125 at level 1; ×1875/1500 → 1406; ×2250/1875 → 1686.
        Assert.Equal(1686, hero.Health);
        Assert.Equal(2250, CreatureHealthRules.MaxHealth(database, goblin, hero));
        // 50 % of level 3's 5500 is 2750, under level 1's cap of 5000.
        Assert.Equal(2750, hero.GoldHeld);
    }

    [Fact]
    public void ARandomPartyLeadsWithADwarf_DrawsInvaders_ForcesKillPlayer_AndIsNotRaisedWhileAlive()
    {
        (WorldState world, GameDatabase database) = World(
            Dk2TerrainFixture.Goblin() with { CreatureId = HeroPartyRules.DwarfCreatureId, Name = "Dwarf" },
            Dk2TerrainFixture.Knight() with
            {
                CreatureId = Invader, Name = "Royal Guard",
                Flags = Dk2TerrainFixture.Knight().Flags | CreatureFlags.MpdRandomInvader,
            });
        Place(world, database, partyId: 2, members:
        [
            Member(Goblin, level: 1, gold: 0, health: 100, flags: 0x0B, objective: 18, party: 2),
            Member(Goblin, level: 1, gold: 0, health: 100, flags: 0x0D, objective: 18, party: 2),
        ]);
        HeroPartyState party = world.HeroParties[0];
        var rng = new SimRandom(11);

        Assert.True(HeroPartyRules.Spawn(world, database, rng, Tree(2, 2), party, PointId, partyType: 2, _ => { }));
        Assert.Equal(HeroPartyRules.DwarfCreatureId, world.Creatures[0].CreatureTypeId);
        Assert.Equal(Invader, world.Creatures[1].CreatureTypeId);
        // No level-ups on the first raising, so the job is forced to KillPlayer.
        Assert.All(world.Creatures, c => Assert.Equal(JobType.KillPlayer, c.GoodJob));
        Assert.All(world.Creatures, c => Assert.Equal(1, c.Level));

        // Alive: the second raising does nothing, not even a count.
        Assert.True(HeroPartyRules.Spawn(world, database, rng, Tree(2, 2), party, PointId, partyType: 2, _ => { }));
        Assert.Equal(2, world.Creatures.Count);
        Assert.Equal((byte)1, party.SpawnCount);

        // Dead: the third raising buys one level-up per earlier raising and keeps the record's job.
        world.RemoveCreature(world.Creatures[0].Id);
        world.RemoveCreature(world.Creatures[0].Id);
        Assert.True(HeroPartyRules.Spawn(world, database, rng, Tree(2, 2), party, PointId, partyType: 2, _ => { }));
        Assert.Equal(2, world.Creatures.Count);
        Assert.All(world.Creatures, c => Assert.Equal(2, c.Level));
        Assert.All(world.Creatures, c => Assert.Equal(JobType.KillCreatures, c.GoodJob));
        Assert.Equal((byte)2, party.SpawnCount);
    }

    [Fact]
    public void ALevelUpCarriesHealthAcrossAsItsShareOfTheMaximum_AndStopsAtTen()
    {
        (WorldState world, GameDatabase database) = World();
        CreatureDef goblin = database.CreatureById(Goblin);
        CreatureState state = world.SpawnCreature(database, Goblin, Keeper, new TilePos(2, 2));
        state.Health = 750;

        Assert.True(CreatureLevelRules.LevelUp(database, goblin, state));
        Assert.Equal(2, state.Level);
        Assert.Equal(937, state.Health); // 750/1500 in 12-bit fixed point is exactly 1/2; 1875/2 truncates.

        state.Level = CreatureLevelRules.MaxLevel;
        Assert.False(CreatureLevelRules.LevelUp(database, goblin, state));
    }

    [Fact]
    public void TheSpawnTileIsTheActionPointsCentre_AndTheOriginWhenNoPointMatches()
    {
        (WorldState world, GameDatabase database) = World();
        Assert.Equal(new TilePos(7, 5), HeroPartyRules.SpawnTile(world, database, PointId, out bool gate));
        Assert.False(gate);
        Assert.Equal(new TilePos(0, 0), HeroPartyRules.SpawnTile(world, database, 200, out _));
    }

    [Fact]
    public void APartyIdWithNoRecordIsAnEmptySlot_NothingHappensAndTheActionSucceeds()
    {
        (WorldState world, GameDatabase database) = World();
        TriggerRunResult result = Raise(world, database, partyId: 5, partyType: 0);
        Assert.Empty(world.Creatures);
        Assert.Empty(result.Partial);
        Assert.Empty(result.Unhandled);
    }

    /// <summary>Condition 64 (<c>0x005187AD</c>): occupied templates minus members still active.
    /// A knocked-out member is missing at once, not after its dying window — as the owner's
    /// Level 2 playback showed, flag 10 turning the tick after the fourth Dwarf went to 216
    /// (ADR-0248).</summary>
    [Fact]
    public void Condition64CountsAKnockedOutMemberAsMissing_BeforeItsBodyIsGone()
    {
        (WorldState world, GameDatabase database) = World();
        Place(world, database, partyId: 0, members:
        [
            Member(Goblin, level: 1, gold: 0, health: 100, flags: 0x0B),
            Member(Goblin, level: 1, gold: 0, health: 100, flags: 0x0D),
        ]);
        Assert.True(HeroPartyRules.Spawn(
            world, database, null, Tree(0, 0), world.HeroParties[0], PointId, partyType: 0, _ => { }));
        var sim = new GameSimulation(database, world, 7,
            [new Systems.CreatureHealthSystem()], MissingTree(partyId: 0, atLeast: 1));

        sim.Run(SimulationTiming.TicksPerSecond);
        Assert.Equal(0, world.Triggers.Read(10));

        world.Creatures[1].Health = 0;
        sim.Run(SimulationTiming.TicksPerSecond * 2);

        Assert.True(CreatureDeathRules.IsDying(world.Creatures[1]));
        Assert.Equal(1, world.Triggers.Read(10));
    }

    /// <summary>DK2 walks the world list for every owner but Good, then Good's own list, so a
    /// member now owned by a keeper still counts as present.</summary>
    [Fact]
    public void Condition64CountsAMemberOfAnyOwner_NotOnlyTheHeroPlayers()
    {
        (WorldState world, GameDatabase database) = World();
        Place(world, database, partyId: 0, members: [Member(Goblin, level: 1, gold: 0, health: 100, flags: 0x0B)]);
        Assert.True(HeroPartyRules.Spawn(
            world, database, null, Tree(0, 0), world.HeroParties[0], PointId, partyType: 0, _ => { }));
        world.Creatures[0].OwnerPlayerId = Keeper;
        var sim = new GameSimulation(database, world, 7, [], MissingTree(partyId: 0, atLeast: 1));

        sim.Run(SimulationTiming.TicksPerSecond);

        Assert.Equal(0, world.Triggers.Read(10));
    }

    /// <summary>A world root asking <c>missing members of the party &gt;= atLeast</c>, setting flag 10.</summary>
    private static TriggerTree MissingTree(byte partyId, int atLeast) =>
        new(
        [
            new GenericTriggerRecord(new byte[16])
            {
                Body = new byte[8], TriggerId = 1, Type = 64, RepeatCount = 1,
                ChildTriggerId = 0xFFFF, NextTriggerId = 0,
                Parameters = new Dictionary<string, long>
                {
                    ["Comparison"] = 5, ["PartyId"] = partyId, ["Value"] = atLeast,
                },
            },
            new ActionTriggerRecord(new byte[16])
            {
                Body = new byte[8], TriggerId = 0xFFFF, Type = 7, RepeatCount = 0,
                Parameters = new Dictionary<string, long> { ["FlagId"] = 10, ["Flag"] = 0x0A, ["Value"] = 1 },
            },
        ], 1, playerRoots: []);

    private static (WorldState World, GameDatabase Database) World(params CreatureDef[] extra)
    {
        GameDatabase database = Dk2TerrainFixture.Database(extra);
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        ThingProjection.Place(world, database,
        [
            new ActionPointThing(new byte[56])
            {
                StartX = 6, StartY = 4, EndX = 8, EndY = 6, ActionPointId = PointId, Name = "",
            },
        ]);
        return (world, database);
    }

    private static void Place(WorldState world, GameDatabase database, byte partyId, GoodCreatureThing[] members) =>
        ThingProjection.Place(world, database,
        [
            new HeroPartyThing(new byte[555])
            {
                Name = "party", TriggerId = 5, PartyId = partyId,
                Members = members,
            },
        ]);

    private static GoodCreatureThing Member(
        byte creature, byte level, ushort gold, int health, byte flags,
        byte objective = 0, int target = 0, byte player = 0, ushort trigger = 0, byte flags2 = 0, byte party = 0) =>
        new(new byte[32])
        {
            CreatureId = creature, Level = level, GoldHeld = gold, InitialHealth = health, Flags = flags,
            Objective = objective, ObjectiveTargetActionPointId = target, ObjectiveTargetPlayerId = player,
            TriggerId = trigger, Flags2 = flags2, PartyId = party,
        };

    private static TriggerRunResult Raise(WorldState world, GameDatabase database, byte partyId, byte partyType) =>
        new TriggerExecutor(Tree(partyId, partyType), world.Triggers, world, database, new SimRandom(1)).Run(1);

    private static TriggerTree Tree(byte partyId, byte partyType) =>
        new(
        [
            new GenericTriggerRecord(new byte[16])
            {
                Body = new byte[8], TriggerId = 1, Type = 34, RepeatCount = 1,
                ChildTriggerId = 0xFFFF, NextTriggerId = 0,
                Parameters = new Dictionary<string, long> { ["Comparison"] = 5, ["Value"] = 0 },
            },
            new ActionTriggerRecord(new byte[16])
            {
                Body = new byte[8], TriggerId = unchecked((ushort)-1), Type = 14, RepeatCount = 0,
                Parameters = new Dictionary<string, long>
                {
                    ["PartyId"] = partyId, ["PartyType"] = partyType, ["ActionPointId"] = PointId,
                },
            },
        ], 1, playerRoots: []);
}
