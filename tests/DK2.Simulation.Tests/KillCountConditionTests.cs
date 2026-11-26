// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>Trigger conditions 22 (PLAYER_CREATURES_KILLED, <c>0x004C7D0F</c>) and 23
/// (PLAYER_KILLS_CREATURES, <c>0x004C7DF0</c>) over the kill tallies <c>0x00487C70</c> keeps.</summary>
public class KillCountConditionTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const ushort Root = 5;

    [Fact]
    public void ACreditedDeathTalliesTheKillerByTheVictimsOwner_HeroesAndOwnIncluded()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        CreatureDef goblin = db.CreatureById(Dk2TerrainFixture.GoblinCreatureId);
        CreatureDef hero = goblin with { Flags = goblin.Flags | CreatureFlags.IsGood };

        Die(world, goblin, Rival, credited: Keeper);
        Die(world, hero, PlayerIds.Good, credited: Keeper);
        Die(world, goblin, Keeper, credited: Keeper);
        Die(world, goblin, Rival, credited: 0); // uncredited: only the owner's own count moves
        CreatureState uncounted = new() { OwnerPlayerId = Rival, Health = 0, DeathUncounted = true };
        LevelStatisticsRules.CreditDeath(world, goblin, uncounted, Keeper);

        ref PlayerLevelStatistics mine = ref world.Player(Keeper).Statistics;
        Assert.Equal(1, mine.KillsByVictimOwner[Rival]);
        Assert.Equal(1, mine.KillsByVictimOwner[PlayerIds.Good]);
        Assert.Equal(1, mine.KillsByVictimOwner[Keeper]);
        Assert.Equal(1, mine.EnemyCreaturesKilled); // +0x146 leaves out the hero and my own
        Assert.Equal(2, world.Player(Rival).Statistics.OwnCreaturesKilled);
    }

    [Theory]
    // Type 22, selector = the killer: how many of mine it killed. Selector 0: my own dead.
    [InlineData(22, Rival, 2, true)]
    [InlineData(22, Rival, 3, false)]
    [InlineData(22, 0, 4, true)]
    // Type 23, selector = the victims' owner: how many of theirs I killed (heroes count). Selector 0: +0x146.
    [InlineData(23, PlayerIds.Good, 5, true)]
    [InlineData(23, PlayerIds.Good, 6, false)]
    [InlineData(23, 0, 7, true)]
    [InlineData(23, 0, 8, false)]
    public void TheConditionsReadTheTalliesInBothDirections(byte type, byte selector, int atLeast, bool fires)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        world.Player(Rival).Statistics.KillsByVictimOwner[Keeper] = 2;
        world.Player(Keeper).Statistics.OwnCreaturesKilled = 4;
        world.Player(Keeper).Statistics.KillsByVictimOwner[PlayerIds.Good] = 5;
        world.Player(Keeper).Statistics.EnemyCreaturesKilled = 7;

        var parameters = new Dictionary<string, long> { ["Comparison"] = 5, ["Value"] = atLeast };
        parameters[type == 22 ? "PlayerId" : "PlayerSlot"] = selector;
        var tree = new TriggerTree(
            [Condition(Root, type, child: 0xFFFF, parameters), SetFlag(0xFFFF, flag: 1)], 0,
            playerRoots: [new TriggerPlayerRoot(Keeper, Root)]);
        var sim = new GameSimulation(db, world, seed: 7, [], tree);

        sim.Tick();

        Assert.Equal(fires ? 1 : 0, sim.World.Triggers.Read(1));
    }

    private static WorldState World(GameDatabase db)
    {
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        Dk2TerrainFixture.SetKnownIdentities(world);
        return world;
    }

    private static void Die(WorldState world, CreatureDef definition, byte owner, byte credited) =>
        LevelStatisticsRules.CreditDeath(world, definition, new CreatureState { OwnerPlayerId = owner, Health = 0 }, credited);

    private static TriggerRecord Condition(ushort id, byte type, ushort child, Dictionary<string, long> parameters) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 255,
            ChildTriggerId = child, NextTriggerId = 0, Parameters = parameters,
        };

    private static TriggerRecord SetFlag(ushort id, byte flag) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 7, RepeatCount = 0, ChildTriggerId = 0, NextTriggerId = 0,
            Parameters = new Dictionary<string, long> { ["FlagId"] = flag, ["Flag"] = 0x0A, ["Value"] = 1 },
        };
}
