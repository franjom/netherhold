// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>ADR-0580: the dispatcher <c>0x00476FC0</c> runs one state handler a turn, so the states a
/// flight ends through each hold a turn: the walk's arrival enters 202, 251 or 254; 202's handler
/// (<c>0x00461850</c>) returns 0 on the next; 251's dispatches and sets 252, which holds while the
/// worker's action 79 plays (<c>0x00458680</c>); and state 1's handler waits for the turn after a
/// combat handler set it. DK2's Level 4 captures (eight seeds) show 202, 251 and 1 a tick each and
/// 252 four ticks for an Imp (436 of 444).</summary>
public class PostFlightDwellTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int TravelToPoint = 6;
    private const int Withdrawn = 202;
    private const int RespondingToThreat = 251;
    private const int StandingDown = 252;
    private const int ReportingDanger = 254;

    [Fact]
    public void AWithdrawnImpHoldsTwoHundredTwoATurn_ThenIdleATurn_ThenChoosesInTen()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        StatePlacement.As(imp, CreatureActivity.Withdrawing);
        GameSimulation sim = Sim(db, world);

        List<int> states = PerTick(sim, imp, 3);

        Assert.Equal([Withdrawn, CreatureStateMachine.Idle, ImpStates.TaskSelection], states);
    }

    [Fact]
    public void AWithdrawnGoblinHoldsTwoHundredTwoATurn_ThenIdleATurn_ThenRequestsEightyNine()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        StatePlacement.As(goblin, CreatureActivity.Withdrawing);
        GameSimulation sim = Sim(db, world);

        List<int> states = PerTick(sim, goblin, 3);

        Assert.Equal([Withdrawn, CreatureStateMachine.Idle, CreatureJobSystem.IdleChooserState], states);
    }

    [Fact]
    public void AnImpRaisingTheAlarmHoldsTwoFiftyOneATurn_TwoFiftyTwoWhileItsActionPlays_ThenIdleATurn()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceGuardRoom(world, 14, 10);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, 14, 10);
        CreatureStateMachine.TrySetState(default, imp, TravelToPoint);
        imp.UtilityNewState = RespondingToThreat;
        imp.AlarmRoomTile = (10 * world.Width) + 14;
        imp.AlarmX = CreatureState.CentreOf(4);
        imp.AlarmY = CreatureState.CentreOf(4);
        imp.AlarmDeficit = 10;
        Assert.Equal(CreatureActivity.RaisingAlarm, imp.Activity);
        GameSimulation sim = Sim(db, world);
        int hold = AnimationTimingRules.CountedHoldTicks(db.CreatureById(imp.CreatureTypeId), 29, 1);

        List<int> states = PerTick(sim, imp, hold + 4);

        List<int> expected = [RespondingToThreat];
        expected.AddRange(Enumerable.Repeat(StandingDown, hold));
        expected.Add(CreatureStateMachine.Idle);
        expected.Add(ImpStates.TaskSelection);
        Assert.Equal(expected, states.Take(expected.Count));
    }

    [Fact]
    public void ARaiserThatIsNotAWorkerIsIdleInTheTurnItDispatches_Then89()
    {
        // 0x00475660 returns 0 for a non-worker after SetState(1) and SetState(252): idle in that turn.
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceGuardRoom(world, 14, 10);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, 14, 10);
        CreatureStateMachine.TrySetState(default, goblin, TravelToPoint);
        goblin.UtilityNewState = RespondingToThreat;
        goblin.AlarmRoomTile = (10 * world.Width) + 14;
        goblin.AlarmX = CreatureState.CentreOf(4);
        goblin.AlarmY = CreatureState.CentreOf(4);
        goblin.AlarmDeficit = 10;
        GameSimulation sim = Sim(db, world);

        List<int> states = PerTick(sim, goblin, 3);

        Assert.Equal([RespondingToThreat, CreatureStateMachine.Idle, CreatureJobSystem.IdleChooserState], states);
    }

    [Fact]
    public void APitFighterIdledByACombatHandlerTriesThePitOnTheNextTurn()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        StatePlacement.As(goblin, CreatureActivity.Withdrawing);
        goblin.CombatPitFighter = true;
        var combat = new CombatSystem();
        var sim = new GameSimulation(db, world, 1,
            [new CreatureUpdateSystem(combat, combat.States, new CombatPitSystem(), new CreatureJobSystem())]);

        // No pit here: 1's handler tries 138, is refused and lets the fighter go (BackToThePit).
        sim.Tick();
        Assert.Equal(Withdrawn, goblin.Dk2StateId);
        sim.Tick();
        Assert.Equal((CreatureStateMachine.Idle, true), (goblin.Dk2StateId, goblin.CombatPitFighter));
        sim.Tick();
        Assert.False(goblin.CombatPitFighter);
    }

    [Fact]
    public void AResponderCountsItsThirtyTicksFromTheTurnAfterItArrives()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        CreatureStateMachine.TrySetState(default, goblin, TravelToPoint);
        goblin.UtilityNewState = ReportingDanger;
        Assert.Equal(CreatureActivity.Reporting, goblin.Activity);
        GameSimulation sim = Sim(db, world);

        List<int> states = PerTick(sim, goblin, FearRules.ReportTicks + 2);

        // The arrival's turn, then thirty counted turns in 254, then idle.
        Assert.All(states.Take(FearRules.ReportTicks + 1), s => Assert.Equal(ReportingDanger, s));
        Assert.Equal(CreatureStateMachine.Idle, states[FearRules.ReportTicks + 1]);
    }

    // ------------------------------------------------------------------ helpers

    private static List<int> PerTick(GameSimulation sim, CreatureState creature, int ticks)
    {
        var states = new List<int>();
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            states.Add(creature.Dk2StateId);
        }
        return states;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world)
    {
        var combat = new CombatSystem();
        return new GameSimulation(db, world, 1,
            [new CreatureUpdateSystem(combat, combat.States, new CreatureJobSystem(), new CreatureMovementSystem(), new ImpTaskSystem())]);
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

    private static void PlaceGuardRoom(WorldState world, int cx, int cy)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                ref TileState tile = ref world.Tile(new TilePos(cx + dx, cy + dy));
                tile.TerrainId = Dk2TerrainFixture.GuardRoomTerrain;
                tile.OwnerPlayerId = Keeper;
            }
        }
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, int x, int y) =>
        world.SpawnCreature(db, type, Keeper, new TilePos(x, y));
}
