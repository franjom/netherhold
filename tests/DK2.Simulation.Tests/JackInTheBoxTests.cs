// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Jack In The Box (jack-in-the-box.md): the trap's shot strikes the worker on its tile and
/// makes a Jack in state 273, which wears out walking to its victim's keeper's heart and explodes.</summary>
public class JackInTheBoxTests
{
    private sealed class SpringOnce(ShotState shot) : ISimulationSystem
    {
        public string Name => "SpringOnce";
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            JackInTheBoxRules.Spring(context, shot);
        }
    }

    // 004B039A damages first; failed allocation004B03C9 skips trap destruction and Jack setup.
    [Fact]
    public void FullPoolKeepsTheVictimDamageButLeavesTheTrapIntact()
    {
        var (db, world) = Open();
        ThingProjection.Place(world, db, [new TrapThing(new byte[16])
            { X = 5, Y = 5, TrapId = Dk2TerrainFixture.JackInTheBoxTrapId, PlayerId = Keeper, NumberOfShots = 1 }]);
        CreatureState victim = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 5));
        for (int i = 1; i < 448; i++) world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(1, 1));
        int health = victim.Health, trapHealth = Assert.Single(world.Traps).Health;
        var shot = new ShotState { OwnerPlayerId = Keeper, PosX = 5 * Fp32.One + 2048,
            PosY = 5 * Fp32.One + 2048, Data1 = 100, Data2 = victim.Id, TargetKind = FightTargetKind.Creature };
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new SpringOnce(shot)]);
        sim.ObserveWith(sink);
        sim.Tick();
        Assert.InRange(victim.Health, 1, health - 1);
        Assert.Equal(trapHealth, Assert.Single(world.Traps).Health);
        Assert.DoesNotContain(world.Creatures, JackInTheBoxRules.IsJack);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "jack_sprung");
    }

    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    /// <summary>State 6, the walk state 273 resumes from (creature-state-table.md).</summary>
    private const int WalkState = 6;

    /// <summary>Creature 46 as far as the Jack's rules read it: 240 health, no body, an Artisan.</summary>
    private static CreatureDef Jack() => Dk2TerrainFixture.Goblin() with
    {
        Name = "Jack",
        CreatureId = JackInTheBoxRules.JackCreatureId,
        Hp = 240,
        Flags = CreatureFlags.CanWalkOnWater | CreatureFlags.IsEvil | CreatureFlags.IsImmuneToChicken | CreatureFlags.IsFearless,
        JobClass = JobClass.Artisan,
    };

    private static (GameDatabase db, WorldState world) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database(Jack());
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 12), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, 1,
            [new CreatureStatusSystem(), new JackInTheBoxSystem(), new CreatureMovementSystem(), new ShotSystem(), new TrapSystem(), new TrapRemovalSystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    private static CreatureState Sprung(GameDatabase db, WorldState world, int x, int y, byte heartOf)
    {
        CreatureState jack = world.SpawnCreature(db, JackInTheBoxRules.JackCreatureId, Keeper, new TilePos(x, y));
        StatePlacement.Place(jack, JackInTheBoxRules.SpringState);
        jack.HeartTargetPlayerId = heartOf;
        return jack;
    }

    [Fact]
    public void TheTrapFiresAtAnEnemyWorkerOnItsTile_ItsShotStrikesItForTenThousand_AndAJackOfTheTrapsKeeperSpringsInTwoSeventyThree()
    {
        (GameDatabase db, WorldState world) = Open();
        ref TileState trapTile = ref world.Tile(5, 5);
        trapTile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        trapTile.OwnerPlayerId = Keeper;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = 5, Y = 5, TrapId = Dk2TerrainFixture.JackInTheBoxTrapId, PlayerId = Keeper, NumberOfShots = 1 }]);
        Assert.True(TrapRules.FiringBuilt(db, db.Traps[Dk2TerrainFixture.JackInTheBoxTrapId]));
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Rival, new TilePos(5, 5));
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Run(2);
        Assert.Contains(sink.Events, e => e.Kind == "trap_fired");
        var sprung = Assert.Single(sink.Events, e => e.Kind == "jack_sprung");
        Assert.Contains($"victim={imp.Id} ", sprung.Detail);
        Assert.True(CreatureDeathRules.IsDown(imp));
        CreatureState jack = Assert.Single(world.Creatures, c => JackInTheBoxRules.IsJack(c));
        Assert.Equal((Keeper, Rival), (jack.OwnerPlayerId, jack.HeartTargetPlayerId));
        Assert.Equal(CreatureActivity.JackInTheBox, jack.Activity);
        Assert.Empty(world.Traps);
    }

    /// <summary>Process 39 clears <c>+0x46F</c> bits 0 and 3 (<c>0x0048DF10(0)</c>, <c>0x0048DFA0(0)</c>): the
    /// fight gate refuses the Jack unless forced and nothing may attack it. In M7Traps (2026-11-01) DK2's
    /// Jack walked past the keeper's Imp to the heart and neither fought (jack-in-the-box.md §7).</summary>
    [Fact]
    public void ASprungJackNeverLooksForAFight_AndCannotBeAttacked_WhileAnOrdinaryCreatureCanDoBoth()
    {
        (GameDatabase db, WorldState world) = Open();
        ref TileState trapTile = ref world.Tile(5, 5);
        trapTile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        trapTile.OwnerPlayerId = Keeper;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = 5, Y = 5, TrapId = Dk2TerrainFixture.JackInTheBoxTrapId, PlayerId = Keeper, NumberOfShots = 1 }]);
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Rival, new TilePos(5, 5));
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(9, 5));
        GameSimulation sim = Sim(db, world, new ListEventSink());
        sim.Run(2);

        CreatureState jack = Assert.Single(world.Creatures, c => JackInTheBoxRules.IsJack(c));
        Assert.Equal(CreatureFightFlags.None, jack.FightFlags);
        // In 273 the state table already refuses both; the bits are what refuse them on the walk (state 6).
        for (int i = 0; i < 20 && jack.Dk2StateId != WalkState; i++) sim.Run(1);
        Assert.Equal(WalkState, jack.Dk2StateId);
        Assert.False(CombatRules.MayLookForAFight(world, db, jack, force: false));
        Assert.True(CombatRules.MayLookForAFight(world, db, jack, force: true));
        Assert.False(CombatRules.IsAttackable(jack));
        jack.FightFlags = CreatureFightFlags.MayLookForAFight | CreatureFightFlags.Attackable;
        Assert.True(CombatRules.MayLookForAFight(world, db, jack, force: false));
        Assert.True(CombatRules.IsAttackable(jack));
        Assert.Equal(CreatureFightFlags.MayLookForAFight | CreatureFightFlags.Attackable, goblin.FightFlags);
        Assert.True(CombatRules.MayLookForAFight(world, db, goblin, force: false));
        Assert.True(CombatRules.IsAttackable(goblin));
    }

    [Fact]
    public void AJackLosesAPointATickInTwoSeventyThree_AndFiftyOneATickOnTheTileOneEastAndSouthOfItsTargetsHeartPosition()
    {
        (GameDatabase db, WorldState world) = Open();
        // Rival has no heart: DK2's initial heart position, (1, 1), so the tile is (2, 2).
        CreatureState there = Sprung(db, world, 2, 2, Rival);
        int start = there.Health;
        GameSimulation sim = Sim(db, world, new ListEventSink());

        sim.Run(1);
        Assert.Equal(start - 51, there.Health);
        sim.Run(1);
        Assert.Equal(start - 102, there.Health);
    }

    [Fact]
    public void AJackAwayFromItsTargetWalksThereResumingTwoSeventyThree_PayingAPointATickOnTheWayToo()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState jack = Sprung(db, world, 8, 6, Rival);
        int start = jack.Health;
        GameSimulation sim = Sim(db, world, new ListEventSink());

        sim.Run(1);
        Assert.Equal(6, jack.Dk2StateId);
        Assert.Equal(JackInTheBoxRules.SpringState, jack.UtilityNewState);
        Assert.Equal(start - 1, jack.Health);
        sim.Run(5);
        Assert.Equal(start - 6, jack.Health);
    }

    [Fact]
    public void ADyingJackExplodesOnce_ShotSixtyFiveOfItsKeeperWithTheDefinitionsWords_HurtingAnEnemyWithinThreeTiles()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState jack = Sprung(db, world, 8, 6, Rival);
        jack.Health = 1;
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(9, 6));
        int before = goblin.Health;
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Run(1);
        ShotState blast = Assert.Single(world.Shots);
        Assert.Equal((Dk2TerrainFixture.JackExplosionShotId, Keeper, 20000, 3), (blast.ShotTypeId, blast.OwnerPlayerId, blast.Data1, blast.Data2));
        sim.Run(3);
        Assert.Single(sink.Events, e => e.Kind == "jack_exploded");
        Assert.True(goblin.Health < before);
    }

    [Fact]
    public void AreaDamageWrapsAsDK2s32BitArithmeticDoes_SixteenTilesSquaresToZeroAndReachesNobody_FifteenReaches()
    {
        foreach ((int tiles, bool hurt) in new[] { (15, true), (16, false) })
        {
            (GameDatabase db, WorldState world) = Open();
            CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(9, 6));
            int before = goblin.Health;
            ShotDef blast = db.Shots[Dk2TerrainFixture.JackExplosionShotId];
            ShotRules.Spawn(world, blast, Keeper, CreatureState.CentreOf(8), CreatureState.CentreOf(6), 100, tiles, 0,
                FightTargetKind.None, CombatRules.NoTarget, 0, -1, 0, 0);
            new GameSimulation(db, world, 1, [new ShotSystem()]).Run(1);
            Assert.Equal(hurt, goblin.Health < before);
        }
    }

    [Fact]
    public void TheJacksFieldsSurviveASnapshotRoundTrip()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState jack = Sprung(db, world, 8, 6, Rival);
        jack.DeathShotFired = true;
        GameSimulation sim = Sim(db, world, new ListEventSink());

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new JackInTheBoxSystem()]);
        CreatureState restored = copy.World.CreatureById(jack.Id)!;
        Assert.Equal((Rival, true), (restored.HeartTargetPlayerId, restored.DeathShotFired));
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }
}
