// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>ADR-0584: a creature that gave up chasing a retreating target records it (<c>+0x53B</c>,
/// state 8 <c>0x00470E30</c>); while it does, the Blocker, Support and Flanker arms pass over any
/// candidate in state 6 resuming 251 or 202 (<c>0x0045B224</c>, <c>0x0045B5E4</c>, <c>0x0045BBB4</c>), and
/// the creature update clears the record after 60 turns (<c>0x00489A8A</c>-<c>0x00489AB5</c>). Retreating is
/// <c>0x0045F000</c>: state 6 resuming 251 or 202, not 203.</summary>
public class RetreatTrackingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int TravelToPoint = 6;
    private const int Withdrawn = 202;
    private const int RespondingToThreat = 251;
    private const int Fleeing = 203;

    [Theory]
    [InlineData(Withdrawn, true, false)]
    [InlineData(Withdrawn, false, true)]
    [InlineData(RespondingToThreat, true, false)]
    [InlineData(RespondingToThreat, false, true)]
    public void ABlockerTrackingARetreatPassesOverAnImpWalkingAway(int resume, bool tracking, bool engages)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Walking(db, world, resume);
        // Spawned after the Imp, so it scans first in the tick (newest first).
        CreatureState knight = world.SpawnCreature(db, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, imp.Tile);
        knight.PosX = imp.PosX + 3000;
        knight.PosY = imp.PosY;
        if (tracking) knight.RetreatingCreature = 999;
        GameSimulation sim = new(db, world, 1, [new CombatSystem()]);

        sim.Tick();

        Assert.Equal(engages, knight.FightTargetKind == FightTargetKind.Creature && knight.FightTargetId == imp.Id);
    }

    [Fact]
    public void ABlitzerTrackingARetreatStillTakesAnImpWalkingAway()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Walking(db, world, Withdrawn);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Good, imp.Tile);
        Assert.Equal(FightStyle.Blitzer, db.CreatureById(goblin.CreatureTypeId).FightStyle);
        goblin.PosX = imp.PosX + 3000;
        goblin.PosY = imp.PosY;
        goblin.RetreatingCreature = 999;
        GameSimulation sim = new(db, world, 1, [new CombatSystem()]);

        sim.Tick();

        Assert.Equal((FightTargetKind.Creature, imp.Id), (goblin.FightTargetKind, goblin.FightTargetId));
    }

    [Fact]
    public void TheTrackingLapsesOnItsSixtiethTurn_TakingTheChaseCountWithIt()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = world.SpawnCreature(db, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, new TilePos(10, 10));
        knight.RetreatingCreature = 999;
        knight.ChaseTicks = 3;
        GameSimulation sim = new(db, world, 1, [new CombatSystem()]);

        sim.Run(CombatSystem.RetreatTrackingTicks - 1);
        Assert.Equal((999, 3, CombatSystem.RetreatTrackingTicks - 1), (knight.RetreatingCreature, knight.ChaseTicks, knight.AvoidRetreatersCounter));
        sim.Tick();
        Assert.Equal((0, 0, 0), (knight.RetreatingCreature, knight.ChaseTicks, knight.AvoidRetreatersCounter));
    }

    [Theory]
    [InlineData(TravelToPoint, Withdrawn, true)]
    [InlineData(TravelToPoint, RespondingToThreat, true)]
    [InlineData(TravelToPoint, 254, false)]
    [InlineData(Fleeing, 0, false)]
    [InlineData(Withdrawn, 0, false)]
    public void RetreatingIsAWalkTo251Or202(int state, int resume, bool retreating)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        CreatureStateMachine.TrySetState(default, goblin, state);
        goblin.UtilityNewState = resume;
        Assert.Equal(state, goblin.Dk2StateId);

        Assert.Equal(retreating, ApproachRules.IsRetreating(goblin));
    }

    [Theory]
    [InlineData(Withdrawn, 12, 13, true)]
    [InlineData(RespondingToThreat, 12, 13, true)]
    [InlineData(Withdrawn, 254, 255, true)]
    [InlineData(Withdrawn, 255, 0, false)]
    public void ApproachReentryPreservesTheByteChaseCounter(int resume, int initial, int next, bool givesUp)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Walking(db, world, resume);
        CreatureState knight = world.SpawnCreature(db, Dk2TerrainFixture.KnightCreatureId,
            PlayerIds.Good, new TilePos(7, 10));
        CreatureStateMachine.TrySetState(default, knight, 199);
        knight.FightTargetKind = FightTargetKind.Creature;
        knight.FightTargetId = imp.Id;
        knight.AttackType = CombatRules.AttackMode.MeleeAtCreature;
        knight.AttackRangeSquared = CombatRules.MeleeReachSquared(
            db.CreatureById(knight.CreatureTypeId), db.CreatureById(imp.CreatureTypeId));
        CombatRules.Register(imp, knight.Id, ranged: false);
        Assert.Equal(12, ApproachRules.ChaseLimitTicks(db, knight));
        knight.ChaseTicks = initial;
        CreatureState attacker = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId,
            Keeper, new TilePos(17, 17));
        attacker.FightTargetKind = FightTargetKind.Creature;
        attacker.FightTargetId = knight.Id;
        CombatRules.Register(knight, attacker.Id, ranged: false);
        int calls = 0;
        var sim = new GameSimulation(db, world, 1, [new Probe(context =>
        {
            if (++calls == 1)
                Assert.True(ApproachRules.Enter(context, knight, imp.PosX, imp.PosY));
            else
            {
                var hearts = new HeartSites();
                hearts.Refresh(world, db);
                ApproachRules.Tick(context, new WorldSight(context.Navigation, world, db),
                    hearts, new RoomCache(), knight, db.CreatureById(knight.CreatureTypeId), []);
            }
        })]);

        sim.Tick();
        Assert.Equal(initial, knight.ChaseTicks);
        Assert.Equal(8, knight.Dk2StateId);
        sim.Tick();
        Assert.Equal(next, knight.ChaseTicks);
        Assert.Equal(givesUp ? imp.Id : 0, knight.RetreatingCreature);
        Assert.Contains(attacker.Id, knight.EngagedMelee);
        Assert.Equal(knight.Id, attacker.FightTargetId);
        Assert.Equal(givesUp ? FightTargetKind.None : FightTargetKind.Creature, knight.FightTargetKind);
        Assert.Equal(givesUp ? 199 : 8, knight.Dk2StateId);
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 0)]
    public void FightingClearsTheRetreatCountOnlyWhenItNeedsAnotherApproach(bool outOfReach, int expected)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(10, 10));
        CreatureState knight = world.SpawnCreature(db, Dk2TerrainFixture.KnightCreatureId,
            PlayerIds.Good, new TilePos(outOfReach ? 7 : 10, 10));
        knight.PosX -= outOfReach ? 0 : 1000;
        CreatureStateMachine.TrySetState(default, knight, 199);
        knight.FightTargetKind = FightTargetKind.Creature;
        knight.FightTargetId = imp.Id;
        knight.AttackType = CombatRules.AttackMode.MeleeAtCreature;
        knight.AttackRangeSquared = CombatRules.MeleeReachSquared(
            db.CreatureById(knight.CreatureTypeId), db.CreatureById(imp.CreatureTypeId));
        CombatRules.Register(imp, knight.Id, ranged: false);
        knight.ChaseTicks = 7;
        var combat = new CombatSystem();
        var sim = new GameSimulation(db, world, 1, [combat.States]);

        sim.Tick();

        Assert.Equal(expected, knight.ChaseTicks);
        Assert.Equal(outOfReach ? 8 : 199, knight.Dk2StateId);
    }

    private sealed class Probe(Action<SimulationContext> run) : ISimulationSystem
    {
        public string Name => "probe";
        public void Execute(SimulationContext context) => run(context);
    }

    // ------------------------------------------------------------------ helpers

    private static CreatureState Walking(GameDatabase db, WorldState world, int resume)
    {
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(10, 10));
        CreatureStateMachine.TrySetState(default, imp, TravelToPoint);
        imp.UtilityNewState = resume;
        return imp;
    }

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.TerrainId == Dk2TerrainFixture.Rock || tile.TerrainId == Dk2TerrainFixture.Gold
                    || tile.TerrainId == Dk2TerrainFixture.Gems)
                    tile.TerrainId = Dk2TerrainFixture.DirtPath;
            }
        }
        return (db, world);
    }
}
